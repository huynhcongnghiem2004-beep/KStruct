using System;

namespace ColumnRebarPro.V3
{
    internal static class V3BottleneckRules
    {
        // TCVN 5574:2018, 10.3.7: mandrel diameter, NOT centreline radius.
        internal static double MinimumBendDiameter(double diameter, bool ribbed)
        {
            Positive(diameter);
            return diameter * (ribbed ? (diameter < 20 ? 5 : 8) : (diameter < 20 ? 2.5 : 4));
        }

        // Necessary lower bounds only; 0.4*alpha*L0,an and the calculated lap still apply (10.3.6.2).
        internal static double MinimumLap(double diameter)
        {
            Positive(diameter);
            return Math.Max(20 * diameter, 250);
        }

        internal static double Shift(double diameter, double clearance)
        {
            Positive(diameter);
            if (!Finite(clearance) || clearance < 0) throw new InvalidOperationException("Khe hở cổ chai phải hữu hạn và không âm.");
            return diameter + clearance;
        }

        internal static double Height(double diameter, double clearance, double slope)
        {
            Positive(slope);
            return Shift(diameter, clearance) * slope;
        }

        internal static double TangentLength(double diameter, double clearance, double slope, double bendDiameter)
        {
            Positive(bendDiameter);
            double shift = Shift(diameter, clearance), height = Height(diameter, clearance, slope);
            double radius = (bendDiameter + diameter) / 2;
            double tangent = radius * Math.Tan(Math.Atan2(shift, height) / 2);
            if (Math.Sqrt(shift * shift + height * height) <= 2 * tangent + .1)
                throw new InvalidOperationException("Chiều cao nhấn quá ngắn cho gối uốn đã chọn; tăng tỷ lệ 1:n hoặc dùng chi tiết thiết kế phù hợp.");
            return tangent;
        }

        internal static void ValidateCenters(System.Collections.Generic.IList<V3AnchorArray> arrays,
            double halfX, double halfY, double diameter, double clearance)
        {
            double shift = Shift(diameter, clearance);
            var points = new System.Collections.Generic.List<double[]>();
            foreach (var a in arrays)
                for (int i = 0; i < a.Count; i++)
                {
                    double x = a.X + i * a.StepX, y = a.Y + i * a.StepY;
                    double sx = x + (a.HookX != 0 ? (a.X <= 0 ? shift : -shift) : 0);
                    double sy = y + (a.HookX == 0 ? (a.Y <= 0 ? shift : -shift) : 0);
                    if (Math.Abs(sx) > halfX + 1e-6 || Math.Abs(sy) > halfY + 1e-6)
                        throw new InvalidOperationException("Không đủ chỗ nhấn cổ chai trong lõi cột với cover đã nhập.");
                    points.Add(new[] { x, y, sx, sy });
                }
            for (int i = 0; i < points.Count; i++)
                for (int j = 0; j < points.Count; j++)
                {
                    double dx = points[i][2] - points[j][0], dy = points[i][3] - points[j][1];
                    if (dx * dx + dy * dy < diameter * diameter - 1e-6)
                        throw new InvalidOperationException("Thép cổ chai giao thanh tầng trên; kiểm tra mật độ thanh và khe hở nhấn.");
                    if (j <= i) continue;
                    dx = points[i][2] - points[j][2]; dy = points[i][3] - points[j][3];
                    if (dx * dx + dy * dy < diameter * diameter - 1e-6)
                        throw new InvalidOperationException("Các thanh sau nhấn cổ chai giao nhau trong lõi cột.");
                }
        }

        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static void Positive(double value)
        {
            if (!Finite(value) || value <= 0) throw new InvalidOperationException("Thông số cổ chai phải hữu hạn và lớn hơn 0.");
        }
    }
}
