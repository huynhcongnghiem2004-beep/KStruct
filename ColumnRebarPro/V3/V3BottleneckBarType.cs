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
            if (source == null || config == null)
                throw new InvalidOperationException("Chưa chọn RebarBarType thép chủ thực tế từ project.");
            double minimum = V3Units.Ft(V3BottleneckRules.MinimumBendDiameter(config.MainDiameter, config.BottleneckRibbed));
            if (source.StandardBendDiameter >= minimum - 1e-8) return source;
            // NEVER duplicate or change RebarBarType in the user's project.
            // In particular, prior V4 code silently created KSTR_TCVN_BEND_* types.
            throw new InvalidOperationException(
                "Thép chủ " + source.Name + " (ID " + source.Id.Value +
                ") không đủ đường kính uốn cho cổ chai. Yêu cầu tối thiểu " +
                V3Units.Mm(minimum).ToString("0.##", CultureInfo.InvariantCulture) +
                " mm; loại hiện tại " +
                V3Units.Mm(source.StandardBendDiameter).ToString("0.##", CultureInfo.InvariantCulture) +
                " mm. Hãy chọn RebarBarType khác đã có trong project. " +
                "KStruct không tự tạo và không chỉnh sửa RebarBarType.");
        }

        internal static void CheckBends(Rebar bar, V3RebarConfig config, int minimumArcCount = 2)
        {
            double radius = V3Units.Ft((V3BottleneckRules.MinimumBendDiameter(config.MainDiameter, config.BottleneckRibbed) + config.MainDiameter) / 2);
            var curves = bar.GetCenterlineCurves(false, false, false, bar.IsRebarFreeForm() ? MultiplanarOption.IncludeAllMultiplanarCurves : MultiplanarOption.IncludeOnlyPlanarCurves, 0);
            var arcs = curves.OfType<Arc>().ToList();
            if (arcs.Count < minimumArcCount || arcs.Any(a => a.Radius < radius - V3Units.Ft(.1)))
                throw new InvalidOperationException("Hình nhấn cổ chai không đủ số đoạn uốn theo phương án hoặc bán kính dưới mức tối thiểu TCVN 5574:2018, 10.3.7.");
        }
    }
}
