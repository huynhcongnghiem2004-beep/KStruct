using System;

namespace ColumnRebarPro.V3
{
    // Explicit opt-in policy: only designed anchor or transition paths may add
    // a new project Shape. Never add new shapes for straight bars or stirrups.
    internal static class V4ShapeCreationPolicy
    {
        internal static bool CanCreate(string group) =>
            string.Equals(group, "thanh neo/chuyển tiếp", StringComparison.Ordinal);
    }
}
