using System;

namespace SDK_HD.KStruct.FootingRebar
{
    /// <summary>
    /// QUY TẮC ĐẾM THANH, cần chốt với kỹ sư trước khi dùng thật.
    /// span = khoảng giữa tim thanh đầu và tim thanh cuối.
    /// </summary>
    public static class SpacingCalculator
    {
        private const double Eps = 1e-9;

        public static void Compute(double span, double spacing, SpacingMode mode,
                                   out int count, out double actualSpacing)
        {
            if (double.IsNaN(span) || double.IsInfinity(span) || span < 0)
                throw new ArgumentException("Khoảng đặt thép phải là số hữu hạn và không âm.");
            if (double.IsNaN(spacing) || double.IsInfinity(spacing) || spacing <= 0)
                throw new ArgumentException("Khoảng cách phải là số hữu hạn và lớn hơn 0.");
            if (!Enum.IsDefined(typeof(SpacingMode), mode))
                throw new ArgumentException("Chế độ chia khoảng cách không hợp lệ.");

            if (span < Eps) { count = 1; actualSpacing = 0; return; }

            if (mode == SpacingMode.FixedFromEdge)
            {
                double intervals = Math.Floor(span / spacing + Eps);
                if (intervals >= int.MaxValue)
                    throw new ArgumentException("Số thanh vượt quá giới hạn hỗ trợ.");
                count = (int)intervals + 1;
                actualSpacing = spacing;
            }
            else
            {
                double intervalCount = Math.Ceiling(span / spacing - Eps);
                if (intervalCount > int.MaxValue - 1)
                    throw new ArgumentException("Số thanh vượt quá giới hạn hỗ trợ.");
                int gaps = (int)intervalCount;
                if (gaps < 1) gaps = 1;
                count = gaps + 1;
                actualSpacing = span / gaps;
            }
        }
    }
}
