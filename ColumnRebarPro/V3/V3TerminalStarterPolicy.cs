namespace ColumnRebarPro.V3
{
    internal static class V3TerminalStarterPolicy
    {
        internal static bool CoversTerminalColumn(double incomingLengthMm, double columnHeightMm, double topInsetMm)
        {
            return IsFinite(incomingLengthMm) && IsFinite(columnHeightMm) && IsFinite(topInsetMm) &&
                columnHeightMm > topInsetMm &&
                incomingLengthMm >= columnHeightMm - topInsetMm - 1e-6;
        }

        internal static bool IncludeUpperLongitudinalPosition(bool incomingContinuousStarter, bool supplemental)
        {
            return !incomingContinuousStarter || supplemental;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
