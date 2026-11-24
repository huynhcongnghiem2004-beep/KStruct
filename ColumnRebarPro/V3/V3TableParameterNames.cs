using System;
using System.Collections.Generic;
using System.Linq;

namespace ColumnRebarPro.V3
{
    internal static class V3TableParameterNames
    {
        internal static string Normalize(string name) => new string((name ?? "")
            .Where(c => !char.IsWhiteSpace(c) && c != '_').Select(char.ToUpperInvariant).ToArray());

        internal static int Find(IList<string> names, string requested)
        {
            var exact = Enumerable.Range(0, names.Count)
                .Where(i => string.Equals(names[i], requested, StringComparison.Ordinal)).ToList();
            if (exact.Count == 1) return exact[0];
            if (exact.Count > 1) throw new InvalidOperationException("Có nhiều tham số cùng tên: " + requested);
            string key = Normalize(requested);
            var matches = Enumerable.Range(0, names.Count).Where(i => Normalize(names[i]) == key).ToList();
            if (matches.Count > 1)
                throw new InvalidOperationException("Tên tham số bảng không duy nhất: " + requested);
            return matches.Count == 1 ? matches[0] : -1;
        }
    }
}
