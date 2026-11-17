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
            double minimumMm = V3BottleneckRules.MinimumBendDiameter(
                config.MainDiameter, config.BottleneckRibbed);
            double minimum = V3Units.Ft(minimumMm);
            if (source.StandardBendDiameter <= 0)
                throw new InvalidOperationException("RebarBarType có Standard Bend Diameter không hợp lệ.");
            if (config.AllowExistingUndersizeBend || source.StandardBendDiameter >= minimum - V3Units.Ft(.01))
                return source;

            // Preserve the user's project type. Create a derived type with the same
            // bar properties and a code-sized mandrel; the new type is part of the
            // caller's transaction and is rolled back if the column build fails.
            if (!doc.IsModifiable)
                throw new InvalidOperationException("Không thể tạo RebarBarType uốn riêng ngoài Transaction.");

            string diameter = config.MainDiameter.ToString("0.##", CultureInfo.InvariantCulture);
            string bend = minimumMm.ToString("0.##", CultureInfo.InvariantCulture);
            string name = Prefix + source.Id.Value.ToString(CultureInfo.InvariantCulture) +
                "_D" + diameter + "_B" + bend;
            RebarBarType derived = new FilteredElementCollector(doc)
                .OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
                .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
            if (derived != null)
            {
                if (Math.Abs(V3Units.Mm(derived.BarNominalDiameter) - config.MainDiameter) > .01 ||
                    V3Units.Mm(derived.StandardBendDiameter) + .01 < minimumMm)
                    throw new InvalidOperationException("RebarBarType dẫn xuất '" + name +
                        "' đã tồn tại nhưng sai đường kính thép hoặc đường kính uốn.");
                return derived;
            }

            derived = source.Duplicate(name) as RebarBarType;
            if (derived == null)
                throw new InvalidOperationException("Không tạo được RebarBarType riêng cho đường kính uốn " + bend + " mm.");
            derived.StandardBendDiameter = minimum;
            doc.Regenerate();
            if (Math.Abs(V3Units.Mm(derived.BarNominalDiameter) - config.MainDiameter) > .01 ||
                V3Units.Mm(derived.StandardBendDiameter) + .01 < minimumMm)
                throw new InvalidOperationException("Revit không áp dụng đúng đường kính uốn cho RebarBarType dẫn xuất '" + name + "'.");
            return derived;
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
