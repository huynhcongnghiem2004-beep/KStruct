using System;
using System.Collections.Generic;
using System.Linq;

namespace SDK_HD.KStruct.NativeDocumentation
{
    // Pure selection of the nearest physical pile row for a centered section.
    // "User-specified offset" is preserved. No fabricated pile coordinates.
    internal static class FootingPileSectionAlignment
    {
        internal static double ChooseOffsetMm(double requestedMm, bool isExplicit, IEnumerable<double> pileRowsMm,
            double allowedHalfMm, out bool snapped)
        {
            snapped = false;
            if (double.IsNaN(requestedMm) || double.IsInfinity(requestedMm) ||
                double.IsNaN(allowedHalfMm) || double.IsInfinity(allowedHalfMm) || allowedHalfMm <= 0)
                throw new ArgumentException("Offset hoặc kích thước đài không hợp lệ.");
            if (Math.Abs(requestedMm) >= allowedHalfMm)
                throw new InvalidOperationException("Tuyến cắt nằm ngoài đài cọc.");
            if (isExplicit) return requestedMm;
            double[] rows = pileRowsMm
                .Where(x => !double.IsNaN(x) && !double.IsInfinity(x) && Math.Abs(x) < allowedHalfMm)
                .OrderBy(x => Math.Abs(x - requestedMm)).ThenBy(x => x).ToArray();
            if (rows.Any(x => Math.Abs(x - requestedMm) <= FootingPileSectionPolicy.CenterToleranceMm))
                return requestedMm;
            if (rows.Length == 0) return requestedMm;
            snapped = true;
            return rows[0];
        }
    }
}
