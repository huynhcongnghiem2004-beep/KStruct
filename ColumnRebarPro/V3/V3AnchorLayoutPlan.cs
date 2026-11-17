using System;
using System.Collections.Generic;

namespace ColumnRebarPro.V3
{
    public enum V3AnchorDirection { Inward, Outward, PositiveX, NegativeX, PositiveY, NegativeY }

    internal sealed class V3AnchorArray
    {
        internal double X, Y, StepX, StepY, HookX, HookY;
        internal int Count;
    }

    // Local column axes, millimetres. Array direction is perpendicular to the L bar plane.
    internal static class V3AnchorLayoutPlan
    {
        internal static List<V3AnchorArray> Create(int cx, int cy, double width, double depth,
            double cover, double tieDiameter, double mainDiameter, V3AnchorDirection direction)
        {
            foreach (double value in new[] { width, depth, cover, tieDiameter, mainDiameter })
                if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
                    throw new InvalidOperationException("Thông số bố trí neo chân L phải hữu hạn và dương.");
            if (cx < 2 || cy < 2 || cx > 20 || cy > 20 || !Enum.IsDefined(typeof(V3AnchorDirection), direction))
                throw new InvalidOperationException("Số thanh hoặc hướng neo chân L không hợp lệ.");
            double inset = cover + tieDiameter + mainDiameter / 2;
            double hx = width / 2 - inset, hy = depth / 2 - inset;
            if (hx <= 0 || hy <= 0) throw new InvalidOperationException("Thép neo không vừa tiết diện cột.");
            double dx = 2 * hx / (cx - 1), dy = 2 * hy / (cy - 1);
            var arrays = new List<V3AnchorArray>();
            if (direction == V3AnchorDirection.Inward || direction == V3AnchorDirection.Outward)
            {
                double sign = direction == V3AnchorDirection.Inward ? 1 : -1;
                arrays.Add(Array(-hx, -hy, dx, 0, cx, 0, sign));
                arrays.Add(Array(-hx, hy, dx, 0, cx, 0, -sign));
                // Corner bars belong to the two X edges, never to both faces.
                if (cy > 2)
                {
                    arrays.Add(Array(-hx, -hy + dy, 0, dy, cy - 2, sign, 0));
                    arrays.Add(Array(hx, -hy + dy, 0, dy, cy - 2, -sign, 0));
                }
            }
            else if (direction == V3AnchorDirection.PositiveX || direction == V3AnchorDirection.NegativeX)
            {
                double sign = direction == V3AnchorDirection.PositiveX ? 1 : -1;
                for (int i = 0; i < cx; i++)
                {
                    bool edge = i == 0 || i == cx - 1;
                    arrays.Add(Array(-hx + dx * i, -hy, 0, edge ? dy : 2 * hy, edge ? cy : 2, sign, 0));
                }
            }
            else
            {
                double sign = direction == V3AnchorDirection.PositiveY ? 1 : -1;
                for (int j = 0; j < cy; j++)
                {
                    bool edge = j == 0 || j == cy - 1;
                    arrays.Add(Array(-hx, -hy + dy * j, edge ? dx : 2 * hx, 0, edge ? cx : 2, 0, sign));
                }
            }
            return arrays;
        }

        private static V3AnchorArray Array(double x, double y, double stepX, double stepY,
            int count, double hookX, double hookY) => new V3AnchorArray
            { X = x, Y = y, StepX = stepX, StepY = stepY, Count = count, HookX = hookX, HookY = hookY };

        internal static double HorizontalLength(double total, double embeddedVertical)
        {
            if (double.IsNaN(total) || double.IsInfinity(total) || double.IsNaN(embeddedVertical) ||
                double.IsInfinity(embeddedVertical) || embeddedVertical <= 0 || total <= embeddedVertical)
                throw new InvalidOperationException("Tổng kích thước neo phải lớn hơn đoạn đứng trong móng để có chân L ngang.");
            return total - embeddedVertical;
        }
    }
}
