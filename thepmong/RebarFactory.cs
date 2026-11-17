using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace SDK_HD.KStruct.FootingRebar
{
    public sealed class CreatedSet
    {
        public BarSet Set { get; }
        public ElementId RebarId { get; }
        public CreatedSet(BarSet set, ElementId id) { Set = set; RebarId = id; }
    }

    /// <summary>
    /// Lớp biến RebarPlan thành các thực thể Rebar trong Revit API.
    /// Hỗ trợ cả thanh thẳng và thanh đa giác (móc chữ L/U, đai khép kín) tuân thủ TCVN.
    /// Phải được gọi trong một Transaction đang mở.
    /// </summary>
    public static class RebarFactory
    {
        public static List<CreatedSet> Create(Document doc, FootingFrame frame, RebarPlan plan)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            var result = new List<CreatedSet>();
            foreach (BarSet set in plan.Sets)
            {
                RebarBarType barType = FindBarType(doc, set.Diameter);

                var curves = new List<Curve>();
                if (set.FirstBarPoints != null && set.FirstBarPoints.Count >= 2)
                {
                    for (int i = 0; i < set.FirstBarPoints.Count - 1; i++)
                    {
                        XYZ p0 = frame.ToWorld(set.FirstBarPoints[i]);
                        XYZ p1 = frame.ToWorld(set.FirstBarPoints[i + 1]);
                        if (p0.DistanceTo(p1) > 0.002) // > ~0.6mm
                        {
                            curves.Add(Line.CreateBound(p0, p1));
                        }
                    }
                }
                else
                {
                    XYZ p0 = frame.ToWorld(set.FirstStart);
                    XYZ p1 = frame.ToWorld(set.FirstEnd);
                    curves.Add(Line.CreateBound(p0, p1));
                }

                if (curves.Count == 0) continue;

                var localNormal = new XYZ(set.LocalNormal.X, set.LocalNormal.Y, set.LocalNormal.Z);
                XYZ normal = frame.ToWorldDirection(localNormal);

                RebarStyle style = set.IsStirrup ? RebarStyle.StirrupTie : RebarStyle.Standard;

                Rebar rebar = null;
                try
                {
                    rebar = Rebar.CreateFromCurves(
                        doc, style, barType,
                        null, null,
                        frame.Instance, normal,
                        curves,
                        RebarHookOrientation.Left, RebarHookOrientation.Left,
                        true, true);
                }
                catch
                {
                    // Dự phòng nếu Revit không giải được dạng đa giác trên một số family đặc biệt:
                    // Thử tạo với thanh thẳng chính giữa
                    if (curves.Count > 1)
                    {
                        var fallbackCurves = new List<Curve> { curves[1] };
                        rebar = Rebar.CreateFromCurves(
                            doc, RebarStyle.Standard, barType,
                            null, null,
                            frame.Instance, normal,
                            fallbackCurves,
                            RebarHookOrientation.Left, RebarHookOrientation.Left,
                            true, true);
                    }
                }

                if (rebar == null)
                    throw new InvalidOperationException("Revit không tạo được thanh thép cho " + set.Name + ".");

                if (set.Count > 1)
                {
                    double stepMm = Math.Sqrt(set.Step.X * set.Step.X + set.Step.Y * set.Step.Y + set.Step.Z * set.Step.Z);
                    double arrayLength = UnitConv.ToFeet(stepMm * (set.Count - 1));
                    XYZ distribution = frame.ToWorldDirection(new XYZ(set.Step.X, set.Step.Y, set.Step.Z));
                    bool barsOnNormalSide = normal.DotProduct(distribution) >= 0;
                    rebar.GetShapeDrivenAccessor().SetLayoutAsFixedNumber(
                        set.Count, arrayLength, barsOnNormalSide, true, true);
                }

                result.Add(new CreatedSet(set, rebar.Id));
            }
            return result;
        }

        private static RebarBarType FindBarType(Document doc, double diameterMm)
        {
            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(RebarBarType))
                .Cast<RebarBarType>()
                .ToList();

            var found = collector.FirstOrDefault(bt =>
                Math.Abs(UnitConv.ToMm(bt.BarModelDiameter) - diameterMm) < 0.5);

            if (found == null)
            {
                // Thử tìm theo tên chứa số đường kính, ví dụ "12", "D12", "d12", "T12"
                string dStr = ((int)Math.Round(diameterMm)).ToString();
                found = collector.FirstOrDefault(bt =>
                    bt.Name.Equals(dStr, StringComparison.OrdinalIgnoreCase) ||
                    bt.Name.Equals("D" + dStr, StringComparison.OrdinalIgnoreCase) ||
                    bt.Name.Equals("d" + dStr, StringComparison.OrdinalIgnoreCase) ||
                    bt.Name.Equals("T" + dStr, StringComparison.OrdinalIgnoreCase) ||
                    bt.Name.Contains(" " + dStr) ||
                    bt.Name.Contains("Ø" + dStr));
            }

            if (found == null)
                throw new InvalidOperationException(
                    string.Format("Không tìm thấy RebarBarType đúng đường kính Ø{0:0.#} mm. Hãy nạp đúng loại thép; không thể tự chọn loại gần nhất vì có thể làm thay đổi lớp bảo vệ.", diameterMm));
            return found;
        }
    }
}
