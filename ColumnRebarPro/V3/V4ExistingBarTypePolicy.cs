using System;

namespace ColumnRebarPro.V3
{
    // A user-selected physical RebarBarType is immutable to this command.
    // Never auto-Duplicate or silently increase bend diameter in the RVT.
    internal static class V4ExistingBarTypePolicy
    {
        internal static void RequireAdequateBend(string typeName,
            double actualDiameterMm, double minimumDiameterMm)
        {
            if (double.IsNaN(actualDiameterMm) || double.IsInfinity(actualDiameterMm) ||
                double.IsNaN(minimumDiameterMm) || double.IsInfinity(minimumDiameterMm) ||
                actualDiameterMm <= 0 || minimumDiameterMm <= 0)
                throw new InvalidOperationException("Thông số uốn của RebarBarType không hợp lệ.");
            if (actualDiameterMm + 0.001 < minimumDiameterMm)
                throw new InvalidOperationException(
                    "RebarBarType '" + typeName + "' có đường kính uốn không đủ (hiện tại " +
                    actualDiameterMm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                    " mm, cần tối thiểu " +
                    minimumDiameterMm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                    " mm). Chọn loại thép sẵn có phù hợp. KStruct không tự tạo RebarBarType.");
        }
    }
}
