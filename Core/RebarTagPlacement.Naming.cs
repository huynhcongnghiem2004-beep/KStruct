using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace KStruct.Core
{
    public static partial class RebarTagPlacement
    {
        private static int? Read(string name, bool vertical)
        {
            var tokens = Regex.Split(name ?? "", @"[^\p{L}\p{N}]+");
            Func<string,bool> has = token => tokens.Any(t => string.Equals(t, token, StringComparison.OrdinalIgnoreCase));
            if (vertical)
            {
                if (has("TOP")) return 1;
                if (has("BOT")) return -1;
                if (has("MID")) return 0;
            }
            else
            {
                if (has("P")) return 1;
                if (has("T")) return -1;
            }
            return null;
        }

        public static int ResolveSide(string familyName, string typeName, int fallback = 1)
            => Read(typeName, false) ?? Read(familyName, false) ?? (fallback < 0 ? -1 : 1);

        public static int ResolveVertical(string familyName, string typeName)
            => Read(typeName, true) ?? Read(familyName, true) ?? 0;

        public static double SelectPosition(string familyName, string typeName, double a, double b)
        {
            int v = ResolveVertical(familyName, typeName);
            return v > 0 ? Math.Max(a,b) : v < 0 ? Math.Min(a,b) : (a+b)/2;
        }

    }
}
