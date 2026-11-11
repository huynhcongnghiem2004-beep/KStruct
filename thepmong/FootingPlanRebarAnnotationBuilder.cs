using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace SDK_HD.KStruct.NativeDocumentation
{
    // Notes are based on actual hosted Rebar, never inferred from the drawn 2D Family.
    internal static class FootingPlanRebarAnnotationBuilder
    {
        internal const string Kind = "FootingPlanRebarNote2D";
        private sealed class BarNote
        {
            internal string Text;
            internal string Source;
            internal string Direction;
        }
        private static string Direction(Rebar bar, FamilyInstance host)
        {
            try
            {
                int index = Enumerable.Range(0, bar.NumberOfBarPositions).First(i => bar.DoesBarExistAtPosition(i));
                var axes = FootingDrawingGeometry.Axes(host);
                XYZ longest = XYZ.Zero;
                foreach (Curve curve in bar.GetTransformedCenterlineCurves(false, false, true,
                    MultiplanarOption.IncludeAllMultiplanarCurves, index))
                {
                    var points = curve.Tessellate();
                    for (int i = 1; i < points.Count; i++)
                    {
                        XYZ d = points[i] - points[i - 1];
                        d = new XYZ(d.X, d.Y, 0);
                        if (d.GetLength() > longest.GetLength()) longest = d;
                    }
                }
                if (longest.GetLength() < 1e-8) return bar.UniqueId;
                return Math.Abs(longest.Normalize().DotProduct(axes.BasisX.Normalize())) >=
                    Math.Abs(longest.Normalize().DotProduct(axes.BasisY.Normalize())) ? "X" : "Y";
            }
            catch { return bar.UniqueId; }
        }
        internal static void Refresh(Document doc, ViewPlan view, FamilyInstance host, bool top,
            FootingDrawingProfile profile, IList<string> warnings)
        {
            var previous = FootingDrawingDetailState.Annotations(view, host, Kind).OfType<TextNote>().ToList();
            if (!profile.CreateTags)
            {
                FootingDrawingDetailState.Clear(view, host, Kind);
                return;
            }
            var box = host.get_BoundingBox(null);
            var bars = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
                .Where(r => r.GetHostId() == host.Id &&
                    FootingDrawingVisibility.InLayer(r, box, top)).OrderBy(r => r.Id.Value).ToList();
            var notes = new List<BarNote>();
            foreach (Rebar bar in bars)
            {
                var type = doc.GetElement(bar.GetTypeId()) as RebarBarType;
                Parameter diameter = type?.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER);
                if (diameter == null || !diameter.HasValue || diameter.AsDouble() <= 0)
                { warnings.Add("Rebar " + bar.Id.Value + ": không đọc được đường kính."); continue; }
                double d = UnitUtils.ConvertFromInternalUnits(diameter.AsDouble(), UnitTypeId.Millimeters);
                string name = "Ø" + Math.Round(d, 1).ToString("0.#", CultureInfo.InvariantCulture);
                int count = Math.Max(0, Enumerable.Range(0, bar.NumberOfBarPositions).Count(i => bar.DoesBarExistAtPosition(i)));
                Parameter spacing = bar.get_Parameter(BuiltInParameter.REBAR_ELEM_BAR_SPACING);
                if (spacing != null && spacing.StorageType == StorageType.Double && spacing.AsDouble() > 1e-8 && count > 1)
                {
                    double millimeters = UnitUtils.ConvertFromInternalUnits(spacing.AsDouble(), UnitTypeId.Millimeters);
                    name += "a" + Math.Round(millimeters).ToString("0", CultureInfo.InvariantCulture);
                }
                else if (count > 0) name += " (" + count.ToString(CultureInfo.InvariantCulture) + " thanh)";
                else { warnings.Add("Rebar " + bar.Id.Value + ": không có vị trí thanh hợp lệ."); continue; }
                Parameter number = bar.get_Parameter(BuiltInParameter.REBAR_NUMBER);
                string rebarNumber = number?.AsValueString() ?? number?.AsString();
                if (!string.IsNullOrWhiteSpace(rebarNumber))
                    name = "[" + rebarNumber.Trim() + "] " + name;
                notes.Add(new BarNote { Text = name, Source = bar.UniqueId, Direction = Direction(bar, host) });
            }
            // Group equal notes to avoid multiplying identical callouts on one plan.
            notes = notes.GroupBy(x => x.Direction + ":" + x.Text, StringComparer.Ordinal)
                .Select(g => g.First()).Take(12).ToList();
            if (notes.Count == 0)
            {
                FootingDrawingDetailState.Clear(view, host, Kind);
                warnings.Add(view.Name + ": không có dữ liệu Rebar hợp lệ để tạo chú thích thép 2D.");
                return;
            }
            if (bars.Count > notes.Count && notes.Count == 12) warnings.Add(view.Name + ": nhóm ghi chú thép được giới hạn 12 nhãn; kiểm tra trên Sheet.");
            TextNoteType textType = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>().OrderBy(t => t.Id.Value).FirstOrDefault();
            if (textType == null) throw new InvalidOperationException("Không có TextNoteType để ghi chú thép trên mặt bằng.");
            var vertices = FootingDrawingGeometry.Rectangle(host, 0);
            double maxRight = vertices.Max(p => p.DotProduct(view.RightDirection));
            double maxUp = vertices.Max(p => p.DotProduct(view.UpDirection));
            XYZ origin = view.Origin;
            // TextNote position is in model units, scaled from a paper annotation offset.
            double step = UnitUtils.ConvertToInternalUnits(5.5 * view.Scale, UnitTypeId.Millimeters);
            double offset = UnitUtils.ConvertToInternalUnits(8.0 * view.Scale, UnitTypeId.Millimeters);
            if (view.AreAnnotationCategoriesHidden)
                throw new InvalidOperationException("View/template đang ẩn toàn bộ Annotation; không thể ghi chú thép.");
            var newly = new List<TextNote>();
            for (int i = 0; i < notes.Count; i++)
            {
                // Use viewport directions so note text remains horizontal after footing rotation.
                XYZ p = origin + view.RightDirection * (maxRight + offset - origin.DotProduct(view.RightDirection)) +
                    view.UpDirection * (maxUp - i * step - origin.DotProduct(view.UpDirection));
                p = p - view.ViewDirection * (p - origin).DotProduct(view.ViewDirection);
                TextNote existing = previous.FirstOrDefault(t => FootingDrawingDetailState.SourceId(t) == notes[i].Source);
                if (existing != null)
                {
                    if (existing.Text != notes[i].Text) existing.Text = notes[i].Text;
                    XYZ old = existing.Coord;
                    if ((old - p).GetLength() > 1e-7) ElementTransformUtils.MoveElement(doc, existing.Id, p - old);
                    newly.Add(existing);
                }
                else
                {
                    TextNote created = TextNote.Create(doc, view.Id, p, notes[i].Text, textType.Id);
                    FootingDrawingDetailState.Mark(created, host, view, Kind, notes[i].Source);
                    newly.Add(created);
                }
            }
            var retain = new HashSet<ElementId>(newly.Select(t => t.Id));
            var obsolete = previous.Where(t => !retain.Contains(t.Id)).Select(t => t.Id).ToList();
            if (obsolete.Count > 0)
            {
                var deleted = doc.Delete(obsolete);
                if (deleted.Any(id => !obsolete.Contains(id)))
                    throw new InvalidOperationException("Xóa TextNote cũ ảnh hưởng annotation khác; đã hủy cập nhật.");
            }
            var crop = view.GetCropRegionShapeManager();
            if (crop.CanHaveAnnotationCrop)
            {
                Parameter active = view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
                if (active != null && !active.IsReadOnly) active.Set(1);
                crop.RightAnnotationCropOffset = Math.Max(crop.RightAnnotationCropOffset,
                    UnitUtils.ConvertToInternalUnits(65, UnitTypeId.Millimeters));
            }
        }
    }
}
