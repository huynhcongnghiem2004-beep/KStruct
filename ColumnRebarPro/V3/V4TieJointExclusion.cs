using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace ColumnRebarPro.V3
{
    // Compute vertical zones where a column is connected to beams/slabs.
    // IntersectsElementFilter misses some joins after Join Geometry; use
    // conservative, category-specific projected checks for those cases.
    internal static class V4TieJointExclusion
    {
        internal static List<Tuple<double, double>> Find(Document doc, V3SegmentWork work)
        {
            if (doc == null || work?.Column == null) throw new ArgumentNullException(nameof(work));
            var columnBox = work.Column.get_BoundingBox(null);
            if (columnBox == null)
                throw new InvalidOperationException("Không xác định được khung hình học cột để loại vùng nút.");
            var filter = new BoundingBoxIntersectsFilter(new Outline(columnBox.Min, columnBox.Max));
            var candidates = new List<Element>();
            candidates.AddRange(new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType().WherePasses(filter).ToElements());
            candidates.AddRange(new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType().WherePasses(filter).ToElements());

            double zero = V3Units.Mm(work.Geometry.Bottom);
            XYZ center = (columnBox.Min + columnBox.Max) * 0.5;
            var ranges = new List<Tuple<double, double>>();
            foreach (Element element in candidates.Where(e => e.Id != work.Column.Id))
            {
                BoundingBoxXYZ box = element.get_BoundingBox(null);
                if (box == null || !LikelyJoint(element, box, center, columnBox)) continue;
                double bottom = Math.Max(work.Geometry.Bottom, box.Min.Z);
                double top = Math.Min(work.Geometry.Top, box.Max.Z);
                if (top > bottom + V3Units.Ft(0.1))
                    ranges.Add(Tuple.Create(V3Units.Mm(bottom) - zero, V3Units.Mm(top) - zero));
            }
            return ranges;
        }

        private static bool LikelyJoint(Element element, BoundingBoxXYZ box,
            XYZ center, BoundingBoxXYZ column)
        {
            const double margin = 0.01; // feet; geometric screening only
            if (element.Category?.Id.Value == (long)BuiltInCategory.OST_Floors)
            {
                // A floor crossing the column axis is a probable joined slab joint.
                return center.X >= box.Min.X - margin && center.X <= box.Max.X + margin &&
                       center.Y >= box.Min.Y - margin && center.Y <= box.Max.Y + margin;
            }

            var location = element.Location as LocationCurve;
            if (location == null || !location.Curve.IsBound) return false;
            XYZ a = location.Curve.GetEndPoint(0), b = location.Curve.GetEndPoint(1);
            double dx=b.X-a.X, dy=b.Y-a.Y;
            double length2=dx*dx+dy*dy;
            if (length2 < 1e-10) return false;
            double t=Math.Max(0,Math.Min(1,
                ((center.X-a.X)*dx+(center.Y-a.Y)*dy)/length2));
            double px=a.X+dx*t, py=a.Y+dy*t;
            double distance=Math.Sqrt(Math.Pow(px-center.X,2)+Math.Pow(py-center.Y,2));
            // A beam centreline at or into the column footprint qualifies,
            // including a beam whose solid has been joined away at the node.
            double halfWidth=(column.Max.X-column.Min.X)/2;
            double halfDepth=(column.Max.Y-column.Min.Y)/2;
            double halfDiagonal=Math.Sqrt(halfWidth*halfWidth+halfDepth*halfDepth);
            return distance <= halfDiagonal + margin;
        }
    }
}
