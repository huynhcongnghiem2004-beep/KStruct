using System;
using System.Collections.Generic;
using System.Linq;

namespace KStruct.Core
{
    public enum RebarTagSideMode
    {
        Left,
        Right,
        BothSides,
        Auto
    }

    public sealed class RebarTagAnchor2D
    {
        public string Key { get; set; }
        public double U { get; set; }
        public double V { get; set; }
    }

    public sealed class RebarTagLayout2D
    {
        public string Key { get; set; }
        public int Side { get; set; }
        public double AnchorU { get; set; }
        public double AnchorV { get; set; }
        public double HeadU { get; set; }
        public double HeadV { get; set; }
        public bool NeedsElbow { get; set; }
    }

    /// <summary>
    /// View-coordinate layout policy. Values may be feet, millimetres or any
    /// other consistent unit; the policy deliberately has no Revit dependency.
    /// </summary>
    public static class RebarTagLayoutPolicy
    {
        public static IReadOnlyList<RebarTagLayout2D> Create(
            IEnumerable<RebarTagAnchor2D> anchors,
            double columnMinU,
            double columnMaxU,
            double offset,
            double verticalSpacing,
            RebarTagSideMode mode)
        {
            if (anchors == null) throw new ArgumentNullException(nameof(anchors));
            RequireFinite(columnMinU, nameof(columnMinU));
            RequireFinite(columnMaxU, nameof(columnMaxU));
            RequirePositive(offset, nameof(offset));
            RequirePositive(verticalSpacing, nameof(verticalSpacing));
            if (columnMaxU <= columnMinU)
                throw new ArgumentOutOfRangeException(nameof(columnMaxU), "Column bounds must be ordered.");

            var source = anchors.Select((a, index) =>
            {
                if (a == null) throw new ArgumentException("Tag anchors cannot contain null.", nameof(anchors));
                RequireFinite(a.U, nameof(a.U));
                RequireFinite(a.V, nameof(a.V));
                return new Item { Anchor = a, Index = index };
            }).ToList();

            double center = (columnMinU + columnMaxU) / 2.0;
            int leftCount = 0, rightCount = 0;
            foreach (Item item in source.OrderBy(i => i.Anchor.V).ThenBy(i => i.Index))
            {
                switch (mode)
                {
                    case RebarTagSideMode.Left:
                        item.Side = -1;
                        break;
                    case RebarTagSideMode.Right:
                        item.Side = 1;
                        break;
                    case RebarTagSideMode.BothSides:
                        item.Side = item.Anchor.U < center ? -1 : item.Anchor.U > center ? 1 :
                            (leftCount <= rightCount ? -1 : 1);
                        break;
                    default:
                        // Auto favours the nearest edge. Equal distances are balanced
                        // so dense groups do not accumulate on only one side.
                        double leftDistance = Math.Abs(item.Anchor.U - columnMinU);
                        double rightDistance = Math.Abs(columnMaxU - item.Anchor.U);
                        item.Side = Math.Abs(leftDistance - rightDistance) > 1e-9
                            ? (leftDistance < rightDistance ? -1 : 1)
                            : (leftCount <= rightCount ? -1 : 1);
                        break;
                }
                if (item.Side < 0) leftCount++; else rightCount++;
            }

            foreach (IGrouping<int, Item> group in source.GroupBy(i => i.Side))
            {
                double? previous = null;
                foreach (Item item in group.OrderBy(i => i.Anchor.V).ThenBy(i => i.Index))
                {
                    double headV = item.Anchor.V;
                    if (previous.HasValue && headV - previous.Value < verticalSpacing)
                        headV = previous.Value + verticalSpacing;
                    item.HeadV = headV;
                    previous = headV;
                }
            }

            return source.OrderBy(i => i.Index).Select(item => new RebarTagLayout2D
            {
                Key = item.Anchor.Key,
                Side = item.Side,
                AnchorU = item.Anchor.U,
                AnchorV = item.Anchor.V,
                HeadU = item.Side < 0 ? columnMinU - offset : columnMaxU + offset,
                HeadV = item.HeadV,
                NeedsElbow = Math.Abs(item.HeadV - item.Anchor.V) > 1e-9
            }).ToList();
        }

        private static void RequireFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name);
        }

        private static void RequirePositive(double value, string name)
        {
            RequireFinite(value, name);
            if (value <= 0) throw new ArgumentOutOfRangeException(name);
        }

        private sealed class Item
        {
            internal RebarTagAnchor2D Anchor;
            internal int Index;
            internal int Side;
            internal double HeadV;
        }
    }
}
