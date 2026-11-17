using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace ColumnRebarPro.V3
{
    // MỌI khoảng cách ở đây tính bằng mm TRÊN GIẤY (đo từ bản vẽ mẫu).
    // Đổi sang kích thước mô hình bằng P(view, mm) = mm * view.Scale, nên đổi tỷ lệ view bản vẽ vẫn giữ đúng bố cục.
    internal static class V3DrawStyle
    {
        // ---- Mặt đứng 1:50 ----
        public const double ZoneChainOffset = 15;   // chuỗi Dim vùng đai (680/2190/680) cách mép trái cột
        public const double TotalChainOffset = 21;  // chuỗi Dim tổng (3850, 4500) cách mép trái cột
        public const double TickLength = 1.5;       // chân tham chiếu của Dim, nhô ra khỏi mép cột
        public const double CropMargin = 8;         // chừa chỗ cho chữ Dim phía ngoài chuỗi ngoài cùng
        public const double TagGap = 7;             // tag ① cách mép trái cột (dùng khi chỉnh TagStirrupLeft)

        // ---- Vạch cắt 2-2, 3-3 trên mặt đứng ----
        public const double MarkLeft = 2;           // vạch cắt nhô ra bên trái cột
        public const double MarkRight = 16;         // vạch cắt nhô ra bên phải cột
        public const double MarkTriW = 2.2;         // đáy tam giác
        public const double MarkTriH = 2.8;         // chiều cao tam giác (mũi chúc xuống)
        public const double MarkTextGap = 0.8;      // số 2 / 3 cách tam giác

        // ---- Mặt cắt ngang 1:25 ----
        public const double CrossDimOffset = 4;     // Dim 300 (trên) và 200 (trái) cách mép mặt cắt
        public const double TableWidth = 47;        // bảng thép
        public const double TableRow = 5.5;
        public const double TableSplit = 0.59;      // vị trí vạch chia cột
        public const double TableGap = 4;           // bảng cách mép dưới mặt cắt

        public static double P(View view, double paperMm) { return V3Units.Ft(paperMm * view.Scale); }

        // Line style riêng (subcategory của Lines), tạo nếu chưa có.
        public static GraphicsStyle Line(Document doc, string name, Color color, int weight, string patternContains)
        {
            Category lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            Category sub = lines.SubCategories.Contains(name) ? lines.SubCategories.get_Item(name)
                : doc.Settings.Categories.NewSubcategory(lines, name);
            sub.LineColor = color;
            sub.SetLineWeight(weight, GraphicsStyleType.Projection);
            if (!string.IsNullOrEmpty(patternContains))
            {
                var pattern = new FilteredElementCollector(doc).OfClass(typeof(LinePatternElement)).Cast<LinePatternElement>()
                    .FirstOrDefault(p => p.Name.IndexOf(patternContains, StringComparison.OrdinalIgnoreCase) >= 0);
                if (pattern != null) sub.SetLinePatternId(pattern.Id, GraphicsStyleType.Projection);
            }
            return sub.GetGraphicsStyle(GraphicsStyleType.Projection);
        }

        // Text Type màu đỏ, nhân bản từ Text Type người dùng chọn.
        public static ElementId RedText(Document doc, ElementId baseId)
        {
            const string name = "KSTR Chữ đỏ";
            var found = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>().FirstOrDefault(t => t.Name == name);
            if (found != null) return found.Id;
            var src = (doc.GetElement(baseId) as TextNoteType)
                ?? (doc.GetElement(doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType)) as TextNoteType);
            var copy = (TextNoteType)src.Duplicate(name);
            Parameter color = copy.get_Parameter(BuiltInParameter.LINE_COLOR) ?? copy.LookupParameter("Color");
            if (color != null && !color.IsReadOnly) color.Set(255);   // R + G*256 + B*65536 -> đỏ
            return copy.Id;
        }

        // Filled Region Type đặc (solid) để tô tam giác; Invalid nếu project không có.
        public static ElementId SolidRegion(Document doc)
        {
            foreach (var t in new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>())
            {
                var pattern = doc.GetElement(t.ForegroundPatternId) as FillPatternElement;
                if (pattern != null && pattern.GetFillPattern().IsSolidFill) return t.Id;
            }
            return ElementId.InvalidElementId;
        }
    }
}
