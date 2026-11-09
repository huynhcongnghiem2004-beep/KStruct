using System;
using System.Collections.Generic;
using System.Linq;

namespace ColumnRebarPro.V3
{
    // Pure position splitting to keep every tie Set clear of geometric joint zones.
    internal static class V4TieBandExclusion
    {
        internal static List<V3TieBand> KeepOutside(
            IEnumerable<V3TieBand> bands, IEnumerable<Tuple<double, double>> excluded)
        {
            if (bands == null || excluded == null) throw new ArgumentNullException("bands/excluded");
            var ranges = excluded.ToList();
            var result = new List<V3TieBand>();
            foreach (var band in bands)
            {
                var keep = Enumerable.Range(0, band.Count)
                    .Select(i => band.Position(i))
                    .Where(z => !ranges.Any(r => z >= r.Item1 - 0.01 && z <= r.Item2 + 0.01))
                    .ToList();
                var run = new List<double>();
                double step = band.Count > 1 ? (band.End - band.Start) / (band.Count - 1) : 0;
                foreach (double z in keep)
                {
                    if (run.Count > 0 && Math.Abs(z - run[run.Count - 1] - step) > 0.01)
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
                Start=run[0], End=run[run.Count-1], Count=run.Count,
                MaximumSpacing=original.MaximumSpacing, FixedNumber=original.FixedNumber
            });
        }
    }
}
