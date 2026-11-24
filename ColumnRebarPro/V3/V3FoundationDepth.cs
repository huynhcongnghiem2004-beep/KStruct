using System;

namespace ColumnRebarPro.V3
{
    internal static class V3FoundationDepth
    {
        // All values use the same length unit. Cover is to the outer steel surface.
        internal static double CenterlineDepth(double height, double cover, double diameter)
        {
            if (!(height > 0) || !(cover > 0) || !(diameter > 0) ||
                double.IsInfinity(height + cover + diameter))
                throw new InvalidOperationException("Chiều cao móng, cover và đường kính thép phải hữu hạn và lớn hơn 0.");
            double depth = height - cover - diameter / 2;
            if (depth <= 0)
                throw new InvalidOperationException("High Foundation không đủ cho cover và bán kính thép.");
            return depth;
        }
    }
}
