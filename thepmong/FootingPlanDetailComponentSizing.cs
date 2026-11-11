using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;

namespace SDK_HD.KStruct.NativeDocumentation
{
    // Explicitly configured dimension mapping; no assumptions about dependent family formulas.
    internal static class FootingPlanDetailComponentSizing
    {
        internal static readonly string[] Names = { "L", "W", "L1", "L2", "X1", "X2", "Y1", "Y2" };

        internal static string[] Expressions(FootingDrawingProfile p) => new[]
        {
            p.PlanDetailLExpression, p.PlanDetailWExpression,
            p.PlanDetailL1Expression, p.PlanDetailL2Expression,
            p.PlanDetailX1Expression, p.PlanDetailX2Expression,
            p.PlanDetailY1Expression, p.PlanDetailY2Expression
        };

        internal static double Evaluate(string expression, double b, double h, double cover) =>
            FootingPlanDetailMath.Evaluate(expression, b, h, cover);

        internal static void Apply(Document doc, FamilyInstance placed, FamilyInstance footing,
            BoundingBoxXYZ local, FootingDrawingProfile profile, bool top, IList<string> warnings)
        {
            if (!profile.PlanDetailSizingConfirmed)
                throw new InvalidOperationException("Chưa xác nhận ánh xạ kích thước Detail Item theo Family Types; không đặt ký hiệu sai kích thước.");
            double b = UnitUtils.ConvertFromInternalUnits(local.Max.X - local.Min.X, UnitTypeId.Millimeters);
            double h = UnitUtils.ConvertFromInternalUnits(local.Max.Y - local.Min.Y, UnitTypeId.Millimeters);
            double c = profile.PlanDetailCoverMm;
            // Rebar graphics are drawn on bar centerlines; concrete clear cover refers
            // to the bar OUTER face, so nominal graphic inset = clear cover + diameter/2.
            if (profile.PlanDetailAlignToCover)
            {
                var cover = FootingPlanCoverResolver.Read(doc, footing, profile, top, warnings);
                c = cover.Item1 + cover.Item2 / 2.0;
            }
            if (!Finite(b) || !Finite(h) || b <= 0 || h <= 0 || !Finite(c) || c < 0 ||
                c * 2 >= Math.Min(b, h))
                throw new InvalidOperationException("Kích thước móng hoặc lớp bảo vệ không hợp lệ.");
            string[] mapping = Expressions(profile);
            if (profile.PlanDetailAlignToCover)
            {
                // Structural alignment overrides legacy full-footing dimensions L=B/W=H.
                // The Family's L/W must control the line-to-line rebar envelope.
                mapping[0] = "B-2*C";
                mapping[1] = "H-2*C";
            }
            var desired = new List<Tuple<string, Parameter, double>>();
            for (int i = 0; i < Names.Length; i++)
            {
                string e = (mapping[i] ?? "").Trim();
                if (e.Length == 0 || e.Equals("KEEP", StringComparison.OrdinalIgnoreCase)) continue;
                IList<Parameter> found = placed.GetParameters(Names[i]);
                if (found.Count != 1 || found[0].StorageType != StorageType.Double || found[0].IsReadOnly)
                    throw new InvalidOperationException("Tham số Instance '" + Names[i] +
                        "' bị thiếu, trùng tên, sai kiểu hoặc đang khóa công thức trên Family.");
                double millimeters = Evaluate(e, b, h, c);
                if (!Finite(millimeters) || millimeters < 0 || (Names[i] == "L" || Names[i] == "W") && millimeters <= 0)
                    throw new InvalidOperationException("Giá trị kích thước " + Names[i] + " không hợp lệ: " + millimeters);
                desired.Add(Tuple.Create(Names[i], found[0],
                    UnitUtils.ConvertToInternalUnits(millimeters, UnitTypeId.Millimeters)));
            }
            if (profile.PlanDetailAlignToCover && desired.Count < 2)
                throw new InvalidOperationException("Family thép 2D thiếu L/W điều khiển kích thước để căn lớp bảo vệ.");
            if (desired.Count == 0)
                throw new InvalidOperationException("Chưa khai báo tham số kích thước nào; hãy cấu hình ít nhất L hoặc W.");
            foreach (var entry in desired)
            {
                if (!entry.Item2.Set(entry.Item3))
                    throw new InvalidOperationException("Không ghi được tham số " + entry.Item1 + " trên Detail Item.");
            }
            doc.Regenerate();
            const double toleranceFeet = 0.5 / 304.8; // 0.5 mm
            foreach (var entry in desired)
                if (Math.Abs(entry.Item2.AsDouble() - entry.Item3) > toleranceFeet)
                    throw new InvalidOperationException("Family thay đổi giá trị " + entry.Item1 +
                        " do công thức/ràng buộc. Kiểm tra Family Types và ánh xạ trước khi tạo hồ sơ.");
        }
        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
