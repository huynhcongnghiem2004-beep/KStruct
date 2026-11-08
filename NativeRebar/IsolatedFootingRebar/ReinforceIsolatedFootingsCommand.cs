using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ReinforceIsolatedFootingsCommand : IExternalCommand
    {
        private sealed class FootingFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => IsFooting(element as FamilyInstance);
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class BarOption
        {
            public RebarBarType Type { get; }
            public string Label { get; }
            public BarOption(RebarBarType type)
            {
                Type = type;
                Parameter diameter = type.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER);
                double mm = diameter != null && diameter.HasValue
                    ? UnitUtils.ConvertFromInternalUnits(diameter.AsDouble(), UnitTypeId.Millimeters) : 0;
                Label = "Ø" + mm.ToString("0.##") + " mm  |  " + type.Name;
            }
        }

        private sealed class HookOption
        {
            public RebarHookType Type;
            public string Name;
            public HookOption(RebarHookType type)
            {
                Type = type;
                Name = type == null ? "Không dùng móc RebarHookType" : type.Name;
            }
        }

        private sealed class OptionsData
        {
            public RebarBarType TypeX, TypeY;
            public double CoverBottomMm, CoverTopMm, CoverSideMm;
            public double SpacingXmm, SpacingYmm;
            public LayerHookOptions BottomHooks, MiddleHooks, TopHooks;
            public bool Bottom, Middle, Top;
            public bool SideVertical;
            public bool SideHorizontal;
            public RebarHookType OStirrupHookType;
            public bool SideCStirrups;
            public bool CStirrupHookX, CStirrupHookY;
            public double HorizontalMaxSpacingMm;
            public double CStirrupLegXmm, CStirrupLegYmm;
            public bool ColumnDowels;
            public double DowelAnchorageMm, DowelProjectionMm;
        }

        private sealed class LayerHookOptions
        {
            public RebarHookType HookX, HookY;
        }

        private sealed class LayerHookControls
        {
            public CheckBox UseX, UseY;
            public System.Windows.Controls.ComboBox TypeX, TypeY;
        }

        private sealed class FootingFrame
        {
            public XYZ Center;
            public XYZ X;
            public XYZ Y;
            public double MinX, MaxX, MinY, MaxY, Bottom, Top;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<FamilyInstance> footings = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<FamilyInstance>()
                    .Where(IsFooting).Distinct().ToList();
                if (footings.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new FootingFilter(),
                        "Chọn móng đơn hoặc đài cọc dạng FamilyInstance cần bố trí thép");
                    footings = picked.Select(r => doc.GetElement(r)).OfType<FamilyInstance>().Where(IsFooting).Distinct().ToList();
                }
                if (footings.Count == 0) return Result.Cancelled;

                TaskDialogResult scope = TaskDialog.Show("KStruct - Phạm vi thép móng / đài cọc",
                    "Lệnh tạo lưới thép hai phương cho móng đơn hoặc đài cọc có mặt bằng gần chữ nhật. Có thể thêm thép đứng cạnh, đai C/O quanh thành móng và bốn thép chờ chữ L tại cột. Lệnh không tính sức kháng đài cọc, không thiết kế neo riêng quanh đầu cọc; không dùng cho móng tròn hoặc mặt bằng tam giác/bất quy tắc.",
                    TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel);
                if (scope != TaskDialogResult.Ok) return Result.Cancelled;

                List<RebarBarType> types = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType))
                    .Cast<RebarBarType>().OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (types.Count == 0) throw new InvalidOperationException("Project chưa có RebarBarType.");
                List<RebarHookType> hookTypes = new FilteredElementCollector(doc).OfClass(typeof(RebarHookType))
                    .Cast<RebarHookType>().OrderBy(h => h.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                OptionsData options = ChooseOptions(data.Application.MainWindowHandle, types, hookTypes, GetFrame(footings[0]));
                if (options == null) return Result.Cancelled;

                int completed = 0, barsCreated = 0, replacedBars = 0;
                var skipped = new List<string>();
                using (var tx = new Transaction(doc, "KStruct - Thép móng / đài cọc"))
                {
                    tx.Start();
                    foreach (FamilyInstance footing in footings)
                    {
                        using (var sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                List<ElementId> previousManagedBars = FindManagedFootingBars(doc, footing.Id);
                                int count = ReinforceFooting(doc, footing, options);
                                if (count <= 0) throw new InvalidOperationException("Không tạo được bộ thép mới; bộ KStruct cũ được giữ nguyên.");
                                if (previousManagedBars.Count > 0) doc.Delete(previousManagedBars);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận cốt thép móng.");
                                completed++;
                                barsCreated += count;
                                replacedBars += previousManagedBars.Count;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Móng " + footing.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (completed == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Thép móng / đài cọc", "Không tạo được thép móng nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các thanh mới.");
                }

                string report = "Đã tạo " + barsCreated + " thanh" + (options.SideVertical ? " (có thép đứng cạnh)" : "") +
                    (options.SideHorizontal ? " (có đai ngang thành móng)" : "") +
                    (options.SideCStirrups ? " (có đai C thành móng)" : "") +
                    (options.ColumnDowels ? " (có thép chờ cột)" : "") +
                    " cho " + completed + "/" + footings.Count + " móng/đài cọc.";
                if (replacedBars > 0) report += "\nĐã thay " + replacedBars + " thanh KStruct cũ sau khi tạo bộ mới thành công.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " móng:\n" + string.Join("\n", skipped.Take(8));
                if (!options.ColumnDowels) report += "\nLệnh chưa tạo thép chờ/neo vào cột.";
                TaskDialog.Show("KStruct - Thép móng / đài cọc", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Thép móng / đài cọc", ex.Message);
                return Result.Failed;
            }
        }

        private static List<ElementId> FindManagedFootingBars(Document doc, ElementId footingId)
        {
            string prefix = "KSTR_FOOTING_" + footingId.Value + "_";
            return new FilteredElementCollector(doc).OfClass(typeof(Rebar)).WhereElementIsNotElementType()
                .Cast<Rebar>().Where(bar => bar.GetHostId() == footingId)
                .Where(bar =>
                {
                    Parameter partition = bar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
                    return partition != null && partition.HasValue && partition.AsString() != null &&
                        partition.AsString().StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                }).Select(bar => bar.Id).ToList();
        }

        private static int ReinforceFooting(Document doc, FamilyInstance footing, OptionsData options)
        {
            FootingFrame frame = GetFrame(footing);
            ValidateRectangularFootprint(footing, frame);
            double upperCoverage = GetTopFaceCoverage(footing, frame);
            if ((options.Middle || options.Top) && upperCoverage < 0.90)
                throw new InvalidOperationException("Lớp giữa/trên chưa hỗ trợ đài có mặt trên nhỏ hoặc bậc lớn; hiện chỉ có thể tạo lớp đáy an toàn theo footprint mặt đáy.");
            if (options.SideVertical && upperCoverage < 0.995)
                throw new InvalidOperationException("Thép đứng cạnh chưa hỗ trợ đài có thành nghiêng hoặc bậc lớn; hãy tắt cấu tạo thép đứng cạnh.");
            double coverBottom = UnitUtils.ConvertToInternalUnits(options.CoverBottomMm, UnitTypeId.Millimeters);
            double coverTop = UnitUtils.ConvertToInternalUnits(options.CoverTopMm, UnitTypeId.Millimeters);
            double coverSide = UnitUtils.ConvertToInternalUnits(options.CoverSideMm, UnitTypeId.Millimeters);
            double spacingX = UnitUtils.ConvertToInternalUnits(options.SpacingXmm, UnitTypeId.Millimeters);
            double spacingY = UnitUtils.ConvertToInternalUnits(options.SpacingYmm, UnitTypeId.Millimeters);
            double horizontalMaxSpacing = UnitUtils.ConvertToInternalUnits(options.HorizontalMaxSpacingMm, UnitTypeId.Millimeters);
            double radius = Math.Max(options.TypeX.BarNominalDiameter, options.TypeY.BarNominalDiameter) / 2.0;
            double edge = coverSide + radius;
            double minX = frame.MinX + edge, maxX = frame.MaxX - edge;
            double minY = frame.MinY + edge, maxY = frame.MaxY - edge;
            if (maxX - minX <= doc.Application.ShortCurveTolerance || maxY - minY <= doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Cover/thép không vừa kích thước mặt bằng móng.");
            int alongX = Math.Max(2, (int)Math.Ceiling((maxY - minY) / spacingX) + 1);
            int alongY = Math.Max(2, (int)Math.Ceiling((maxX - minX) / spacingY) + 1);
            int layers = (options.Bottom ? 1 : 0) + (options.Middle ? 1 : 0) + (options.Top ? 1 : 0);
            if (layers == 0 && !options.SideVertical && !options.SideHorizontal && !options.SideCStirrups && !options.ColumnDowels) throw new InvalidOperationException("Chọn ít nhất một lớp ngang, thép cạnh, đai C/O hoặc thép chờ cột.");
            if ((long)(alongX + alongY) * layers > 1000) throw new InvalidOperationException("Khoảng cách yêu cầu tạo trên 1000 thanh cho một móng.");
            double height = frame.Top - frame.Bottom;
            double maxDiameter = Math.Max(options.TypeX.BarNominalDiameter, options.TypeY.BarNominalDiameter);
            double layerGap = maxDiameter + UnitUtils.ConvertToInternalUnits(2, UnitTypeId.Millimeters);
            double verticalRoom = coverBottom + coverTop + layers * (maxDiameter + layerGap);
            if (height <= 2 * edge || (layers > 0 && height <= verticalRoom))
            {
                double heightMm = UnitUtils.ConvertFromInternalUnits(height, UnitTypeId.Millimeters);
                double requiredMm = UnitUtils.ConvertFromInternalUnits(Math.Max(2 * edge, verticalRoom), UnitTypeId.Millimeters);
                double diameterMm = UnitUtils.ConvertFromInternalUnits(maxDiameter, UnitTypeId.Millimeters);
                double bottomCoverMm = UnitUtils.ConvertFromInternalUnits(coverBottom, UnitTypeId.Millimeters);
                double topCoverMm = UnitUtils.ConvertFromInternalUnits(coverTop, UnitTypeId.Millimeters);
                throw new InvalidOperationException("Chiều dày móng không đủ cho các lớp thép đã chọn và cover. " +
                    "Code đo được " + heightMm.ToString("0.##", CultureInfo.CurrentCulture) + " mm; cần lớn hơn " +
                    requiredMm.ToString("0.##", CultureInfo.CurrentCulture) + " mm (Ø lớn nhất " +
                    diameterMm.ToString("0.##", CultureInfo.CurrentCulture) + " mm, cover đáy/trên " +
                    bottomCoverMm.ToString("0.##", CultureInfo.CurrentCulture) + "/" +
                    topCoverMm.ToString("0.##", CultureInfo.CurrentCulture) + " mm, " + layers + " lớp).");
            }

            int created = 0;
            if (options.Bottom)
                created += CreateMat(doc, footing, frame, options.TypeX, options.TypeY, minX, maxX, minY, maxY, spacingX, spacingY,
                    frame.Bottom + coverBottom + maxDiameter / 2.0 + layerGap / 2.0, layerGap, "BOT", options.BottomHooks);
            if (options.Middle)
                created += CreateMat(doc, footing, frame, options.TypeX, options.TypeY, minX, maxX, minY, maxY, spacingX, spacingY,
                    (frame.Bottom + coverBottom + maxDiameter / 2.0 + frame.Top - coverTop - maxDiameter / 2.0) / 2.0, layerGap, "MID", options.MiddleHooks);
            if (options.Top)
                created += CreateMat(doc, footing, frame, options.TypeX, options.TypeY, minX, maxX, minY, maxY, spacingX, spacingY,
                    frame.Top - coverTop - maxDiameter / 2.0 - layerGap / 2.0, layerGap, "TOP", options.TopHooks);
            if (options.SideVertical)
                created += CreateSideVerticalBars(doc, footing, frame, options.TypeX, minX, maxX, minY, maxY,
                    frame.Bottom + coverBottom + options.TypeX.BarNominalDiameter / 2.0,
                    frame.Top - coverTop - options.TypeX.BarNominalDiameter / 2.0, spacingX, spacingY);
            if (options.SideHorizontal)
            {
                double coverage = GetTopFaceCoverage(footing, frame);
                if (coverage < 0.995)
                    throw new InvalidOperationException("Đai ngang thành móng chỉ hỗ trợ mặt bằng gần chữ nhật (mặt top phủ ít nhất 99,5% bounding box).");
                double inset = coverSide + options.TypeX.BarNominalDiameter / 2.0;
                created += CreateHorizontalSideTies(doc, footing, frame, options.TypeX,
                    frame.MinX + inset, frame.MaxX - inset, frame.MinY + inset, frame.MaxY - inset,
                    frame.Bottom + coverBottom + options.TypeX.BarNominalDiameter / 2.0,
                    frame.Top - coverTop - options.TypeX.BarNominalDiameter / 2.0,
                    horizontalMaxSpacing,
                    options.OStirrupHookType,
                    doc.Application.ShortCurveTolerance);
            }
            if (options.SideCStirrups)
            {
                if (GetTopFaceCoverage(footing, frame) < 0.995)
                    throw new InvalidOperationException("Đai C thành móng chỉ hỗ trợ mặt bằng gần chữ nhật (mặt top phủ ít nhất 99,5% bounding box).");
                double inset = coverSide + options.TypeX.BarNominalDiameter / 2.0;
                double legX = UnitUtils.ConvertToInternalUnits(options.CStirrupLegXmm, UnitTypeId.Millimeters);
                double legY = UnitUtils.ConvertToInternalUnits(options.CStirrupLegYmm, UnitTypeId.Millimeters);
                if ((options.CStirrupHookX && (legX <= options.TypeX.BarNominalDiameter || legX >= frame.MaxY - frame.MinY - 2 * inset)) ||
                    (options.CStirrupHookY && (legY <= options.TypeX.BarNominalDiameter || legY >= frame.MaxX - frame.MinX - 2 * inset)))
                    throw new InvalidOperationException("Chiều dài nhánh trả đai C phải dương và vừa mặt bằng móng.");
                created += CreateHorizontalCStirrups(doc, footing, frame, options.TypeX,
                    frame.MinX + inset, frame.MaxX - inset, frame.MinY + inset, frame.MaxY - inset,
                    frame.Bottom + coverBottom + options.TypeX.BarNominalDiameter / 2.0,
                    frame.Top - coverTop - options.TypeX.BarNominalDiameter / 2.0,
                    horizontalMaxSpacing, options.CStirrupHookX, options.CStirrupHookY, legX, legY,
                    doc.Application.ShortCurveTolerance);
            }
            if (options.ColumnDowels)
            {
                FamilyInstance column = FindColumn(doc, frame);
                if (column == null) throw new InvalidOperationException("Không tìm thấy cột Structural Column đặt trên móng.");
                double anchorage = UnitUtils.ConvertToInternalUnits(options.DowelAnchorageMm, UnitTypeId.Millimeters);
                double projection = UnitUtils.ConvertToInternalUnits(options.DowelProjectionMm, UnitTypeId.Millimeters);
                created += CreateColumnDowels(doc, footing, column, frame, options.TypeX,
                    coverSide + options.TypeX.BarNominalDiameter / 2.0,
                    frame.Bottom + coverBottom + options.TypeX.BarNominalDiameter / 2.0,
                    anchorage, projection);
            }
            return created;
        }

        private static void ValidateRectangularFootprint(FamilyInstance footing, FootingFrame frame)
        {
            double frameArea = (frame.MaxX - frame.MinX) * (frame.MaxY - frame.MinY);
            double tolerance = UnitUtils.ConvertToInternalUnits(25, UnitTypeId.Millimeters);
            double bottomArea = LargestHorizontalBottomFaceArea(footing.get_Geometry(new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine
            }), frame.Bottom, tolerance);
            if (frameArea <= 0 || bottomArea <= 0 || bottomArea / frameArea < 0.90)
                throw new InvalidOperationException("Lệnh tạo lưới móng cần footprint mặt đáy gần chữ nhật, phủ tối thiểu 90% bounding box. Mặt đáy tam giác, tròn hoặc bất quy tắc chưa được hỗ trợ.");
        }

        private static double LargestHorizontalBottomFaceArea(GeometryElement geometry, double bottomZ, double tolerance)
        {
            if (geometry == null) return 0;
            double area = 0;
            foreach (GeometryObject item in geometry)
            {
                if (item is Solid solid && solid.Faces.Size > 0)
                {
                    foreach (Face face in solid.Faces)
                    {
                        PlanarFace planar = face as PlanarFace;
                        if (planar == null || planar.FaceNormal.Z > -0.995 || Math.Abs(planar.Origin.Z - bottomZ) > tolerance)
                            continue;

                        // Use the outside loop instead of net face area. Pile penetrations can
                        // create inner loops in the cap underside without changing its footprint.
                        foreach (CurveLoop loop in planar.GetEdgesAsCurveLoops())
                            area = Math.Max(area, CurveLoopArea(loop));
                    }
                }
                else if (item is GeometryInstance instance)
                {
                    area = Math.Max(area, LargestHorizontalBottomFaceArea(instance.GetInstanceGeometry(), bottomZ, tolerance));
                }
            }
            return area;
        }

        private static double CurveLoopArea(CurveLoop loop)
        {
            var points = new List<XYZ>();
            foreach (Curve curve in loop)
            {
                IList<XYZ> tessellated = curve.Tessellate();
                for (int i = 0; i < tessellated.Count; i++)
                {
                    XYZ point = tessellated[i];
                    if (i == 0 && points.Count > 0 && points[points.Count - 1].DistanceTo(point) < 1e-9)
                        continue;
                    points.Add(point);
                }
            }
            if (points.Count < 3) return 0;

            XYZ origin = points[0];
            double twiceArea = 0;
            for (int i = 0; i < points.Count; i++)
            {
                XYZ a = points[i] - origin;
                XYZ b = points[(i + 1) % points.Count] - origin;
                twiceArea += a.X * b.Y - b.X * a.Y;
            }
            return Math.Abs(twiceArea) / 2.0;
        }

        private static double LargestHorizontalTopFaceArea(GeometryElement geometry, double topZ, double tolerance)
        {
            if (geometry == null) return 0;
            double area = 0;
            foreach (GeometryObject item in geometry)
            {
                if (item is Solid solid && solid.Faces.Size > 0)
                {
                    foreach (Face face in solid.Faces)
                    {
                        PlanarFace planar = face as PlanarFace;
                        if (planar == null || planar.FaceNormal.Z < 0.995 || Math.Abs(planar.Origin.Z - topZ) > tolerance)
                            continue;
                        area = Math.Max(area, planar.Area);
                    }
                }
                else if (item is GeometryInstance instance)
                {
                    area = Math.Max(area, LargestHorizontalTopFaceArea(instance.GetInstanceGeometry(), topZ, tolerance));
                }
            }
            return area;
        }

        private static double GetTopFaceCoverage(FamilyInstance footing, FootingFrame frame)
        {
            double footprintArea = (frame.MaxX - frame.MinX) * (frame.MaxY - frame.MinY);
            double topArea = LargestHorizontalTopFaceArea(footing.get_Geometry(new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine
            }), frame.Top, UnitUtils.ConvertToInternalUnits(25, UnitTypeId.Millimeters));
            return footprintArea <= 0 ? 0 : topArea / footprintArea;
        }

        private static int CreateHorizontalSideTies(Document doc, FamilyInstance footing, FootingFrame frame,
            RebarBarType type, double minX, double maxX, double minY, double maxY,
            double bottomZ, double topZ, double maxSpacing, RebarHookType hookType, double shortCurveTolerance)
        {
            if (maxSpacing <= 0 || topZ - bottomZ <= shortCurveTolerance ||
                maxX - minX <= shortCurveTolerance || maxY - minY <= shortCurveTolerance)
                throw new InvalidOperationException("Cover/khoảng cách không tạo được đai ngang khép kín trong móng.");
            XYZ a = frame.Center + frame.X * minX + frame.Y * minY + XYZ.BasisZ * (bottomZ - frame.Center.Z);
            XYZ b = frame.Center + frame.X * maxX + frame.Y * minY + XYZ.BasisZ * (bottomZ - frame.Center.Z);
            XYZ c = frame.Center + frame.X * maxX + frame.Y * maxY + XYZ.BasisZ * (bottomZ - frame.Center.Z);
            XYZ d = frame.Center + frame.X * minX + frame.Y * maxY + XYZ.BasisZ * (bottomZ - frame.Center.Z);
            var curves = new List<Curve>
            {
                Line.CreateBound(a, b), Line.CreateBound(b, c),
                Line.CreateBound(c, d), Line.CreateBound(d, a)
            };
            Rebar tie = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.StirrupTie, type, hookType, hookType, footing,
                XYZ.BasisZ, curves, RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
            if (tie == null) throw new InvalidOperationException("Revit không tạo được đai ngang thành móng; kiểm tra RebarShape StirrupTie trong project.");
            using (RebarShapeDrivenAccessor accessor = tie.GetShapeDrivenAccessor())
                accessor.SetLayoutAsMaximumSpacing(maxSpacing, topZ - bottomZ, true, true, true);
            Parameter partition = tie.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
            if (partition != null && !partition.IsReadOnly)
                partition.Set("KSTR_FOOTING_" + footing.Id.Value + "_SIDE_H");
            return Math.Max(1, tie.NumberOfBarPositions);
        }

        private static int CreateHorizontalCStirrups(Document doc, FamilyInstance footing, FootingFrame frame,
            RebarBarType type, double minX, double maxX, double minY, double maxY,
            double bottomZ, double topZ, double maxSpacing, bool hookX, bool hookY,
            double hookLengthX, double hookLengthY, double shortCurveTolerance)
        {
            if (maxSpacing <= 0 || topZ - bottomZ <= shortCurveTolerance)
                throw new InvalidOperationException("Bước tối đa hoặc hình học đai C không hợp lệ.");
            var sides = new[]
            {
                new { A = new XYZ(minX, minY, 0), B = new XYZ(maxX, minY, 0), Inward = frame.Y, AlongX = true },
                new { A = new XYZ(maxX, minY, 0), B = new XYZ(maxX, maxY, 0), Inward = -frame.X, AlongX = false },
                new { A = new XYZ(maxX, maxY, 0), B = new XYZ(minX, maxY, 0), Inward = -frame.Y, AlongX = true },
                new { A = new XYZ(minX, maxY, 0), B = new XYZ(minX, minY, 0), Inward = frame.X, AlongX = false }
            };
            int created = 0;
            for (int sideIndex = 0; sideIndex < sides.Length; sideIndex++)
            {
                var side = sides[sideIndex];
                XYZ a = frame.Center + frame.X * side.A.X + frame.Y * side.A.Y + XYZ.BasisZ * (bottomZ - frame.Center.Z);
                XYZ b = frame.Center + frame.X * side.B.X + frame.Y * side.B.Y + XYZ.BasisZ * (bottomZ - frame.Center.Z);
                bool addReturns = side.AlongX ? hookX : hookY;
                double legLength = side.AlongX ? hookLengthX : hookLengthY;
                var curves = new List<Curve> { Line.CreateBound(a, b) };
                if (addReturns)
                {
                    XYZ c = b + side.Inward * legLength;
                    XYZ d = a + side.Inward * legLength;
                    curves.Add(Line.CreateBound(b, c));
                    curves.Add(Line.CreateBound(c, d));
                }
                Rebar bar = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.Standard, type, null, null, footing,
                    XYZ.BasisZ, curves, RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                if (bar == null) throw new InvalidOperationException("Revit không tạo được thanh C ngang thành móng.");
                using (RebarShapeDrivenAccessor accessor = bar.GetShapeDrivenAccessor())
                    accessor.SetLayoutAsMaximumSpacing(maxSpacing, topZ - bottomZ, true, true, true);
                Parameter partition = bar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
                if (partition != null && !partition.IsReadOnly)
                    partition.Set("KSTR_FOOTING_" + footing.Id.Value + "_SIDE_C_" + sideIndex);
                created += Math.Max(1, bar.NumberOfBarPositions);
            }
            return created;
        }

        private static FamilyInstance FindColumn(Document doc, FootingFrame frame)
        {
            double tolerance = UnitUtils.ConvertToInternalUnits(50, UnitTypeId.Millimeters);
            return new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralColumns)
                .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .Select(column => new { Column = column, Box = column.get_BoundingBox(null) })
                .Where(item => item.Box != null &&
                    item.Box.Min.Z <= frame.Top + tolerance && item.Box.Max.Z >= frame.Top - tolerance &&
                    frame.Center.X >= item.Box.Min.X && frame.Center.X <= item.Box.Max.X &&
                    frame.Center.Y >= item.Box.Min.Y && frame.Center.Y <= item.Box.Max.Y)
                .OrderBy(item => HorizontalDistance(item.Box, frame.Center))
                .Select(item => item.Column).FirstOrDefault();
        }

        private static double HorizontalDistance(BoundingBoxXYZ box, XYZ point)
        {
            double dx = Math.Max(box.Min.X - point.X, Math.Max(0, point.X - box.Max.X));
            double dy = Math.Max(box.Min.Y - point.Y, Math.Max(0, point.Y - box.Max.Y));
            return dx * dx + dy * dy;
        }

        private static int CreateColumnDowels(Document doc, FamilyInstance footing, FamilyInstance column,
            FootingFrame frame, RebarBarType type, double columnInset, double bottomZ, double anchorage, double projection)
        {
            if (anchorage <= type.BarNominalDiameter || projection <= 0)
                throw new InvalidOperationException("Chiều dài neo trong móng phải lớn hơn đường kính thép và chiều nhô phải dương.");
            BoundingBoxXYZ box = column.get_BoundingBox(null);
            LocationPoint location = column.Location as LocationPoint;
            if (box == null || location == null) throw new InvalidOperationException("Cột phải là FamilyInstance dạng điểm.");

            double angle = location.Rotation;
            XYZ axisX = new XYZ(Math.Cos(angle), Math.Sin(angle), 0).Normalize();
            XYZ axisY = new XYZ(-Math.Sin(angle), Math.Cos(angle), 0).Normalize();
            Transform boxTransform = box.Transform ?? Transform.Identity;
            var corners = new List<XYZ>();
            for (int ix = 0; ix < 2; ix++) for (int iy = 0; iy < 2; iy++) for (int iz = 0; iz < 2; iz++)
                corners.Add(boxTransform.OfPoint(new XYZ(ix == 0 ? box.Min.X : box.Max.X,
                    iy == 0 ? box.Min.Y : box.Max.Y, iz == 0 ? box.Min.Z : box.Max.Z)));
            double halfX = corners.Max(point => Math.Abs((point - location.Point).DotProduct(axisX))) - columnInset;
            double halfY = corners.Max(point => Math.Abs((point - location.Point).DotProduct(axisY))) - columnInset;
            if (halfX <= type.BarNominalDiameter || halfY <= type.BarNominalDiameter)
                throw new InvalidOperationException("Cover/thép không vừa tiết diện cột để bố trí bốn thanh chờ.");

            double topZ = frame.Top + projection;
            int created = 0;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            {
                XYZ xy = location.Point + axisX * (sx * halfX) + axisY * (sy * halfY);
                XYZ inward = (-axisX * sx - axisY * sy).Normalize();
                XYZ anchorXY = xy + inward * anchorage;
                XYZ relativeAnchor = anchorXY - frame.Center;
                double localX = relativeAnchor.DotProduct(frame.X);
                double localY = relativeAnchor.DotProduct(frame.Y);
                if (localX < frame.MinX + columnInset || localX > frame.MaxX - columnInset ||
                    localY < frame.MinY + columnInset || localY > frame.MaxY - columnInset)
                    throw new InvalidOperationException("Chiều neo đã nhập không vừa mặt bằng móng.");

                XYZ bend = new XYZ(xy.X, xy.Y, bottomZ);
                XYZ anchor = new XYZ(anchorXY.X, anchorXY.Y, bottomZ);
                XYZ top = new XYZ(xy.X, xy.Y, topZ);
                XYZ normal = inward.CrossProduct(XYZ.BasisZ).Normalize();
                var curves = new List<Curve> { Line.CreateBound(anchor, bend), Line.CreateBound(bend, top) };
                Rebar rebar = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.Standard, type, null, null, footing, normal,
                    curves, RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                if (rebar == null) throw new InvalidOperationException("Revit không tạo được thép chờ cột.");
                Parameter partition = rebar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
                if (partition != null && !partition.IsReadOnly)
                    partition.Set("KSTR_FOOTING_" + footing.Id.Value + "_DOWEL");
                created++;
            }
            return created;
        }

        private static int CreateSideVerticalBars(Document doc, FamilyInstance footing, FootingFrame frame,
            RebarBarType type, double minX, double maxX, double minY, double maxY, double bottom, double top, double spacingX, double spacingY)
        {
            int countY = Math.Max(2, (int)Math.Ceiling((maxY - minY) / spacingY) + 1);
            int countX = Math.Max(2, (int)Math.Ceiling((maxX - minX) / spacingX) + 1);
            if (2L * (countX + countY) - 4 > 1000) throw new InvalidOperationException("Khoảng cách tối đa yêu cầu tạo trên 1000 thanh đứng cạnh.");
            int created = 0;
            foreach (double x in new[] { minX, maxX })
            {
                XYZ start = frame.Center + frame.X * x + frame.Y * minY + XYZ.BasisZ * (bottom - frame.Center.Z);
                XYZ end = frame.Center + frame.X * x + frame.Y * minY + XYZ.BasisZ * (top - frame.Center.Z);
                created += CreateBarSet(doc, footing, type, null, frame.Y, start, end,
                    spacingY, maxY - minY, x == minX ? "SIDE_XMIN" : "SIDE_XMAX");
            }
            foreach (double y in new[] { minY, maxY })
            {
                XYZ start = frame.Center + frame.X * minX + frame.Y * y + XYZ.BasisZ * (bottom - frame.Center.Z);
                XYZ end = frame.Center + frame.X * minX + frame.Y * y + XYZ.BasisZ * (top - frame.Center.Z);
                created += CreateBarSet(doc, footing, type, null, frame.X, start, end,
                    spacingX, maxX - minX, y == minY ? "SIDE_YMIN" : "SIDE_YMAX", false, false);
            }
            return created;
        }

        private static int CreateMat(Document doc, FamilyInstance footing, FootingFrame frame, RebarBarType typeX, RebarBarType typeY,
            double minX, double maxX, double minY, double maxY, double spacingX, double spacingY, double centerZ, double gap, string layer,
            LayerHookOptions hooks)
        {
            double zX = centerZ - gap / 2.0, zY = centerZ + gap / 2.0;
            XYZ xStart = frame.Center + frame.X * minX + frame.Y * minY + XYZ.BasisZ * (zX - frame.Center.Z);
            XYZ xEnd = frame.Center + frame.X * maxX + frame.Y * minY + XYZ.BasisZ * (zX - frame.Center.Z);
            XYZ yStart = frame.Center + frame.X * minX + frame.Y * minY + XYZ.BasisZ * (zY - frame.Center.Z);
            XYZ yEnd = frame.Center + frame.X * minX + frame.Y * maxY + XYZ.BasisZ * (zY - frame.Center.Z);

            // One Rebar set per bar direction. "X" identifies bars parallel to X; spacingX
            // controls the distance between those bars across the Y span, and vice versa.
            int created = CreateBarSet(doc, footing, typeX, hooks?.HookX, frame.Y, xStart, xEnd,
                spacingX, maxY - minY, layer + "_X");
            created += CreateBarSet(doc, footing, typeY, hooks?.HookY, frame.X, yStart, yEnd,
                spacingY, maxX - minX, layer + "_Y");
            return created;
        }

        private static FootingFrame GetFrame(FamilyInstance footing)
        {
            LocationPoint location = footing.Location as LocationPoint;
            double angle = location == null ? 0 : location.Rotation;
            XYZ xAxis = new XYZ(Math.Cos(angle), Math.Sin(angle), 0).Normalize();
            XYZ yAxis = new XYZ(-Math.Sin(angle), Math.Cos(angle), 0).Normalize();

            // Select the cap by its family dimensions when available. The concrete blinding
            // layer may have a larger underside than the cap, but its 100 mm thickness must not
            // define the 700 mm footing frame. Pile solids are also excluded by their dimensions.
            double familyLength = GetFamilyDimension(footing, "Length", "Lendth");
            double familyWidth = GetFamilyDimension(footing, "Width");
            double foundationThickness = GetFamilyDimension(footing, "Foundation Thickness");
            var solids = new List<Solid>();
            GeometryElement geometry = footing.get_Geometry(new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = false,
                DetailLevel = ViewDetailLevel.Fine
            });
            CollectFootingSolids(geometry, solids);
            var candidates = solids.Select(solid =>
            {
                var solidPoints = new List<XYZ>();
                foreach (Edge edge in solid.Edges)
                    foreach (XYZ point in edge.AsCurve().Tessellate()) solidPoints.Add(point);
                double bottomArea = solid.Faces.Cast<Face>().OfType<PlanarFace>()
                    .Where(face => face.FaceNormal.Z <= -0.995)
                    .Select(face => face.Area).DefaultIfEmpty(0).Max();
                return new
                {
                    Solid = solid,
                    BottomArea = bottomArea,
                    Volume = Math.Abs(solid.Volume),
                    Length = solidPoints.Count == 0 ? 0 : solidPoints.Max(point => point.DotProduct(xAxis)) - solidPoints.Min(point => point.DotProduct(xAxis)),
                    Width = solidPoints.Count == 0 ? 0 : solidPoints.Max(point => point.DotProduct(yAxis)) - solidPoints.Min(point => point.DotProduct(yAxis)),
                    Thickness = solidPoints.Count == 0 ? 0 : solidPoints.Max(point => point.Z) - solidPoints.Min(point => point.Z)
                };
            }).Where(item => item.BottomArea > 0 && item.Volume > 1e-9).ToList();

            double dimensionTolerance = UnitUtils.ConvertToInternalUnits(25, UnitTypeId.Millimeters);
            Func<double, double, bool> dimensionMatches = (actual, expected) =>
                expected <= 0 || Math.Abs(actual - expected) <= dimensionTolerance;
            var capCandidate = candidates
                .Where(item => ((dimensionMatches(item.Length, familyLength) && dimensionMatches(item.Width, familyWidth)) ||
                    (dimensionMatches(item.Length, familyWidth) && dimensionMatches(item.Width, familyLength))) &&
                    dimensionMatches(item.Thickness, foundationThickness))
                .OrderByDescending(item => item.BottomArea)
                .ThenByDescending(item => item.Volume)
                .FirstOrDefault();

            var points = new List<XYZ>();
            double bottom, top;
            if (capCandidate != null)
            {
                foreach (Edge edge in capCandidate.Solid.Edges)
                    foreach (XYZ point in edge.AsCurve().Tessellate()) points.Add(point);
                bottom = points.Count == 0 ? 0 : points.Min(point => point.Z);
                top = points.Count == 0 ? 0 : points.Max(point => point.Z);
            }
            else if (familyLength > 0 && familyWidth > 0 && foundationThickness > 0)
            {
                // If the cap and piles are fused into one solid, its full Z extent includes the
                // pile shafts. Locate the cap from its broad upper face, then use the family
                // thickness to set the reinforcement elevation. This also skips blinding below.
                double footprintArea = familyLength * familyWidth;
                PlanarFace capTopFace = solids.SelectMany(solid => solid.Faces.Cast<Face>())
                    .OfType<PlanarFace>()
                    .Where(face => face.FaceNormal.Z >= 0.995 && face.Area >= footprintArea * 0.70)
                    .OrderByDescending(face => face.Origin.Z)
                    .ThenByDescending(face => face.Area)
                    .FirstOrDefault();
                if (capTopFace == null)
                    throw new InvalidOperationException("Không nhận diện được mặt trên thân đài theo Length/Width. Kiểm tra solid đài và parameter Foundation Thickness; các solid cọc sẽ được bỏ qua.");

                foreach (EdgeArray loop in capTopFace.EdgeLoops)
                    foreach (Edge edge in loop)
                        foreach (XYZ point in edge.AsCurve().Tessellate()) points.Add(point);
                top = capTopFace.Origin.Z;
                bottom = top - foundationThickness;
            }
            else
            {
                var fallback = candidates.OrderByDescending(item => item.BottomArea)
                    .ThenByDescending(item => item.Volume).FirstOrDefault();
                if (fallback == null)
                    throw new InvalidOperationException("Không tách được thân đài khỏi hình học cọc/bê tông lót. Family cần có solid đài hoặc các parameter Length, Width, Foundation Thickness.");
                foreach (Edge edge in fallback.Solid.Edges)
                    foreach (XYZ point in edge.AsCurve().Tessellate()) points.Add(point);
                bottom = points.Count == 0 ? 0 : points.Min(point => point.Z);
                top = points.Count == 0 ? 0 : points.Max(point => point.Z);
            }
            if (points.Count == 0) throw new InvalidOperationException("Không đọc được biên thân đài từ hình học hoặc mặt trên.");

            double minX = points.Min(point => point.DotProduct(xAxis));
            double maxX = points.Max(point => point.DotProduct(xAxis));
            double minY = points.Min(point => point.DotProduct(yAxis));
            double maxY = points.Max(point => point.DotProduct(yAxis));

            // LocationPoint.Rotation is not guaranteed to place the family Length axis on
            // model X. Compare the actual cap footprint against the Length/Width parameters;
            // if the family axes are exchanged, rotate the reinforcement frame by 90 degrees.
            if (familyLength > 0 && familyWidth > 0)
            {
                double spanX = maxX - minX;
                double spanY = maxY - minY;
                double directError = Math.Abs(spanX - familyLength) + Math.Abs(spanY - familyWidth);
                double swappedError = Math.Abs(spanX - familyWidth) + Math.Abs(spanY - familyLength);
                if (swappedError + dimensionTolerance < directError)
                {
                    XYZ previousX = xAxis;
                    xAxis = yAxis;
                    yAxis = -previousX;
                    minX = points.Min(point => point.DotProduct(xAxis));
                    maxX = points.Max(point => point.DotProduct(xAxis));
                    minY = points.Min(point => point.DotProduct(yAxis));
                    maxY = points.Max(point => point.DotProduct(yAxis));
                }
            }
            double centerX = (minX + maxX) / 2.0;
            double centerY = (minY + maxY) / 2.0;
            double centerZ = (bottom + top) / 2.0;

            // Keep the family dimensions for the reinforcement frame and preview. Geometry
            // extents are the fallback for footing families without these parameters.
            double length = familyLength > 0 ? familyLength : maxX - minX;
            double width = familyWidth > 0 ? familyWidth : maxY - minY;
            double thickness = foundationThickness > 0 ? foundationThickness : top - bottom;

            XYZ center = xAxis * centerX + yAxis * centerY + XYZ.BasisZ * centerZ;
            minX = -length / 2.0; maxX = length / 2.0;
            minY = -width / 2.0; maxY = width / 2.0;
            bottom = centerZ - thickness / 2.0;
            top = centerZ + thickness / 2.0;
            if (length <= 0 || width <= 0 || thickness <= 0)
                throw new InvalidOperationException("Mặt bằng thân đài không hợp lệ.");
            return new FootingFrame
            {
                Center = center, X = xAxis, Y = yAxis,
                MinX = minX, MaxX = maxX, MinY = minY, MaxY = maxY,
                Bottom = bottom, Top = top
            };
        }

        private static double GetFamilyDimension(FamilyInstance footing, params string[] parameterNames)
        {
            foreach (string name in parameterNames)
            {
                Parameter parameter = footing.LookupParameter(name);
                if ((parameter == null || !parameter.HasValue) && footing.Symbol != null)
                    parameter = footing.Symbol.LookupParameter(name);
                if (parameter != null && parameter.HasValue && parameter.StorageType == StorageType.Double)
                {
                    double value = parameter.AsDouble();
                    if (value > 0 && !double.IsNaN(value) && !double.IsInfinity(value))
                        return value;
                }
            }
            return 0;
        }

        private static void CollectFootingSolids(GeometryElement geometry, IList<Solid> solids)
        {
            if (geometry == null) return;
            foreach (GeometryObject item in geometry)
            {
                if (item is Solid solid && solid.Faces.Size > 0)
                {
                    solids.Add(solid);
                }
                else if (item is GeometryInstance instance)
                {
                    CollectFootingSolids(instance.GetInstanceGeometry(), solids);
                }
            }
        }

        private static int CreateBarSet(Document doc, FamilyInstance footing, RebarBarType type,
            RebarHookType hookType, XYZ distributionNormal, XYZ start, XYZ end,
            double maxSpacing, double arrayLength, string partitionSuffix,
            bool includeFirstBar = true, bool includeLastBar = true)
        {
            Rebar rebar = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.Standard, type, hookType, hookType, footing,
                distributionNormal, new List<Curve> { Line.CreateBound(start, end) },
                RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
            if (rebar == null) throw new InvalidOperationException("Revit không tạo được Rebar set phương " + partitionSuffix + ".");
            using (RebarShapeDrivenAccessor accessor = rebar.GetShapeDrivenAccessor())
                accessor.SetLayoutAsMaximumSpacing(maxSpacing, arrayLength, true, includeFirstBar, includeLastBar);
            Parameter partition = rebar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
            if (partition != null && !partition.IsReadOnly)
                partition.Set("KSTR_FOOTING_" + footing.Id.Value + "_" + partitionSuffix);
            return Math.Max(1, rebar.NumberOfBarPositions);
        }

        private static bool IsFooting(FamilyInstance instance)
        {
            return instance != null && instance.Category != null &&
                instance.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation;
        }

        private static BarOption ResolveBarOption(System.Windows.Controls.ComboBox combo, IList<BarOption> choices)
        {
            string input = (combo.Text ?? string.Empty).Trim();
            BarOption selected = combo.SelectedItem as BarOption;
            if (string.IsNullOrWhiteSpace(input) && selected != null) return selected;

            BarOption exact = choices.FirstOrDefault(option =>
                string.Equals(option.Label, input, StringComparison.CurrentCultureIgnoreCase) ||
                string.Equals(option.Type.Name, input, StringComparison.CurrentCultureIgnoreCase));
            if (exact != null) return exact;

            if (selected != null && string.Equals(selected.Label, input, StringComparison.CurrentCultureIgnoreCase))
                return selected;

            string numericText = input.Replace("Ø", string.Empty).Replace("ø", string.Empty)
                .Replace("mm", string.Empty).Replace("MM", string.Empty).Trim();
            if (double.TryParse(numericText, NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.CurrentCulture, out double diameterMm) ||
                double.TryParse(numericText, NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture, out diameterMm))
            {
                BarOption match = choices.FirstOrDefault(option =>
                    Math.Abs(UnitUtils.ConvertFromInternalUnits(option.Type.BarNominalDiameter, UnitTypeId.Millimeters) - diameterMm) < 0.01);
                if (match != null) return match;
                throw new InvalidOperationException("Project chưa có RebarBarType đường kính Ø" +
                    diameterMm.ToString("0.##", CultureInfo.CurrentCulture) + " mm. Hãy chọn type có sẵn trong danh sách hoặc nạp type này vào project.");
            }

            throw new InvalidOperationException("Không nhận diện được đường kính/type thép '" + input +
                "'. Chọn type trong danh sách hoặc nhập đường kính có RebarBarType tương ứng, ví dụ 12 hoặc Ø12.");
        }

        private static OptionsData ChooseOptions(IntPtr owner, IList<RebarBarType> types, IList<RebarHookType> hookTypes, FootingFrame previewFrame)
        {
            List<BarOption> choices = types.Select(t => new BarOption(t))
                .OrderBy(option => option.Type.BarNominalDiameter)
                .ThenBy(option => option.Type.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var window = new Window
            {
                Title = "Thép móng / đài cọc - KStruct", Width = 1120, Height = 840,
                MinWidth = 980, MinHeight = 700, ResizeMode = ResizeMode.CanResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false
            };
            ApplyRebarWindowTheme(window);
            var root = new StackPanel { Margin = new Thickness(8) };
            var basicPanel = new StackPanel();
            var comboX = new System.Windows.Controls.ComboBox
            {
                MinWidth = 0, MinHeight = 30, DisplayMemberPath = "Label", ItemsSource = choices, SelectedIndex = 0,
                IsEditable = true, IsTextSearchEnabled = true, StaysOpenOnEdit = true,
                ToolTip = "Chọn type có sẵn hoặc nhập đường kính (mm), ví dụ 12 hoặc Ø12."
            };
            var comboY = new System.Windows.Controls.ComboBox
            {
                MinWidth = 0, MinHeight = 30, DisplayMemberPath = "Label", ItemsSource = choices, SelectedIndex = 0,
                IsEditable = true, IsTextSearchEnabled = true, StaysOpenOnEdit = true,
                ToolTip = "Chọn type có sẵn hoặc nhập đường kính (mm), ví dụ 12 hoặc Ø12."
            };
            var diameterGrid = CreateFieldGrid(2);
            AddGridField(diameterGrid, 0, "Đường kính thép X", comboX);
            AddGridField(diameterGrid, 1, "Đường kính thép Y", comboY);
            basicPanel.Children.Add(diameterGrid);

            var coverGrid = CreateFieldGrid(3);
            var coverBottom = AddGridInput(coverGrid, 0, "Cover đáy (mm)", "50");
            var coverTop = AddGridInput(coverGrid, 1, "Cover trên (mm)", "50");
            var coverSide = AddGridInput(coverGrid, 2, "Cover cạnh (mm)", "50");
            basicPanel.Children.Add(new TextBlock { Text = "LỚP BẢO VỆ", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 1) });
            basicPanel.Children.Add(coverGrid);

            var spacingGrid = CreateFieldGrid(2);
            var spacingX = AddGridInput(spacingGrid, 0, "Bước tối đa thép X (mm)", "150");
            var spacingY = AddGridInput(spacingGrid, 1, "Bước tối đa thép Y (mm)", "150");
            basicPanel.Children.Add(new TextBlock { Text = "BƯỚC RẢI LƯỚI", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 1) });
            basicPanel.Children.Add(spacingGrid);
            basicPanel.Children.Add(new TextBlock { Text = "Mỗi phương tạo một Rebar set; khoảng cách thực tế không vượt bước tối đa.",
                FontSize = 10, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(112, 128, 148)),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            root.Children.Add(CreateSettingsCard("THÉP CHỦ & LỚP BẢO VỆ", "Đường kính, cover và bước rải theo nhóm", basicPanel));

            var layerPanel = new StackPanel();
            root.Children.Add(CreateSettingsCard("CÁC LỚP LƯỚI", "Bật đúng lớp cần bố trí", layerPanel));
            var bottom = new CheckBox { Content = "Lớp đáy", IsChecked = true, Margin = new Thickness(0, 7, 0, 2) };
            var middle = new CheckBox { Content = "Lớp giữa", IsChecked = false, Margin = new Thickness(0, 0, 0, 2) };
            var top = new CheckBox { Content = "Lớp trên", IsChecked = false, Margin = new Thickness(0, 0, 0, 4) };
            layerPanel.Children.Add(bottom);
            layerPanel.Children.Add(middle);
            layerPanel.Children.Add(top);
            LayerHookControls bottomHooks = AddLayerHookControls(layerPanel, "Móc lớp đáy", hookTypes);
            LayerHookControls middleHooks = AddLayerHookControls(layerPanel, "Móc lớp giữa", hookTypes);
            LayerHookControls topHooks = AddLayerHookControls(layerPanel, "Móc lớp trên", hookTypes);

            var detailPanel = new StackPanel();
            root.Children.Add(CreateSettingsCard("THÉP CẠNH, ĐAI & THÉP CHỜ", "Cấu tạo bổ sung quanh thành đài và chân cột", detailPanel));
            var sides = new CheckBox { Content = "Thép đứng quanh cạnh móng", IsChecked = false, Margin = new Thickness(0, 0, 0, 4) };
            var horizontalSides = new CheckBox { Content = "Đai ngang khép kín quanh thành (dùng thép X)", IsChecked = false, Margin = new Thickness(0, 0, 0, 2) };
            var oHookChoices = new List<HookOption> { new HookOption(null) };
            oHookChoices.AddRange(hookTypes.Select(type => new HookOption(type)));
            var oHookCombo = new System.Windows.Controls.ComboBox
            {
                MinWidth = 380, MinHeight = 22, DisplayMemberPath = "Name", ItemsSource = oHookChoices,
                SelectedIndex = 0, IsEnabled = false
            };
            var horizontalCStirrups = new CheckBox { Content = "Tạo đai C theo bốn cạnh thành móng", IsChecked = false, Margin = new Thickness(0, 0, 0, 2) };
            var dowels = new CheckBox { Content = "Tạo 4 thép chờ tại góc cột (neo xiên trong móng)", IsChecked = false, Margin = new Thickness(0, 0, 0, 4) };



            detailPanel.Children.Add(sides);
            detailPanel.Children.Add(horizontalSides);
            detailPanel.Children.Add(new TextBlock { Text = "LOẠI MÓC CHO ĐAI O", FontWeight = FontWeights.SemiBold });
            detailPanel.Children.Add(oHookCombo);
            horizontalSides.Checked += (sender, args) => oHookCombo.IsEnabled = true;
            horizontalSides.Unchecked += (sender, args) => { oHookCombo.IsEnabled = false; oHookCombo.SelectedIndex = 0; };
            var horizontalQuantity = AddInput(detailPanel, "Bước tối đa đai theo chiều cao (mm):", "150");
            horizontalQuantity.IsEnabled = false;
            Action updateHorizontalQuantityState = () =>
                horizontalQuantity.IsEnabled = horizontalSides.IsChecked == true || horizontalCStirrups.IsChecked == true;
            horizontalSides.Checked += (sender, args) => updateHorizontalQuantityState();
            horizontalSides.Unchecked += (sender, args) => updateHorizontalQuantityState();
            horizontalCStirrups.Checked += (sender, args) => updateHorizontalQuantityState();
            horizontalCStirrups.Unchecked += (sender, args) => updateHorizontalQuantityState();
            detailPanel.Children.Add(horizontalCStirrups);
            var cHookX = new CheckBox { Content = "Trả đầu đai C ở hai cạnh theo X", IsChecked = false };
            var cHookY = new CheckBox { Content = "Trả đầu đai C ở hai cạnh theo Y", IsChecked = false };
            var cReturnsGrid = CreateFieldGrid(2);
            var cReturnXPanel = new StackPanel();
            cReturnXPanel.Children.Add(cHookX);
            cReturnXPanel.Children.Add(new TextBlock { Text = "Chiều dài nhánh X (mm)", FontSize = 10, Margin = new Thickness(0, 3, 0, 1) });
            var cLegX = new System.Windows.Controls.TextBox { MinWidth = 0, MinHeight = 29, Text = "300" };
            cReturnXPanel.Children.Add(cLegX);
            var cReturnYPanel = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            cReturnYPanel.Children.Add(cHookY);
            cReturnYPanel.Children.Add(new TextBlock { Text = "Chiều dài nhánh Y (mm)", FontSize = 10, Margin = new Thickness(0, 3, 0, 1) });
            var cLegY = new System.Windows.Controls.TextBox { MinWidth = 0, MinHeight = 29, Text = "300" };
            cReturnYPanel.Children.Add(cLegY);
            System.Windows.Controls.Grid.SetColumn(cReturnXPanel, 0);
            System.Windows.Controls.Grid.SetColumn(cReturnYPanel, 1);
            cReturnsGrid.Children.Add(cReturnXPanel);
            cReturnsGrid.Children.Add(cReturnYPanel);
            detailPanel.Children.Add(cReturnsGrid);
            cHookX.IsEnabled = cLegX.IsEnabled = cHookY.IsEnabled = cLegY.IsEnabled = false;
            horizontalCStirrups.Checked += (sender, args) =>
            {
                cHookX.IsEnabled = cHookY.IsEnabled = true;
                cLegX.IsEnabled = cHookX.IsChecked == true;
                cLegY.IsEnabled = cHookY.IsChecked == true;
            };
            horizontalCStirrups.Unchecked += (sender, args) =>
            {
                cHookX.IsEnabled = cHookY.IsEnabled = cLegX.IsEnabled = cLegY.IsEnabled = false;
            };
            cHookX.Checked += (sender, args) => cLegX.IsEnabled = horizontalCStirrups.IsChecked == true;
            cHookX.Unchecked += (sender, args) => cLegX.IsEnabled = false;
            cHookY.Checked += (sender, args) => cLegY.IsEnabled = horizontalCStirrups.IsChecked == true;
            cHookY.Unchecked += (sender, args) => cLegY.IsEnabled = false;
            detailPanel.Children.Add(dowels);
            var dowelGrid = CreateFieldGrid(2);
            var anchorage = AddGridInput(dowelGrid, 0, "Neo trong móng (mm)", "400");
            var projection = AddGridInput(dowelGrid, 1, "Nhô trên đỉnh móng (mm)", "600");
            detailPanel.Children.Add(dowelGrid);
            anchorage.IsEnabled = projection.IsEnabled = false;
            dowels.Checked += (sender, args) => anchorage.IsEnabled = projection.IsEnabled = true;
            dowels.Unchecked += (sender, args) => anchorage.IsEnabled = projection.IsEnabled = false;

            var profileName = new System.Windows.Controls.TextBox { MinWidth = 180, MinHeight = 23 };
            var profileBox = new System.Windows.Controls.ComboBox
            {
                ItemsSource = FootingRebarProfileStore.Load(), DisplayMemberPath = "Name", MinWidth = 180, MinHeight = 23
            };
            var profilePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 2) };
            profilePanel.Children.Add(new TextBlock { Text = "Tên preset:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
            profilePanel.Children.Add(profileName);
            var profileSettings = new StackPanel();
            root.Children.Add(CreateSettingsCard("PRESET CẤU HÌNH", "Lưu, nạp hoặc trao đổi thông số", profileSettings));
            profileSettings.Children.Add(profilePanel);
            var profileButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 4) };
            var loadProfile = new Button { Content = "Nạp preset", MinWidth = 82, Margin = new Thickness(0, 0, 5, 0) };
            var saveProfile = new Button { Content = "Lưu preset", MinWidth = 82, Margin = new Thickness(0, 0, 5, 0) };
            var deleteProfile = new Button { Content = "Xóa preset", MinWidth = 82, Margin = new Thickness(0, 0, 8, 0) };
            profileButtons.Children.Add(loadProfile);
            profileButtons.Children.Add(saveProfile);
            profileButtons.Children.Add(deleteProfile);
            profileButtons.Children.Add(profileBox);
            profileSettings.Children.Add(profileButtons);
            var profileTransferButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
            var exportProfiles = new Button { Content = "Xuất bộ preset XML", MinWidth = 120, Margin = new Thickness(0, 0, 6, 0) };
            var importProfiles = new Button { Content = "Nhập bộ preset XML", MinWidth = 120 };
            profileTransferButtons.Children.Add(exportProfiles);
            profileTransferButtons.Children.Add(importProfiles);
            profileSettings.Children.Add(profileTransferButtons);

            exportProfiles.Click += (sender, args) =>
            {
                List<FootingRebarProfile> saved = FootingRebarProfileStore.Load();
                if (saved.Count == 0)
                {
                    TaskDialog.Show("KStruct - Preset móng", "Chưa có preset nào để xuất.");
                    return;
                }
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Xuất preset cốt thép móng đơn",
                    Filter = "KStruct footing presets (*.xml)|*.xml",
                    FileName = "KStruct_FootingProfiles.xml",
                    AddExtension = true,
                    DefaultExt = ".xml",
                    OverwritePrompt = true
                };
                new WindowInteropHelper(window).Owner = owner;
                if (dialog.ShowDialog(window) != true) return;
                try
                {
                    FootingRebarProfileStore.Export(dialog.FileName, saved);
                    TaskDialog.Show("KStruct - Preset móng", "Đã xuất " + saved.Count + " preset ra file XML.");
                }
                catch (Exception ex) { TaskDialog.Show("KStruct - Preset móng", ex.Message); }
            };
            importProfiles.Click += (sender, args) =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Nhập preset cốt thép móng đơn",
                    Filter = "KStruct footing presets (*.xml)|*.xml|All files (*.*)|*.*",
                    Multiselect = false,
                    CheckFileExists = true
                };
                if (dialog.ShowDialog(window) != true) return;
                try
                {
                    List<FootingRebarProfile> imported = FootingRebarProfileStore.Import(dialog.FileName);
                    if (imported.Count == 0) throw new InvalidOperationException("File không chứa preset hợp lệ.");
                    List<FootingRebarProfile> merged = FootingRebarProfileStore.Load();
                    foreach (FootingRebarProfile item in imported)
                    {
                        merged.RemoveAll(existing => string.Equals(existing.Name, item.Name, StringComparison.OrdinalIgnoreCase));
                        merged.Add(item);
                    }
                    FootingRebarProfileStore.Save(merged);
                    profileBox.ItemsSource = FootingRebarProfileStore.Load();
                    TaskDialog.Show("KStruct - Preset móng", "Đã nhập " + imported.Count + " preset. Preset trùng tên được cập nhật.");
                }
                catch (Exception ex) { TaskDialog.Show("KStruct - Preset móng", "Không đọc được file preset: " + ex.Message); }
            };

            loadProfile.Click += (sender, args) =>
            {
                FootingRebarProfile profile = profileBox.SelectedItem as FootingRebarProfile;
                if (profile == null) return;
                BarOption xChoice = choices.FirstOrDefault(choice => string.Equals(choice.Type.Name, profile.TypeXName, StringComparison.OrdinalIgnoreCase));
                BarOption yChoice = choices.FirstOrDefault(choice => string.Equals(choice.Type.Name, profile.TypeYName, StringComparison.OrdinalIgnoreCase));
                if (xChoice == null || yChoice == null)
                {
                    TaskDialog.Show("KStruct - Preset móng", "Không tìm thấy loại thép X/Y đã lưu trong project hiện tại.");
                    return;
                }
                if (!HookAvailable(profile.BottomHookXName, hookTypes) || !HookAvailable(profile.BottomHookYName, hookTypes) ||
                    !HookAvailable(profile.MiddleHookXName, hookTypes) || !HookAvailable(profile.MiddleHookYName, hookTypes) ||
                    !HookAvailable(profile.TopHookXName, hookTypes) || !HookAvailable(profile.TopHookYName, hookTypes) ||
                    (!string.IsNullOrWhiteSpace(profile.OStirrupHookTypeName) &&
                        !hookTypes.Any(type => string.Equals(type.Name, profile.OStirrupHookTypeName, StringComparison.OrdinalIgnoreCase))))
                {
                    TaskDialog.Show("KStruct - Preset móng", "Preset đang dùng RebarHookType không có trong project hiện tại.");
                    return;
                }
                comboX.SelectedItem = xChoice; comboY.SelectedItem = yChoice;
                coverBottom.Text = FormatMm(profile.CoverBottomMm); coverTop.Text = FormatMm(profile.CoverTopMm);
                coverSide.Text = FormatMm(profile.CoverSideMm); spacingX.Text = FormatMm(profile.SpacingXmm); spacingY.Text = FormatMm(profile.SpacingYmm);
                bottom.IsChecked = profile.Bottom; middle.IsChecked = profile.Middle; top.IsChecked = profile.Top;
                sides.IsChecked = profile.SideVertical; dowels.IsChecked = profile.ColumnDowels;
                horizontalSides.IsChecked = profile.SideHorizontal;
                oHookCombo.SelectedItem = oHookChoices.FirstOrDefault(choice =>
                    string.IsNullOrWhiteSpace(profile.OStirrupHookTypeName) ? choice.Type == null :
                    choice.Type != null && string.Equals(choice.Type.Name, profile.OStirrupHookTypeName, StringComparison.OrdinalIgnoreCase));
                double legacySpacing = UnitUtils.ConvertFromInternalUnits(previewFrame.Top - previewFrame.Bottom, UnitTypeId.Millimeters) /
                    Math.Max(2, (profile.HorizontalLayerQuantity > 0 ? profile.HorizontalLayerQuantity : 3) + 1);
                horizontalQuantity.Text = FormatMm(profile.HorizontalMaxSpacingMm > 0 ? profile.HorizontalMaxSpacingMm : legacySpacing);
                horizontalCStirrups.IsChecked = profile.SideCStirrups;
                cHookX.IsChecked = profile.CStirrupHookX;
                cHookY.IsChecked = profile.CStirrupHookY;
                cLegX.Text = FormatMm(profile.CStirrupLegXmm > 0 ? profile.CStirrupLegXmm : 300);
                cLegY.Text = FormatMm(profile.CStirrupLegYmm > 0 ? profile.CStirrupLegYmm : 300);
                anchorage.Text = FormatMm(profile.DowelAnchorageMm); projection.Text = FormatMm(profile.DowelProjectionMm);
                ApplyHookProfile(bottomHooks, profile.BottomHookXName, profile.BottomHookXLengthMm,
                    profile.BottomHookYName, profile.BottomHookYLengthMm, hookTypes);
                ApplyHookProfile(middleHooks, profile.MiddleHookXName, profile.MiddleHookXLengthMm,
                    profile.MiddleHookYName, profile.MiddleHookYLengthMm, hookTypes);
                ApplyHookProfile(topHooks, profile.TopHookXName, profile.TopHookXLengthMm,
                    profile.TopHookYName, profile.TopHookYLengthMm, hookTypes);
                profileName.Text = profile.Name;
            };
            saveProfile.Click += (sender, args) =>
            {
                string name = (profileName.Text ?? string.Empty).Trim();
                if (name.Length == 0 || comboX.SelectedItem == null || comboY.SelectedItem == null)
                {
                    TaskDialog.Show("KStruct - Preset móng", "Nhập tên preset và chọn loại thép X/Y.");
                    return;
                }
                double cb, ct, cs, sx, sy, anch, proj, parsedCLegX = 0, parsedCLegY = 0;
                double horizontalMaxSpacing;
                if (!double.TryParse(coverBottom.Text, out cb) || !double.TryParse(coverTop.Text, out ct) ||
                    !double.TryParse(coverSide.Text, out cs) || !double.TryParse(spacingX.Text, out sx) ||
                    !double.TryParse(spacingY.Text, out sy) || !double.TryParse(anchorage.Text, out anch) ||
                    !double.TryParse(projection.Text, out proj) || cb <= 0 || ct <= 0 || cs <= 0 || sx <= 0 || sy <= 0 ||
                    ((horizontalSides.IsChecked == true || horizontalCStirrups.IsChecked == true) && (!double.TryParse(horizontalQuantity.Text, out horizontalMaxSpacing) || horizontalMaxSpacing <= 0)) ||
                    (cHookX.IsChecked == true && (!double.TryParse(cLegX.Text, out parsedCLegX) || parsedCLegX <= 0)) ||
                    (cHookY.IsChecked == true && (!double.TryParse(cLegY.Text, out parsedCLegY) || parsedCLegY <= 0)) ||
                    !HooksAreValid(bottom.IsChecked == true, bottomHooks) || !HooksAreValid(middle.IsChecked == true, middleHooks) ||
                    !HooksAreValid(top.IsChecked == true, topHooks))
                {
                    TaskDialog.Show("KStruct - Preset móng", "Thông số hiện tại không hợp lệ để lưu.");
                    return;
                }
                var profile = new FootingRebarProfile
                {
                    Name = name, TypeXName = ((BarOption)comboX.SelectedItem).Type.Name, TypeYName = ((BarOption)comboY.SelectedItem).Type.Name,
                    CoverBottomMm = cb, CoverTopMm = ct, CoverSideMm = cs, SpacingXmm = sx, SpacingYmm = sy,
                    Bottom = bottom.IsChecked == true, Middle = middle.IsChecked == true, Top = top.IsChecked == true,
                    SideVertical = sides.IsChecked == true, SideHorizontal = horizontalSides.IsChecked == true,
                    HorizontalMaxSpacingMm = double.TryParse(horizontalQuantity.Text, out double savedMaxSpacing) ? savedMaxSpacing : 150,
                    OStirrupHookTypeName = ((HookOption)oHookCombo.SelectedItem)?.Type?.Name,
                    SideCStirrups = horizontalCStirrups.IsChecked == true,
                    CStirrupHookX = cHookX.IsChecked == true, CStirrupHookY = cHookY.IsChecked == true,
                    CStirrupLegXmm = double.TryParse(cLegX.Text, out double savedCLegX) ? savedCLegX : 300,
                    CStirrupLegYmm = double.TryParse(cLegY.Text, out double savedCLegY) ? savedCLegY : 300,
                    ColumnDowels = dowels.IsChecked == true,
                    DowelAnchorageMm = anch, DowelProjectionMm = proj
                };
                CaptureHookProfile(bottomHooks, out string bottomXName, out double bottomXLength,
                    out string bottomYName, out double bottomYLength);
                profile.BottomHookXName = bottomXName; profile.BottomHookXLengthMm = bottomXLength;
                profile.BottomHookYName = bottomYName; profile.BottomHookYLengthMm = bottomYLength;
                CaptureHookProfile(middleHooks, out string middleXName, out double middleXLength,
                    out string middleYName, out double middleYLength);
                profile.MiddleHookXName = middleXName; profile.MiddleHookXLengthMm = middleXLength;
                profile.MiddleHookYName = middleYName; profile.MiddleHookYLengthMm = middleYLength;
                CaptureHookProfile(topHooks, out string topXName, out double topXLength,
                    out string topYName, out double topYLength);
                profile.TopHookXName = topXName; profile.TopHookXLengthMm = topXLength;
                profile.TopHookYName = topYName; profile.TopHookYLengthMm = topYLength;
                List<FootingRebarProfile> saved = FootingRebarProfileStore.Load();
                saved.RemoveAll(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
                saved.Add(profile);
                FootingRebarProfileStore.Save(saved);
                profileBox.ItemsSource = FootingRebarProfileStore.Load();
                profileBox.SelectedItem = ((IEnumerable<FootingRebarProfile>)profileBox.ItemsSource)
                    .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
                TaskDialog.Show("KStruct - Preset móng", "Đã lưu preset '" + name + "' trong %APPDATA%\\KStruct.");
            };
            deleteProfile.Click += (sender, args) =>
            {
                FootingRebarProfile selected = profileBox.SelectedItem as FootingRebarProfile;
                if (selected == null) return;
                TaskDialogResult answer = TaskDialog.Show("KStruct - Preset móng",
                    "Xóa preset '" + selected.Name + "'?", TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);
                if (answer != TaskDialogResult.Yes) return;
                List<FootingRebarProfile> saved = FootingRebarProfileStore.Load();
                saved.RemoveAll(item => string.Equals(item.Name, selected.Name, StringComparison.OrdinalIgnoreCase));
                FootingRebarProfileStore.Save(saved);
                profileBox.ItemsSource = FootingRebarProfileStore.Load();
                profileName.Clear();
            };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "Tạo thép  ▶", Width = 125, MinHeight = 38, IsDefault = true, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 112, 184)), BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(27, 90, 151)), Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) =>
            {
                double cb, ct, cs, sx, sy, a = 0, p = 0, parsedCLegX = 0, parsedCLegY = 0;
                double horizontalMaxSpacing;
                BarOption selectedX, selectedY;
                try
                {
                    selectedX = ResolveBarOption(comboX, choices);
                    selectedY = ResolveBarOption(comboY, choices);
                }
                catch (InvalidOperationException ex)
                {
                    TaskDialog.Show("KStruct - Đường kính thép", ex.Message);
                    return;
                }
                if (selectedX == null || selectedY == null ||
                    !double.TryParse(coverBottom.Text, out cb) || cb <= 0 ||
                    !double.TryParse(coverTop.Text, out ct) || ct <= 0 ||
                    !double.TryParse(coverSide.Text, out cs) || cs <= 0 ||
                    !double.TryParse(spacingX.Text, out sx) || sx <= 0 ||
                    !double.TryParse(spacingY.Text, out sy) || sy <= 0 ||
                    ((horizontalSides.IsChecked == true || horizontalCStirrups.IsChecked == true) && (!double.TryParse(horizontalQuantity.Text, out horizontalMaxSpacing) || horizontalMaxSpacing <= 0)) ||
                    (cHookX.IsChecked == true && (!double.TryParse(cLegX.Text, out parsedCLegX) || parsedCLegX <= 0)) ||
                    (cHookY.IsChecked == true && (!double.TryParse(cLegY.Text, out parsedCLegY) || parsedCLegY <= 0)) ||
                    !HooksAreValid(bottom.IsChecked == true, bottomHooks) ||
                    !HooksAreValid(middle.IsChecked == true, middleHooks) ||
                    !HooksAreValid(top.IsChecked == true, topHooks) ||
                    (bottom.IsChecked != true && middle.IsChecked != true && top.IsChecked != true && sides.IsChecked != true && horizontalSides.IsChecked != true && horizontalCStirrups.IsChecked != true && dowels.IsChecked != true) ||
                    (dowels.IsChecked == true && (!double.TryParse(anchorage.Text, out a) || a <= 0 ||
                        !double.TryParse(projection.Text, out p) || p <= 0)))
                {
                    TaskDialog.Show("KStruct - Thép móng đơn", "Cover/khoảng cách theo hai phương phải lớn hơn 0; chiều neo/nhô phải dương và chọn ít nhất một cấu tạo.");
                    return;
                }
                comboX.SelectedItem = selectedX;
                comboY.SelectedItem = selectedY;
                comboX.Text = selectedX.Label;
                comboY.Text = selectedY.Label;
                window.DialogResult = true;
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            var preview = new PileCapRebarPreviewControl();
            Action redrawPreview = () => preview.Update(
                UnitUtils.ConvertFromInternalUnits(previewFrame.MaxX - previewFrame.MinX, UnitTypeId.Millimeters),
                UnitUtils.ConvertFromInternalUnits(previewFrame.MaxY - previewFrame.MinY, UnitTypeId.Millimeters),
                UnitUtils.ConvertFromInternalUnits(previewFrame.Top - previewFrame.Bottom, UnitTypeId.Millimeters),
                ParsePreviewMm(coverBottom.Text, 50), ParsePreviewMm(coverTop.Text, 50),
                ParsePreviewMm(coverSide.Text, 50), ParsePreviewMm(spacingX.Text, 150), ParsePreviewMm(spacingY.Text, 150),
                bottom.IsChecked == true, middle.IsChecked == true, top.IsChecked == true,
                sides.IsChecked == true, horizontalSides.IsChecked == true, horizontalCStirrups.IsChecked == true,
                dowels.IsChecked == true,
                comboX.SelectedItem is BarOption bx ? UnitUtils.ConvertFromInternalUnits(bx.Type.BarNominalDiameter, UnitTypeId.Millimeters) : 12,
                comboY.SelectedItem is BarOption by ? UnitUtils.ConvertFromInternalUnits(by.Type.BarNominalDiameter, UnitTypeId.Millimeters) : 12,
                ParsePreviewMm(anchorage.Text, 400), ParsePreviewMm(projection.Text, 600));
            foreach (var input in new[] { coverBottom, coverTop, coverSide, spacingX, spacingY, anchorage, projection })
                input.TextChanged += (sender, args) => redrawPreview();
            comboX.SelectionChanged += (sender, args) => redrawPreview();
            comboY.SelectionChanged += (sender, args) => redrawPreview();
            foreach (var toggle in new[] { bottom, middle, top, sides, horizontalSides, horizontalCStirrups, dowels })
            {
                toggle.Checked += (sender, args) => redrawPreview();
                toggle.Unchecked += (sender, args) => redrawPreview();
            }

            var contentGrid = new System.Windows.Controls.Grid { Margin = new Thickness(12) };
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            contentGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(500) });
            contentGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var banner = new Border
            {
                CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 12, 18, 12), Margin = new Thickness(0, 0, 0, 8),
                Background = new System.Windows.Media.LinearGradientBrush(
                    System.Windows.Media.Color.FromRgb(20, 47, 84), System.Windows.Media.Color.FromRgb(39, 108, 171), 0)
            };
            var bannerContent = new System.Windows.Controls.Grid();
            bannerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bannerContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var bannerText = new StackPanel();
            bannerText.Children.Add(new TextBlock { Text = "KSTRUCT  /  KẾT CẤU", FontSize = 10, FontWeight = FontWeights.Bold,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(157, 202, 245)) });
            bannerText.Children.Add(new TextBlock { Text = "BỐ TRÍ THÉP MÓNG & ĐÀI CỌC", FontSize = 19, FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 2, 0, 0) });
            bannerText.Children.Add(new TextBlock { Text = "Thiết lập thông số · xem trước trực tiếp · tạo Rebar", FontSize = 11,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(215, 228, 242)), Margin = new Thickness(0, 2, 0, 0) });
            bannerContent.Children.Add(bannerText);
            var livePill = new Border { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 247, 240)),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 6, 10, 6), VerticalAlignment = VerticalAlignment.Center };
            livePill.Child = new TextBlock { Text = "●  LIVE PREVIEW", FontSize = 10, FontWeight = FontWeights.Bold,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 125, 82)) };
            System.Windows.Controls.Grid.SetColumn(livePill, 1);
            bannerContent.Children.Add(livePill);
            banner.Child = bannerContent;
            System.Windows.Controls.Grid.SetRow(banner, 0);
            System.Windows.Controls.Grid.SetColumnSpan(banner, 2);
            contentGrid.Children.Add(banner);
            var settingsScroll = new ScrollViewer
            {
                Content = root,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            System.Windows.Controls.Grid.SetColumn(settingsScroll, 0);
            System.Windows.Controls.Grid.SetRow(settingsScroll, 1);
            contentGrid.Children.Add(settingsScroll);
            var previewBorder = new Border
            {
                Margin = new Thickness(10, 4, 4, 4), Padding = new Thickness(10),
                Background = System.Windows.Media.Brushes.WhiteSmoke,
                BorderBrush = System.Windows.Media.Brushes.LightGray, BorderThickness = new Thickness(1),
                Child = new ScrollViewer
                {
                    Content = preview,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                }
            };
            System.Windows.Controls.Grid.SetColumn(previewBorder, 1);
            System.Windows.Controls.Grid.SetRow(previewBorder, 1);
            contentGrid.Children.Add(previewBorder);
            window.Content = contentGrid;
            redrawPreview();
            new WindowInteropHelper(window).Owner = owner;
            if (window.ShowDialog() != true) return null;
            double anchorageMm = 0, projectionMm = 0;
            double.TryParse(anchorage.Text, out anchorageMm);
            double.TryParse(projection.Text, out projectionMm);
            double.TryParse(coverBottom.Text, out double coverBottomMm);
            double.TryParse(coverTop.Text, out double coverTopMm);
            double.TryParse(coverSide.Text, out double coverSideMm);
            double.TryParse(spacingX.Text, out double spacingXMm);
            double.TryParse(spacingY.Text, out double spacingYMm);
            return new OptionsData
            {
                TypeX = ((BarOption)comboX.SelectedItem).Type,
                TypeY = ((BarOption)comboY.SelectedItem).Type,
                CoverBottomMm = coverBottomMm,
                CoverTopMm = coverTopMm,
                CoverSideMm = coverSideMm,
                SpacingXmm = spacingXMm,
                SpacingYmm = spacingYMm,
                BottomHooks = ToLayerHookOptions(bottomHooks),
                MiddleHooks = ToLayerHookOptions(middleHooks),
                TopHooks = ToLayerHookOptions(topHooks),
                Bottom = bottom.IsChecked == true,
                Middle = middle.IsChecked == true,
                Top = top.IsChecked == true,
                SideVertical = sides.IsChecked == true,
                SideHorizontal = horizontalSides.IsChecked == true,
                OStirrupHookType = (oHookCombo.SelectedItem as HookOption)?.Type,
                SideCStirrups = horizontalCStirrups.IsChecked == true,
                CStirrupHookX = cHookX.IsChecked == true, CStirrupHookY = cHookY.IsChecked == true,
                CStirrupLegXmm = double.TryParse(cLegX.Text, out double finalCLegX) ? finalCLegX : 0,
                CStirrupLegYmm = double.TryParse(cLegY.Text, out double finalCLegY) ? finalCLegY : 0,
                HorizontalMaxSpacingMm = double.TryParse(horizontalQuantity.Text, out double finalMaxSpacing) ? finalMaxSpacing : 150,
                ColumnDowels = dowels.IsChecked == true,
                DowelAnchorageMm = anchorageMm,
                DowelProjectionMm = projectionMm
            };
        }

        private static Border CreateSettingsCard(string title, string subtitle, UIElement content)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title, FontSize = 12, FontWeight = FontWeights.Bold,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 66, 104)),
                Margin = new Thickness(0, 0, 0, 2)
            });
            if (!string.IsNullOrWhiteSpace(subtitle))
                stack.Children.Add(new TextBlock
                {
                    Text = subtitle, FontSize = 10,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(112, 128, 148)),
                    Margin = new Thickness(0, 0, 0, 8)
                });
            stack.Children.Add(content);
            return new Border
            {
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(221, 229, 239)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9),
                Padding = new Thickness(11), Margin = new Thickness(0, 0, 0, 9), Child = stack
            };
        }

        private static void ApplyRebarWindowTheme(Window window)
        {
            window.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(242, 246, 251));
            window.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
            var textBox = new Style(typeof(System.Windows.Controls.TextBox));
            textBox.Setters.Add(new Setter(System.Windows.Controls.Control.PaddingProperty, new Thickness(7, 4, 7, 4)));
            textBox.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 29.0));
            textBox.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 6)));
            textBox.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, System.Windows.Media.Brushes.White));
            textBox.Setters.Add(new Setter(System.Windows.Controls.Control.BorderBrushProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(199, 211, 226))));
            textBox.Setters.Add(new Setter(System.Windows.Controls.Control.BorderThicknessProperty, new Thickness(1)));
            window.Resources.Add(typeof(System.Windows.Controls.TextBox), textBox);

            var combo = new Style(typeof(System.Windows.Controls.ComboBox));
            combo.Setters.Add(new Setter(System.Windows.Controls.Control.PaddingProperty, new Thickness(7, 3, 7, 3)));
            combo.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 30.0));
            combo.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 6)));
            combo.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, System.Windows.Media.Brushes.White));
            combo.Setters.Add(new Setter(System.Windows.Controls.Control.BorderBrushProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(199, 211, 226))));
            window.Resources.Add(typeof(System.Windows.Controls.ComboBox), combo);

            var check = new Style(typeof(CheckBox));
            check.Setters.Add(new Setter(System.Windows.Controls.Control.PaddingProperty, new Thickness(3)));
            check.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 61, 82))));
            window.Resources.Add(typeof(CheckBox), check);

            var button = new Style(typeof(Button));
            button.Setters.Add(new Setter(System.Windows.Controls.Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
            button.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 30.0));
            button.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 2, 5, 2)));
            button.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 238, 246))));
            button.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 60, 91))));
            button.Setters.Add(new Setter(System.Windows.Controls.Control.BorderBrushProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(205, 216, 230))));
            window.Resources.Add(typeof(Button), button);
        }
        private static double ParsePreviewMm(string text, double fallback)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) &&
                !double.IsNaN(value) && !double.IsInfinity(value) && value > 0 ? value : fallback;
        }
        private static LayerHookControls AddLayerHookControls(System.Windows.Controls.Panel root, string title,
            IList<RebarHookType> hookTypes)
        {
            var group = new GroupBox { Header = title, Margin = new Thickness(0, 5, 0, 2), Padding = new Thickness(6) };
            var panel = new StackPanel();
            var controls = new LayerHookControls
            {
                UseX = new CheckBox { Content = "Móc hai đầu thanh phương X" },
                UseY = new CheckBox { Content = "Móc hai đầu thanh phương Y" },
                TypeX = new System.Windows.Controls.ComboBox { MinWidth = 0, MinHeight = 27, DisplayMemberPath = "Name", ItemsSource = hookTypes, IsEnabled = false },
                TypeY = new System.Windows.Controls.ComboBox { MinWidth = 0, MinHeight = 27, DisplayMemberPath = "Name", ItemsSource = hookTypes, IsEnabled = false }
            };
            if (hookTypes.Count > 0)
            {
                controls.TypeX.SelectedIndex = 0;
                controls.TypeY.SelectedIndex = 0;
            }
            controls.UseX.Checked += (sender, args) => controls.TypeX.IsEnabled = true;
            controls.UseX.Unchecked += (sender, args) => controls.TypeX.IsEnabled = false;
            controls.UseY.Checked += (sender, args) => controls.TypeY.IsEnabled = true;
            controls.UseY.Unchecked += (sender, args) => controls.TypeY.IsEnabled = false;
            var fields = CreateFieldGrid(2);
            var xPanel = new StackPanel();
            xPanel.Children.Add(controls.UseX);
            xPanel.Children.Add(new TextBlock { Text = "Loại móc X (chiều dài theo type):", FontSize = 10, Margin = new Thickness(0, 3, 0, 1) });
            xPanel.Children.Add(controls.TypeX);
            var yPanel = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            yPanel.Children.Add(controls.UseY);
            yPanel.Children.Add(new TextBlock { Text = "Loại móc Y (chiều dài theo type):", FontSize = 10, Margin = new Thickness(0, 3, 0, 1) });
            yPanel.Children.Add(controls.TypeY);
            System.Windows.Controls.Grid.SetColumn(xPanel, 0);
            System.Windows.Controls.Grid.SetColumn(yPanel, 1);
            fields.Children.Add(xPanel);
            fields.Children.Add(yPanel);
            panel.Children.Add(fields);
            panel.Children.Add(new TextBlock { Text = "Chiều dài móc do RebarHookType/shape của project xác định.",
                FontSize = 10, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(112, 128, 148)),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            group.Content = panel;
            root.Children.Add(group);
            return controls;
        }

        private static string FormatMm(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);

        private static bool HookAvailable(string name, IList<RebarHookType> types)
        {
            return string.IsNullOrWhiteSpace(name) || types.Any(type => string.Equals(type.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static void ApplyHookProfile(LayerHookControls controls, string hookXName, double hookXLengthMm,
            string hookYName, double hookYLengthMm, IList<RebarHookType> hookTypes)
        {
            RebarHookType hookX = string.IsNullOrWhiteSpace(hookXName) ? null : hookTypes.FirstOrDefault(type =>
                string.Equals(type.Name, hookXName, StringComparison.OrdinalIgnoreCase));
            RebarHookType hookY = string.IsNullOrWhiteSpace(hookYName) ? null : hookTypes.FirstOrDefault(type =>
                string.Equals(type.Name, hookYName, StringComparison.OrdinalIgnoreCase));
            if (hookX != null) controls.TypeX.SelectedItem = hookX;
            if (hookY != null) controls.TypeY.SelectedItem = hookY;
            controls.UseX.IsChecked = hookX != null;
            controls.UseY.IsChecked = hookY != null;
        }

        private static void CaptureHookProfile(LayerHookControls controls,
            out string hookXName, out double hookXLengthMm, out string hookYName, out double hookYLengthMm)
        {
            hookXName = controls.UseX.IsChecked == true ? (controls.TypeX.SelectedItem as RebarHookType)?.Name : null;
            hookYName = controls.UseY.IsChecked == true ? (controls.TypeY.SelectedItem as RebarHookType)?.Name : null;
            hookXLengthMm = 0;
            hookYLengthMm = 0;
        }

        private static double ParseMm(string text)
        {
            double value;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ? value : 0;
        }

        private static bool HooksAreValid(bool layerEnabled, LayerHookControls controls)
        {
            if (!layerEnabled) return true;
            return (controls.UseX.IsChecked != true || controls.TypeX.SelectedItem != null) &&
                   (controls.UseY.IsChecked != true || controls.TypeY.SelectedItem != null);
        }

        private static LayerHookOptions ToLayerHookOptions(LayerHookControls controls)
        {
            return new LayerHookOptions
            {
                HookX = controls.UseX.IsChecked == true ? controls.TypeX.SelectedItem as RebarHookType : null,
                HookY = controls.UseY.IsChecked == true ? controls.TypeY.SelectedItem as RebarHookType : null
            };
        }

        private static System.Windows.Controls.TextBox AddInput(System.Windows.Controls.Panel root, string label, string value)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 5, 0, 2) });
            var box = new System.Windows.Controls.TextBox { MinWidth = 380, MinHeight = 22, Text = value };
            root.Children.Add(box);
            return box;
        }

        private static System.Windows.Controls.Grid CreateFieldGrid(int columns)
        {
            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 1, 0, 2) };
            for (int i = 0; i < columns; i++)
                grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return grid;
        }

        private static void AddGridField(System.Windows.Controls.Grid grid, int column, string label, UIElement control)
        {
            var field = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 8, 0, 0, 0) };
            field.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 3, 0, 2), TextWrapping = TextWrapping.Wrap });
            if (control is FrameworkElement element)
            {
                element.HorizontalAlignment = HorizontalAlignment.Stretch;
                element.Margin = new Thickness(0, 1, 0, 4);
            }
            field.Children.Add(control);
            System.Windows.Controls.Grid.SetColumn(field, column);
            grid.Children.Add(field);
        }

        private static System.Windows.Controls.TextBox AddGridInput(System.Windows.Controls.Grid grid, int column, string label, string value)
        {
            var box = new System.Windows.Controls.TextBox { MinWidth = 0, MinHeight = 29, Text = value };
            AddGridField(grid, column, label, box);
            return box;
        }
    }
}
