using System;
using System.Collections.Generic;
using System.Linq;

namespace ColumnRebarPro.V3
{
    public static class V3RebarValidator
    {
        public static void Preflight(IList<V3SegmentWork> works)
        {
            if (works == null || works.Count == 0) throw new InvalidOperationException("Không có cột để tạo thép.");
            foreach (var item in works)
            {
                var g = item.Geometry;
                var c = item.Config;
                if (g == null || c == null) throw new InvalidOperationException("Thiếu thông tin cột hoặc cấu hình thép.");
                if (!c.DesignValuesConfirmed || string.IsNullOrWhiteSpace(c.DesignReference))
                    throw new InvalidOperationException("Cột " + item.Segment.Description +
                        " chưa xác nhận thông số theo hồ sơ thiết kế kết cấu đã duyệt. Nhập mã/số bản vẽ tham chiếu và xác nhận trong cửa sổ cấu hình.");
                if (c.Splice != V3SpliceStyle.None || c.StaggerSplices)
                    throw new InvalidOperationException("Nối chồng giữa tầng chưa được hỗ trợ. KStruct không tạo thanh nối bằng chiều dài suy diễn; hãy cung cấp chi tiết nối được duyệt trước khi triển khai.");
                if (c.TieStyle == V3TieStyle.Unspecified || c.TieLayout == V3TieLayout.Unspecified)
                    throw new InvalidOperationException("Chọn hình dạng đai và quy tắc bố trí đai theo thiết kế đã duyệt.");
                double[] values = { g.Width, g.Depth, g.Bottom, g.Top, c.MainDiameter,
                    c.TieDiameter, c.Cover, c.EndZone, c.EndSpacing, c.MidSpacing, c.LapLength, c.StarterLength, c.JointBeamHeight, c.JointSpacing };
                if (values.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
                    throw new InvalidOperationException("Kích thước và cấu hình thép phải là số hữu hạn.");
                if (V3Units.Mm(g.Height) <= 2 * (c.Cover + c.TieDiameter))
                    throw new InvalidOperationException("Chiều cao cột không đủ cho lớp bảo vệ và hai mặt đai.");
                if (c.StarterBars && c.StarterLength <= 0)
                    throw new InvalidOperationException("Chiều dài thép chờ phải lớn hơn 0 mm.");
                if (c.Cx < 2 || c.Cy < 2 || c.Cx > 20 || c.Cy > 20)
                    throw new InvalidOperationException("Số thanh mỗi cạnh phải từ 2 đến 20.");
                if (c.MainDiameter <= 0 || c.TieDiameter <= 0 || c.Cover <= 0 ||
                    c.EndZone < 0 || c.EndSpacing <= 0 || c.MidSpacing <= 0)
                    throw new InvalidOperationException("Có giá trị đường kính, lớp bảo vệ hoặc bước đai không hợp lệ.");
                if (c.TieLayout == V3TieLayout.FixedNumber &&
                    (c.EndTieCount < 2 || c.MidTieCount < 2 || c.EndTieCount > 1002 || c.MidTieCount > 1002))
                    throw new InvalidOperationException("Số vị trí đai theo vùng phải từ 2 đến 1002.");
                if (c.JointTies && (!c.StarterBars || c.JointBeamHeight <= 0 || c.JointSpacing <= 0 ||
                    c.StarterLength < c.JointBeamHeight))
                    throw new InvalidOperationException("Đai nút phía trên đỉnh cột cần thép chờ dài ít nhất bằng chiều cao nút đã nhập.");
                V3RebarSetCreator.TiePlan(item);
                double a = V3Units.Mm(g.Width), b = V3Units.Mm(g.Depth), h = V3Units.Mm(g.Height);
                double availableX = a - 2 * (c.Cover + c.TieDiameter + c.MainDiameter / 2);
                double availableY = b - 2 * (c.Cover + c.TieDiameter + c.MainDiameter / 2);
                if (availableX <= 0 || availableY <= 0 || h < 500)
                    throw new InvalidOperationException("Thép không vừa tiết diện/chiều cao cột " + item.Segment.Description);
                // Basic geometry congestion screening, NOT TCVN engineering verification.
                double clearX = availableX / (c.Cx - 1) - c.MainDiameter;
                double clearY = availableY / (c.Cy - 1) - c.MainDiameter;
                if (clearX < 0 || clearY < 0)
                    throw new InvalidOperationException("Các thanh thép giao nhau trong tiết diện cột " + item.Segment.Description);
                if (c.TieStyle == V3TieStyle.Hook135 && availableX < 8 * c.TieDiameter)
                    throw new InvalidOperationException("Cạnh theo Cx quá nhỏ để dựng dạng đai móc hiện tại.");
            }
            for (int i = 1; i < works.Count; i++)
            {
                var lower = works[i - 1]; var upper = works[i];
                if (upper.Geometry.Bottom < lower.Geometry.Top - V3Units.Ft(2))
                    throw new InvalidOperationException("Tuyến có cột chồng lấn chiều cao. Kiểm tra cột trùng hoặc chọn lại tuyến.");
                if (lower.HasNext || upper.HasPrevious || lower.TransitionUp != null || upper.TransitionDown != null)
                    throw new InvalidOperationException("Không thể tạo nối chồng hoặc chuyển tiếp tự động. Cần chi tiết cấu tạo được thiết kế và duyệt.");
            }
        }
        public static string Notice =
            "Kiểm tra sơ bộ chỉ xét hình học, lớp bảo vệ đầu vào và khả năng tạo thanh. " +
            "CHƯA kiểm tra đầy đủ TCVN 5574:2018 (neo/nối, nội lực, uốn, khoảng hở, đai, kháng chấn). " +
            "Kỹ sư kết cấu phải duyệt trước khi phát hành bản vẽ.";
    }
}
