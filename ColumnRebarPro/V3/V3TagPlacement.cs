using System;
using System.Linq;

namespace ColumnRebarPro.V3
{
    internal enum V3TagHorizontalSide
    {
        Left = -1,
        Right = 1
    }

    internal static class V3TagPlacement
    {
        internal static V3TagHorizontalSide Resolve(string familyName, string typeName,
            V3TagHorizontalSide fallback)
        {
            V3TagHorizontalSide? fromType = ResolveName(typeName);
            if (fromType.HasValue) return fromType.Value;
            V3TagHorizontalSide? fromFamily = ResolveName(familyName);
            return fromFamily ?? fallback;
        }

        private static V3TagHorizontalSide? ResolveName(string name)
        {
            string[] tokens = (name ?? "").Split(
                (name ?? "").Where(c => !char.IsLetterOrDigit(c)).Distinct().ToArray(),
                StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Any(token => string.Equals(token, "P", StringComparison.OrdinalIgnoreCase)))
                return V3TagHorizontalSide.Right;
            if (tokens.Any(token => string.Equals(token, "T", StringComparison.OrdinalIgnoreCase)))
                return V3TagHorizontalSide.Left;
            return null;
        }
    }
}
