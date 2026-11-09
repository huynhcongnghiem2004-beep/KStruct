using System;
using System.Collections.Generic;
using System.Linq;

namespace ColumnRebarPro.V3
{
    internal static class V4SheetUpdateGuard
    {
        internal static bool CanReplaceViews(IEnumerable<string> names)
        {
            if (names == null) return false;
            var views = names.ToList();
            if (views.Count == 0 || views.Any(string.IsNullOrWhiteSpace)) return false;
            bool longitudinal = views.Any(name =>
                name.StartsWith("KSTR V3 MCD ", StringComparison.OrdinalIgnoreCase));
            bool cross = views.Any(name =>
                name.StartsWith("KSTR V3 MCN ", StringComparison.OrdinalIgnoreCase));
            return longitudinal && cross && views.All(name =>
                name.StartsWith("KSTR V3 MCD ", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("KSTR V3 MCN ", StringComparison.OrdinalIgnoreCase));
        }
    }
}
