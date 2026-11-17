using System;
using System.Globalization;

namespace SDK_HD.KStruct.NativeDocumentation
{
    // Platform-independent mapping, used by the Revit adapter and executable tests.
    internal static class FootingPlanDetailMath
    {
        internal static double Evaluate(string expression, double b, double h, double cover)
        {
            if (!Finite(b) || !Finite(h) || !Finite(cover))
                throw new ArgumentException("Kích thước không hữu hạn.");
            string e = (expression ?? "").Trim().ToUpperInvariant().Replace(" ", "");
            if (e == "B") return b;
            if (e == "H") return h;
            if (e == "C") return cover;
            if (e == "B-2*C") return b - 2 * cover;
            if (e == "H-2*C") return h - 2 * cover;
            if (e == "B-C") return b - cover;
            if (e == "H-C") return h - cover;
            if (double.TryParse(e, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && Finite(value))
                return value;
            throw new InvalidOperationException("Công thức kích thước không hỗ trợ: '" + expression +
                "'. Chọn B, H, C, B-2*C, H-2*C, B-C, H-C hoặc số mm.");
        }
        internal static double ClearCoverToBarCenter(double concreteClearMm, double diameterMm)
        {
            if (!Finite(concreteClearMm) || !Finite(diameterMm) ||
                concreteClearMm <= 0 || diameterMm <= 0)
                throw new ArgumentException("Lớp bảo vệ hoặc đường kính thép không hợp lệ.");
            return concreteClearMm + diameterMm / 2.0;
        }
        internal static double AlignedSpan(double concreteWidthMm, double concreteClearMm, double diameterMm)
        {
            double center = ClearCoverToBarCenter(concreteClearMm, diameterMm);
            if (!Finite(concreteWidthMm) || concreteWidthMm <= 2 * center)
                throw new ArgumentException("Kích thước móng không đủ cho hai lớp bảo vệ và thép.");
            return concreteWidthMm - 2 * center;
        }

        internal static double NormalizeRotation(double radians)
        {
            if (!Finite(radians)) throw new ArgumentException("Góc xoay không hợp lệ.");
            return Math.Atan2(Math.Sin(radians), Math.Cos(radians));
        }
        internal static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    }
}
