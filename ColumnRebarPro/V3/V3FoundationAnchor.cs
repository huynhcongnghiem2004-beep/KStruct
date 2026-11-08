using System;
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
    }

    internal static class V3FoundationAnchor
    {
        internal static void Attach(Document doc, V3SegmentWork bottomWork, V3FoundationAnchorSettings settings)
        {
            if (settings == null || !settings.Enabled) return;
            if (bottomWork == null || bottomWork.Column == null)
                throw new InvalidOperationException("Không xác định được cột chân tuyến để neo xuống móng.");
            if (settings.AnchorLengthMm <= 0 || settings.CoverMm <= 0)
                throw new InvalidOperationException("Nhập chiều dài neo và lớp bảo vệ đáy móng theo thiết kế đã duyệt.");

            BoundingBoxXYZ columnBox = bottomWork.Column.get_BoundingBox(null);
            XYZ center = bottomWork.Geometry.Center;
            double xyTol = V3Units.Ft(50), zTol = V3Units.Ft(150);
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

            FamilyInstance footing = candidates[0].Foundation;
            Parameter thickness = footing.LookupParameter("Foundation Thickness");
            if ((thickness == null || !thickness.HasValue) && footing.Symbol != null)
                thickness = footing.Symbol.LookupParameter("Foundation Thickness");
            if (thickness == null || !thickness.HasValue || thickness.StorageType != StorageType.Double || thickness.AsDouble() <= 0)
                throw new InvalidOperationException("Móng cần có tham số Foundation Thickness để xác định đoạn neo an toàn.");

            double cover = V3Units.Ft(settings.CoverMm);
            double barRadius = V3Units.Ft(bottomWork.Config.MainDiameter / 2.0);
            double zBend = bottomWork.Geometry.Bottom - thickness.AsDouble() + cover + barRadius;
            double vertical = bottomWork.Geometry.Bottom - zBend;
            if (vertical <= cover + barRadius || vertical > thickness.AsDouble())
                throw new InvalidOperationException("Chiều dày móng không đủ để đặt chân neo L với cover đã chọn.");

            bottomWork.FoundationHost = footing;
            bottomWork.FoundationBottom = bottomWork.Geometry.Bottom - thickness.AsDouble();
            bottomWork.FoundationBendZ = zBend;
            bottomWork.FoundationCoverMm = settings.CoverMm;
            bottomWork.FoundationAnchorLengthMm = settings.AnchorLengthMm;
        }
    }
}
