using System;
using System.Collections.Generic;
using System.Linq;

namespace ColumnRebarPro.V3
{
    internal static class V4StackTieSettings
    {
        // One tie style for the entire column stack. Hook IDs only propagate
        // to floors using the same physical RebarBarType as the edited floor.
        internal static void Apply(V3RebarConfig selected, IEnumerable<V3RebarConfig> floors)
        {
            if (selected == null || floors == null) throw new ArgumentNullException("selected/floors");
            if (selected.TieStyle == V3TieStyle.Unspecified)
                throw new InvalidOperationException("Chưa chọn kiểu đai toàn tuyến.");
            foreach (var config in floors.Where(c => c != null))
            {
                config.TieStyle = selected.TieStyle;
                config.JointTies = false;
                config.JointBeamHeight = 0;
                config.JointSpacing = 0;
                config.TieLayout = V3TieLayout.MaximumSpacing;
                if (Math.Abs(config.TieDiameter - selected.TieDiameter) < 0.01 &&
                    string.Equals(config.TieBarTypeUniqueId, selected.TieBarTypeUniqueId,
                        StringComparison.Ordinal))
                {
                    config.TieHook135UniqueId = selected.TieHook135UniqueId;
                    config.TieHook90UniqueId = selected.TieHook90UniqueId;
                }
            }
        }
    }
}
