using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ColumnRebarPro.V3
{
    internal sealed class V3FoundationAnchorSettings
    {
        internal bool Enabled;
        internal double CoverMm;
        internal double AnchorLengthMm;
        internal V3AnchorDirection Direction;
    }

    internal static class V3FoundationAnchor
    {
        internal static void Attach(Document doc, V3SegmentWork bottomWork, V3FoundationAnchorSettings settings)
        {
            if (bottomWork == null || bottomWork.Column == null)
                throw new InvalidOperationException("Không xác định được cột chân tuyến để neo xuống móng.");
            bottomWork.Config.FoundationAnchorEnabled = settings != null && settings.Enabled;
            if (settings == null || !settings.Enabled) return;
            if (!Enum.IsDefined(typeof(V3AnchorDirection), settings.Direction) ||
                double.IsNaN(settings.AnchorLengthMm) || double.IsInfinity(settings.AnchorLengthMm) ||
                double.IsNaN(settings.CoverMm) || double.IsInfinity(settings.CoverMm) ||
                settings.AnchorLengthMm <= 0 || settings.CoverMm <= 0)
                throw new InvalidOperationException("Nhập chiều dài neo và lớp bảo vệ đáy móng theo thiết kế đã duyệt.");

            XYZ center = bottomWork.Geometry.Center;
            double xyTol = V3Units.Ft(50), zTol = V3Units.Ft(1);
            var candidates = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFoundation)
                .WhereElementIsNotElementType()
                .OfType<FamilyInstance>()
                .Select(f => new { Foundation = f, Box = f.get_BoundingBox(null) })
                .Where(x => x.Box != null &&
                    x.Box.Min.X <= center.X + xyTol && x.Box.Max.X >= center.X - xyTol &&
                    x.Box.Min.Y <= center.Y + xyTol && x.Box.Max.Y >= center.Y - xyTol &&
                    Math.Abs(x.Box.Max.Z - bottomWork.Geometry.Bottom) <= zTol)
                .OrderBy(x => Math.Abs(x.Box.Max.Z - bottomWork.Geometry.Bottom))
                .ToList();
            if (candidates.Count == 0)
                throw new InvalidOperationException("Không tìm thấy móng Structural Foundation ngay dưới cột chân tuyến. Kiểm tra vị trí và cao độ cột/móng.");

            if (candidates.Count > 1)
                throw new InvalidOperationException("Có nhiều móng tại chân cột; cần xử lý mô hình để xác định duy nhất móng nhận neo.");
            FamilyInstance footing = candidates[0].Foundation;
            BoundingBoxXYZ box = candidates[0].Box;
            double cover = V3Units.Ft(settings.CoverMm);
            double barRadius = V3Units.Ft(bottomWork.Config.MainDiameter / 2.0);
            double capBottom = ResolveCapBottom(footing, bottomWork.Geometry.Bottom);
            double zBend = capBottom + cover + barRadius;
            double vertical = bottomWork.Geometry.Bottom - zBend;
            if (vertical <= cover + barRadius)
                throw new InvalidOperationException("Chiều dày móng không đủ để đặt chân neo L với cover đã chọn.");
            // Ln is the horizontal leg, independent of the vertical embedment.
            // The footing-solid envelope check below remains mandatory.
            if (settings.AnchorLengthMm < 400)
                throw new InvalidOperationException("Chiều dài chân neo L_n phải đạt tối thiểu 400 mm theo cấu hình giao diện.");

            bottomWork.FoundationHost = footing;
            bottomWork.FoundationBottom = capBottom;
            bottomWork.FoundationBendZ = zBend;
            bottomWork.FoundationCoverMm = settings.CoverMm;
            bottomWork.FoundationAnchorLengthMm = settings.AnchorLengthMm;
            bottomWork.Config.FoundationAnchorLength = settings.AnchorLengthMm;
            bottomWork.Config.FoundationAnchorCover = settings.CoverMm;
            bottomWork.Config.FoundationAnchorDirection = settings.Direction;
            V3AnchorGeometryCheck.Validate(doc, bottomWork);
        }
        // Resolve the structural cap independently of nested pile and blinding solids.
        // Never substitute the whole-family BoundingBox for the cap underside.
        private static double ResolveCapBottom(FamilyInstance footing, double columnBottom)
        {
            double thickness = ReadDimension(footing, "Foundation Thickness");
            if (thickness <= 0)
                throw new InvalidOperationException("Không đọc được Foundation Thickness của đài. Không thể phân biệt đáy đài với cọc lồng; dừng tạo neo.");
            double length = ReadDimension(footing, "Length");
            double width = ReadDimension(footing, "Width");
            var solids = new List<Solid>();
            CollectSolids(footing.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }), solids);
            double tolerance = V3Units.Ft(25);
            var matches = new List<double>();
            foreach (Solid solid in solids)
            {
                var points = new List<XYZ>();
                foreach (Edge edge in solid.Edges)
                    points.AddRange(edge.AsCurve().Tessellate());
                if (points.Count == 0) continue;
                double minZ = points.Min(p => p.Z), maxZ = points.Max(p => p.Z);
                double sx = points.Max(p => p.X) - points.Min(p => p.X);
                double sy = points.Max(p => p.Y) - points.Min(p => p.Y);
                if (Math.Abs(maxZ - columnBottom) > tolerance ||
                    Math.Abs(maxZ - minZ - thickness) > tolerance)
                    continue;
                if (length > 0 && width > 0 &&
                    !((Math.Abs(sx - length) <= tolerance && Math.Abs(sy - width) <= tolerance) ||
                      (Math.Abs(sx - width) <= tolerance && Math.Abs(sy - length) <= tolerance)))
                    continue;
                matches.Add(minZ);
            }
            if (matches.Count != 1)
                throw new InvalidOperationException("Không xác định duy nhất Solid thân đài (tìm thấy " +
                    matches.Count + "). Kiểm tra Foundation Thickness, hình học đài/cọc Nested và cao độ; không lấy đáy cọc để neo.");
            return matches[0];
        }

        private static double ReadDimension(FamilyInstance instance, string name)
        {
            Parameter parameter = instance.LookupParameter(name) ?? instance.Symbol?.LookupParameter(name);
            return parameter != null && parameter.HasValue && parameter.StorageType == StorageType.Double
                ? parameter.AsDouble() : 0;
        }

        private static void CollectSolids(GeometryElement geometry, List<Solid> solids)
        {
            if (geometry == null) return;
            foreach (GeometryObject obj in geometry)
            {
                var solid = obj as Solid;
                if (solid != null && solid.Faces.Size > 0 && solid.Volume > 1e-9)
                    solids.Add(solid);
                var nested = obj as GeometryInstance;
                if (nested != null)
                    CollectSolids(nested.GetInstanceGeometry(), solids);
            }
        }

    }
}
