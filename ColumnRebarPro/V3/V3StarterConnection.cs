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
                w.HasPrevious = false; w.HasNext = false;
                w.IncomingStarterLength = 0; w.IncomingJoint = 0;
                w.IncomingStagger = false; w.StarterWarning = null;
                w.TransitionUp = null; w.TransitionDown = null;
            }
            // Shared connection resolution for both preflight and builder entry points.
            for (int i = 0; i + 1 < works.Count; i++)
            {
                var lower = works[i]; var upper = works[i + 1];
                double diff = upper.Geometry.Bottom - lower.Geometry.Top;
                bool connectionRequested = lower.Config.StarterBars;
                bool contact = Math.Abs(diff) <= V3Units.Ft(1);
                if (connectionRequested && !contact)
                    throw new InvalidOperationException("Không thể nối tầng " + lower.Segment.LevelName + " → " +
                        upper.Segment.LevelName + ": hai cột không tiếp giáp tại mạch ngừng (sai lệch " +
                        Math.Round(V3Units.Mm(upper.Geometry.Bottom - lower.Geometry.Top), 1) + " mm). Kiểm tra cao độ/offset cột.");
            }
            for (int i=0; i<works.Count; i++)
            {
                var c=works[i].Config;
                if (c.StaggerSplices)
                    throw new InvalidOperationException("Cấu hình so le mối nối chưa được hỗ trợ; nhập cấu tạo được duyệt và tắt tùy chọn so le.");
                if (c.Splice != V3SpliceStyle.None)
                    throw new InvalidOperationException("Nối chồng giữa tầng chưa được hỗ trợ. KStruct không tạo thanh nối bằng chiều dài suy diễn; hãy cung cấp chi tiết nối được duyệt trước khi triển khai.");
                if (c.StarterBars && c.StarterLength <= 0)
                    throw new InvalidOperationException("Nhập chiều dài thép chờ theo thiết kế đã duyệt.");
            }
            for (int i=0; i+1<works.Count; i++)
            {
                var lower=works[i]; var upper=works[i+1];
                var a=lower.Config; var b=upper.Config;
                bool starterPair=a.StarterBars;
                if (!starterPair) continue;
                var g=lower.Geometry; var h=upper.Geometry;
                if (!V3TransitionPlanner.SameStructure(lower, upper))
                    throw new InvalidOperationException("Tuyến có thay đổi tiết diện hoặc cấu hình thép tại " +
                        lower.Segment.LevelName + " → " + upper.Segment.LevelName +
                        ". Cần chi tiết chuyển tiếp được thiết kế và duyệt; KStruct không tự suy diễn cấu tạo.");
                upper.IncomingStarterLength=a.StarterLength;
                upper.IncomingStagger=a.StaggerSplices;
                upper.IncomingJoint=h.Bottom;
                double availableLength = V3Units.Mm(h.Height) - b.Cover - b.TieDiameter;
                if (upper.IncomingStarterLength >= availableLength)
                    throw new InvalidOperationException("Tầng " + upper.Segment.LevelName +
                        " không đủ chiều cao hình học cho chiều dài thép chờ đã nhập.");
            }
        }
    }
}