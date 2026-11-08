using System;
using System.Collections.Generic;

namespace SDK_HD.KStruct.FootingRebar
{
    public readonly struct Point3
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public Point3(double x, double y, double z) { X = x; Y = y; Z = z; }

        public override string ToString() => $"({X:0.##}, {Y:0.##}, {Z:0.##})";
    }

    public readonly struct BarSegment
    {
        public Point3 Start { get; }
        public Point3 End { get; }
        public BarSegment(Point3 start, Point3 end) { Start = start; End = end; }
    }

    public enum BarDirection
    {
        AlongX,
        AlongY,
        HorizontalTie
    }

    /// <summary>
    /// Một nhóm thanh thép song song, cách đều nhau (tương ứng một Rebar set trong Revit).
    /// </summary>
    public sealed class BarSet
    {
        public string Name { get; }
        public BarDirection Direction { get; }
        public double Diameter { get; }
        /// <summary>Các điểm đa giác của thanh thép đầu tiên trong hệ tọa độ cục bộ của móng.</summary>
        public IReadOnlyList<Point3> FirstBarPoints { get; }
        /// <summary>Vector dịch chuyển từ thanh này sang thanh kế tiếp (mm).</summary>
        public Point3 Step { get; }
        public int Count { get; }
        /// <summary>Là đai cấu tạo khép kín (StirrupTie) hay cốt thép tiêu chuẩn (Standard).</summary>
        public bool IsStirrup { get; }

        /// <summary>Vector pháp tuyến cục bộ của mặt phẳng chứa thanh thép (dùng tính vector normal trong Revit).</summary>
        public Point3 LocalNormal { get; }

        public Point3 FirstStart => FirstBarPoints != null && FirstBarPoints.Count > 0 ? FirstBarPoints[0] : default;
        public Point3 FirstEnd => FirstBarPoints != null && FirstBarPoints.Count > 1 ? FirstBarPoints[FirstBarPoints.Count - 1] : default;

        public BarSet(string name, BarDirection direction, double diameter,
                      Point3 firstStart, Point3 firstEnd, Point3 step, int count)
            : this(name, direction, diameter, new[] { firstStart, firstEnd }, step, count, false,
                   direction == BarDirection.AlongX ? new Point3(0, 1, 0) : new Point3(1, 0, 0))
        {
        }

        public BarSet(string name, BarDirection direction, double diameter,
                      IReadOnlyList<Point3> firstBarPoints, Point3 step, int count,
                      bool isStirrup = false, Point3? localNormal = null)
        {
            if (firstBarPoints == null || firstBarPoints.Count < 2)
                throw new ArgumentException("Mỗi thanh thép phải có tối thiểu 2 điểm.");
            Name = name;
            Direction = direction;
            Diameter = diameter;
            FirstBarPoints = firstBarPoints;
            Step = step;
            Count = Math.Max(1, count);
            IsStirrup = isStirrup;

            if (localNormal.HasValue)
            {
                LocalNormal = localNormal.Value;
            }
            else
            {
                LocalNormal = direction == BarDirection.AlongX ? new Point3(0, 1, 0)
                            : direction == BarDirection.AlongY ? new Point3(1, 0, 0)
                            : new Point3(0, 0, 1);
            }
        }

        /// <summary>
        /// Liệt kê các đoạn thẳng cấu thành các thanh trong nhóm (phục vụ vẽ preview hoặc kiểm tra).
        /// </summary>
        public IEnumerable<BarSegment> Bars()
        {
            for (int i = 0; i < Count; i++)
            {
                double dx = i * Step.X;
                double dy = i * Step.Y;
                double dz = i * Step.Z;

                for (int p = 0; p < FirstBarPoints.Count - 1; p++)
                {
                    var s = new Point3(FirstBarPoints[p].X + dx, FirstBarPoints[p].Y + dy, FirstBarPoints[p].Z + dz);
                    var e = new Point3(FirstBarPoints[p + 1].X + dx, FirstBarPoints[p + 1].Y + dy, FirstBarPoints[p + 1].Z + dz);
                    yield return new BarSegment(s, e);
                }
            }
        }

        /// <summary>
        /// Trả về danh sách toạ độ điểm của từng thanh trong bộ.
        /// </summary>
        public IEnumerable<IReadOnlyList<Point3>> AllBarsPoints()
        {
            for (int i = 0; i < Count; i++)
            {
                double dx = i * Step.X;
                double dy = i * Step.Y;
                double dz = i * Step.Z;

                var pts = new Point3[FirstBarPoints.Count];
                for (int p = 0; p < FirstBarPoints.Count; p++)
                {
                    pts[p] = new Point3(FirstBarPoints[p].X + dx, FirstBarPoints[p].Y + dy, FirstBarPoints[p].Z + dz);
                }
                yield return pts;
            }
        }
    }

    /// <summary>Kế hoạch thép thuần dữ liệu. Không đụng Revit API; RebarFactory sẽ commit kế hoạch này.</summary>
    public sealed class RebarPlan
    {
        public IReadOnlyList<BarSet> Sets { get; }
        public RebarPlan(IReadOnlyList<BarSet> sets) { Sets = sets ?? new List<BarSet>(); }
    }
}
