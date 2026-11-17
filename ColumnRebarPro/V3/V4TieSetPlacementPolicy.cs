using System;

namespace ColumnRebarPro.V3
{
    // Revit Maximum Spacing can compute a different number of placements from
    // our deduplicated zone planner. A Fixed Number fallback is safe only if it
    // preserves the approved maximum center-to-center distance.
    internal static class V4TieSetPlacementPolicy
    {
        internal static bool NeedsFixedNumber(int planned, int actual,
            double lengthMm, double maximumSpacingMm)
        {
            if (planned < 2 || actual < 0 ||
                double.IsNaN(lengthMm) || double.IsInfinity(lengthMm) ||
                double.IsNaN(maximumSpacingMm) || double.IsInfinity(maximumSpacingMm) ||
                lengthMm < 0 || maximumSpacingMm <= 0)
                throw new InvalidOperationException("Thông số bộ thép đai không hợp lệ.");
            double resultingSpacing = lengthMm / (planned - 1);
            if (resultingSpacing > maximumSpacingMm + 0.001)
                throw new InvalidOperationException(
                    "Không thể hiệu chỉnh số vị trí đai: bước đai thực tế sẽ vượt bước đai tối đa.");
            return actual != planned;
        }
    }
}
