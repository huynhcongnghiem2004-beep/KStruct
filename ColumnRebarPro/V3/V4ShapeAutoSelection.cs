using System;
using System.Collections.Generic;
using System.Linq;

namespace ColumnRebarPro.V3
{
    // Select a shape only when identity is saved or a single compatible candidate exists.
    // Multiple matches must be explicitly chosen; never select by list order.
    internal static class V4ShapeAutoSelection
    {
        internal static string Choose(string savedId, IEnumerable<string> eligibleIds)
        {
            if (eligibleIds == null) throw new ArgumentNullException(nameof(eligibleIds));
            var ids = eligibleIds.Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal).ToList();
            if (!string.IsNullOrWhiteSpace(savedId))
                return ids.Contains(savedId, StringComparer.Ordinal) ? savedId : null;
            return ids.Count == 1 ? ids[0] : null;
        }
    }
}
