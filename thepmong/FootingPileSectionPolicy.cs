using System;
using System.Globalization;
namespace SDK_HD.KStruct.NativeDocumentation
{
    // Pure, independently testable release rules; internal calculations use millimetres.
    internal static class FootingPileSectionPolicy
    {
        internal const double CenterToleranceMm = 1.0;
        internal const double DiameterToleranceMm = 0.1;
        internal static bool IsCentered(double distanceMm) =>
            !double.IsNaN(distanceMm) && !double.IsInfinity(distanceMm) &&
            Math.Abs(distanceMm) <= CenterToleranceMm;
        internal static bool DiameterMatches(double actualMm, double targetMm) =>
            !double.IsNaN(actualMm) && !double.IsNaN(targetMm) &&
            !double.IsInfinity(actualMm) && !double.IsInfinity(targetMm) &&
            targetMm > 0 && Math.Abs(actualMm - targetMm) <= DiameterToleranceMm;
        internal static string TypeName(double diameterMm)
        {
            if (double.IsNaN(diameterMm) || double.IsInfinity(diameterMm) || diameterMm <= 0)
                throw new ArgumentException("Đường kính cọc phải là số mm dương.");
            // 0.001 mm granularity avoids rounding distinct diameters to a single type.
            return "KStruct_Coc2D_D" + Math.Round(diameterMm, 3, MidpointRounding.AwayFromZero)
                .ToString("0.000", CultureInfo.InvariantCulture).Replace(".", "_") + "mm";
        }
    }
}
