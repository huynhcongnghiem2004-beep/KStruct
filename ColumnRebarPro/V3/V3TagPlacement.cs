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

        // KStruct naming convention: BOT means upper tag position, not bottom reinforcement.
        internal static bool IsUpperTag(string familyName, string typeName)
        {
            return ResolveVertical(typeName) ?? ResolveVertical(familyName) ?? false;
        }

        private static bool? ResolveVertical(string name)
        {
            var tokens = System.Text.RegularExpressions.Regex.Split(name ?? "", @"[^\p{L}\p{N}]+");
            if (tokens.Any(t => string.Equals(t, "BOT", StringComparison.OrdinalIgnoreCase))) return true;
            if (tokens.Any(t => string.Equals(t, "MID", StringComparison.OrdinalIgnoreCase))) return false;
            return null;
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
