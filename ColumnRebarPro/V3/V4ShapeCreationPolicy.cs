using System;
using System.Collections.Generic;

namespace ColumnRebarPro.V3
{
    // An explicit allowlist prevents unrelated commands from creating project
    // RebarShapes. It does not permit cloning/changing any RebarBarType.
    internal static class V4ShapeCreationPolicy
    {
        private static readonly HashSet<string> Groups = new HashSet<string>(StringComparer.Ordinal)
        {
            "thanh dọc",
            "thanh neo/chuyển tiếp",
            "đai bao",
            "đai phụ/cross-tie"
        };
        internal static bool CanCreate(string group) => group != null && Groups.Contains(group);
    }
}
