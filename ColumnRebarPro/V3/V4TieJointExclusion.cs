using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace ColumnRebarPro.V3
{
    // Do not place ties inside a *geometrically intersecting* structural
    // framing or floor joint. No generic "top of storey" assumption.
    internal static class V4TieJointExclusion
    {
        internal static List<Tuple<double, double>> Find(Document doc, V3SegmentWork work)
        {
            if (doc == null || work?.Column == null) throw new ArgumentNullException(nameof(work));
            var filter = new ElementIntersectsElementFilter(work.Column);
            var candidates = new List<Element>();
            candidates.AddRange(new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFraming)
                .WhereElementIsNotElementType().WherePasses(filter).ToElements());
            candidates.AddRange(new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Floors)
                .WhereElementIsNotElementType().WherePasses(filter).ToElements());
            var ranges = new List<Tuple<double, double>>();
            foreach (var element in candidates.Where(x => x.Id != work.Column.Id))
            {
                var box = element.get_BoundingBox(null);
                if (box == null) continue;
                var bottom = V3Units.Mm(Math.Max(work.Geometry.Bottom, box.Min.Z));
                var top = V3Units.Mm(Math.Min(work.Geometry.Top, box.Max.Z));
                if (top > bottom + 0.1) ranges.Add(Tuple.Create(bottom, top));
            }
            // Band positions are relative to the column's base elevation.
            double zero = V3Units.Mm(work.Geometry.Bottom);
            return ranges.Select(r => Tuple.Create(r.Item1 - zero, r.Item2 - zero)).ToList();
        }

    }
}
