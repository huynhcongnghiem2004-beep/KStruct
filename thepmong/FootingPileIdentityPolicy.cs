using System;
using System.Linq;

namespace SDK_HD.KStruct.NativeDocumentation
{
    // Pure name policy used by the shared modeled-pile resolver.
    internal static class FootingPileIdentityPolicy
    {
        internal static bool Matches(string family, string type, string configuredFilter)
        {
            family = family ?? "";
            type = type ?? "";
            string combined = (family + " " + type).ToLowerInvariant();
            if (new[] { "pile cap", "pilecap", "đài", "dai coc", "đế cọc" }
                .Any(x => combined.Contains(x))) return false;
            var tokens = (string.IsNullOrWhiteSpace(configuredFilter) ? "cọc,coc,pile" : configuredFilter)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            if (tokens.Length == 0)
                throw new ArgumentException("Bộ lọc cọc không có từ khóa hợp lệ.");
            return tokens.Any(x =>
                family.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0 ||
                type.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static bool InCap(double x, double y, double zMin, double zMax,
            double minX, double maxX, double minY, double maxY, double capBottom, double tolerance)
        {
            if (new[] { x, y, zMin, zMax, minX, maxX, minY, maxY, capBottom, tolerance }
                .Any(v => double.IsNaN(v) || double.IsInfinity(v))) return false;
            if (tolerance < 0 || minX > maxX || minY > maxY || zMin > zMax) return false;
            return x >= minX - tolerance && x <= maxX + tolerance &&
                   y >= minY - tolerance && y <= maxY + tolerance &&
                   zMin <= capBottom + tolerance && zMax >= capBottom - tolerance;
        }
    }
}
