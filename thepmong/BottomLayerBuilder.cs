using System;
using System.Collections.Generic;

namespace SDK_HD.KStruct.FootingRebar
{
    /// <summary>
    /// Bộ tạo kế hoạch cốt thép móng đơn &amp; móng cọc tuân thủ TCVN 5574:2018 &amp; TCVN 198:1997.
    /// Hỗ trợ bẻ neo lên (Hook Up) hoặc bẻ neo xuống (Hook Down) tùy chỉnh chiều dài đoạn neo.
    /// Tính toán thuần hình học C#, hoàn toàn độc lập với Revit API.
    /// </summary>
    public sealed class BottomLayerBuilder
    {
        public RebarPlan Build(FootingModel f, CoverSettings c, BottomLayerSettings s)
        {
            var dummyTop = new TopLayerSettings(false, new BarLayerSettings(false, 12, 200), new BarLayerSettings(false, 12, 200));
            var dummySide = new SideTiesSettings(false);
            var full = new FootingRebarFullSettings(f.FootingType, c, s, dummyTop, dummySide);
            return Build(f, full);
        }

        public RebarPlan Build(FootingModel f, FootingRebarFullSettings s)
        {
            if (f == null) throw new ArgumentNullException(nameof(f));
            if (s == null) throw new ArgumentNullException(nameof(s));

            CoverSettings c = s.Cover;
            BottomLayerSettings b = s.Bottom;
            var sets = new List<BarSet>();

            double dx = b.AlongX.Enabled ? b.AlongX.Diameter : 0;
            double dy = b.AlongY.Enabled ? b.AlongY.Diameter : 0;

            if (c.Bottom + dx + dy + c.Top > f.Height)
                throw new ArgumentException("Hai lớp thép đáy và lớp bảo vệ trên không nằm gọn trong chiều cao móng.");

            // 1. Thép đáy phương X (Bottom-X) - nằm dưới cùng
            if (b.AlongX.Enabled)
            {
                var l = b.AlongX;
                double z = c.Bottom + l.Diameter / 2.0;
                double yFirst = c.Side + l.Diameter / 2.0;
                double spanY = f.WidthY - 2.0 * yFirst;
                SpacingCalculator.Compute(spanY, l.Spacing, l.Mode, out int count, out double stepY);

                double xStart = c.Side + l.Diameter / 2.0;
                double xEnd = f.LengthX - c.Side - l.Diameter / 2.0;

                IReadOnlyList<Point3> pts;
                if (l.HasHook && l.HookType != HookTypeOption.None)
                {
                    double hookH = ResolveHookLength(f, c, z, l.Diameter, l.HookLength, l.HookType, "bottom layer");

                    double zHook = l.HookType == HookTypeOption.HookUp ? (z + hookH) : (z - hookH);

                    pts = new[]
                    {
                        new Point3(xStart, yFirst, zHook),
                        new Point3(xStart, yFirst, z),
                        new Point3(xEnd, yFirst, z),
                        new Point3(xEnd, yFirst, zHook)
                    };
                }
                else
                {
                    pts = new[]
                    {
                        new Point3(xStart, yFirst, z),
                        new Point3(xEnd, yFirst, z)
                    };
                }

                sets.Add(new BarSet("Bottom-X", BarDirection.AlongX, l.Diameter,
                    pts, new Point3(0, stepY, 0), count, false, new Point3(0, 1, 0)));
            }

            // 2. Thép đáy phương Y (Bottom-Y) - nằm ngay trên lớp X
            if (b.AlongY.Enabled)
            {
                var l = b.AlongY;
                double z = c.Bottom + dx + l.Diameter / 2.0;
                double xFirst = c.Side + l.Diameter / 2.0;
                double spanX = f.LengthX - 2.0 * xFirst;
                SpacingCalculator.Compute(spanX, l.Spacing, l.Mode, out int count, out double stepX);

                double yStart = c.Side + l.Diameter / 2.0;
                double yEnd = f.WidthY - c.Side - l.Diameter / 2.0;

                IReadOnlyList<Point3> pts;
                if (l.HasHook && l.HookType != HookTypeOption.None)
                {
                    double hookH = ResolveHookLength(f, c, z, l.Diameter, l.HookLength, l.HookType, "bottom layer");

                    double zHook = l.HookType == HookTypeOption.HookUp ? (z + hookH) : (z - hookH);

                    pts = new[]
                    {
                        new Point3(xFirst, yStart, zHook),
                        new Point3(xFirst, yStart, z),
                        new Point3(xFirst, yEnd, z),
                        new Point3(xFirst, yEnd, zHook)
                    };
                }
                else
                {
                    pts = new[]
                    {
                        new Point3(xFirst, yStart, z),
                        new Point3(xFirst, yEnd, z)
                    };
                }

                sets.Add(new BarSet("Bottom-Y", BarDirection.AlongY, l.Diameter,
                    pts, new Point3(stepX, 0, 0), count, false, new Point3(1, 0, 0)));
            }

            // 3. Thép lớp trên (Top Rebar - đặc biệt quan trọng cho đài cọc theo TCVN 10304:2025 & TCVN 198:1997)
            if (s.Top.Enabled)
            {
                double topY_D = s.Top.AlongY.Enabled ? s.Top.AlongY.Diameter : 0;
                double topX_D = s.Top.AlongX.Enabled ? s.Top.AlongX.Diameter : 0;

                // Top-Y nằm sát trên cùng
                if (s.Top.AlongY.Enabled)
                {
                    var l = s.Top.AlongY;
                    double z = f.Height - c.Top - l.Diameter / 2.0;
                    double xFirst = c.Side + l.Diameter / 2.0;
                    double spanX = f.LengthX - 2.0 * xFirst;
                    SpacingCalculator.Compute(spanX, l.Spacing, l.Mode, out int count, out double stepX);

                    double yStart = c.Side + l.Diameter / 2.0;
                    double yEnd = f.WidthY - c.Side - l.Diameter / 2.0;

                    IReadOnlyList<Point3> pts;
                    if (l.HasHook && l.HookType != HookTypeOption.None)
                    {
                        double hookH = ResolveHookLength(f, c, z, l.Diameter, l.HookLength, l.HookType, "top layer");

                        double zHook = l.HookType == HookTypeOption.HookDown ? (z - hookH) : (z + hookH);

                        pts = new[]
                        {
                            new Point3(xFirst, yStart, zHook),
                            new Point3(xFirst, yStart, z),
                            new Point3(xFirst, yEnd, z),
                            new Point3(xFirst, yEnd, zHook)
                        };
                    }
                    else
                    {
                        pts = new[]
                        {
                            new Point3(xFirst, yStart, z),
                            new Point3(xFirst, yEnd, z)
                        };
                    }

                    sets.Add(new BarSet("Top-Y", BarDirection.AlongY, l.Diameter,
                        pts, new Point3(stepX, 0, 0), count, false, new Point3(1, 0, 0)));
                }

                // Top-X nằm ngay dưới Top-Y
                if (c.Bottom + dx + dy + c.Top + topX_D + topY_D > f.Height)
                    throw new ArgumentException("Các lớp thép đáy/lớp trên và lớp bảo vệ không nằm gọn trong chiều cao móng.");

                if (s.Top.AlongX.Enabled)
                {
                    var l = s.Top.AlongX;
                    double z = f.Height - c.Top - topY_D - l.Diameter / 2.0;
                    double yFirst = c.Side + l.Diameter / 2.0;
                    double spanY = f.WidthY - 2.0 * yFirst;
                    SpacingCalculator.Compute(spanY, l.Spacing, l.Mode, out int count, out double stepY);

                    double xStart = c.Side + l.Diameter / 2.0;
                    double xEnd = f.LengthX - c.Side - l.Diameter / 2.0;

                    IReadOnlyList<Point3> pts;
                    if (l.HasHook && l.HookType != HookTypeOption.None)
                    {
                        double hookH = ResolveHookLength(f, c, z, l.Diameter, l.HookLength, l.HookType, "top layer");

                        double zHook = l.HookType == HookTypeOption.HookDown ? (z - hookH) : (z + hookH);

                        pts = new[]
                        {
                            new Point3(xStart, yFirst, zHook),
                            new Point3(xStart, yFirst, z),
                            new Point3(xEnd, yFirst, z),
                            new Point3(xEnd, yFirst, zHook)
                        };
                    }
                    else
                    {
                        pts = new[]
                        {
                            new Point3(xStart, yFirst, z),
                            new Point3(xEnd, yFirst, z)
                        };
                    }

                    sets.Add(new BarSet("Top-X", BarDirection.AlongX, l.Diameter,
                        pts, new Point3(0, stepY, 0), count, false, new Point3(0, 1, 0)));
                }
            }

            // 4. Thép đai cấu tạo hông đài theo TCVN 198:1997 (khi H > 700 mm)
            if (s.SideTies.Enabled && f.Height > 400)
            {
                var tie = s.SideTies;
                double zStart = c.Bottom + 120.0;
                double zEnd = f.Height - c.Top - 120.0;

                if (zEnd > zStart)
                {
                    double heightSpan = zEnd - zStart;
                    int count = Math.Max(2, (int)Math.Ceiling(heightSpan / tie.Spacing) + 1);
                    double stepZ = heightSpan / (count - 1);

                    double x1 = c.Side + tie.Diameter / 2.0;
                    double x2 = f.LengthX - c.Side - tie.Diameter / 2.0;
                    double y1 = c.Side + tie.Diameter / 2.0;
                    double y2 = f.WidthY - c.Side - tie.Diameter / 2.0;

                    // Vòng đai chu vi khép kín (5 điểm)
                    var tiePts = new[]
                    {
                        new Point3(x1, y1, zStart),
                        new Point3(x2, y1, zStart),
                        new Point3(x2, y2, zStart),
                        new Point3(x1, y2, zStart),
                        new Point3(x1, y1, zStart)
                    };

                    sets.Add(new BarSet("SideTies", BarDirection.HorizontalTie, tie.Diameter,
                        tiePts, new Point3(0, 0, stepZ), count, true, new Point3(0, 0, 1)));
                }
            }

            ValidateCoverEnvelope(f, c, sets);
            return new RebarPlan(sets);
        }

