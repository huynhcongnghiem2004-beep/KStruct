using System;
using System.Collections.Generic;

namespace SDK_HD.KStruct.FootingRebar
{
    public enum Severity { Warning, Error }

    public sealed class RuleIssue
    {
        public string Code { get; }
        public string Message { get; }
        public Severity Severity { get; }
        public RuleIssue(string code, string message, Severity severity)
        {
            Code = code;
            Message = message;
            Severity = severity;
        }
    }

    public interface IDesignCodeRules
    {
        string Name { get; }
        IReadOnlyList<RuleIssue> Validate(FootingModel f, CoverSettings c, BottomLayerSettings s);
        IReadOnlyList<RuleIssue> ValidateFull(FootingModel f, FootingRebarFullSettings settings);
    }

    /// <summary>
    /// Kiểm tra các yêu cầu kỹ thuật theo TCVN 5574:2018 (Kết cấu BT &amp; BTCT)
    /// và TCVN 198:1997 (Cấu tạo móng &amp; đài cọc).
    /// </summary>
    public sealed class Tcvn5574FootingRules : IDesignCodeRules
    {
        public string Name { get; set; } = "TCVN 5574:2018 & TCVN 198:1997";

        /// <summary>Lớp bảo vệ tối thiểu khi có bê tông lót (mm) theo TCVN 5574:2018 Bảng 19.</summary>
        public double MinCoverWithLeanConcrete { get; set; } = 35;

        /// <summary>Lớp bảo vệ tối thiểu khuyến nghị thực tế cho móng có lót (mm).</summary>
        public double RecommendedCoverFoundation { get; set; } = 50;

        /// <summary>Lớp bảo vệ tối thiểu khi KHÔNG có bê tông lót (mm) theo TCVN 5574:2018 Bảng 19.</summary>
        public double MinCoverWithoutLeanConcrete { get; set; } = 70;

        /// <summary>Lớp bảo vệ thành bên và mặt trên tối thiểu (mm).</summary>
        public double MinCoverSideAndTop { get; set; } = 25;

        /// <summary>Khoảng hở thông thủy tối thiểu giữa hai thanh = max(d, giá trị này) (mm) theo Điều 10.3.</summary>
        public double MinClearSpacingFloor { get; set; } = 25;

        /// <summary>Khoảng cách tim-tim tối đa cho thép bản đáy móng/đài cọc (mm).</summary>
        public double MaxBarSpacing { get; set; } = 200;

        /// <summary>Đường kính tối thiểu cốt thép chịu lực đáy móng (mm).</summary>
        public double MinBarDiameter { get; set; } = 10;

        /// <summary>Ngưỡng chiều cao đài cọc bắt buộc đặt thép đai cấu tạo hông (mm) theo TCVN 198:1997.</summary>
        public double HeightThresholdForSideTies { get; set; } = 700;

        public IReadOnlyList<RuleIssue> Validate(FootingModel f, CoverSettings c, BottomLayerSettings s)
        {
            var dummyTop = new TopLayerSettings(false, new BarLayerSettings(false, 12, 200), new BarLayerSettings(false, 12, 200));
            var dummySide = new SideTiesSettings(false);
            var full = new FootingRebarFullSettings(f.FootingType, c, s, dummyTop, dummySide);
            return ValidateFull(f, full);
        }

