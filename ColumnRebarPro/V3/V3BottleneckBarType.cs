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
            if (!config.AllowExistingUndersizeBend)
                V4ExistingBarTypePolicy.RequireAdequateBend(source.Name,
                    V3Units.Mm(source.StandardBendDiameter), V3Units.Mm(minimum));
            else if (source.StandardBendDiameter <= 0)
                throw new InvalidOperationException("RebarBarType có Standard Bend Diameter không hợp lệ.");
            // Always reuse the selected physical type from the project.
            // This opt-in exception is a geometric override, NOT code compliance.
            return source;
        }

        internal static void CheckBends(Rebar bar, V3RebarConfig config, int minimumArcCount = 2)
        {
            var physicalType = bar.Document.GetElement(bar.GetTypeId()) as RebarBarType;
            if (physicalType == null) throw new InvalidOperationException("Không xác định được RebarBarType thực tế.");
            double bendDiameter = config.AllowExistingUndersizeBend
                ? V3Units.Mm(physicalType.StandardBendDiameter)
                : V3BottleneckRules.MinimumBendDiameter(config.MainDiameter, config.BottleneckRibbed);
            double radius = V3Units.Ft((bendDiameter + config.MainDiameter) / 2);
            var curves = bar.GetCenterlineCurves(false, false, false, bar.IsRebarFreeForm() ? MultiplanarOption.IncludeAllMultiplanarCurves : MultiplanarOption.IncludeOnlyPlanarCurves, 0);
            var arcs = curves.OfType<Arc>().ToList();
            if (arcs.Count < minimumArcCount || arcs.Any(a => a.Radius < radius - V3Units.Ft(.1)))
                throw new InvalidOperationException("Hình nhấn cổ chai không đủ số đoạn uốn hoặc bán kính thực tế nhỏ hơn cấu hình cho phép. " +
                    (config.AllowExistingUndersizeBend ? "Ngoại lệ chỉ được áp dụng khi thiết kế kết cấu đã duyệt." : "Kiểm tra TCVN 5574:2018, 10.3.7."));
        }
    }
}
