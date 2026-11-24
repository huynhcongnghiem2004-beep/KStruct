using System;
using System.Collections.Generic;
using System.Linq;

namespace ColumnRebarPro.V3
{
    internal static class V3AnchorGap
    {
        // Distances along the probe, in the same unit as length and tolerance.
        internal static Tuple<double, double> FirstGap(IEnumerable<Tuple<double, double>> intervals,
            double length, double tolerance)
        {
            double covered = 0;
            foreach (var interval in intervals.OrderBy(x => x.Item1))
            {
                double start = Math.Max(0, interval.Item1), end = Math.Min(length, interval.Item2);
                if (end <= covered) continue;
                if (start > covered + tolerance)
                    return Tuple.Create(covered, Math.Min(start, length));
                covered = Math.Max(covered, end);
                if (covered >= length - tolerance) return null;
            }
            return covered < length - tolerance ? Tuple.Create(covered, length) : null;
        }
    }
}
