using System;
using System.Collections.Generic;
using System.Linq;

namespace ColumnRebarPro.V3
{
    // Only propagate an explicitly chosen Hook to floors with the SAME tie diameter
    // and an empty Hook selection. Different designs/explicit floors are preserved.
    internal static class V4FloorHookInheritance
    {
        internal static void Apply(IEnumerable<V3RebarConfig> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var configs = source.Where(c => c != null).ToList();
            foreach (var group in configs.GroupBy(c => c.TieDiameter))
            {
                Inherit(group.ToList(), c => c.TieHook135UniqueId,
                    (c, id) => c.TieHook135UniqueId = id);
                Inherit(group.ToList(), c => c.TieHook90UniqueId,
                    (c, id) => c.TieHook90UniqueId = id);
            }
        }

        private static void Inherit(List<V3RebarConfig> configs,
            Func<V3RebarConfig, string> read, Action<V3RebarConfig, string> write)
        {
            var selected = configs.Select(read).Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal).ToList();
            // A unique selection is safe to propagate; multiple distinct explicit
            // choices are intentional or conflicting and must not be guessed.
            if (selected.Count != 1) return;
            foreach (var config in configs)
                if (string.IsNullOrWhiteSpace(read(config))) write(config, selected[0]);
        }
    }
}
