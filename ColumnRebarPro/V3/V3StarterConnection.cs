using System;
using System.Collections.Generic;
using System.Linq;

namespace ColumnRebarPro.V3
{
    internal static class V3StarterConnection
    {
        internal static void Resolve(IList<V3SegmentWork> works)
        {
            foreach (var w in works)
            {
                w.HasPrevious = false; w.HasNext = false; w.BottleneckToUpper = false; w.BottleneckUpperBarStart = 0; w.ConnectionPlan = null;
                w.IncomingStarterLength = 0; w.IncomingJoint = 0;
                w.IncomingSupplementalAnchorLengthMm = 0; w.IncomingSupplementalPositions.Clear();
                w.IncomingSpliceOffsets.Clear();
                w.IncomingStagger = false; w.StarterWarning = null;
                w.TransitionUp = null; w.TransitionDown = null;
            }
            foreach (var work in works)
            {
                var c = work.Config;
                V3LegacySplice.ValidateForCreation(c);
                if (c.StarterBars && (!Finite(c.StarterLength) || c.StarterLength <= 0))
                    throw new InvalidOperationException("Nhập chiều dài thép chờ hữu hạn, lớn hơn 0 mm.");
            }
            for (int i = 0; i + 1 < works.Count; i++)
            {
                var lower = works[i]; var upper = works[i + 1];
                var a = lower.Config; var b = upper.Config;
                if (!a.StarterBars) continue;
                var g = lower.Geometry; var h = upper.Geometry;
                double gapMm = V3Units.Mm(h.Bottom - g.Top);
                double heightMm = V3Units.Mm(h.Height);
                if (!Finite(gapMm) || !Finite(heightMm) || !Finite(b.Cover) || !Finite(b.MainDiameter) ||
                    heightMm <= 0 || b.Cover <= 0 || b.MainDiameter <= 0)
                    throw new InvalidOperationException("Cao độ, chiều cao hoặc cấu hình cột tầng trên không hợp lệ.");
                // A beam/slab zone can separate the physical column solids. Keep the entered
                // extension above the lower column's top; never silently lengthen the bar.
                if (gapMm < -2 - 1e-6)
                    throw new InvalidOperationException("Tầng " + lower.Segment.LevelName + " → " + upper.Segment.LevelName +
                        ": hai cột chồng lấn chiều cao " + Math.Round(-gapMm, 1) + " mm. Kiểm tra cột trùng hoặc cao độ/offset.");
                if (!a.BottleneckRibbed || !b.BottleneckRibbed)
                    throw new InvalidOperationException("Nối cổ chai đầu thẳng hiện áp dụng thép có gân; thép trơn cần chi tiết móc/U phù hợp theo TCVN 5574:2018.");
                double incomingMm = a.StarterLength - gapMm;
                double upperEndInsetMm = b.Cover + b.MainDiameter / 2;
                double minimumLap = V3BottleneckRules.MinimumLap(Math.Max(a.MainDiameter, b.MainDiameter));
                if (incomingMm - upperEndInsetMm < minimumLap - 1e-6)
                    throw new InvalidOperationException("Chiều dài chờ tầng " + lower.Segment.LevelName + " chưa đủ: khoảng hở " +
                        Math.Round(Math.Max(0, gapMm), 1) + " mm, chờ đã nhập " + Math.Round(a.StarterLength, 1) +
                        " mm; để có đoạn nối tối thiểu max(20d, 250 mm), chiều dài chờ từ đỉnh cột dưới phải ít nhất " +
                        Math.Round(gapMm + upperEndInsetMm + minimumLap, 1) + " mm. Chiều dài tính toán theo TCVN 5574 có thể lớn hơn.");
                var plan = V3ConnectionAdapter.Plan(lower, upper);
                int splicedBars = plan.Bars.Count(bar => !bar.Terminated);
                int barsAtLargerSection = Math.Max(a.BarCount, b.BarCount);
                bool staggerRequired=splicedBars>barsAtLargerSection/2;
                if (a.BarCount != b.BarCount || staggerRequired)
                {
                    if (!Finite(a.JointBeamHeight) || a.JointBeamHeight <= 0)
                        throw new InvalidOperationException("Nút " + lower.Segment.LevelName + " → " + upper.Segment.LevelName +
                            " cần chiều cao dầm nhập theo bản vẽ để xử lý thanh dư hoặc nối so le.");
                    if (Math.Abs(gapMm - a.JointBeamHeight) > 10)
                        throw new InvalidOperationException("Chiều cao dầm nhập " + Math.Round(a.JointBeamHeight, 1) +
                            " mm không khớp khoảng cách hình học giữa hai cột " + Math.Round(gapMm, 1) + " mm.");
                }
                foreach (var bar in plan.Bars.Where(candidate => !candidate.Terminated))
                    bar.SpliceUpperStartZMm=V3Units.Mm(h.Bottom)+upperEndInsetMm;
                if(staggerRequired)
                {
                    double availableLap=incomingMm-upperEndInsetMm;
                    double stagger=(availableLap+Math.Max(a.MainDiameter,b.MainDiameter))/3;
                    double staggeredLap=availableLap-stagger;
                    if(staggeredLap<minimumLap-1e-6)
                        throw new InvalidOperationException("Chiều dài chờ " + Math.Round(a.StarterLength,1) +
                            " mm không đủ vừa nối tối thiểu và tách hai nhóm so le 50%. Tăng chiều dài theo chi tiết được duyệt; add-in không tự dùng 800 mm/40d.");
                    if(stagger<=0 || stagger>a.JointBeamHeight || a.StarterLength-2*stagger<=0 ||
                        V3Units.Mm(h.Height)-upperEndInsetMm<=stagger)
                        throw new InvalidOperationException("Không đủ chiều cao dầm/cột để bố trí hai nhóm nối so le 50%.");
                    int rank=0;
                    foreach(var bar in plan.Bars.Where(candidate=>!candidate.Terminated).OrderBy(candidate=>candidate.LowerIndex))
                    {
                        double offset=(rank++%2==0)?stagger:-stagger;
                        bar.SpliceUpperStartZMm=V3Units.Mm(h.Bottom)+upperEndInsetMm+offset;
                        bar.SpliceEndOffsetMm=offset<0?-2*stagger:0;
                        upper.IncomingSpliceOffsets.Add(new V3PositionOffset {Position=bar.Upper,OffsetMm=offset});
                    }
                    plan.StaggerDistanceMm=stagger;
                    plan.StaggeredBarCount=splicedBars;
                }
                if (plan.UnmatchedUpperPositions.Count > 0)
                {
                    double anchorLength = b.SupplementalAnchorLengthMm;
                    if (!Finite(anchorLength) || anchorLength <= 0)
                        throw new InvalidOperationException("Tầng " + upper.Segment.LevelName + " nhiều hơn " +
                            plan.UnmatchedUpperPositions.Count + " thanh. Nhập chiều dài neo thẳng trong dầm theo chi tiết đã duyệt để tạo thép chờ bổ sung.");
                    if (a.JointBeamHeight <= 0 || anchorLength > a.JointBeamHeight + 1e-6)
                        throw new InvalidOperationException("Chiều dài neo bổ sung " + Math.Round(anchorLength, 1) +
                            " mm không nằm gọn trong chiều cao dầm đã nhập (" + Math.Round(a.JointBeamHeight, 1) + " mm).");
                    var lowerProfile = V3ConnectionAdapter.Profile(lower);
                    var lowerBars = V3ColumnPairing.Points(lowerProfile);
                    double minimumCenterDistance = (a.MainDiameter + b.MainDiameter) / 2 + a.BottleneckClearanceMm;
                    foreach (var extra in plan.UnmatchedUpperPositions)
                        if (lowerBars.Any(existing => (existing.Point - extra).Length < minimumCenterDistance - 1e-5))
                            throw new InvalidOperationException("Thép chờ bổ sung tầng " + upper.Segment.LevelName +
                                " bị xung đột với thép cột dưới trong vùng dầm; cần bố trí lại mặt cắt theo chi tiết được duyệt.");
                    upper.IncomingSupplementalAnchorLengthMm = anchorLength;
                    upper.IncomingSupplementalPositions.AddRange(plan.UnmatchedUpperPositions);
                }
                double bendHeight = plan.MaximumHeight;
                if (bendHeight >= V3Units.Mm(g.Height) - a.Cover - a.MainDiameter / 2)
                    throw new InvalidOperationException("Cột tầng " + lower.Segment.LevelName + " không đủ chiều cao để nhấn cổ chai.");
                lower.ConnectionPlan = plan;
                lower.BottleneckToUpper = true;
                lower.BottleneckUpperBarStart = h.Bottom + V3Units.Ft(upperEndInsetMm);
                if (incomingMm >= heightMm - upperEndInsetMm)
                    throw new InvalidOperationException("Tầng " + upper.Segment.LevelName +
                        " không đủ chiều cao hình học cho phần thép chờ đi vào cột (" + Math.Round(incomingMm, 1) + " mm).");
                upper.IncomingStarterLength = incomingMm;
                upper.IncomingJoint = h.Bottom;
                upper.IncomingStagger = staggerRequired; // Reflect the actual planned splice offsets, not an unused UI preference.
            }
            for (int i = 1; i < works.Count; i++)
            {
                var upper = works[i];var lower = works[i-1];
                if (!lower.BottleneckToUpper || upper.ConnectionPlan == null) continue;
                double straightTop = upper.Geometry.Top - V3Units.Ft(upper.ConnectionPlan.MaximumHeight);
                if (lower.Geometry.Top + V3Units.Ft(lower.Config.StarterLength) >= straightTop)
                    throw new InvalidOperationException("Thép chờ tầng " + lower.Segment.LevelName +
                        " đi vào vùng cổ chai của tầng " + upper.Segment.LevelName + "; kiểm tra lại chiều dài và vị trí nối.");
            }
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
