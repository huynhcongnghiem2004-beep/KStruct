using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CreateIsolatedFootingDrawingSheetsCommand : IExternalCommand
    {
        private sealed class FootingFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) { return IsIsolatedFooting(element as FamilyInstance); }
            public bool AllowReference(Reference reference, XYZ position) { return false; }
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<FamilyInstance> footings = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .OfType<FamilyInstance>().Where(IsIsolatedFooting).Distinct().ToList();
                if (footings.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new FootingFilter(),
                        "Chọn các móng đơn cần lập hồ sơ mặt cắt");
                    footings = picked.Select(x => doc.GetElement(x)).OfType<FamilyInstance>().Where(IsIsolatedFooting).Distinct().ToList();
                }
                if (footings.Count == 0) return Result.Cancelled;

                ViewFamilyType sectionType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>().FirstOrDefault(x => x.ViewFamily == ViewFamily.Section);
                if (sectionType == null) throw new InvalidOperationException("Project chưa có Section View Family Type.");
                ViewFamilyType planType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>().FirstOrDefault(x => x.ViewFamily == ViewFamily.StructuralPlan)
                    ?? new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                        .FirstOrDefault(x => x.ViewFamily == ViewFamily.FloorPlan);
                if (planType == null) throw new InvalidOperationException("Project chưa có Structural Plan hoặc Floor Plan View Family Type.");
                FamilySymbol titleBlock = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().OrderBy(x => x.FamilyName)
                    .ThenBy(x => x.Name).FirstOrDefault();
                if (titleBlock == null) throw new InvalidOperationException("Project chưa nạp Title Block Family Type.");

                HashSet<string> viewNames = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                    .Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                HashSet<string> sheetNumbers = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .Select(x => x.SheetNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var sheets = new List<ElementId>();
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Bản vẽ móng đơn"))
                {
                    tx.Start();
                    foreach (FamilyInstance footing in footings)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                List<ViewPlan> plans = CreateRebarPlanViews(doc, footing, planType, viewNames);
                                List<ViewSection> sections = CreateSections(doc, footing, sectionType, viewNames);
                                ViewSheet sheet = ViewSheet.Create(doc, titleBlock.Id);
                                sheet.SheetNumber = UniqueSheetNumber("KSMD-" + footing.Id.Value, sheetNumbers);
                                sheet.Name = "Móng - " + ElementMark(footing);
                                List<View> sheetViews = new List<View> { plans[0], plans[1], sections[0], sections[1] };
                                PlaceViewports(doc, sheet, sheetViews);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận sheet hoặc viewport.");
                                foreach (ViewSection view in sections) viewNames.Add(view.Name);
                                sheetNumbers.Add(sheet.SheetNumber);
                                sheets.Add(sheet.Id);
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Móng " + footing.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (sheets.Count == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Bản vẽ móng đơn", "Không tạo được sheet.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các bản vẽ mới.");
                }
                ui.Selection.SetElementIds(sheets);
                string report = "Đã tạo " + sheets.Count + "/" + footings.Count +
                    " sheet, mỗi sheet có mặt bằng lớp thép trên/dưới và hai mặt cắt vuông góc.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " móng:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Bản vẽ móng đơn", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Bản vẽ móng đơn", ex.Message);
                return Result.Failed;
            }
        }

        private static bool IsIsolatedFooting(FamilyInstance instance)
        {
            return instance != null && instance.Category != null &&
                instance.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation &&
                instance.Location is LocationPoint;
        }

        private static List<ViewSection> CreateSections(Document doc, FamilyInstance footing, ViewFamilyType type, HashSet<string> names)
        {
            BoundingBoxXYZ source = footing.get_BoundingBox(null);
            LocationPoint location = footing.Location as LocationPoint;
            if (source == null || location == null) throw new InvalidOperationException("Móng cần có LocationPoint và bounding box.");
            double angle = location.Rotation;
            XYZ localX = new XYZ(Math.Cos(angle), Math.Sin(angle), 0).Normalize();
            XYZ localY = new XYZ(-Math.Sin(angle), Math.Cos(angle), 0).Normalize();
            List<XYZ> corners = WorldCorners(source);
            XYZ center = location.Point;
            var views = new List<ViewSection>();
            double margin = UnitUtils.ConvertToInternalUnits(300, UnitTypeId.Millimeters);
            for (int i = 0; i < 2; i++)
            {
                XYZ viewDirection = i == 0 ? localX : localY;
                XYZ right = XYZ.BasisZ.CrossProduct(viewDirection).Normalize();
                Transform transform = Transform.Identity;
                transform.Origin = center;
                transform.BasisX = right;
                transform.BasisY = XYZ.BasisZ;
                transform.BasisZ = right.CrossProduct(XYZ.BasisZ).Normalize();
                XYZ z = transform.BasisZ;
                double minX = corners.Min(p => (p - center).DotProduct(right)) - margin;
                double maxX = corners.Max(p => (p - center).DotProduct(right)) + margin;
                double minY = corners.Min(p => p.Z - center.Z) - margin;
                double maxY = corners.Max(p => p.Z - center.Z) + margin;
                double minZ = corners.Min(p => (p - center).DotProduct(z)) - margin;
                double maxZ = corners.Max(p => (p - center).DotProduct(z)) + margin;
                var crop = new BoundingBoxXYZ
                {
                    Transform = transform,
                    Min = new XYZ(minX, minY, minZ),
                    Max = new XYZ(maxX, maxY, maxZ)
                };
                ViewSection view = ViewSection.CreateSection(doc, type.Id, crop);
                view.Name = UniqueName("KStruct - MĐ " + footing.Id.Value + " - MC" + (i + 1), names);
                view.CropBoxActive = true;
                view.CropBoxVisible = false;
                view.Scale = 20;
                views.Add(view);
            }
            return views;
        }

        private static List<ViewPlan> CreateRebarPlanViews(Document doc, FamilyInstance footing,
            ViewFamilyType type, HashSet<string> names)
        {
            BoundingBoxXYZ source = footing.get_BoundingBox(null);
            if (source == null) throw new InvalidOperationException("Không đọc được bounding box móng để tạo mặt bằng thép.");
            Level level = doc.GetElement(footing.LevelId) as Level;
            if (level == null)
            {
                double targetElevation = (source.Min.Z + source.Max.Z) / 2.0;
                level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(x => Math.Abs(x.Elevation - targetElevation)).FirstOrDefault();
            }
            if (level == null) throw new InvalidOperationException("Không tìm được Level cho mặt bằng thép móng.");

            List<Rebar> hostedBars = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).WhereElementIsNotElementType()
                .Cast<Rebar>().Where(x => x.GetHostId() == footing.Id).ToList();
            string footingPrefix = "KSTR_FOOTING_" + footing.Id.Value + "_";
            var layers = new[] { "TOP", "BOT" };
            var views = new List<ViewPlan>();
            double margin = UnitUtils.ConvertToInternalUnits(200, UnitTypeId.Millimeters);
            double rangeHalfDepth = UnitUtils.ConvertToInternalUnits(75, UnitTypeId.Millimeters);
            double minX = source.Min.X, maxX = source.Max.X, minY = source.Min.Y, maxY = source.Max.Y;
            List<XYZ> corners = WorldCorners(source);
            minX = corners.Min(x => x.X) - margin;
            maxX = corners.Max(x => x.X) + margin;
            minY = corners.Min(x => x.Y) - margin;
            maxY = corners.Max(x => x.Y) + margin;
            double hostBottom = corners.Min(x => x.Z);
            double hostTop = corners.Max(x => x.Z);

            foreach (string layer in layers)
            {
                List<Rebar> layerBars = hostedBars.Where(x => ReadPartition(x).StartsWith(footingPrefix + layer + "_",
                    StringComparison.OrdinalIgnoreCase)).ToList();
                double elevation = layerBars.Select(x => x.get_BoundingBox(null))
                    .Where(x => x != null).Select(x => (x.Min.Z + x.Max.Z) / 2.0).DefaultIfEmpty(double.NaN).Average();
                if (double.IsNaN(elevation))
                    elevation = layer == "BOT" ? hostBottom + rangeHalfDepth : hostTop - rangeHalfDepth;

                ViewPlan view = ViewPlan.Create(doc, type.Id, level.Id);
                view.Name = UniqueName("KStruct - Móng " + footing.Id.Value + " - " + layer, names);
                names.Add(view.Name);
                view.Scale = 20;
                view.DetailLevel = ViewDetailLevel.Fine;
                view.Discipline = ViewDiscipline.Structural;
                view.CropBox = new BoundingBoxXYZ
                {
                    Transform = Transform.Identity,
                    Min = new XYZ(minX, minY, hostBottom - margin),
                    Max = new XYZ(maxX, maxY, hostTop + margin)
                };
                view.CropBoxActive = true;
                view.CropBoxVisible = false;

                PlanViewRange range = view.GetViewRange();
                SetRangePlane(range, PlanViewPlane.TopClipPlane, level.Id, elevation + rangeHalfDepth - level.Elevation);
                SetRangePlane(range, PlanViewPlane.CutPlane, level.Id, elevation + rangeHalfDepth / 2.0 - level.Elevation);
                SetRangePlane(range, PlanViewPlane.BottomClipPlane, level.Id, elevation - rangeHalfDepth - level.Elevation);
                SetRangePlane(range, PlanViewPlane.ViewDepthPlane, level.Id, elevation - 2.0 * rangeHalfDepth - level.Elevation);
                view.SetViewRange(range);

                List<ElementId> hide = hostedBars.Where(x =>
                {
                    string partition = ReadPartition(x);
                    bool hasLayer = partition.StartsWith(footingPrefix + "TOP_", StringComparison.OrdinalIgnoreCase) ||
                        partition.StartsWith(footingPrefix + "BOT_", StringComparison.OrdinalIgnoreCase) ||
                        partition.StartsWith(footingPrefix + "MID_", StringComparison.OrdinalIgnoreCase);
                    return hasLayer && !partition.StartsWith(footingPrefix + layer + "_", StringComparison.OrdinalIgnoreCase);
                }).Select(x => x.Id).ToList();
                if (hide.Count > 0) view.HideElements(hide);
                views.Add(view);
            }
            return views;
        }

        private static void SetRangePlane(PlanViewRange range, PlanViewPlane plane, ElementId levelId, double offset)
        {
            range.SetLevelId(plane, levelId);
            range.SetOffset(plane, offset);
        }

        private static string ReadPartition(Rebar rebar)
        {
            Parameter partition = rebar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
            return partition == null || !partition.HasValue ? string.Empty : partition.AsString() ?? string.Empty;
        }

        private static void PlaceViewports(Document doc, ViewSheet sheet, IList<View> views)
        {
            BoundingBoxXYZ sheetBox = sheet.get_BoundingBox(null);
            if (sheetBox == null) throw new InvalidOperationException("Không đọc được khổ giấy để bố trí các view.");
            double width = sheetBox.Max.X - sheetBox.Min.X;
            double height = sheetBox.Max.Y - sheetBox.Min.Y;
            if (width <= 0 || height <= 0) throw new InvalidOperationException("Khổ giấy không hợp lệ.");
            double cellWidth = width * 0.44;
            double cellHeight = height * 0.42;
            double leftX = sheetBox.Min.X + width * 0.27;
            double rightX = sheetBox.Min.X + width * 0.73;
            double topY = sheetBox.Min.Y + height * 0.73;
            double bottomY = sheetBox.Min.Y + height * 0.27;
            XYZ[] centers = { new XYZ(leftX, topY, 0), new XYZ(rightX, topY, 0),
                new XYZ(leftX, bottomY, 0), new XYZ(rightX, bottomY, 0) };
            var viewports = new List<Viewport>();
            for (int i = 0; i < views.Count; i++)
                viewports.Add(Viewport.Create(doc, sheet.Id, views[i].Id, centers[i]));
            doc.Regenerate();
            for (int i = 0; i < viewports.Count; i++)
            {
                Viewport viewport = viewports[i];
                Outline outline = viewport.GetBoxOutline();
                double viewWidth = outline.MaximumPoint.X - outline.MinimumPoint.X;
                double viewHeight = outline.MaximumPoint.Y - outline.MinimumPoint.Y;
                double scaleFactor = Math.Max(viewWidth / cellWidth, viewHeight / cellHeight);
                if (scaleFactor > 1.0)
                {
                    views[i].Scale = Math.Min(200, Math.Max(views[i].Scale + 1,
                        (int)Math.Ceiling(views[i].Scale * scaleFactor)));
                    doc.Regenerate();
                }
                viewport.SetBoxCenter(centers[i]);
            }
        }

        private static List<XYZ> WorldCorners(BoundingBoxXYZ box)
        {
            Transform transform = box.Transform ?? Transform.Identity;
            var result = new List<XYZ>();
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                result.Add(transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X,
                    y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z)));
            return result;
        }

        private static string ElementMark(Element element)
        {
            Parameter mark = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            return mark == null || !mark.HasValue || string.IsNullOrWhiteSpace(mark.AsString())
                ? element.Id.Value.ToString() : mark.AsString().Trim();
        }

        private static string UniqueName(string baseName, HashSet<string> names)
        {
            if (!names.Contains(baseName)) return baseName;
            for (int i = 2; ; i++) { string value = baseName + " (" + i + ")"; if (!names.Contains(value)) return value; }
        }

        private static string UniqueSheetNumber(string baseNumber, HashSet<string> numbers)
        {
            string value = baseNumber;
            for (int i = 2; numbers.Contains(value); i++) value = baseNumber + "-" + i;
            return value;
        }
    }
}
