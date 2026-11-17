using System;
using System.Globalization;

namespace ColumnRebarPro.V3
{
    internal static class V3RebarOwnership
    {
        internal static string Partition(long columnId) => "KSTR_V3_" + columnId.ToString(CultureInfo.InvariantCulture);
        internal static bool Matches(long columnId, string partition) =>
            string.Equals(Partition(columnId), partition, StringComparison.Ordinal);
    }
}
