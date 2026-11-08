using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ReinforceRetainingWallCommand : IExternalCommand
    {
        private sealed class WallFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) { return e is Wall; }
            public bool AllowReference(Reference r, XYZ p) { return false; }
        }
        private sealed class BarChoice
        {
            public RebarBarType Type { get; private set; }
            public string Display { get; private set; }
            public BarChoice(RebarBarType type)
            {
                Type = type;
                Display = type.Name + " | Ø" + UnitUtils.ConvertFromInternalUnits(type.BarNominalDiameter, UnitTypeId.Millimeters).ToString("0.#") + " mm";
            }
        }
        internal sealed class Options { public RebarBarType VerticalA, VerticalB, HorizontalA, HorizontalB, FootingTypeAlong, FootingTypeCross, ConstructiveType; public RebarHookType TopHook, BottomHook, ConstructiveHook; public bool ReplaceExisting, DrawConstructive, DrawAdditional, PickSpanByPoints; public int AdditionalSide; public double CoverTopBottomMm, CoverFacesMm, CoverStartEndMm, HorizontalOffsetStartMm, HorizontalOffsetEndMm, TopOffsetMm, BottomOffsetMm, VerticalSpacingMm, HorizontalSpacingMm, BaseAnchorMm, FootingSpacingAlongMm, FootingSpacingCrossMm, ConstructiveSpacingMm, AdditionalHeightMm, TopHookLengthMm, BottomHookLengthMm; }
        private sealed class OpeningRegion { public double MinAlong, MaxAlong, MinZ, MaxZ; }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Wall> walls = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Wall>().Distinct().ToList();
                if (walls.Count == 0)
                    walls = ui.Selection.PickObjects(ObjectType.Element, new WallFilter(), "Chọn tường thẳng đứng làm thân tường chắn")
                        .Select(r => doc.GetElement(r)).OfType<Wall>().Distinct().ToList();
                if (walls.Count == 0) return Result.Cancelled;
                List<RebarBarType> types = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType))
                    .Cast<RebarBarType>().OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (types.Count == 0) throw new InvalidOperationException("Project chưa có RebarBarType.");
                List<RebarHookType> hookTypes = new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>().OrderBy(h => h.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                Options options = ChooseOptions(data.Application.MainWindowHandle, types, hookTypes);
                if (options == null) return Result.Cancelled;
                var wallSpans = new Dictionary<ElementId, Line>();
                foreach (Wall wall in walls)
                {
                    LocationCurve wallLocation = wall.Location as LocationCurve;
                    Line fullLine = wallLocation == null ? null : wallLocation.Curve as Line;
                    if (fullLine == null) throw new InvalidOperationException("Chỉ hỗ trợ tường có tim thẳng.");
                    wallSpans[wall.Id] = options.PickSpanByPoints ? PickScopeLine(ui, wall, fullLine) : fullLine;
                }

                int completed = 0, totalBars = 0, removedBars = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Cốt thép tường chắn"))
                {
                    tx.Start();
                    foreach (Wall wall in walls)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                int removed = options.ReplaceExisting ? DeleteGeneratedRebar(doc, wall) : 0;
                                int created = ReinforceWall(doc, wall, wallSpans[wall.Id], options);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận cốt thép tường chắn.");
                                completed++;
                                totalBars += created;
                                removedBars += removed;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Tường " + wall.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (completed == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Cốt thép tường chắn", "Không gia cường được tường nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không chấp nhận thay đổi.");
                }
                string report = "Đã tạo " + totalBars + " Rebar cho " + completed + "/" + walls.Count + " tường, gồm thép tường theo từng mặt và lưới móng hai phương lớp trên/dưới nếu có WallFoundation.";
                if (options.ReplaceExisting) report += " Đã thay " + removedBars + " thanh KStruct được đánh dấu trước đó.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " tường:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Cốt thép tường chắn", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Cốt thép tường chắn", ex.Message);
                return Result.Failed;
            }
        }

        private static int DeleteGeneratedRebar(Document doc, Wall wall)
        {
            var prefixes = new List<string> { "KSTR_RW_" + wall.Id.Value + "_" };
            foreach (WallFoundation footing in new FilteredElementCollector(doc).OfClass(typeof(WallFoundation))
                .Cast<WallFoundation>().Where(f => f.WallId == wall.Id))
                prefixes.Add("KSTR_RW_" + footing.Id.Value + "_");
            List<ElementId> ids = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
                .Where(bar =>
                {
                    Parameter partition = bar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
                    if (partition == null || partition.StorageType != StorageType.String) return false;
                    string value = partition.AsString();
                    return !string.IsNullOrEmpty(value) && prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal));
                })
                .Select(bar => bar.Id).ToList();
            if (ids.Count > 0) doc.Delete(ids);
            return ids.Count;
        }

        private static Line PickScopeLine(UIDocument ui, Wall wall, Line fullLine)
        {
            XYZ first = ui.Selection.PickPoint("Chọn điểm đầu phạm vi cốt thép trên tường " + wall.Id.Value);
            XYZ second = ui.Selection.PickPoint("Chọn điểm cuối phạm vi cốt thép trên tường " + wall.Id.Value);
            XYZ axis = fullLine.Direction.Normalize();
            XYZ origin = fullLine.GetEndPoint(0);
            double a = Math.Max(0, Math.Min(fullLine.Length, (first - origin).DotProduct(axis)));
            double b = Math.Max(0, Math.Min(fullLine.Length, (second - origin).DotProduct(axis)));
            double start = Math.Min(a, b), end = Math.Max(a, b);
            if (end - start <= ui.Document.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Hai điểm chọn phải xác định được một đoạn có chiều dài trên tường.");
            return Line.CreateBound(origin + axis * start, origin + axis * end);
        }

        private static int ReinforceWall(Document doc, Wall wall, Line axisLine, Options o)
        {
            BoundingBoxXYZ box = wall.get_BoundingBox(null);
            if (axisLine == null || box == null) throw new InvalidOperationException("Chỉ hỗ trợ tường thẳng có bounding box.");
            XYZ axis = axisLine.Direction.Normalize();
            XYZ normal = wall.Orientation.Normalize();
            if (Math.Abs(axis.Z) > 1e-3 || Math.Abs(normal.Z) > 1e-3 || Math.Abs(axis.DotProduct(normal)) > 1e-3)
                throw new InvalidOperationException("Chỉ hỗ trợ tường đứng, thẳng và có phương hai mặt xác định.");
            double coverTopBottom = UnitUtils.ConvertToInternalUnits(o.CoverTopBottomMm, UnitTypeId.Millimeters);
            double coverFaces = UnitUtils.ConvertToInternalUnits(o.CoverFacesMm, UnitTypeId.Millimeters);
            double coverStartEnd = UnitUtils.ConvertToInternalUnits(o.CoverStartEndMm, UnitTypeId.Millimeters);
            double anchor = UnitUtils.ConvertToInternalUnits(o.BaseAnchorMm, UnitTypeId.Millimeters);
            double verticalSpacing = UnitUtils.ConvertToInternalUnits(o.VerticalSpacingMm, UnitTypeId.Millimeters);
            double horizontalSpacing = UnitUtils.ConvertToInternalUnits(o.HorizontalSpacingMm, UnitTypeId.Millimeters);
            double maxDia = new[] { o.VerticalA.BarNominalDiameter, o.VerticalB.BarNominalDiameter, o.HorizontalA.BarNominalDiameter, o.HorizontalB.BarNominalDiameter }.Max();
            double halfThickness = wall.Width / 2.0;
            double endClear = coverStartEnd + maxDia / 2.0;
            double horizontalStartClear = endClear + UnitUtils.ConvertToInternalUnits(o.HorizontalOffsetStartMm, UnitTypeId.Millimeters);
            double horizontalEndClear = endClear + UnitUtils.ConvertToInternalUnits(o.HorizontalOffsetEndMm, UnitTypeId.Millimeters);
            double zClear = coverTopBottom + maxDia / 2.0;
            double bottomOffset = o.BottomOffsetMm > 0 ? UnitUtils.ConvertToInternalUnits(o.BottomOffsetMm, UnitTypeId.Millimeters) : zClear;
            double topOffset = o.TopOffsetMm > 0 ? UnitUtils.ConvertToInternalUnits(o.TopOffsetMm, UnitTypeId.Millimeters) : zClear;
            if ((o.TopOffsetMm > 0 && topOffset < zClear) || (o.BottomOffsetMm > 0 && bottomOffset < zClear)) throw new InvalidOperationException("Độ lùi trên/dưới phải đủ cho cover và bán kính thanh.");
            double length = axisLine.Length;
            double z0 = box.Min.Z + bottomOffset;
            double z1 = box.Max.Z - topOffset;
            if (halfThickness <= coverFaces + maxDia / 2.0 || length <= 2 * endClear || z1 <= z0)
                throw new InvalidOperationException("Kích thước tường không đủ cho cover và đường kính thép.");
            XYZ mid = axisLine.Evaluate(0.5, true);
            List<OpeningRegion> openings = ReadOpeningRegions(doc, wall, axisLine);
            double openingAlongClear = coverStartEnd + maxDia / 2.0;
            double openingZClear = coverTopBottom + maxDia / 2.0;
            int verticalCount = Count(length - 2 * endClear, verticalSpacing);
            int horizontalCount = Count(z1 - z0, horizontalSpacing);
            if (verticalCount * 2 + horizontalCount * 2 > 2400)
                throw new InvalidOperationException("Mật độ thép vượt quá 2400 thanh cho một tường.");
            int created = 0;
            double baseZ = z0;
            for (int side = -1; side <= 1; side += 2)
            {
                RebarBarType verticalType = side < 0 ? o.VerticalA : o.VerticalB;
                RebarBarType horizontalType = side < 0 ? o.HorizontalA : o.HorizontalB;
                double vOffset = halfThickness - coverFaces - verticalType.BarNominalDiameter / 2.0;
                double hOffset = halfThickness - coverFaces - horizontalType.BarNominalDiameter / 2.0;
                if (vOffset <= 0 || hOffset <= 0) throw new InvalidOperationException("Cover hoặc đường kính thép không vừa bề dày tường.");
                for (int i = 0; i < verticalCount; i++)
                {
                    double s = -length / 2.0 + endClear + (verticalCount == 1 ? (length - 2 * endClear) / 2.0 : (length - 2 * endClear) * i / (verticalCount - 1.0));
                    IEnumerable<Tuple<double, double>> blocked = openings
                        .Where(opening => s >= opening.MinAlong - openingAlongClear && s <= opening.MaxAlong + openingAlongClear)
                        .Select(opening => Tuple.Create(opening.MinZ - openingZClear, opening.MaxZ + openingZClear));
                    foreach (Tuple<double, double> interval in VisibleIntervals(z0, z1, blocked))
                    {
                        if (interval.Item2 - interval.Item1 <= doc.Application.ShortCurveTolerance) continue;
                        bool atBottom = interval.Item1 <= z0 + doc.Application.ShortCurveTolerance;
                        bool atTop = interval.Item2 >= z1 - doc.Application.ShortCurveTolerance;
                        XYZ root = new XYZ(mid.X, mid.Y, interval.Item1) + axis * s + normal * (side * vOffset);
                        XYZ top = new XYZ(mid.X, mid.Y, interval.Item2) + axis * s + normal * (side * vOffset);
                        var curves = new List<Curve>();
                        double bottomLeg = o.BottomHookLengthMm > 0
                            ? UnitUtils.ConvertToInternalUnits(o.BottomHookLengthMm, UnitTypeId.Millimeters)
                            : anchor;
                        if (atBottom && bottomLeg > doc.Application.ShortCurveTolerance)
                        {
                            // Keep the L leg inside the wall thickness, away from the exposed face.
                            XYZ toe = root - normal * (side * bottomLeg);
                            curves.Add(Line.CreateBound(toe, root));
                        }
                        curves.Add(Line.CreateBound(root, top));
                        double topHookLength = UnitUtils.ConvertToInternalUnits(o.TopHookLengthMm, UnitTypeId.Millimeters);
                        if (atTop && topHookLength > doc.Application.ShortCurveTolerance)
                        {
                            XYZ hookEnd = top - normal * (side * topHookLength);
                            curves.Add(Line.CreateBound(top, hookEnd));
                        }
                        CreateRebar(doc, wall, verticalType, curves, axis,
                            atBottom && o.BottomHookLengthMm <= 0 ? o.BottomHook : null,
                            atTop && topHookLength <= doc.Application.ShortCurveTolerance ? o.TopHook : null, "V" + side);
                        created++;
                    }
                }
                for (int i = 0; i < horizontalCount; i++)
                {
                    double z = horizontalCount == 1 ? (z0 + z1) / 2.0 : z0 + (z1 - z0) * i / (horizontalCount - 1.0);
                    IEnumerable<Tuple<double, double>> blocked = openings
                        .Where(opening => z >= opening.MinZ - openingZClear && z <= opening.MaxZ + openingZClear)
                        .Select(opening => Tuple.Create(opening.MinAlong - openingAlongClear, opening.MaxAlong + openingAlongClear));
                    foreach (Tuple<double, double> interval in VisibleIntervals(-length / 2.0 + horizontalStartClear, length / 2.0 - horizontalEndClear, blocked))
                    {
                        if (interval.Item2 - interval.Item1 <= doc.Application.ShortCurveTolerance) continue;
                        XYZ a = mid + axis * interval.Item1 + normal * (side * hOffset) + XYZ.BasisZ * (z - mid.Z);
                        XYZ b = mid + axis * interval.Item2 + normal * (side * hOffset) + XYZ.BasisZ * (z - mid.Z);
                        CreateRebar(doc, wall, horizontalType, new List<Curve> { Line.CreateBound(a, b) }, normal, null, null, "H" + side);
                        created++;
                    }
                }
                foreach (OpeningRegion opening in openings)
                    created += CreateOpeningEdgeBars(doc, wall, mid, axis, normal, side, vOffset, hOffset,
                        verticalType, horizontalType, opening, openings, length, z0, z1, horizontalStartClear, horizontalEndClear, openingAlongClear, openingZClear);
            }
            if (o.DrawAdditional) created += CreateAdditionalVerticalBars(doc, wall, axisLine, axis, normal, box, baseZ, z1, endClear, verticalSpacing, openings, openingAlongClear, openingZClear, o);
            if (o.DrawConstructive) created += CreateConstructiveRebar(doc, wall, axisLine, axis, normal, box, z0, z1, horizontalCount, openings, openingAlongClear, openingZClear, o);
            foreach (WallFoundation footing in new FilteredElementCollector(doc).OfClass(typeof(WallFoundation)).Cast<WallFoundation>().Where(f => f.WallId == wall.Id))
                created += ReinforceFooting(doc, footing, o, normal, axis);
            return created;
        }

        private static int CreateAdditionalVerticalBars(Document doc, Wall wall, Line axisLine, XYZ axis, XYZ normal,
            BoundingBoxXYZ box, double bottomZ, double wallTopZ, double endClear, double spacing,
            IList<OpeningRegion> openings, double openingAlongClear, double openingZClear, Options o)
        {
            int count = Count(axisLine.Length - 2.0 * endClear, spacing);
            if (count < 1) return 0;
            double height = UnitUtils.ConvertToInternalUnits(o.AdditionalHeightMm, UnitTypeId.Millimeters);
            double coverFaces = UnitUtils.ConvertToInternalUnits(o.CoverFacesMm, UnitTypeId.Millimeters);
            double anchor = UnitUtils.ConvertToInternalUnits(o.BaseAnchorMm, UnitTypeId.Millimeters);
            RebarBarType type = o.AdditionalSide == 0 ? o.VerticalA : o.VerticalB;
            int side = o.AdditionalSide == 0 ? -1 : 1;
            double faceOffset = wall.Width / 2.0 - coverFaces - type.BarNominalDiameter / 2.0;
            if (height <= doc.Application.ShortCurveTolerance || bottomZ + height > wallTopZ + doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Chiều cao thanh bổ sung phải nằm trong chiều cao tường.");
            if (faceOffset <= doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Cover không đủ chỗ cho thanh bổ sung trong bề dày tường.");

            XYZ start = axisLine.GetEndPoint(0);
            double upperZ = Math.Min(bottomZ + height, wallTopZ);
            int created = 0;
            for (int i = 0; i < count; i++)
            {
                double along = -axisLine.Length / 2.0 + endClear + (count == 1 ? (axisLine.Length - 2.0 * endClear) / 2.0 :
                    (axisLine.Length - 2.0 * endClear) * i / (count - 1.0));
                double localAlong = along + axisLine.Length / 2.0;
                IEnumerable<Tuple<double, double>> blocked = openings
                    .Where(opening => along >= opening.MinAlong - openingAlongClear && along <= opening.MaxAlong + openingAlongClear)
                    .Select(opening => Tuple.Create(opening.MinZ - openingZClear, opening.MaxZ + openingZClear));
                foreach (Tuple<double, double> interval in VisibleIntervals(bottomZ, upperZ, blocked))
                {
                    if (interval.Item2 - interval.Item1 <= doc.Application.ShortCurveTolerance) continue;
                    XYZ root = start + axis * localAlong + normal * (side * faceOffset) + XYZ.BasisZ * (interval.Item1 - start.Z);
                    XYZ upper = start + axis * localAlong + normal * (side * faceOffset) + XYZ.BasisZ * (interval.Item2 - start.Z);
                    var curves = new List<Curve>();
                    if (interval.Item1 <= bottomZ + doc.Application.ShortCurveTolerance && anchor > doc.Application.ShortCurveTolerance)
                    {
                        XYZ toe = root - normal * (side * anchor);
                        curves.Add(Line.CreateBound(toe, root));
                    }
                    curves.Add(Line.CreateBound(root, upper));
                    Rebar bar = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.StirrupTie, type, null, null, wall, axis,
                        curves, RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                    if (bar == null) throw new InvalidOperationException("Revit không tạo được thép bổ sung phía " + (side < 0 ? "A" : "B") + ".");
                    Parameter partition = bar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
                    if (partition != null && !partition.IsReadOnly)
                        partition.Set("KSTR_RW_" + wall.Id.Value + "_ADDITIONAL_" + (side < 0 ? "A" : "B") + "_" + i);
                    created++;
                }
            }
            return created;
        }

        private static List<OpeningRegion> ReadOpeningRegions(Document doc, Wall wall, Line axisLine)
        {
            IList<ElementId> insertIds = wall.FindInserts(true, true, true, true);
            var result = new List<OpeningRegion>();
            if (insertIds == null) return result;
            XYZ origin = axisLine.Evaluate(0.5, true);
            XYZ axis = axisLine.Direction.Normalize();
            foreach (ElementId id in insertIds.Distinct())
            {
                Element insert = doc.GetElement(id);
                BoundingBoxXYZ box = insert == null ? null : insert.get_BoundingBox(null);
                if (box == null) throw new InvalidOperationException("Không đọc được bounding box phần tử chèn " + id.Value + "; đã dừng để tránh thép xuyên qua lỗ.");
                Transform transform = box.Transform ?? Transform.Identity;
                var points = new List<XYZ>();
                for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                    points.Add(transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X,
                        y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z)));
                result.Add(new OpeningRegion
                {
                    MinAlong = points.Min(p => (p - origin).DotProduct(axis)),
                    MaxAlong = points.Max(p => (p - origin).DotProduct(axis)),
                    MinZ = points.Min(p => p.Z), MaxZ = points.Max(p => p.Z)
                });
            }
            return result;
        }

        private static List<Tuple<double, double>> VisibleIntervals(double start, double end,
            IEnumerable<Tuple<double, double>> blocked)
        {
            var visible = new List<Tuple<double, double>>();
            if (end <= start) return visible;
            double cursor = start;
            foreach (Tuple<double, double> interval in blocked
                .Select(x => Tuple.Create(Math.Max(start, x.Item1), Math.Min(end, x.Item2)))
                .Where(x => x.Item2 > x.Item1).OrderBy(x => x.Item1))
            {
                if (interval.Item1 > cursor + 1e-6) visible.Add(Tuple.Create(cursor, interval.Item1));
                cursor = Math.Max(cursor, interval.Item2);
                if (cursor >= end - 1e-6) break;
            }
            if (end > cursor + 1e-6) visible.Add(Tuple.Create(cursor, end));
            return visible;
        }

        private static int CreateOpeningEdgeBars(Document doc, Wall wall, XYZ origin, XYZ axis, XYZ normal,
            int side, double verticalOffset, double horizontalOffset, RebarBarType verticalType, RebarBarType horizontalType,
            OpeningRegion opening, IList<OpeningRegion> allOpenings, double length, double zBottom, double zTop,
            double horizontalStartClear, double horizontalEndClear, double alongClear, double zClear)
        {
            double s0 = Math.Max(-length / 2.0 + horizontalStartClear, opening.MinAlong - alongClear);
            double s1 = Math.Min(length / 2.0 - horizontalEndClear, opening.MaxAlong + alongClear);
            double z0 = Math.Max(zBottom, opening.MinZ - zClear);
            double z1 = Math.Min(zTop, opening.MaxZ + zClear);
            if (s1 <= s0 || z1 <= z0) return 0;
            int created = 0;
            foreach (double z in new[] { z0, z1 })
            {
                IEnumerable<Tuple<double, double>> blocked = allOpenings.Where(other => !ReferenceEquals(other, opening) &&
                        z >= other.MinZ - zClear && z <= other.MaxZ + zClear)
                    .Select(other => Tuple.Create(other.MinAlong - alongClear, other.MaxAlong + alongClear));
                foreach (Tuple<double, double> span in VisibleIntervals(s0, s1, blocked))
                {
                    if (span.Item2 - span.Item1 <= doc.Application.ShortCurveTolerance) continue;
                    XYZ a = origin + axis * span.Item1 + normal * (side * horizontalOffset) + XYZ.BasisZ * (z - origin.Z);
                    XYZ b = origin + axis * span.Item2 + normal * (side * horizontalOffset) + XYZ.BasisZ * (z - origin.Z);
                    CreateRebar(doc, wall, horizontalType, new List<Curve> { Line.CreateBound(a, b) }, normal, null, null, "OPEN_H");
                    created++;
                }
            }
            foreach (double s in new[] { s0, s1 })
            {
                IEnumerable<Tuple<double, double>> blocked = allOpenings.Where(other => !ReferenceEquals(other, opening) &&
                        s >= other.MinAlong - alongClear && s <= other.MaxAlong + alongClear)
                    .Select(other => Tuple.Create(other.MinZ - zClear, other.MaxZ + zClear));
                foreach (Tuple<double, double> span in VisibleIntervals(z0, z1, blocked))
                {
                    if (span.Item2 - span.Item1 <= doc.Application.ShortCurveTolerance) continue;
                    XYZ a = origin + axis * s + normal * (side * verticalOffset) + XYZ.BasisZ * (span.Item1 - origin.Z);
                    XYZ b = origin + axis * s + normal * (side * verticalOffset) + XYZ.BasisZ * (span.Item2 - origin.Z);
                    CreateRebar(doc, wall, verticalType, new List<Curve> { Line.CreateBound(a, b) }, axis, null, null, "OPEN_V");
                    created++;
                }
            }
            return created;
        }

        private static int CreateConstructiveRebar(Document doc, Wall wall, Line axisLine, XYZ axis, XYZ normal,
            BoundingBoxXYZ box, double z0, double z1, int horizontalCount, IList<OpeningRegion> openings,
            double openingAlongClear, double openingZClear, Options o)
        {
            RebarBarType type = o.ConstructiveType;
            double diameter = type.BarNominalDiameter;
            double bend = type.StirrupTieBendDiameter;
            double coverFaces = UnitUtils.ConvertToInternalUnits(o.CoverFacesMm, UnitTypeId.Millimeters);
            double coverEnds = UnitUtils.ConvertToInternalUnits(o.CoverStartEndMm, UnitTypeId.Millimeters);
            double coverTopBottom = UnitUtils.ConvertToInternalUnits(o.CoverTopBottomMm, UnitTypeId.Millimeters);
            double faceOffset = wall.Width / 2.0 - coverFaces - bend / 2.0 - diameter / 2.0;
            double arrayLength = axisLine.Length - 2.0 * coverEnds - diameter;
            if (faceOffset <= doc.Application.ShortCurveTolerance || arrayLength <= doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Không đủ bề rộng/chiều dài để bố trí thép constructive.");
            XYZ start = axisLine.GetEndPoint(0);
            double horizontalDia = Math.Max(o.HorizontalA.BarNominalDiameter, o.HorizontalB.BarNominalDiameter);
            double spacing = UnitUtils.ConvertToInternalUnits(o.ConstructiveSpacingMm, UnitTypeId.Millimeters);
            double z0Offset = horizontalDia / 2.0 + bend / 2.0 + diameter / 2.0;
            int alongCount = Count(arrayLength, spacing);
            if ((long)alongCount * ((horizontalCount + 1) / 2) > 2400)
                throw new InvalidOperationException("Mật độ thép constructive vượt quá 2400 thanh trên một tường.");
            int created = 0;
            // BimSpeed places transverse constructive sets at alternate horizontal-bar levels.
            for (int i = 0; i < horizontalCount; i += 2)
            {
                double z = (horizontalCount == 1 ? (z0 + z1) / 2.0 : z0 + (z1 - z0) * i / (horizontalCount - 1.0)) + z0Offset;
                if (z < box.Min.Z + coverTopBottom + bend / 2.0 + diameter / 2.0 ||
                    z > box.Max.Z - coverTopBottom - bend / 2.0 - diameter / 2.0) continue;
                for (int j = 0; j < alongCount; j++)
                {
                    double localAlong = coverEnds + diameter / 2.0 +
                        (alongCount == 1 ? arrayLength / 2.0 : arrayLength * j / (alongCount - 1.0));
                    double along = localAlong - axisLine.Length / 2.0;
                    bool blocked = openings.Any(opening => along >= opening.MinAlong - openingAlongClear &&
                        along <= opening.MaxAlong + openingAlongClear && z >= opening.MinZ - openingZClear && z <= opening.MaxZ + openingZClear);
                    if (blocked) continue;
                    XYZ a = start + axis * localAlong - normal * faceOffset + XYZ.BasisZ * (z - start.Z);
                    XYZ b = start + axis * localAlong + normal * faceOffset + XYZ.BasisZ * (z - start.Z);
                    Rebar bar = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.StirrupTie, type, o.ConstructiveHook, o.ConstructiveHook,
                        wall, axis, new List<Curve> { Line.CreateBound(a, b) }, RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                    if (bar == null) throw new InvalidOperationException("Revit không tạo được thanh constructive tại lớp ngang " + (i + 1) + ".");
                    Parameter partition = bar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
                    if (partition != null && !partition.IsReadOnly)
                        partition.Set("KSTR_RW_" + wall.Id.Value + "_CONSTRUCTIVE_" + (i + 1) + "_" + (j + 1));
                    created++;
                }
            }
            return created;
        }

        private sealed class FootingVertex { public double Along, Cross; public FootingVertex(double a, double c) { Along = a; Cross = c; } }
        private sealed class FootingPolygon { public List<FootingVertex> Outer; public List<List<FootingVertex>> Holes; }
        private sealed class FootingLayer { public double Z; public List<FootingPolygon> Polygons; }

        internal static int ReinforceFooting(Document doc, WallFoundation footing, Options o, XYZ normal, XYZ axis)
        {
            double cover = UnitUtils.ConvertToInternalUnits(o.CoverTopBottomMm, UnitTypeId.Millimeters);
            double diaAlong = o.FootingTypeAlong.BarNominalDiameter;
            double diaCross = o.FootingTypeCross.BarNominalDiameter;
            double clearAlong = cover + diaAlong / 2.0;
            double clearCross = cover + diaCross / 2.0;
            double spacingAlong = UnitUtils.ConvertToInternalUnits(o.FootingSpacingAlongMm, UnitTypeId.Millimeters);
            double spacingCross = UnitUtils.ConvertToInternalUnits(o.FootingSpacingCrossMm, UnitTypeId.Millimeters);
            FootingLayer bottom = GetFootingLayer(doc, footing, axis, normal, false);
            FootingLayer top = GetFootingLayer(doc, footing, axis, normal, true);
            double maxClear = Math.Max(clearAlong, clearCross);
            if (top.Z - bottom.Z <= 2 * maxClear) throw new InvalidOperationException("Chiều dày WallFoundation không đủ cho hai lớp thép theo cover.");
            return CreateFootingLayer(doc, footing, bottom, o.FootingTypeAlong, o.FootingTypeCross, axis, normal,
                    bottom.Z + maxClear, clearAlong, clearCross, spacingAlong, spacingCross, "BOT") +
                CreateFootingLayer(doc, footing, top, o.FootingTypeAlong, o.FootingTypeCross, axis, normal,
                    top.Z - maxClear, clearAlong, clearCross, spacingAlong, spacingCross, "TOP");
        }

        private static FootingLayer GetFootingLayer(Document doc, WallFoundation footing, XYZ axis, XYZ normal, bool top)
        {
            var solids = new List<Solid>();
            GeometryElement geometry = footing.get_Geometry(new Autodesk.Revit.DB.Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = true });
            if (geometry != null) CollectFootingSolids(geometry, solids);
            var faces = solids.SelectMany(s => s.Faces.Cast<Face>()).OfType<PlanarFace>()
                .Where(f => top ? f.FaceNormal.Z > 0.999 : f.FaceNormal.Z < -0.999).ToList();
            if (faces.Count == 0) throw new InvalidOperationException("WallFoundation không có mặt phẳng ngang để lấy biên thép.");
            double z = top ? faces.Max(f => f.Origin.Z) : faces.Min(f => f.Origin.Z);
            double tolerance = Math.Max(doc.Application.ShortCurveTolerance * 4.0, UnitUtils.ConvertToInternalUnits(2, UnitTypeId.Millimeters));
            var polygons = new List<FootingPolygon>();
            foreach (PlanarFace face in faces.Where(f => Math.Abs(f.Origin.Z - z) <= tolerance))
            {
                IList<CurveLoop> loops;
                try { loops = face.GetEdgesAsCurveLoops(); } catch { continue; }
                var rings = loops.Select(loop => FootingLoop(loop, axis, normal, doc.Application.ShortCurveTolerance)).Where(r => r.Count >= 3).ToList();
                if (rings.Count == 0) continue;
                List<FootingVertex> outer = rings.OrderByDescending(FootingArea).First();
                polygons.Add(new FootingPolygon { Outer = outer, Holes = rings.Where(r => !ReferenceEquals(r, outer)).ToList() });
            }
            if (polygons.Count == 0) throw new InvalidOperationException("Không đọc được đường biên mặt WallFoundation.");
            return new FootingLayer { Z = z, Polygons = polygons };
        }

        private static void CollectFootingSolids(GeometryElement geometry, IList<Solid> solids)
        {
            foreach (GeometryObject item in geometry)
            {
                Solid solid = item as Solid;
                if (solid != null) { if (solid.Faces.Size > 0 && solid.Volume > 1e-9) solids.Add(solid); continue; }
                GeometryInstance instance = item as GeometryInstance;
                if (instance != null) CollectFootingSolids(instance.GetInstanceGeometry(), solids);
            }
        }

        private static List<FootingVertex> FootingLoop(CurveLoop loop, XYZ axis, XYZ normal, double tolerance)
        {
            var points = new List<FootingVertex>();
            foreach (Curve curve in loop)
                foreach (XYZ point in curve.Tessellate())
                {
                    var vertex = new FootingVertex(point.DotProduct(axis), point.DotProduct(normal));
                    if (points.Count == 0 || FootingDistance(points[points.Count - 1], vertex) > tolerance / 10.0) points.Add(vertex);
                }
            if (points.Count > 1 && FootingDistance(points[0], points[points.Count - 1]) <= tolerance / 10.0) points.RemoveAt(points.Count - 1);
            return points;
        }

        private static int CreateFootingLayer(Document doc, WallFoundation footing, FootingLayer layer,
            RebarBarType typeAlong, RebarBarType typeCross, XYZ axis, XYZ normal, double z,
            double clearAlong, double clearCross, double spacingAlong, double spacingCross, string layerName)
        {
            int created = 0;
            foreach (FootingPolygon polygon in layer.Polygons)
            {
                double minA = polygon.Outer.Min(p => p.Along), maxA = polygon.Outer.Max(p => p.Along);
                double minC = polygon.Outer.Min(p => p.Cross), maxC = polygon.Outer.Max(p => p.Cross);
                double spanA = maxA - minA - 2 * clearCross, spanC = maxC - minC - 2 * clearAlong;
                if (spanA <= doc.Application.ShortCurveTolerance || spanC <= doc.Application.ShortCurveTolerance)
                    throw new InvalidOperationException("Cover/thép không vừa biên dạng mặt móng.");
                int rowsLong = Count(spanC, spacingAlong), rowsCross = Count(spanA, spacingCross);
                if (rowsLong + rowsCross > 1600) throw new InvalidOperationException("Mật độ thép móng vượt quá 1600 thanh trên một mặt.");
                for (int i = 0; i < rowsLong; i++)
                {
                    double fixedC = minC + clearAlong + (rowsLong == 1 ? spanC / 2.0 : spanC * i / (rowsLong - 1.0));
                    foreach (Tuple<double, double> interval in FootingIntervals(polygon, true, fixedC, clearAlong))
                    {
                        XYZ a = axis * interval.Item1 + normal * fixedC + XYZ.BasisZ * z;
                        XYZ b = axis * interval.Item2 + normal * fixedC + XYZ.BasisZ * z;
                        if (a.DistanceTo(b) <= doc.Application.ShortCurveTolerance) continue;
                        CreateRebar(doc, footing, typeAlong, new List<Curve> { Line.CreateBound(a, b) }, XYZ.BasisZ, null, null, "F_LONG_" + layerName); created++;
                    }
                }
                for (int i = 0; i < rowsCross; i++)
                {
                    double fixedA = minA + clearCross + (rowsCross == 1 ? spanA / 2.0 : spanA * i / (rowsCross - 1.0));
                    foreach (Tuple<double, double> interval in FootingIntervals(polygon, false, fixedA, clearCross))
                    {
                        XYZ a = axis * fixedA + normal * interval.Item1 + XYZ.BasisZ * z;
                        XYZ b = axis * fixedA + normal * interval.Item2 + XYZ.BasisZ * z;
                        if (a.DistanceTo(b) <= doc.Application.ShortCurveTolerance) continue;
                        CreateRebar(doc, footing, typeCross, new List<Curve> { Line.CreateBound(a, b) }, axis, null, null, "F_CROSS_" + layerName); created++;
                    }
                }
            }
            return created;
        }

        private static List<Tuple<double, double>> FootingIntervals(FootingPolygon polygon, bool along, double fixedValue, double inset)
        {
            var intersections = new List<double>();
            AddFootingIntersections(polygon.Outer, along, fixedValue, intersections);
            foreach (List<FootingVertex> hole in polygon.Holes) AddFootingIntersections(hole, along, fixedValue, intersections);
            intersections.Sort();
            var unique = intersections.Where((v, i) => i == 0 || Math.Abs(v - intersections[i - 1]) > 1e-7).ToList();
            var result = new List<Tuple<double, double>>();
            for (int i = 0; i + 1 < unique.Count; i++)
            {
                double a = unique[i], b = unique[i + 1], mid = (a + b) / 2.0;
                if (b - a <= 2 * inset) continue;
                double x = along ? mid : fixedValue, y = along ? fixedValue : mid;
                if (FootingInside(polygon.Outer, x, y) && !polygon.Holes.Any(h => FootingInside(h, x, y)))
                    result.Add(Tuple.Create(a + inset, b - inset));
            }
            return result;
        }

        private static void AddFootingIntersections(IList<FootingVertex> ring, bool along, double fixedValue, IList<double> output)
        {
            for (int i = 0; i < ring.Count; i++)
            {
                FootingVertex a = ring[i], b = ring[(i + 1) % ring.Count];
                double fa = along ? a.Cross : a.Along, fb = along ? b.Cross : b.Along;
                if ((fa <= fixedValue && fb > fixedValue) || (fb <= fixedValue && fa > fixedValue))
                {
                    double t = (fixedValue - fa) / (fb - fa);
                    double va = along ? a.Along : a.Cross, vb = along ? b.Along : b.Cross;
                    output.Add(va + t * (vb - va));
                }
            }
        }

        private static bool FootingInside(IList<FootingVertex> ring, double along, double cross)
        {
            bool inside = false;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                FootingVertex a = ring[i], b = ring[j];
                if ((a.Cross > cross) != (b.Cross > cross) && along < (b.Along - a.Along) * (cross - a.Cross) / (b.Cross - a.Cross) + a.Along) inside = !inside;
            }
            return inside;
        }

        private static double FootingArea(IList<FootingVertex> ring)
        {
            double area = 0;
            for (int i = 0; i < ring.Count; i++) { FootingVertex a = ring[i], b = ring[(i + 1) % ring.Count]; area += a.Along * b.Cross - b.Along * a.Cross; }
            return Math.Abs(area) / 2.0;
        }

        private static double FootingDistance(FootingVertex a, FootingVertex b)
        {
            double x = a.Along - b.Along, y = a.Cross - b.Cross;
            return Math.Sqrt(x * x + y * y);
        }

        private static int Count(double span, double spacing)
        {
            if (span <= 0 || spacing <= 0) throw new InvalidOperationException("Khoảng cách thép không hợp lệ.");
            return Math.Max(1, (int)Math.Ceiling(span / spacing) + 1);
        }

        private static void CreateRebar(Document doc, Element host, RebarBarType type, IList<Curve> curves, XYZ normal, RebarHookType startHook, RebarHookType endHook, string role)
        {
            Rebar bar = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.Standard, type, startHook, endHook, host, normal,
                curves, RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
            if (bar == null) throw new InvalidOperationException("Revit không tạo được thanh " + role + ".");
            Parameter partition = bar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
            if (partition != null && !partition.IsReadOnly) partition.Set("KSTR_RW_" + host.Id.Value + "_" + role);
        }

        private static Options ChooseOptions(IntPtr owner, IList<RebarBarType> types, IList<RebarHookType> hookTypes)
        {
            var choices = types.Select(t => new BarChoice(t)).ToList();
            var window = new Window { Title = "Cốt thép tường chắn - KStruct", Width = 500, Height = 740,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false };
            var root = new StackPanel { Margin = new Thickness(12) };
            var profileLabel = new TextBlock { Text = "Cấu hình đã lưu:", Margin = new Thickness(0, 0, 0, 3) };
            root.Children.Add(profileLabel);
            var profileSelector = new System.Windows.Controls.ComboBox
            {
                MinWidth = 400, MinHeight = 24, DisplayMemberPath = "Name",
                ItemsSource = RetainingWallProfileStore.Load()
            };
            root.Children.Add(profileSelector);
            System.Windows.Controls.TextBox profileName = AddInput(root, "Tên cấu hình để lưu:", "");
            System.Windows.Controls.ComboBox verticalA = AddType(root, "Thép dọc mặt A:", choices);
            System.Windows.Controls.ComboBox verticalB = AddType(root, "Thép dọc mặt B:", choices);
            System.Windows.Controls.ComboBox horizontalA = AddType(root, "Thép ngang mặt A:", choices);
            System.Windows.Controls.ComboBox horizontalB = AddType(root, "Thép ngang mặt B:", choices);
            var hookChoices = new List<HookChoice> { new HookChoice(null) };
            hookChoices.AddRange(hookTypes.Select(h => new HookChoice(h)));
            System.Windows.Controls.ComboBox topHook = AddHookType(root, "Móc đầu trên của thép dọc:", hookChoices);
            System.Windows.Controls.ComboBox bottomHook = AddHookType(root, "Móc chân thép dọc:", hookChoices);
            System.Windows.Controls.TextBox topHookLength = AddInput(root, "Chiều dài móc trên tùy chỉnh (mm, 0 = dùng loại móc):", "0");
            System.Windows.Controls.TextBox bottomHookLength = AddInput(root, "Chiều dài móc chân tùy chỉnh (mm, 0 = dùng neo/loại móc):", "0");
            System.Windows.Controls.TextBox coverTopBottom = AddInput(root, "Cover đỉnh/đáy tường và móng (mm):", "50");
            System.Windows.Controls.TextBox coverFaces = AddInput(root, "Cover hai mặt tường (mm):", "50");
            System.Windows.Controls.TextBox coverStartEnd = AddInput(root, "Cover đầu/cuối tường (mm):", "50");
            System.Windows.Controls.TextBox horizontalOffsetStart = AddInput(root, "Lùi đầu thép ngang phía bắt đầu (mm):", "0");
            System.Windows.Controls.TextBox horizontalOffsetEnd = AddInput(root, "Lùi đầu thép ngang phía kết thúc (mm):", "0");
            System.Windows.Controls.TextBox vSpacing = AddInput(root, "Khoảng cách thép dọc tối đa (mm):", "200");
            System.Windows.Controls.TextBox hSpacing = AddInput(root, "Khoảng cách thép ngang tối đa (mm):", "200");
            System.Windows.Controls.TextBox topOffset = AddInput(root, "Độ lùi đầu trên thanh dọc (mm, 0 = theo cover):", "0");
            System.Windows.Controls.TextBox bottomOffset = AddInput(root, "Độ lùi chân thanh dọc (mm, 0 = theo cover):", "0");
            System.Windows.Controls.TextBox anchor = AddInput(root, "Chiều dài neo chân (mm, 0 = thanh thẳng):", "500");
            System.Windows.Controls.ComboBox footingTypeAlong = AddType(root, "Thép móng theo chiều dọc tường:", choices);
            System.Windows.Controls.ComboBox footingTypeCross = AddType(root, "Thép móng vuông góc tường:", choices);
            System.Windows.Controls.TextBox footingSpacingAlong = AddInput(root, "Bước thép móng dọc tường (mm):", "200");
            System.Windows.Controls.TextBox footingSpacingCross = AddInput(root, "Bước thép móng vuông góc tường (mm):", "200");
            System.Windows.Controls.ComboBox constructiveType = AddType(root, "Loại thép constructive ngang:", choices);
            var constructiveHookChoices = new List<HookChoice> { new HookChoice(null) };
            constructiveHookChoices.AddRange(hookTypes.Where(h => h.Style == RebarStyle.StirrupTie && Math.Abs(h.HookAngle - Math.PI) < 1e-6)
                .Select(h => new HookChoice(h)));
            System.Windows.Controls.ComboBox constructiveHook = AddHookType(root, "Móc 180° cho thép constructive:", constructiveHookChoices);
            System.Windows.Controls.TextBox constructiveSpacing = AddInput(root, "Khoảng cách constructive tối đa (mm):", "200");
            var drawConstructive = new CheckBox { Content = "Tạo thanh constructive ngang nối hai mặt", IsChecked = false, Margin = new Thickness(0, 8, 0, 0) };
            root.Children.Add(drawConstructive);
            var drawAdditional = new CheckBox { Content = "Tạo thép dọc bổ sung một mặt tường", IsChecked = false, Margin = new Thickness(0, 8, 0, 0) };
            root.Children.Add(drawAdditional);
            var pickSpanByPoints = new CheckBox { Content = "Chọn hai điểm giới hạn phạm vi thép trên mỗi tường", IsChecked = false, Margin = new Thickness(0, 8, 0, 0) };
            root.Children.Add(pickSpanByPoints);
            var additionalSideChoices = new List<SideChoice> { new SideChoice(0, "Mặt A / trái"), new SideChoice(1, "Mặt B / phải") };
            System.Windows.Controls.ComboBox additionalSide = AddSide(root, "Phía đặt thép bổ sung:", additionalSideChoices);
            System.Windows.Controls.TextBox additionalHeight = AddInput(root, "Chiều cao thép bổ sung (mm):", "500");
            var replaceExisting = new CheckBox { Content = "Thay cốt thép tường chắn KStruct đã tạo trước đó", IsChecked = false, Margin = new Thickness(0, 8, 0, 0) };
            root.Children.Add(replaceExisting);
            profileSelector.SelectionChanged += (s, e) =>
            {
                RetainingWallProfile profile = profileSelector.SelectedItem as RetainingWallProfile;
                if (profile == null) return;
                SelectBar(verticalA, choices, profile.VerticalA);
                SelectBar(verticalB, choices, profile.VerticalB);
                SelectBar(horizontalA, choices, profile.HorizontalA);
                SelectBar(horizontalB, choices, profile.HorizontalB);
                SelectBar(footingTypeAlong, choices, !string.IsNullOrWhiteSpace(profile.FootingTypeAlong) ? profile.FootingTypeAlong : profile.FootingType);
                SelectBar(footingTypeCross, choices, !string.IsNullOrWhiteSpace(profile.FootingTypeCross) ? profile.FootingTypeCross : profile.FootingType);
                SelectBar(constructiveType, choices, profile.ConstructiveType);
                SelectHook(topHook, hookChoices, profile.TopHook);
                SelectHook(bottomHook, hookChoices, profile.BottomHook);
                topHookLength.Text = NumberText(profile.TopHookLengthMm);
                bottomHookLength.Text = NumberText(profile.BottomHookLengthMm);
                SelectHook(constructiveHook, constructiveHookChoices, profile.ConstructiveHook);
                drawConstructive.IsChecked = profile.DrawConstructive;
                constructiveSpacing.Text = NumberText(profile.ConstructiveSpacingMm > 0 ? profile.ConstructiveSpacingMm : 200);
                drawAdditional.IsChecked = profile.DrawAdditional;
                pickSpanByPoints.IsChecked = profile.PickSpanByPoints;
                SelectSide(additionalSide, additionalSideChoices, profile.AdditionalSide);
                additionalHeight.Text = NumberText(profile.AdditionalHeightMm > 0 ? profile.AdditionalHeightMm : 500);
                replaceExisting.IsChecked = profile.ReplaceExisting;
                coverTopBottom.Text = NumberText(profile.CoverTopBottomMm > 0 ? profile.CoverTopBottomMm : profile.CoverMm > 0 ? profile.CoverMm : 50);
                coverFaces.Text = NumberText(profile.CoverFacesMm > 0 ? profile.CoverFacesMm : profile.CoverMm > 0 ? profile.CoverMm : 50);
                coverStartEnd.Text = NumberText(profile.CoverStartEndMm > 0 ? profile.CoverStartEndMm : profile.CoverMm > 0 ? profile.CoverMm : 50);
                horizontalOffsetStart.Text = NumberText(profile.HorizontalOffsetStartMm);
                horizontalOffsetEnd.Text = NumberText(profile.HorizontalOffsetEndMm);
                vSpacing.Text = NumberText(profile.VerticalSpacingMm);
                hSpacing.Text = NumberText(profile.HorizontalSpacingMm);
                topOffset.Text = NumberText(profile.TopOffsetMm);
                bottomOffset.Text = NumberText(profile.BottomOffsetMm);
                anchor.Text = NumberText(profile.BaseAnchorMm);
                double legacyFootingSpacing = profile.FootingSpacingMm > 0 ? profile.FootingSpacingMm : 200;
                footingSpacingAlong.Text = NumberText(profile.FootingSpacingAlongMm > 0 ? profile.FootingSpacingAlongMm : legacyFootingSpacing);
                footingSpacingCross.Text = NumberText(profile.FootingSpacingCrossMm > 0 ? profile.FootingSpacingCrossMm : legacyFootingSpacing);
            };
            if (profileSelector.Items.Count > 0) profileSelector.SelectedIndex = 0;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var saveProfile = new Button { Content = "Lưu cấu hình", MinWidth = 92, Margin = new Thickness(0, 0, 6, 0) };
            var deleteProfile = new Button { Content = "Xóa cấu hình", MinWidth = 92, Margin = new Thickness(0, 0, 6, 0) };
            saveProfile.Click += (s, e) =>
            {
                string name = profileName.Text.Trim();
                if (name.Length == 0) { TaskDialog.Show("KStruct", "Nhập tên cấu hình trước khi lưu."); return; }
                double ctb, cf, cse, vs, hs, top, bottom, a, fsAlong, fsCross, cs, ah, topHookMm, bottomHookMm, hStart, hEnd;
                if (!TryReadInputs(coverTopBottom, coverFaces, coverStartEnd, vSpacing, hSpacing, topOffset, bottomOffset, anchor, footingSpacingAlong, footingSpacingCross, constructiveSpacing, out ctb, out cf, out cse, out vs, out hs, out top, out bottom, out a, out fsAlong, out fsCross, out cs) ||
                    !double.TryParse(additionalHeight.Text, out ah) || (drawAdditional.IsChecked == true && ah <= 0) || additionalSide.SelectedItem == null ||
                    !double.TryParse(topHookLength.Text, out topHookMm) || topHookMm < 0 ||
                    !double.TryParse(bottomHookLength.Text, out bottomHookMm) || bottomHookMm < 0 ||
                    !double.TryParse(horizontalOffsetStart.Text, out hStart) || hStart < 0 ||
                    !double.TryParse(horizontalOffsetEnd.Text, out hEnd) || hEnd < 0 ||
                    verticalA.SelectedItem == null || verticalB.SelectedItem == null || horizontalA.SelectedItem == null ||
                    horizontalB.SelectedItem == null || footingTypeAlong.SelectedItem == null || footingTypeCross.SelectedItem == null || constructiveType.SelectedItem == null || constructiveHook.SelectedItem == null || topHook.SelectedItem == null || bottomHook.SelectedItem == null)
                { TaskDialog.Show("KStruct", "Kiểm tra các loại thép và giá trị trước khi lưu."); return; }
                var item = new RetainingWallProfile
                {
                    Name = name,
                    VerticalA = ((BarChoice)verticalA.SelectedItem).Type.Name,
                    VerticalB = ((BarChoice)verticalB.SelectedItem).Type.Name,
                    HorizontalA = ((BarChoice)horizontalA.SelectedItem).Type.Name,
                    HorizontalB = ((BarChoice)horizontalB.SelectedItem).Type.Name,
                    FootingType = ((BarChoice)footingTypeAlong.SelectedItem).Type.Name,
                    FootingTypeAlong = ((BarChoice)footingTypeAlong.SelectedItem).Type.Name,
                    FootingTypeCross = ((BarChoice)footingTypeCross.SelectedItem).Type.Name,
                    TopHook = ((HookChoice)topHook.SelectedItem).Type == null ? "" : ((HookChoice)topHook.SelectedItem).Type.Name,
                    BottomHook = ((HookChoice)bottomHook.SelectedItem).Type == null ? "" : ((HookChoice)bottomHook.SelectedItem).Type.Name,
                    TopHookLengthMm = topHookMm,
                    BottomHookLengthMm = bottomHookMm,
                    HorizontalOffsetStartMm = hStart,
                    HorizontalOffsetEndMm = hEnd,
                    ConstructiveType = ((BarChoice)constructiveType.SelectedItem).Type.Name,
                    ConstructiveHook = ((HookChoice)constructiveHook.SelectedItem).Type == null ? "" : ((HookChoice)constructiveHook.SelectedItem).Type.Name,
                    DrawConstructive = drawConstructive.IsChecked == true,
                    DrawAdditional = drawAdditional.IsChecked == true,
                    PickSpanByPoints = pickSpanByPoints.IsChecked == true,
                    AdditionalSide = ((SideChoice)additionalSide.SelectedItem).Side,
                    AdditionalHeightMm = ah,
                    ReplaceExisting = replaceExisting.IsChecked == true,
                    CoverMm = ctb, CoverTopBottomMm = ctb, CoverFacesMm = cf, CoverStartEndMm = cse, VerticalSpacingMm = vs, HorizontalSpacingMm = hs,
                    BaseAnchorMm = a, FootingSpacingMm = fsAlong, FootingSpacingAlongMm = fsAlong,
                    FootingSpacingCrossMm = fsCross, ConstructiveSpacingMm = cs, TopOffsetMm = top, BottomOffsetMm = bottom
                };
                var saved = RetainingWallProfileStore.Load();
                saved.RemoveAll(x => string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase));
                saved.Add(item);
                RetainingWallProfileStore.Save(saved);
                profileSelector.ItemsSource = null;
                profileSelector.ItemsSource = saved;
                profileSelector.SelectedItem = saved.First(x => string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase));
            };
            deleteProfile.Click += (s, e) =>
            {
                RetainingWallProfile selected = profileSelector.SelectedItem as RetainingWallProfile;
                if (selected == null) return;
                var saved = RetainingWallProfileStore.Load();
                saved.RemoveAll(x => string.Equals(x.Name, selected.Name, StringComparison.CurrentCultureIgnoreCase));
                RetainingWallProfileStore.Save(saved);
                profileSelector.ItemsSource = null;
                profileSelector.ItemsSource = saved;
                profileSelector.SelectedIndex = saved.Count > 0 ? 0 : -1;
            };
            var ok = new Button { Content = "Tạo thép", Width = 88, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (s, e) =>
            {
                double ctb, cf, cse, vs, hs, top, bottom, a, fsAlong, fsCross, cs, ah, topHookMm, bottomHookMm, hStart, hEnd;
                if (verticalA.SelectedItem == null || verticalB.SelectedItem == null || horizontalA.SelectedItem == null || horizontalB.SelectedItem == null || topHook.SelectedItem == null || bottomHook.SelectedItem == null ||
                    footingTypeAlong.SelectedItem == null || footingTypeCross.SelectedItem == null || constructiveType.SelectedItem == null || constructiveHook.SelectedItem == null || !TryReadInputs(coverTopBottom, coverFaces, coverStartEnd, vSpacing, hSpacing, topOffset, bottomOffset, anchor, footingSpacingAlong, footingSpacingCross, constructiveSpacing,
                        out ctb, out cf, out cse, out vs, out hs, out top, out bottom, out a, out fsAlong, out fsCross, out cs) ||
                    !double.TryParse(additionalHeight.Text, out ah) || (drawAdditional.IsChecked == true && ah <= 0) || additionalSide.SelectedItem == null ||
                    !double.TryParse(topHookLength.Text, out topHookMm) || topHookMm < 0 ||
                    !double.TryParse(bottomHookLength.Text, out bottomHookMm) || bottomHookMm < 0 ||
                    !double.TryParse(horizontalOffsetStart.Text, out hStart) || hStart < 0 ||
                    !double.TryParse(horizontalOffsetEnd.Text, out hEnd) || hEnd < 0)
                { TaskDialog.Show("KStruct - Cốt thép tường chắn", "Kiểm tra cover, khoảng cách, chiều dài neo và chiều cao thép bổ sung."); return; }
                window.DialogResult = true;
            };
            buttons.Children.Add(saveProfile); buttons.Children.Add(deleteProfile); buttons.Children.Add(ok); buttons.Children.Add(cancel); root.Children.Add(buttons); window.Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            new WindowInteropHelper(window).Owner = owner;
            if (window.ShowDialog() != true) return null;
            return new Options { VerticalA = ((BarChoice)verticalA.SelectedItem).Type, VerticalB = ((BarChoice)verticalB.SelectedItem).Type, HorizontalA = ((BarChoice)horizontalA.SelectedItem).Type, HorizontalB = ((BarChoice)horizontalB.SelectedItem).Type, TopHook = ((HookChoice)topHook.SelectedItem).Type, BottomHook = ((HookChoice)bottomHook.SelectedItem).Type, ReplaceExisting = replaceExisting.IsChecked == true, FootingTypeAlong = ((BarChoice)footingTypeAlong.SelectedItem).Type, FootingTypeCross = ((BarChoice)footingTypeCross.SelectedItem).Type, ConstructiveType = ((BarChoice)constructiveType.SelectedItem).Type, ConstructiveHook = ((HookChoice)constructiveHook.SelectedItem).Type, DrawConstructive = drawConstructive.IsChecked == true, ConstructiveSpacingMm = double.Parse(constructiveSpacing.Text), FootingSpacingAlongMm = double.Parse(footingSpacingAlong.Text), FootingSpacingCrossMm = double.Parse(footingSpacingCross.Text),
                CoverTopBottomMm = double.Parse(coverTopBottom.Text), CoverFacesMm = double.Parse(coverFaces.Text), CoverStartEndMm = double.Parse(coverStartEnd.Text), TopOffsetMm = double.Parse(topOffset.Text), BottomOffsetMm = double.Parse(bottomOffset.Text), VerticalSpacingMm = double.Parse(vSpacing.Text),
                HorizontalSpacingMm = double.Parse(hSpacing.Text), BaseAnchorMm = double.Parse(anchor.Text),
                DrawAdditional = drawAdditional.IsChecked == true, AdditionalSide = ((SideChoice)additionalSide.SelectedItem).Side,
                AdditionalHeightMm = double.Parse(additionalHeight.Text), TopHookLengthMm = double.Parse(topHookLength.Text),
                BottomHookLengthMm = double.Parse(bottomHookLength.Text), HorizontalOffsetStartMm = double.Parse(horizontalOffsetStart.Text),
                HorizontalOffsetEndMm = double.Parse(horizontalOffsetEnd.Text), PickSpanByPoints = pickSpanByPoints.IsChecked == true };
        }

        private static string NumberText(double value)
        {
            return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void SelectBar(System.Windows.Controls.ComboBox combo, IList<BarChoice> choices, string typeName)
        {
            combo.SelectedItem = choices.FirstOrDefault(x => string.Equals(x.Type.Name, typeName, StringComparison.OrdinalIgnoreCase))
                ?? choices.FirstOrDefault();
        }

        private static void SelectHook(System.Windows.Controls.ComboBox combo, IList<HookChoice> choices, string typeName)
        {
            combo.SelectedItem = choices.FirstOrDefault(x => string.Equals(x.Type == null ? "" : x.Type.Name, typeName ?? "", StringComparison.OrdinalIgnoreCase))
                ?? choices.FirstOrDefault();
        }

        private static void SelectSide(System.Windows.Controls.ComboBox combo, IList<SideChoice> choices, int side)
        {
            combo.SelectedItem = choices.FirstOrDefault(x => x.Side == side) ?? choices.FirstOrDefault();
        }

        private static bool TryReadInputs(System.Windows.Controls.TextBox coverTopBottom, System.Windows.Controls.TextBox coverFaces,
            System.Windows.Controls.TextBox coverStartEnd, System.Windows.Controls.TextBox verticalSpacing,
            System.Windows.Controls.TextBox horizontalSpacing, System.Windows.Controls.TextBox topOffset,
            System.Windows.Controls.TextBox bottomOffset, System.Windows.Controls.TextBox anchor,
            System.Windows.Controls.TextBox footingSpacingAlong, System.Windows.Controls.TextBox footingSpacingCross,
            System.Windows.Controls.TextBox constructiveSpacing,
            out double ctb, out double cf, out double cse, out double vs, out double hs, out double top,
            out double bottom, out double a, out double fsAlong, out double fsCross, out double cs)
        {
            ctb = cf = cse = vs = hs = top = bottom = a = fsAlong = fsCross = cs = 0;
            return double.TryParse(coverTopBottom.Text, out ctb) && ctb > 0 &&
                double.TryParse(coverFaces.Text, out cf) && cf > 0 &&
                double.TryParse(coverStartEnd.Text, out cse) && cse > 0 &&
                double.TryParse(verticalSpacing.Text, out vs) && vs > 0 &&
                double.TryParse(horizontalSpacing.Text, out hs) && hs > 0 &&
                double.TryParse(topOffset.Text, out top) && top >= 0 &&
                double.TryParse(bottomOffset.Text, out bottom) && bottom >= 0 &&
                double.TryParse(anchor.Text, out a) && a >= 0 &&
                double.TryParse(footingSpacingAlong.Text, out fsAlong) && fsAlong > 0 &&
                double.TryParse(footingSpacingCross.Text, out fsCross) && fsCross > 0 &&
                double.TryParse(constructiveSpacing.Text, out cs) && cs > 0;
        }

        private sealed class SideChoice
        {
            public int Side { get; private set; }
            public string Display { get; private set; }
            public SideChoice(int side, string display) { Side = side; Display = display; }
        }

        private sealed class HookChoice
        {
            public RebarHookType Type { get; private set; }
            public string Display { get; private set; }
            public HookChoice(RebarHookType type) { Type = type; Display = type == null ? "Không dùng móc" : type.Name; }
        }

        private static System.Windows.Controls.ComboBox AddHookType(System.Windows.Controls.Panel root, string label, IList<HookChoice> choices)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 3) });
            var combo = new System.Windows.Controls.ComboBox { MinWidth = 450, MinHeight = 24, DisplayMemberPath = "Display", ItemsSource = choices, SelectedIndex = 0 };
            root.Children.Add(combo);
            return combo;
        }
        private static System.Windows.Controls.ComboBox AddSide(System.Windows.Controls.Panel root, string label, IList<SideChoice> choices)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 5, 0, 2) });
            var combo = new System.Windows.Controls.ComboBox { MinWidth = 400, MinHeight = 24, DisplayMemberPath = "Display", ItemsSource = choices, SelectedIndex = 0 };
            root.Children.Add(combo);
            return combo;
        }
        private static System.Windows.Controls.ComboBox AddType(System.Windows.Controls.Panel root, string label, IList<BarChoice> choices)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 3) });
            var combo = new System.Windows.Controls.ComboBox { MinWidth = 400, MinHeight = 24, DisplayMemberPath = "Display", ItemsSource = choices, SelectedIndex = 0 };
            root.Children.Add(combo);
            return combo;
        }
        private static System.Windows.Controls.TextBox AddInput(System.Windows.Controls.Panel root, string label, string value)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 5, 0, 2) });
            var box = new System.Windows.Controls.TextBox { MinWidth = 400, MinHeight = 22, Text = value };
            root.Children.Add(box);
            return box;
        }
    }
}









