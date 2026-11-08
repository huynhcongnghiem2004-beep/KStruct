using System;
using System.Collections.Generic;

namespace ColumnRebarPro.V3
{
    internal static class V3StarterConnection
    {
        internal static void Resolve(IList<V3SegmentWork> works)
        {
            foreach (var w in works)
            {
                w.HasPrevious = false; w.HasNext = false; w.BottleneckToUpper = false; w.BottleneckUpperBarStart = 0;
                w.IncomingStarterLength = 0; w.IncomingJoint = 0;
                w.IncomingStagger = false; w.StarterWarning = null;
                w.TransitionUp = null; w.TransitionDown = null;
            }
            foreach (var work in works)
            {
                var c = work.Config;
                if (c.StaggerSplices)
                    throw new InvalidOperationException("Cấu hình so le mối nối chưa được hỗ trợ; nhập cấu tạo được duyệt và tắt tùy chọn so le.");
                if (c.Splice != V3SpliceStyle.None)
                    throw new InvalidOperationException("Nối chồng giữa tầng chưa được hỗ trợ. KStruct không tạo thanh nối bằng chiều dài suy diễn; hãy cung cấp chi tiết nối được duyệt trước khi triển khai.");
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
                if (!V3TransitionPlanner.SameStructure(lower, upper))
                    throw new InvalidOperationException("Tuyến có thay đổi tiết diện hoặc cấu hình thép tại " +
                        lower.Segment.LevelName + " → " + upper.Segment.LevelName +
                        ". Cần chi tiết chuyển tiếp được thiết kế và duyệt; KStruct không tự suy diễn cấu tạo.");
                if (!a.BottleneckRibbed)
                    throw new InvalidOperationException("Nối cổ chai đầu thẳng hiện áp dụng thép có gân; thép trơn cần chi tiết móc/U phù hợp theo TCVN 5574:2018.");
                double incomingMm = a.StarterLength - gapMm;
                double upperEndInsetMm = b.Cover + b.MainDiameter / 2;
                double minimumLap = V3BottleneckRules.MinimumLap(Math.Max(a.MainDiameter, b.MainDiameter));
                if (incomingMm - upperEndInsetMm < minimumLap - 1e-6)
                    throw new InvalidOperationException("Chiều dài chờ tầng " + lower.Segment.LevelName + " chưa đủ: khoảng hở " +
                        Math.Round(Math.Max(0, gapMm), 1) + " mm, chờ đã nhập " + Math.Round(a.StarterLength, 1) +
                        " mm; để có đoạn nối tối thiểu max(20d, 250 mm), chiều dài chờ từ đỉnh cột dưới phải ít nhất " +
                        Math.Round(gapMm + upperEndInsetMm + minimumLap, 1) + " mm. Chiều dài tính toán theo TCVN 5574 có thể lớn hơn.");
                double bendHeight = V3BottleneckRules.Height(a.MainDiameter, a.BottleneckClearanceMm, a.BottleneckSlope);
                if (bendHeight >= V3Units.Mm(g.Height) - a.Cover - a.MainDiameter / 2)
                    throw new InvalidOperationException("Cột tầng " + lower.Segment.LevelName + " không đủ chiều cao để nhấn cổ chai.");
                lower.BottleneckToUpper = true;
                lower.BottleneckUpperBarStart = h.Bottom + V3Units.Ft(upperEndInsetMm);
                if (incomingMm >= heightMm - upperEndInsetMm)
                    throw new InvalidOperationException("Tầng " + upper.Segment.LevelName +
                        " không đủ chiều cao hình học cho phần thép chờ đi vào cột (" + Math.Round(incomingMm, 1) + " mm).");
                upper.IncomingStarterLength = incomingMm;
                upper.IncomingJoint = h.Bottom;
                upper.IncomingStagger = a.StaggerSplices;
            }
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
