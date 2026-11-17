using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ReinforceStraightStairCommand : IExternalCommand
    {
        private sealed class StairFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Stairs || element is StairsRun || element is StairsLanding;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class TypeChoice
        {
            public RebarBarType Type { get; private set; }
            public string Label { get; private set; }
            public TypeChoice(RebarBarType type)
            {
                Type = type;
                Parameter diameter = type.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER);
                double mm = diameter != null && diameter.HasValue
                    ? UnitUtils.ConvertFromInternalUnits(diameter.AsDouble(), UnitTypeId.Millimeters) : 0;
                Label = type.Name + " | Ø" + mm.ToString("0.##") + " mm";
            }
            public override string ToString() => Label;
        }

        private sealed class HookChoice
        {
            public RebarHookType Type { get; }
            public string Label => Type == null ? "Không móc" : Type.Name;
            public HookChoice(RebarHookType type) { Type = type; }
            public override string ToString() => Label;
        }

        private sealed class LayoutOptions
        {
            public RebarBarType MainType;
            public RebarBarType DistributionType;
            public double CoverBottomMm, CoverTopMm, CoverSideMm;
            public double MainSpacingMm;
            public double DistributionSpacingMm;
            public bool IncludeTopLayer;
            public RebarHookType MainHookStart, MainHookEnd;
            public RebarHookType DistributionHookStart, DistributionHookEnd;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                var selected = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .Where(e => e is Stairs || e is StairsRun || e is StairsLanding).ToList();
                if (selected.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new StairFilter(),
                        "Chọn cầu thang, vế thang hoặc chiếu nghỉ bê tông cần bố trí thép");
                    selected = picked.Select(r => doc.GetElement(r))
                        .Where(e => e is Stairs || e is StairsRun || e is StairsLanding).ToList();
                }
                List<Stairs> stairs = selected.OfType<Stairs>().ToList();
                List<StairsRun> runs = selected.OfType<StairsRun>()
                    .Concat(stairs.SelectMany(s => s.GetStairsRuns().Select(id => doc.GetElement(id)).OfType<StairsRun>()))
                    .GroupBy(r => r.Id).Select(g => g.First()).ToList();
                List<StairsLanding> landings = selected.OfType<StairsLanding>()
                    .Concat(stairs.SelectMany(s => s.GetStairsLandings().Select(id => doc.GetElement(id)).OfType<StairsLanding>()))
                    .GroupBy(l => l.Id).Select(g => g.First()).ToList();
                if (runs.Count + landings.Count == 0)
                {
                    TaskDialog.Show("KStruct - Cốt thép cầu thang", "Không tìm thấy vế thang dạng component. Cầu thang sketch/custom chưa được hỗ trợ.");
                    return Result.Cancelled;
                }

                List<RebarBarType> barTypes = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType))
                    .Cast<RebarBarType>().OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (barTypes.Count == 0) throw new InvalidOperationException("Project chưa có RebarBarType.");
                List<RebarHookType> hookTypes = new FilteredElementCollector(doc).OfClass(typeof(RebarHookType))
                    .Cast<RebarHookType>().OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                LayoutOptions options = ChooseOptions(data.Application.MainWindowHandle, barTypes, hookTypes);
                if (options == null) return Result.Cancelled;

                int created = 0, replacedBars = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Cốt thép vế thang thẳng"))
                {
                    tx.Start();
                    foreach (StairsRun run in runs)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                List<ElementId> previousManagedBars = FindManagedStairBars(doc, run.Id);
                                int hostCreated = ReinforceRun(doc, run, options);
                                if (hostCreated <= 0) throw new InvalidOperationException("Không tạo được bộ thép mới; bộ KStruct cũ được giữ nguyên.");
                                if (previousManagedBars.Count > 0) doc.Delete(previousManagedBars);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận cốt thép vế thang.");
                                created += hostCreated;
                                replacedBars += previousManagedBars.Count;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Vế thang " + run.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    foreach (StairsLanding landing in landings)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                List<ElementId> previousManagedBars = FindManagedStairBars(doc, landing.Id);
                                int hostCreated = ReinforceLanding(doc, landing, options);
                                if (hostCreated <= 0) throw new InvalidOperationException("Không tạo được bộ thép mới; bộ KStruct cũ được giữ nguyên.");
                                if (previousManagedBars.Count > 0) doc.Delete(previousManagedBars);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận cốt thép chiếu nghỉ.");
                                created += hostCreated;
                                replacedBars += previousManagedBars.Count;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Chiếu nghỉ " + landing.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (created == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Cốt thép cầu thang", "Không tạo được thanh nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các thanh mới.");
                }

                string report = "Đã tạo " + created + " thanh thép cho " + runs.Count + " vế thang và " + landings.Count + " chiếu nghỉ được chọn. Lệnh tạo thép đáy dọc vế và lưới thép đáy hai phương tại chiếu nghỉ.";
                if (options.IncludeTopLayer) report += " Đã thêm lớp thép trên theo cấu hình.";
                if (replacedBars > 0) report += " Đã thay " + replacedBars + " thanh KStruct cũ sau khi tạo bộ mới thành công.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " vế:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Cốt thép cầu thang", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Cốt thép cầu thang", ex.Message);
                return Result.Failed;
            }
        }

        private static int ReinforceRun(Document doc, StairsRun run, LayoutOptions options)
        {
            if (RebarHostData.GetRebarHostData(run) == null)
                throw new InvalidOperationException("Vế thang này không hỗ trợ làm host cốt thép.");
            CurveLoop path = run.GetStairsPath();
            List<XYZ> pathEnds = path.SelectMany(c => new[] { c.GetEndPoint(0), c.GetEndPoint(1) }).ToList();
            XYZ first = null, last = null;
            double greatestPlanDistance = 0;
            for (int i = 0; i < pathEnds.Count; i++)
                for (int j = i + 1; j < pathEnds.Count; j++)
                {
                    double d = new XYZ(pathEnds[j].X - pathEnds[i].X, pathEnds[j].Y - pathEnds[i].Y, 0).GetLength();
                    if (d > greatestPlanDistance) { greatestPlanDistance = d; first = pathEnds[i]; last = pathEnds[j]; }
                }
            if (first == null || last == null || greatestPlanDistance < 0.1)
                throw new InvalidOperationException("Chỉ hỗ trợ vế thang thẳng có chiều chạy rõ ràng.");

            Stairs parentStairs = run.GetStairs();
            double baseElevation = parentStairs.BaseElevation + run.BaseElevation;
            double topElevation = parentStairs.BaseElevation + run.TopElevation;
            // Orient the run axis from base toward top; use component elevations when the path is plan-only.
            if (Math.Abs(last.Z - first.Z) > 1e-5)
            {
                if (last.Z < first.Z) { XYZ swap = first; first = last; last = swap; }
            }
            else if (topElevation < baseElevation)
            {
                XYZ swap = first; first = last; last = swap;
            }
            XYZ axis = new XYZ(last.X - first.X, last.Y - first.Y, 0).Normalize();
            XYZ across = XYZ.BasisZ.CrossProduct(axis).Normalize();
            double maxPlanDeviation = pathEnds.Max(p => Math.Abs((p - first).DotProduct(across)));
            if (maxPlanDeviation > UnitUtils.ConvertToInternalUnits(50, UnitTypeId.Millimeters))
                throw new InvalidOperationException("Chỉ hỗ trợ vế thang thẳng; vế cong hoặc đổi hướng cần bố trí riêng.");
            BoundingBoxXYZ box = run.get_BoundingBox(null);
            if (box == null) throw new InvalidOperationException("Không đọc được kích thước vế thang.");
            List<XYZ> corners = GetWorldCorners(box);
            double minAlong = corners.Min(p => p.DotProduct(axis));
            double maxAlong = corners.Max(p => p.DotProduct(axis));
            double minAcross = corners.Min(p => p.DotProduct(across));
            double maxAcross = corners.Max(p => p.DotProduct(across));
            if (maxAlong - minAlong < 0.1 || maxAcross - minAcross < 0.1)
                throw new InvalidOperationException("Kích thước vế thang không hợp lệ.");

            StairsRunType runType = doc.GetElement(run.GetTypeId()) as StairsRunType;
            double structuralDepth = runType == null ? 0 : runType.StructuralDepth;
            if (structuralDepth <= 0 && runType != null) structuralDepth = runType.TotalDepth;
            if (structuralDepth <= 0) throw new InvalidOperationException("StairsRunType chưa có chiều dày kết cấu.");

            double coverBottom = UnitUtils.ConvertToInternalUnits(options.CoverBottomMm, UnitTypeId.Millimeters);
            double coverTop = UnitUtils.ConvertToInternalUnits(options.CoverTopMm, UnitTypeId.Millimeters);
            double coverSide = UnitUtils.ConvertToInternalUnits(options.CoverSideMm, UnitTypeId.Millimeters);
            double mainRadius = options.MainType.BarNominalDiameter / 2.0;
            double distributionRadius = options.DistributionType.BarNominalDiameter / 2.0;
            double sideClearance = coverSide + Math.Max(mainRadius, distributionRadius);
            double mainSpacing = UnitUtils.ConvertToInternalUnits(options.MainSpacingMm, UnitTypeId.Millimeters);
            double distributionSpacing = UnitUtils.ConvertToInternalUnits(options.DistributionSpacingMm, UnitTypeId.Millimeters);
            double lowAlong = first.DotProduct(axis);
            double highAlong = last.DotProduct(axis);
            double lowZ = Math.Min(first.Z, last.Z);
            double highZ = Math.Max(first.Z, last.Z);
            if (highZ - lowZ <= 1e-5)
            {
                lowZ = Math.Min(baseElevation, topElevation);
                highZ = Math.Max(baseElevation, topElevation);
            }
            double rise = highZ - lowZ;
            double runLength = highAlong - lowAlong;
            if (runLength < 0.1) throw new InvalidOperationException("Không xác định được chiều chạy thực của vế thang.");
            double slope = rise / runLength;
            double slopeScale = Math.Sqrt(1 + slope * slope);
            double mainBottomOffset = (structuralDepth - coverBottom - mainRadius) * slopeScale;
            double distributionBottomOffset = (structuralDepth - coverBottom - 2 * mainRadius - distributionRadius) * slopeScale;
            double distributionTopOffset = (coverTop + distributionRadius) * slopeScale;
            double mainTopOffset = (coverTop + 2 * distributionRadius + mainRadius) * slopeScale;
            if (mainBottomOffset <= 0 || distributionBottomOffset <= 0)
                throw new InvalidOperationException("Chiều dày kết cấu vế thang không đủ cho cover và hai phương thép lớp dưới.");
            if (options.IncludeTopLayer && structuralDepth <= coverBottom + coverTop + 4 * (mainRadius + distributionRadius) +
                UnitUtils.ConvertToInternalUnits(2, UnitTypeId.Millimeters))
                throw new InvalidOperationException("Chiều dày kết cấu vế thang không đủ cho hai lớp thép theo cover đã nhập.");

            int made = 0;
            double usableAcross = maxAcross - minAcross - 2 * sideClearance;
            double usableAlong = runLength - 2 * sideClearance;
            if (usableAcross <= 0 || usableAlong <= 0) throw new InvalidOperationException("Cover không phù hợp với kích thước vế thang.");

            int mainCount = Math.Max(2, (int)Math.Ceiling(usableAcross / mainSpacing) + 1);
            for (int i = 0; i < mainCount; i++)
            {
                double acrossValue = minAcross + sideClearance + usableAcross * i / (mainCount - 1);
                XYZ p0 = PointAt(axis, across, lowAlong + sideClearance, acrossValue, lowZ - mainBottomOffset);
                XYZ p1 = PointAt(axis, across, highAlong - sideClearance, acrossValue, highZ - mainBottomOffset);
                made += CreateBar(doc, run, options.MainType, p0, p1, across, "MAIN", options.MainHookStart, options.MainHookEnd);
            }

            int distributionCount = Math.Max(2, (int)Math.Ceiling(usableAlong / distributionSpacing) + 1);
            for (int i = 0; i < distributionCount; i++)
            {
                double alongValue = lowAlong + sideClearance + usableAlong * i / (distributionCount - 1);
                double t = (alongValue - lowAlong) / Math.Max(1e-6, highAlong - lowAlong);
                double z = lowZ + (highZ - lowZ) * t - distributionBottomOffset;
                XYZ p0 = PointAt(axis, across, alongValue, minAcross + sideClearance, z);
                XYZ p1 = PointAt(axis, across, alongValue, maxAcross - sideClearance, z);
                made += CreateBar(doc, run, options.DistributionType, p0, p1, XYZ.BasisZ, "DIST", options.DistributionHookStart, options.DistributionHookEnd);
            }
            if (options.IncludeTopLayer)
            {
                for (int i = 0; i < mainCount; i++)
                {
                    double acrossValue = minAcross + sideClearance + usableAcross * i / (mainCount - 1);
                    XYZ p0 = PointAt(axis, across, lowAlong + sideClearance, acrossValue, lowZ - mainTopOffset);
                    XYZ p1 = PointAt(axis, across, highAlong - sideClearance, acrossValue, highZ - mainTopOffset);
                    made += CreateBar(doc, run, options.MainType, p0, p1, across, "TOP_MAIN", options.MainHookStart, options.MainHookEnd);
                }
                for (int i = 0; i < distributionCount; i++)
                {
                    double alongValue = lowAlong + sideClearance + usableAlong * i / (distributionCount - 1);
                    double t = (alongValue - lowAlong) / Math.Max(1e-6, highAlong - lowAlong);
                    double z = lowZ + (highZ - lowZ) * t - distributionTopOffset;
                    XYZ p0 = PointAt(axis, across, alongValue, minAcross + sideClearance, z);
                    XYZ p1 = PointAt(axis, across, alongValue, maxAcross - sideClearance, z);
                    made += CreateBar(doc, run, options.DistributionType, p0, p1, XYZ.BasisZ, "TOP_DIST", options.DistributionHookStart, options.DistributionHookEnd);
                }
            }
            return made;
        }

        private static int ReinforceLanding(Document doc, StairsLanding landing, LayoutOptions options)
        {
            if (RebarHostData.GetRebarHostData(landing) == null)
                throw new InvalidOperationException("Chiếu nghỉ này không hỗ trợ làm host cốt thép.");
            CurveLoop boundary = landing.GetFootprintBoundary();
            var polygon = new List<XYZ>();
            foreach (Curve edge in boundary)
                foreach (XYZ point in edge.Tessellate())
                    if (polygon.Count == 0 || polygon[polygon.Count - 1].DistanceTo(point) > 1e-5)
                        polygon.Add(point);
            if (polygon.Count > 1 && polygon[0].DistanceTo(polygon[polygon.Count - 1]) <= 1e-5)
                polygon.RemoveAt(polygon.Count - 1);
            if (polygon.Count < 3) throw new InvalidOperationException("Không đọc được đường bao chiếu nghỉ.");

            XYZ axis = null;
            double longest = 0;
            for (int i = 0; i < polygon.Count; i++)
            {
                XYZ edge = polygon[(i + 1) % polygon.Count] - polygon[i];
                double length = new XYZ(edge.X, edge.Y, 0).GetLength();
                if (length > longest) { longest = length; axis = new XYZ(edge.X, edge.Y, 0).Normalize(); }
            }
            if (axis == null || longest < 0.1) throw new InvalidOperationException("Chiếu nghỉ không có cạnh thẳng hợp lệ.");
            XYZ across = XYZ.BasisZ.CrossProduct(axis).Normalize();
            BoundingBoxXYZ box = landing.get_BoundingBox(null);
            if (box == null) throw new InvalidOperationException("Không đọc được chiều dày chiếu nghỉ.");
            double lowZ = GetWorldCorners(box).Min(p => p.Z);
            double highZ = GetWorldCorners(box).Max(p => p.Z);
            double coverBottom = UnitUtils.ConvertToInternalUnits(options.CoverBottomMm, UnitTypeId.Millimeters);
            double coverTop = UnitUtils.ConvertToInternalUnits(options.CoverTopMm, UnitTypeId.Millimeters);
            double coverSide = UnitUtils.ConvertToInternalUnits(options.CoverSideMm, UnitTypeId.Millimeters);
            double mainRadius = options.MainType.BarNominalDiameter / 2.0;
            double distributionRadius = options.DistributionType.BarNominalDiameter / 2.0;
            double requiredDepth = coverBottom + coverTop + 4 * (mainRadius + distributionRadius) +
                UnitUtils.ConvertToInternalUnits(2, UnitTypeId.Millimeters);
            if (highZ - lowZ <= (options.IncludeTopLayer ? requiredDepth : coverBottom + 2 * mainRadius + 2 * distributionRadius))
                throw new InvalidOperationException("Chiều dày chiếu nghỉ không đủ cho cover và các lớp thép.");
            double mainSpacing = UnitUtils.ConvertToInternalUnits(options.MainSpacingMm, UnitTypeId.Millimeters);
            double distributionSpacing = UnitUtils.ConvertToInternalUnits(options.DistributionSpacingMm, UnitTypeId.Millimeters);
            double mainBottomZ = lowZ + coverBottom + mainRadius;
            double distributionBottomZ = lowZ + coverBottom + 2 * mainRadius + distributionRadius;
            int created = CreateLandingGrid(doc, landing, options.MainType, polygon, axis, mainSpacing, coverSide, mainBottomZ, "MAIN", options.MainHookStart, options.MainHookEnd);
            created += CreateLandingGrid(doc, landing, options.DistributionType, polygon, across, distributionSpacing, coverSide, distributionBottomZ, "DIST", options.DistributionHookStart, options.DistributionHookEnd);
            if (options.IncludeTopLayer)
            {
                double distributionTopZ = highZ - coverTop - distributionRadius;
                double mainTopZ = highZ - coverTop - 2 * distributionRadius - mainRadius;
                created += CreateLandingGrid(doc, landing, options.MainType, polygon, axis, mainSpacing, coverSide, mainTopZ, "TOP_MAIN", options.MainHookStart, options.MainHookEnd);
                created += CreateLandingGrid(doc, landing, options.DistributionType, polygon, across, distributionSpacing, coverSide, distributionTopZ, "TOP_DIST", options.DistributionHookStart, options.DistributionHookEnd);
            }
            return created;
        }

        private static int CreateLandingGrid(Document doc, StairsLanding landing, RebarBarType type,
            IList<XYZ> polygon, XYZ direction, double spacing, double cover, double z, string mark,
            RebarHookType hookStart, RebarHookType hookEnd)
        {
            double edgeCover = cover + type.BarNominalDiameter / 2.0;
            XYZ normal = XYZ.BasisZ.CrossProduct(direction).Normalize();
            double minOffset = polygon.Min(p => p.DotProduct(normal)) + edgeCover;
            double maxOffset = polygon.Max(p => p.DotProduct(normal)) - edgeCover;
            if (maxOffset - minOffset < 0.1)
                throw new InvalidOperationException("Cover không phù hợp với bề rộng chiếu nghỉ.");
            int count = Math.Max(2, (int)Math.Ceiling((maxOffset - minOffset) / spacing) + 1);
            int created = 0;
            for (int i = 0; i < count; i++)
            {
                double offset = minOffset + (maxOffset - minOffset) * i / (count - 1);
                var intersections = new List<double>();
                for (int edgeIndex = 0; edgeIndex < polygon.Count; edgeIndex++)
                {
                    XYZ a = polygon[edgeIndex];
                    XYZ b = polygon[(edgeIndex + 1) % polygon.Count];
                    double da = a.DotProduct(normal) - offset;
                    double db = b.DotProduct(normal) - offset;
                    if (Math.Abs(da) <= 1e-7 && Math.Abs(db) <= 1e-7)
                    {
                        intersections.Add(a.DotProduct(direction));
                        intersections.Add(b.DotProduct(direction));
                    }
                    else if ((da <= 0 && db >= 0) || (da >= 0 && db <= 0))
                    {
                        double denom = da - db;
                        if (Math.Abs(denom) <= 1e-9) continue;
                        double t = da / denom;
                        XYZ point = a + (b - a).Multiply(t);
                        intersections.Add(point.DotProduct(direction));
                    }
                }
                List<double> hits = intersections.OrderBy(v => v).Aggregate(new List<double>(), (list, v) =>
                {
                    if (list.Count == 0 || Math.Abs(list[list.Count - 1] - v) > 1e-5) list.Add(v);
                    return list;
                });
                for (int h = 0; h + 1 < hits.Count; h += 2)
                {
                    double startAlong = hits[h] + edgeCover;
                    double endAlong = hits[h + 1] - edgeCover;
                    if (endAlong - startAlong < 0.1) continue;
                    XYZ start = PointAt(direction, normal, startAlong, offset, z);
                    XYZ end = PointAt(direction, normal, endAlong, offset, z);
                    created += CreateBar(doc, landing, type, start, end, XYZ.BasisZ, mark, hookStart, hookEnd);
                }
            }
            return created;
        }

        private static int CreateBar(Document doc, Element host, RebarBarType type, XYZ start, XYZ end, XYZ normal, string mark,
            RebarHookType hookStart, RebarHookType hookEnd)
        {
            if (start.DistanceTo(end) <= 1e-5) return 0;
            Rebar bar = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.Standard, type, hookStart, hookEnd, host, normal,
                new Curve[] { Line.CreateBound(start, end) }, RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
            if (bar == null) throw new InvalidOperationException("Revit không tạo được thanh " + mark + ".");
            Parameter partition = bar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
            if (partition != null && !partition.IsReadOnly)
                partition.Set("KSTR_STAIR_" + host.Id.Value + "_" + mark);
            return 1;
        }

        private static List<ElementId> FindManagedStairBars(Document doc, ElementId hostId)
        {
            string prefix = "KSTR_STAIR_" + hostId.Value + "_";
            return new FilteredElementCollector(doc).OfClass(typeof(Rebar)).WhereElementIsNotElementType()
                .Cast<Rebar>().Where(bar => bar.GetHostId() == hostId)
                .Where(bar => (bar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM)?.AsString() ?? "")
                    .StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(bar => bar.Id).ToList();
        }

        private static XYZ PointAt(XYZ axis, XYZ across, double along, double acrossValue, double z)
        {
            // Reconstruct XY from the orthogonal local axes and keep the calculated reinforcement elevation.
            XYZ xy = axis.Multiply(along).Add(across.Multiply(acrossValue));
            return new XYZ(xy.X, xy.Y, z);
        }

        private static List<XYZ> GetWorldCorners(BoundingBoxXYZ box)
        {
            var result = new List<XYZ>();
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                        result.Add(box.Transform.OfPoint(new XYZ(
                            x == 0 ? box.Min.X : box.Max.X,
                            y == 0 ? box.Min.Y : box.Max.Y,
                            z == 0 ? box.Min.Z : box.Max.Z)));
            return result;
        }

        private static LayoutOptions ChooseOptions(IntPtr owner, IList<RebarBarType> types, IList<RebarHookType> hookTypes)
        {
            var choices = types.Select(t => new TypeChoice(t)).ToList();
            var window = new Window
            {
                Title = "Cốt thép vế thang thẳng - KStruct",
                Width = 450,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock
            {
                Text = "Tạo thép đáy dọc và thép phân bố ngang cho vế thang thẳng; có thể thêm hai phương thép lớp trên. Khoảng cách là giá trị tối đa.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            System.Windows.Controls.ComboBox mainType = AddType(root, "Loại thép dọc đáy:", choices);
            System.Windows.Controls.ComboBox distributionType = AddType(root, "Loại thép phân bố ngang:", choices);
            System.Windows.Controls.TextBox coverBottom = AddInput(root, "Cover đáy (mm):", "25");
            System.Windows.Controls.TextBox coverTop = AddInput(root, "Cover trên (mm):", "25");
            System.Windows.Controls.TextBox coverSide = AddInput(root, "Cover cạnh/đầu (mm):", "25");
            System.Windows.Controls.TextBox mainSpacing = AddInput(root, "Khoảng cách thép dọc trên bề rộng (mm):", "150");
            System.Windows.Controls.TextBox distributionSpacing = AddInput(root, "Khoảng cách thép ngang theo vế (mm):", "200");
            var hookChoices = new List<HookChoice> { new HookChoice(null) };
            hookChoices.AddRange(hookTypes.Select(type => new HookChoice(type)));
            var mainHookStart = AddHookChoice(root, "Móc đầu dưới thép dọc:", hookChoices);
            var mainHookEnd = AddHookChoice(root, "Móc đầu trên thép dọc:", hookChoices);
            var distributionHookStart = AddHookChoice(root, "Móc đầu 1 thép phân bố:", hookChoices);
            var distributionHookEnd = AddHookChoice(root, "Móc đầu 2 thép phân bố:", hookChoices);
            var topLayer = new CheckBox { Content = "Bố trí thêm lớp thép trên cho vế và chiếu nghỉ", IsChecked = true, Margin = new Thickness(0, 8, 0, 2) };
            root.Children.Add(topLayer);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var ok = new Button { Content = "Tạo thép", Width = 85, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (s, e) =>
            {
                double cb, ct, cs, ms, ds;
                if (mainType.SelectedItem == null || distributionType.SelectedItem == null ||
                    !double.TryParse(coverBottom.Text, out cb) || cb <= 0 ||
                    !double.TryParse(coverTop.Text, out ct) || ct <= 0 ||
                    !double.TryParse(coverSide.Text, out cs) || cs <= 0 ||
                    !double.TryParse(mainSpacing.Text, out ms) || ms <= 0 ||
                    !double.TryParse(distributionSpacing.Text, out ds) || ds <= 0)
                {
                    TaskDialog.Show("KStruct - Cốt thép cầu thang", "Chọn loại thép và nhập cover/khoảng cách lớn hơn 0.");
                    return;
                }
                window.DialogResult = true;
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            if (window.ShowDialog() != true) return null;
            double.TryParse(coverBottom.Text, out double coverBottomValue);
            double.TryParse(coverTop.Text, out double coverTopValue);
            double.TryParse(coverSide.Text, out double coverSideValue);
            double.TryParse(mainSpacing.Text, out double mainSpacingValue);
            double.TryParse(distributionSpacing.Text, out double distributionSpacingValue);
            return new LayoutOptions
            {
                MainType = ((TypeChoice)mainType.SelectedItem).Type,
                DistributionType = ((TypeChoice)distributionType.SelectedItem).Type,
                CoverBottomMm = coverBottomValue,
                CoverTopMm = coverTopValue,
                CoverSideMm = coverSideValue,
                MainSpacingMm = mainSpacingValue,
                DistributionSpacingMm = distributionSpacingValue,
                IncludeTopLayer = topLayer.IsChecked.GetValueOrDefault(),
                MainHookStart = ((HookChoice)mainHookStart.SelectedItem)?.Type,
                MainHookEnd = ((HookChoice)mainHookEnd.SelectedItem)?.Type,
                DistributionHookStart = ((HookChoice)distributionHookStart.SelectedItem)?.Type,
                DistributionHookEnd = ((HookChoice)distributionHookEnd.SelectedItem)?.Type
            };
        }

        private static System.Windows.Controls.ComboBox AddHookChoice(System.Windows.Controls.Panel root, string label,
            IList<HookChoice> choices)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 5, 0, 2) });
            var combo = new System.Windows.Controls.ComboBox
            {
                ItemsSource = choices, SelectedIndex = 0, MinWidth = 400, MinHeight = 23,
                DisplayMemberPath = "Label"
            };
            root.Children.Add(combo);
            return combo;
        }

        private static System.Windows.Controls.ComboBox AddType(System.Windows.Controls.Panel root, string label, IList<TypeChoice> choices)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 5, 0, 2) });
            var combo = new System.Windows.Controls.ComboBox { MinWidth = 405, MinHeight = 24, ItemsSource = choices, SelectedIndex = 0, DisplayMemberPath = "Label" };
            root.Children.Add(combo);
            return combo;
        }

        private static System.Windows.Controls.TextBox AddInput(System.Windows.Controls.Panel root, string label, string value)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 5, 0, 2) });
            var box = new System.Windows.Controls.TextBox { MinWidth = 405, MinHeight = 22, Text = value };
            root.Children.Add(box);
            return box;
        }
    }
}
