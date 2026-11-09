using System;
using System.Collections.Generic;
using System.Linq;

namespace ColumnRebarPro.V3
{
    // Pure matching rule for stamped sheets, tested without Revit API.
    internal static class V4SheetOwnershipRules
    {
        internal static bool Matches(string expectedStack, string storedStack,
            IEnumerable<string> storedViews, IEnumerable<string> placedViews)
        {
            if (string.IsNullOrWhiteSpace(expectedStack) ||
                !string.Equals(expectedStack, storedStack, StringComparison.Ordinal) ||
                storedViews == null || placedViews == null) return false;
            var saved = storedViews.ToList();
            var current = placedViews.ToList();
            if (saved.Count == 0 || saved.Any(string.IsNullOrWhiteSpace) ||
                current.Any(string.IsNullOrWhiteSpace)) return false;
            return saved.Count == current.Count &&
                saved.Distinct(StringComparer.Ordinal).Count() == saved.Count &&
                new HashSet<string>(saved, StringComparer.Ordinal).SetEquals(current);
        }
    }
}
