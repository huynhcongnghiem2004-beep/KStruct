using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace ColumnRebarPro.V3
{
    internal sealed partial class V3DrawingBuilder
    {
        // Các đối tượng không tính vào khung nội dung (đường extent của lưới/cao độ rất dài).
        private static readonly long[] FitIgnoredCategories =
        {
            (long)BuiltInCategory.OST_Levels, (long)BuiltInCategory.OST_Grids,
            (long)BuiltInCategory.OST_Cameras, (long)BuiltInCategory.OST_Viewports,
            (long)BuiltInCategory.OST_Viewers, (long)BuiltInCategory.OST_Views
        };

        // Thu CropBox ôm sát nội dung thật trong view (thép, kích thước, text, detail...), chỉ thu nhỏ,
        // không bao giờ mở rộng quá khung hiện tại. Gọi sau khi view đã có đủ annotation.
        private void FitCrop(Document doc, View view)
        {
            if (!view.CropBoxActive) return;
            doc.Regenerate();
            BoundingBoxXYZ box = view.CropBox;
            Transform inverse = box.Transform.Inverse;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (Element e in new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType())
            {
                if (e.Category == null || FitIgnoredCategories.Contains(e.Category.Id.Value)) continue;
                BoundingBoxXYZ bb = e.get_BoundingBox(view);
                if (bb == null) continue;
                foreach (double x in new[] { bb.Min.X, bb.Max.X })
                    foreach (double y in new[] { bb.Min.Y, bb.Max.Y })
                        foreach (double z in new[] { bb.Min.Z, bb.Max.Z })
                        {
                            XYZ p = inverse.OfPoint(bb.Transform.OfPoint(new XYZ(x, y, z)));
                            if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                            if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
                        }
            }
            if (minX > maxX || minY > maxY) return;
            double pad = V3Units.Ft(3 * view.Scale);   // chừa 3 mm trên giấy
            double tol = doc.Application.ShortCurveTolerance;
            var fitted = new BoundingBoxXYZ { Transform = box.Transform };
            fitted.Min = new XYZ(Math.Max(box.Min.X, minX - pad), Math.Max(box.Min.Y, minY - pad), box.Min.Z);
            fitted.Max = new XYZ(Math.Min(box.Max.X, maxX + pad), Math.Min(box.Max.Y, maxY + pad), box.Max.Z);
            if (fitted.Max.X - fitted.Min.X < tol || fitted.Max.Y - fitted.Min.Y < tol) return;
            view.CropBox = fitted;
        }

        // Canh giữa tiêu đề viewport (số hiệu view + tên + tỷ lệ) dưới khung view,
        // và kéo đường gạch dưới tiêu đề dài đúng bằng bề rộng viewport.
        private void CenterViewportTitle(Document doc, Viewport port)
        {
            Outline box = port.GetBoxOutline();
            double boxWidth = box.MaximumPoint.X - box.MinimumPoint.X;
            try { port.LabelLineLength = boxWidth; } catch { /* loại tiêu đề không có đường gạch */ }
            doc.Regenerate();
            Outline label = port.GetLabelOutline();
            double shift = (box.MinimumPoint.X + box.MaximumPoint.X) / 2 - (label.MinimumPoint.X + label.MaximumPoint.X) / 2;
            if (Math.Abs(shift) > 1e-6)
                port.LabelOffset = port.LabelOffset + new XYZ(shift, 0, 0);
        }
    }
}
