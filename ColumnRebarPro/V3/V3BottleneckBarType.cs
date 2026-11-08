using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ColumnRebarPro.V3
{
    internal static class V3BottleneckBarType
    {
        internal const string Prefix = "KSTR_TCVN_BEND_";
        internal static RebarBarType Resolve(Document doc, RebarBarType source, V3RebarConfig config)
        {
            double minimum = V3Units.Ft(V3BottleneckRules.MinimumBendDiameter(config.MainDiameter, config.BottleneckRibbed));
            if (source.StandardBendDiameter >= minimum - 1e-8) return source;
            string name = Prefix + source.Id.Value + "_" + Math.Round(V3Units.Mm(minimum), 3).ToString(CultureInfo.InvariantCulture);
            var existing = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
                .FirstOrDefault(t => t.Name == name && Math.Abs(t.BarNominalDiameter - source.BarNominalDiameter) < 1e-8 &&
                    t.StandardBendDiameter >= minimum - 1e-8);
            if (existing != null) return existing;
            // Do not change the source type and all its existing bars.
            var used = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(RebarBarType))
                .Cast<RebarBarType>().Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
            string unique = name;
            for (int i = 2; used.Contains(unique); i++) unique = name + "_" + i;
            var clone = (RebarBarType)source.Duplicate(unique);
            clone.StandardBendDiameter = minimum;
            return clone;
        }

        internal static void CheckBends(Rebar bar, V3RebarConfig config)
        {
            double radius = V3Units.Ft((V3BottleneckRules.MinimumBendDiameter(config.MainDiameter, config.BottleneckRibbed) + config.MainDiameter) / 2);
            var curves = bar.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, 0);
            var arcs = curves.OfType<Arc>().ToList();
            if (arcs.Count < 2 || arcs.Any(a => a.Radius < radius - V3Units.Ft(.1)))
                throw new InvalidOperationException("Hình nhấn cổ chai không đủ hai đoạn uốn hoặc bán kính dưới mức tối thiểu TCVN 5574:2018, 10.3.7.");
        }
    }
}