        public IReadOnlyList<RuleIssue> ValidateFull(FootingModel f, FootingRebarFullSettings s)
        {
            var issues = new List<RuleIssue>();
            CoverSettings c = s.Cover;
            BottomLayerSettings b = s.Bottom;

            double dmaxX = b.AlongX.Enabled ? b.AlongX.Diameter : 0;
            double dmaxY = b.AlongY.Enabled ? b.AlongY.Diameter : 0;
            double dmaxBottom = Math.Max(dmaxX, dmaxY);

            // 1. Kiểm tra lớp bảo vệ đáy móng (TCVN 5574:2018 Điều 10.2)
            if (c.Bottom < dmaxBottom)
            {
                issues.Add(new RuleIssue("COVER_BOTTOM_DMAX",
                    $"Lớp bảo vệ đáy ({c.Bottom:0.#} mm) phải lớn hơn hoặc bằng đường kính thanh thép lớn nhất ({dmaxBottom:0.#} mm).",
                    Severity.Error));
            }

            if (!c.HasLeanConcrete && c.Bottom < MinCoverWithoutLeanConcrete)
            {
                issues.Add(new RuleIssue("COVER_NO_LEAN",
                    $"Móng không có lớp bê tông lót: TCVN 5574:2018 yêu cầu lớp bảo vệ đáy tối thiểu {MinCoverWithoutLeanConcrete:0.#} mm (hiện tại: {c.Bottom:0.#} mm).",
                    Severity.Error));
            }
            else if (c.HasLeanConcrete && c.Bottom < MinCoverWithLeanConcrete)
            {
                issues.Add(new RuleIssue("COVER_BOTTOM_MIN",
                    $"Móng có lớp bê tông lót: TCVN 5574:2018 quy định lớp bảo vệ đáy không nhỏ hơn {MinCoverWithLeanConcrete:0.#} mm (hiện tại: {c.Bottom:0.#} mm).",
                    Severity.Error));
            }
            else if (c.HasLeanConcrete && c.Bottom < RecommendedCoverFoundation)
            {
                issues.Add(new RuleIssue("COVER_BOTTOM_RECOMMENDED",
                    $"Lớp bảo vệ đáy ({c.Bottom:0.#} mm) nhỏ hơn mức khuyến nghị phổ biến cho móng ({RecommendedCoverFoundation:0.#} mm).",
                    Severity.Warning));
            }

            // 2. Kiểm tra lớp bảo vệ bên hông và mặt trên
            if (c.Side < MinCoverSideAndTop || c.Side < dmaxBottom)
            {
                issues.Add(new RuleIssue("COVER_SIDE_MIN",
                    $"Lớp bảo vệ bên hông ({c.Side:0.#} mm) chưa đạt yêu cầu tối thiểu (max {dmaxBottom:0.#} mm, {MinCoverSideAndTop:0.#} mm).",
                    Severity.Error));
            }

            if (c.Top < MinCoverSideAndTop)
            {
                issues.Add(new RuleIssue("COVER_TOP_MIN",
                    $"Lớp bảo vệ mặt trên ({c.Top:0.#} mm) nhỏ hơn mức tối thiểu {MinCoverSideAndTop:0.#} mm.",
                    Severity.Warning));
            }

            // 3. Kiểm tra khoảng hở & bước thép đáy (TCVN 5574:2018 Điều 10.3)
            CheckClearAndSpacing(issues, "Đáy - X", f.WidthY, c, b.AlongX);
            CheckClearAndSpacing(issues, "Đáy - Y", f.LengthX, c, b.AlongY);

            // 4. Kiểm tra thép lớp trên (nếu bật)
            if (s.Top.Enabled)
            {
                if (s.Top.AlongX.Enabled)
                    CheckClearAndSpacing(issues, "Đỉnh - X", f.WidthY, c, s.Top.AlongX);
                if (s.Top.AlongY.Enabled)
                    CheckClearAndSpacing(issues, "Đỉnh - Y", f.LengthX, c, s.Top.AlongY);
            }
            else if (f.FootingType == FootingType.PileCap)
            {
                issues.Add(new RuleIssue("PILE_CAP_TOP_REBAR_RECOMMENDED",
                    "Đối với đài cọc (Pile Cap), TCVN 10304:2025 & TCVN 198:1997 khuyến nghị bố trí lưới thép lớp trên để chịu uốn ngược và chống co ngót nhiệt.",
                    Severity.Warning));
            }

            // 5. Kiểm tra quy định cấu tạo đài cọc chiều cao lớn theo TCVN 198:1997
            if (f.Height > HeightThresholdForSideTies && !s.SideTies.Enabled)
            {
                issues.Add(new RuleIssue("TCVN198_SIDE_TIES_REQUIRED",
                    $"Chiều cao móng/đài H={f.Height:0.#} mm > {HeightThresholdForSideTies:0.#} mm: Theo TCVN 198:1997 (mục 4.3), bắt buộc bố trí thép đai cấu tạo hông đài để chống nứt và co ngót.",
                    Severity.Warning));
            }

            return issues;
        }

        private void CheckClearAndSpacing(List<RuleIssue> issues, string layerName, double spanDimension,
                                          CoverSettings c, BarLayerSettings l)
        {
            if (!l.Enabled) return;

            if (l.Diameter < MinBarDiameter)
            {
                issues.Add(new RuleIssue("DIAMETER_SMALL_" + layerName,
                    $"Đường kính cốt thép {layerName} (Ø{l.Diameter:0.#}) nhỏ hơn mức khuyến nghị Ø{MinBarDiameter:0.#} mm.",
                    Severity.Warning));
            }

            if (l.Spacing > MaxBarSpacing)
            {
                issues.Add(new RuleIssue("MAX_SPACING_" + layerName,
                    $"Khoảng cách cốt thép {layerName} (a={l.Spacing:0.#} mm) vượt quá ngưỡng khuyến nghị {MaxBarSpacing:0.#} mm theo tiêu chuẩn cấu tạo.",
                    Severity.Warning));
            }

            double first = c.Side + l.Diameter / 2;
            SpacingCalculator.Compute(spanDimension - 2 * first, l.Spacing, l.Mode, out int n, out double actualStep);
            if (n > 1)
            {
                double clearGap = actualStep - l.Diameter;
                double minRequiredClear = Math.Max(l.Diameter, MinClearSpacingFloor);
                if (clearGap < minRequiredClear)
                {
                    issues.Add(new RuleIssue("CLEAR_SPACING_" + layerName,
                        $"Khoảng hở thông thủy giữa các thanh {layerName} ({clearGap:0.#} mm) nhỏ hơn mức tối thiểu ({minRequiredClear:0.#} mm) theo TCVN 5574:2018 (khó đầm bê tông).",
                        Severity.Warning));
                }
            }
        }
    }
}
