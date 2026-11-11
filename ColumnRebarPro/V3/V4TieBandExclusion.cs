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
        // Invalid geometry must fail closed; NaN comparisons would silently retain bars.
        if (ranges.Any(r => r == null || double.IsNaN(r.Item1) || double.IsNaN(r.Item2) ||
            double.IsInfinity(r.Item1) || double.IsInfinity(r.Item2) || r.Item1 > r.Item2))
            throw new InvalidOperationException("Khoảng giao dầm/sàn không hợp lệ; dừng bố trí đai.");
        if (bands.Any(b => b == null || b.Count < 1 ||
            double.IsNaN(b.Start) || double.IsNaN(b.End) ||
            double.IsInfinity(b.Start) || double.IsInfinity(b.End) ||
            b.End < b.Start))
            throw new InvalidOperationException("Dữ liệu vùng đai không hợp lệ.");
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

        // A fully covered column previously returned zero bands and reported success.
        internal static void RequireAtLeastOneTie(
            IEnumerable<V3TieBand> planned, IEnumerable<V3TieBand> retained)
        {
            if (planned == null || retained == null)
                throw new ArgumentNullException("planned/retained");
            if (planned.Any(b => b != null && b.Count > 0) &&
                !retained.Any(b => b != null && b.Count > 0))
                throw new InvalidOperationException(
                    "Toàn bộ thép đai đã bị loại bởi vùng giao dầm/sàn. " +
                    "Cần kiểm tra và duyệt cấu tạo đai nút khung trước khi tạo thép.");
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
