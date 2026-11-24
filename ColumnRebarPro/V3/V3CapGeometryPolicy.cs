using System;

namespace ColumnRebarPro.V3
{
    internal static class V3CapGeometryPolicy
    {
        // Conservative fallback only. A deep pile or a cap joined to piles is not a cap prism.
        internal static bool IsSeparateCap(double topArea, double bottomArea, double volume, double depth)
        {
            if (!(topArea > 0) || !(bottomArea > 0) || !(volume > 0) || !(depth > 0)) return false;
            if (double.IsInfinity(topArea + bottomArea + volume + depth)) return false;
            return Math.Abs(topArea - bottomArea) <= topArea * .01 &&
                   Math.Abs(volume - topArea * depth) <= volume * .01 &&
                   depth < Math.Sqrt(topArea);
        }
    }
}
