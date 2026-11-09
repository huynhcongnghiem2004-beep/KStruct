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

        internal static List<V3TieBand> KeepOutside(
            IEnumerable<V3TieBand> bands, IEnumerable<Tuple<double, double>> excluded)
        {
            if (bands == null || excluded == null) throw new ArgumentNullException("Tie bands / exclusions");
            var ranges = excluded.ToList();
            var result = new List<V3TieBand>();
            foreach (var band in bands)
            {
                var keep = Enumerable.Range(0, band.Count)
                    .Select(i => band.Position(i))
                    .Where(z => !ranges.Any(r => z >= r.Item1 - 0.01 && z <= r.Item2 + 0.01))
                    .ToList();
                // Split into consecutive constant-spacing runs, so no Rebar Set
                // spans across the intentionally empty joint.
                var run = new List<double>();
                foreach (var z in keep)
                {
                    if (run.Count > 0 && run.Count > 1 &&
                        Math.Abs((z - run[run.Count-1]) -
                            (run[run.Count-1] - run[run.Count-2])) > 0.01)
                    {
                        AddRun(result, band, run);
                        run.Clear();
                    }
                    run.Add(z);
                }
                AddRun(result, band, run);
            }
            return result;
        }

        private static void AddRun(List<V3TieBand> output, V3TieBand original, List<double> run)
        {
            if (run.Count == 0) return;
            output.Add(new V3TieBand
            {
                Start = run[0],
                End = run[run.Count - 1],
                Count = run.Count,
                MaximumSpacing = original.MaximumSpacing,
                FixedNumber = original.FixedNumber
            });
        }
    }
}
