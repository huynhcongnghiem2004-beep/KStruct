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
            double zBend = box.Min.Z + cover + barRadius;
            double vertical = bottomWork.Geometry.Bottom - zBend;
            if (vertical <= cover + barRadius)
                throw new InvalidOperationException("Chiều dày móng không đủ để đặt chân neo L với cover đã chọn.");
            // Ln is the horizontal leg, independent of the vertical embedment.
            // The footing-solid envelope check below remains mandatory.
            if (settings.AnchorLengthMm < 400)
                throw new InvalidOperationException("Chiều dài chân neo L_n phải đạt tối thiểu 400 mm theo cấu hình giao diện.");

            bottomWork.FoundationHost = footing;
            bottomWork.FoundationBottom = box.Min.Z;
            bottomWork.FoundationBendZ = zBend;
            bottomWork.FoundationCoverMm = settings.CoverMm;
            bottomWork.FoundationAnchorLengthMm = settings.AnchorLengthMm;
            bottomWork.Config.FoundationAnchorLength = settings.AnchorLengthMm;
            bottomWork.Config.FoundationAnchorCover = settings.CoverMm;
            bottomWork.Config.FoundationAnchorDirection = settings.Direction;
            V3AnchorGeometryCheck.Validate(doc, bottomWork);
        }
    }
}
