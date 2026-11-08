using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace SDK_HD.KStruct.NativeDocumentation
{
    internal static class FootingDrawingSheetLayout
    {
        private static double Feet(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        internal static void Place(Document doc, ViewSheet sheet, IList<View> views, FootingDrawingProfile profile)
        {
            doc.Regenerate();
            BoundingBoxUV outline = sheet.Outline;
            double left = outline.Min.U + Feet(profile.SheetMarginMm), right = outline.Max.U - Feet(profile.SheetMarginMm + profile.SheetRightReserveMm);
            double bottom = outline.Min.V + Feet(profile.SheetMarginMm + profile.SheetBottomReserveMm), top = outline.Max.V - Feet(profile.SheetMarginMm);
            if (right <= left || top <= bottom) throw new InvalidOperationException("Vùng đặt view không hợp lệ; hãy chỉnh lề/khung tên.");
            var occupied = new List<Outline>();
            foreach (ElementId id in sheet.GetAllViewports()) occupied.Add(Extent((Viewport)doc.GetElement(id)));
            foreach (Element element in new FilteredElementCollector(doc, sheet.Id).WhereElementIsNotElementType())
            {
                if (element is Viewport || element is FamilyInstance fi && fi.Category?.Id.Value == (long)BuiltInCategory.OST_TitleBlocks) continue;
                BoundingBoxXYZ box = element.get_BoundingBox(sheet);
                if (box != null) occupied.Add(new Outline(box.Min, box.Max));
            }
            int columns = Math.Min(2, views.Count), rows = (views.Count + columns - 1) / columns;
            double cellWidth = (right - left) / columns, cellHeight = (top - bottom) / rows, gap = Feet(6);
            for (int i = 0; i < views.Count; i++)
            {
                if (!Viewport.CanAddViewToSheet(doc, sheet.Id, views[i].Id)) throw new InvalidOperationException("Không thể đặt view " + views[i].Name + " lên sheet.");
                int row = i / columns, col = i % columns;
                double x0 = left + col * cellWidth + gap / 2, x1 = left + (col + 1) * cellWidth - gap / 2;
                double y1 = top - row * cellHeight - gap / 2, y0 = top - (row + 1) * cellHeight + gap / 2;
                XYZ center = new XYZ((x0+x1)/2, (y0+y1)/2, 0);
                Viewport viewport = Viewport.Create(doc, sheet.Id, views[i].Id, center);
                doc.Regenerate();
                Outline extent = Extent(viewport);
                XYZ extentCenter = (extent.MinimumPoint + extent.MaximumPoint) * 0.5;
                viewport.SetBoxCenter(viewport.GetBoxCenter() + center - new XYZ(extentCenter.X, extentCenter.Y, 0));
                doc.Regenerate(); extent = Extent(viewport);
                if (extent.MinimumPoint.X < x0 || extent.MaximumPoint.X > x1 || extent.MinimumPoint.Y < y0 || extent.MaximumPoint.Y > y1)
                    throw new InvalidOperationException("View " + views[i].Name + " và tiêu đề không vừa ô sheet ở tỷ lệ 1:" + views[i].Scale + ". Hãy chọn khổ lớn hơn hoặc đổi tỷ lệ/vùng đặt.");
                if (occupied.Any(o => Intersects(extent, o, gap / 2)))
                    throw new InvalidOperationException("View " + views[i].Name + " chồng nội dung trên sheet. Hãy chọn sheet/vùng đặt khác.");
                occupied.Add(extent);
            }
        }
        private static Outline Extent(Viewport viewport)
        {
            Outline a = viewport.GetBoxOutline(), b = viewport.GetLabelOutline();
            if (b == null) return a;
            return new Outline(new XYZ(Math.Min(a.MinimumPoint.X,b.MinimumPoint.X),Math.Min(a.MinimumPoint.Y,b.MinimumPoint.Y),0),
                new XYZ(Math.Max(a.MaximumPoint.X,b.MaximumPoint.X),Math.Max(a.MaximumPoint.Y,b.MaximumPoint.Y),0));
        }
        private static bool Intersects(Outline a, Outline b, double gap) =>
            a.MinimumPoint.X < b.MaximumPoint.X + gap && a.MaximumPoint.X > b.MinimumPoint.X - gap &&
            a.MinimumPoint.Y < b.MaximumPoint.Y + gap && a.MaximumPoint.Y > b.MinimumPoint.Y - gap;
    }
}
