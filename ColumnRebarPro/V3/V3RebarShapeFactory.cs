using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ColumnRebarPro.V3
{
    internal enum V3RebarShapeGroup { Longitudinal, Anchor, OuterTie, CrossTie }

    internal sealed class V3RebarShapeChoice
    {
        internal string UniqueId;
        internal string Label;
        public override string ToString() { return Label; }
    }

    internal static class V3RebarShapeFactory
    {
        internal static List<V3RebarShapeChoice> Choices(Document doc, RebarBarType barType,
            V3RebarShapeGroup group, V3TieStyle tieStyle, bool alternating)
        {
            if (doc == null || barType == null) return new List<V3RebarShapeChoice>();
            return new FilteredElementCollector(doc).OfClass(typeof(RebarShape)).Cast<RebarShape>()
                .Where(shape => IsCandidate(shape, group, tieStyle, alternating) && shape.GetAllowed(barType))
                .OrderBy(shape => shape.Name, StringComparer.OrdinalIgnoreCase)
                .Select(shape => new V3RebarShapeChoice {
                    UniqueId = shape.UniqueId,
                    Label = shape.Name + " [" + SegmentCount(shape) + " đoạn; " + shape.Id.Value + "]"
                }).ToList();
        }

        internal static RebarShape Resolve(Document doc, string uniqueId, V3RebarShapeGroup group,
            V3TieStyle tieStyle, bool alternating, RebarBarType barType, ElementId columnId)
        {
            RebarShape shape = string.IsNullOrWhiteSpace(uniqueId) ? null : doc.GetElement(uniqueId) as RebarShape;
            if (shape == null || barType == null || !IsCandidate(shape, group, tieStyle, alternating) ||
                !shape.GetAllowed(barType))
                throw new InvalidOperationException("Cột " + columnId.Value + ": chưa chọn Rebar Shape có sẵn phù hợp cho nhóm " +
                    GroupName(group) + ". Không tạo Shape mới.");
            return shape;
        }

        internal static Rebar Create(Document doc, RebarShape shape, RebarBarType barType, Element host,
            XYZ origin, XYZ xVector, XYZ yVector, ElementId columnId, string groupName,
            RebarHookType startHook = null, RebarHookType endHook = null)
        {
            if (shape == null || barType == null || host == null || origin == null ||
                xVector == null || yVector == null || xVector.GetLength() < 1e-6 || yVector.GetLength() < 1e-6 ||
                Math.Abs(xVector.Normalize().DotProduct(yVector.Normalize())) > 1e-5)
                throw new InvalidOperationException("Cột " + columnId.Value + ": hệ trục hoặc Rebar Shape không hợp lệ cho " + groupName + ".");
            if (!shape.GetAllowed(barType))
                throw new InvalidOperationException("Cột " + columnId.Value + ": Rebar Shape '" + shape.Name +
                    "' không được phép dùng với RebarBarType '" + barType.Name + "' cho " + groupName + ".");

            Rebar bar = null;
            try
            {
                bar = Rebar.CreateFromRebarShape(doc, shape, barType, host, origin,
                    xVector, yVector);
                if (bar == null)
                    throw new InvalidOperationException("Revit không tạo được thanh từ Shape đã chọn.");

                using (var accessor = bar.GetShapeDrivenAccessor())
                    accessor.ScaleToBox(origin, xVector, yVector);
                if (startHook != null) SetHook(bar, 0, startHook, barType);
                if (endHook != null) SetHook(bar, 1, endHook, barType);
                doc.Regenerate();

                if (bar.GetShapeId() != shape.Id)
                    throw new InvalidOperationException("Revit đã đổi Shape được chọn khi tạo thanh.");
                if (startHook != null && bar.GetHookTypeId(0) != startHook.Id)
                    throw new InvalidOperationException("Hook đầu thanh không khớp Hook đã chọn.");
                if (endHook != null && bar.GetHookTypeId(1) != endHook.Id)
                    throw new InvalidOperationException("Hook cuối thanh không khớp Hook đã chọn.");
                if (startHook == null && bar.GetHookTypeId(0) != ElementId.InvalidElementId)
                    throw new InvalidOperationException("Shape có Hook đầu ngoài cấu hình được chọn.");
                if (endHook == null && bar.GetHookTypeId(1) != ElementId.InvalidElementId)
                    throw new InvalidOperationException("Shape có Hook cuối ngoài cấu hình được chọn.");
                return bar;
            }

            catch (Exception ex)
            {
                if (bar != null && doc.GetElement(bar.Id) != null) doc.Delete(bar.Id);
                throw new InvalidOperationException("Cột " + columnId.Value + ": không thể tạo/ScaleToBox nhóm " +
                    groupName + " bằng Rebar Shape '" + shape.Name + "'. Kiểm tra cấu hình Shape, kích thước và Hook. " +
                    ex.Message, ex);
            }
        }

        internal static void GetBox(IList<XYZ> points, XYZ xAxis, XYZ yAxis,
            double fallbackDepth, out XYZ origin, out XYZ xVector, out XYZ yVector)
        {
            if (points == null || points.Count == 0 || xAxis == null || yAxis == null)
                throw new InvalidOperationException("Không thể tính hộp kích thước Rebar Shape.");
            XYZ x = xAxis.Normalize(), y = yAxis.Normalize();
            if (Math.Abs(x.DotProduct(y)) > 1e-5)
                throw new InvalidOperationException("Hai phương kích thước Rebar Shape không vuông góc.");
            XYZ normal = x.CrossProduct(y).Normalize();
            double minX = points.Min(point => point.DotProduct(x));
            double maxX = points.Max(point => point.DotProduct(x));
            double minY = points.Min(point => point.DotProduct(y));
            double maxY = points.Max(point => point.DotProduct(y));
            double minNormal = points.Min(point => point.DotProduct(normal));
            double maxNormal = points.Max(point => point.DotProduct(normal));
            if (maxNormal-minNormal > V3Units.Ft(.05))
                throw new InvalidOperationException("Đường thanh không đồng phẳng; không thể xác định hộp Rebar Shape an toàn.");
            double width = maxX - minX, depth = maxY - minY;
            if (depth <= 1e-6 && fallbackDepth > 1e-6)
            {
                double center = (minY + maxY) / 2;
                minY = center - fallbackDepth / 2;
                depth = fallbackDepth;
            }
            if (width <= 1e-6 || depth <= 1e-6)
                throw new InvalidOperationException("Không thể xác định an toàn hai kích thước của Rebar Shape.");
            origin = x * minX + y * minY + normal * minNormal;
            xVector = x * width;
            yVector = y * depth;
        }

        internal static void ValidateEnvelope(Rebar bar, IList<XYZ> expected, double toleranceFt,
            ElementId columnId, string groupName)
        {
            if (bar == null || expected == null || expected.Count == 0)
                throw new InvalidOperationException("Cột " + columnId.Value + ": thiếu hình học kiểm tra cho " + groupName + ".");
            var actual = bar.GetCenterlineCurves(false, false, false,
                MultiplanarOption.IncludeOnlyPlanarCurves, 0)
                .SelectMany(curve => curve.Tessellate()).ToList();
            if (actual.Count == 0)
                throw new InvalidOperationException("Cột " + columnId.Value + ": Shape không tạo được hình học cho " + groupName + ".");
            double[] expectedBounds = Bounds(expected), actualBounds = Bounds(actual);
            double effectiveToleranceFt = Math.Max(toleranceFt, V3Units.Ft(25));
            for (int i = 0; i < 6; i++)
                if (Math.Abs(expectedBounds[i] - actualBounds[i]) > effectiveToleranceFt)
                    throw new InvalidOperationException("Cột " + columnId.Value + ": Shape/ScaleToBox nhóm " + groupName +
                        " không đạt kích thước/vị trí thiết kế (sai lệch vượt " +
                        Math.Round(V3Units.Mm(effectiveToleranceFt), 1) + " mm); đã dừng trước khi thay thép cũ.");
        }

        internal static void ValidateInside(Rebar bar, V3Geometry geometry, double minZ, double maxZ,
            double coverFt, ElementId columnId, string groupName)
        {
            var actual = bar.GetCenterlineCurves(false, false, false,
                MultiplanarOption.IncludeOnlyPlanarCurves, 0)
                .SelectMany(curve => curve.Tessellate()).ToList();
            if (actual.Count == 0) throw new InvalidOperationException("Shape không tạo hình học cho " + groupName + ".");
            double hx = geometry.Width / 2 - coverFt, hy = geometry.Depth / 2 - coverFt;
            foreach (XYZ point in actual)
            {
                XYZ delta = point - geometry.Center;
                if (Math.Abs(delta.DotProduct(geometry.X)) > hx + V3Units.Ft(2) ||
                    Math.Abs(delta.DotProduct(geometry.Y)) > hy + V3Units.Ft(2) ||
                    point.Z < minZ - V3Units.Ft(2) || point.Z > maxZ + V3Units.Ft(2))
                    throw new InvalidOperationException("Cột " + columnId.Value + ": Rebar Shape nhóm " + groupName +
                        " vượt vùng cover/kích thước cho phép; đã dừng trước khi thay thép cũ.");
            }
        }

        internal static void ValidateAxis(Rebar bar, XYZ expectedDirection, ElementId columnId,
            string groupName)
        {
            var curves = bar.GetCenterlineCurves(false, false, false,
                MultiplanarOption.IncludeOnlyPlanarCurves, 0);
            Line longest = curves.OfType<Line>().OrderByDescending(line => line.Length).FirstOrDefault();
            if (longest == null || Math.Abs(longest.Direction.Normalize().DotProduct(expectedDirection.Normalize())) < .999)
                throw new InvalidOperationException("Cột " + columnId.Value + ": hướng thanh của Rebar Shape nhóm " +
                    groupName + " không khớp mặt cắt; đã dừng trước khi thay thép cũ.");
        }

        internal static void ValidateLayout(Rebar bar, int count, double length,
            XYZ direction, ElementId columnId, string groupName)
        {
            if (bar.NumberOfBarPositions != count)
                throw new InvalidOperationException("Cột " + columnId.Value + ": số vị trí Rebar Set của " +
                    groupName + " khác phương án thiết kế.");
            if (count < 2) return;
            using (var accessor = bar.GetShapeDrivenAccessor())
            {
                XYZ first = accessor.GetBarPositionTransform(0).Origin;
                XYZ expected = direction.Normalize() * (length / (count - 1));
                for (int i = 1; i < count; i++)
                {
                    XYZ delta = accessor.GetBarPositionTransform(i).Origin - first;
                    if (delta.DistanceTo(expected * i) > V3Units.Ft(2) &&
                        delta.DistanceTo(expected * -i) > V3Units.Ft(2))
                        throw new InvalidOperationException("Cột " + columnId.Value + ": hướng/khoảng phân bố Rebar Set " +
                            groupName + " không khớp thiết kế.");
                }
            }
        }

        private static void SetHook(Rebar bar, int end, RebarHookType hook, RebarBarType type)
        {
            if (hook == null || !type.GetHookPermission(hook.Id) || !bar.CanUseHookType(hook.Id) ||
                !bar.HookAngleMatchesRebarShapeDefinition(end, hook.Id))
                throw new InvalidOperationException("RebarBarType hoặc Rebar Shape không cho phép Hook " +
                    (end == 0 ? "đầu" : "cuối") + " đã chọn.");
            bar.SetHookTypeId(end, hook.Id);
            if (bar.GetHookTypeId(end) != hook.Id)
                throw new InvalidOperationException("Revit từ chối Hook đã chọn.");
        }

        private static bool IsCandidate(RebarShape shape, V3RebarShapeGroup group,
            V3TieStyle tieStyle, bool alternating)
        {
            if (shape == null) return false;
            int segments = SegmentCount(shape);
            int start = shape.GetDefaultHookAngle(0), end = shape.GetDefaultHookAngle(1);
            switch (group)
            {
                case V3RebarShapeGroup.Longitudinal:
                    return shape.RebarStyle == RebarStyle.Standard && segments == 1 && start == 0 && end == 0;
                case V3RebarShapeGroup.Anchor:
                    return shape.RebarStyle == RebarStyle.Standard && segments >= 2 && start == 0 && end == 0;
                case V3RebarShapeGroup.OuterTie:
                    return shape.RebarStyle == RebarStyle.StirrupTie && segments >= 4 &&
                        (tieStyle == V3TieStyle.Hook135 ? start == 135 && end == 135 : start == 0 && end == 0);
                case V3RebarShapeGroup.CrossTie:
                    return shape.RebarStyle == RebarStyle.StirrupTie && segments >= 1 &&
                        (alternating ? start == 135 && end == 90
                            : start == 135 && end == 135);
                default: return false;
            }
        }

        private static int SegmentCount(RebarShape shape)
        {
            var definition = shape.GetRebarShapeDefinition() as RebarShapeDefinitionBySegments;
            return definition == null ? 0 : definition.NumberOfSegments;
        }

        private static string GroupName(V3RebarShapeGroup group)
        {
            switch (group)
            {
                case V3RebarShapeGroup.Longitudinal: return "thanh dọc thẳng";
                case V3RebarShapeGroup.Anchor: return "thanh neo/chuyển tiếp";
                case V3RebarShapeGroup.OuterTie: return "đai bao";
                default: return "đai phụ/cross-tie";
            }
        }

        private static double[] Bounds(IList<XYZ> points)
        {
            return new[] { points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z),
                points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z) };
        }
    }
}