        private static double ResolveHookLength(FootingModel f, CoverSettings c, double barCenterZ,
                                                double diameter, double requestedLength, HookTypeOption direction, string barName)
        {
            double radius = diameter / 2.0;
            double available = direction == HookTypeOption.HookUp
                ? f.Height - c.Top - radius - barCenterZ
                : barCenterZ - c.Bottom - radius;
            if (available <= 1e-6)
                throw new ArgumentException($"Móc {barName} theo hướng {direction} không đủ chỗ trong lớp bảo vệ của móng.");

            if (requestedLength > 0)
            {
                if (requestedLength > available + 1e-6)
                    throw new ArgumentException($"Chiều dài móc {barName} ({requestedLength:0.#} mm) vượt khoảng trống còn lại ({available:0.#} mm) bên trong lớp bảo vệ móng.");
                return requestedLength;
            }

            if (available < 50.0)
                throw new ArgumentException($"Không đủ chiều cao để tạo móc {barName} mặc định mà vẫn giữ lớp bảo vệ móng.");
            return Math.Min(available, Math.Max(150.0, 15.0 * diameter));
        }

        private static void ValidateCoverEnvelope(FootingModel f, CoverSettings c, IList<BarSet> sets)
        {
            const double tolerance = 1e-6;
            foreach (BarSet set in sets)
            {
                double radius = set.Diameter / 2.0;
                foreach (IReadOnlyList<Point3> bar in set.AllBarsPoints())
                foreach (Point3 p in bar)
                {
                    if (p.X - radius < c.Side - tolerance || p.X + radius > f.LengthX - c.Side + tolerance ||
                        p.Y - radius < c.Side - tolerance || p.Y + radius > f.WidthY - c.Side + tolerance ||
                        p.Z - radius < c.Bottom - tolerance || p.Z + radius > f.Height - c.Top + tolerance)
                        throw new ArgumentException($"Thanh/móc {set.Name} vượt lớp bảo vệ của móng. Tăng kích thước móng hoặc điều chỉnh lớp bảo vệ/thép/móc.");
                }
            }
        }
    }
}
