using System;
using System.Collections.Generic;
using System.Linq;

namespace ColumnRebarPro.V3
{
    // Pure millimetre geometry checks; no Revit API dependency.
    internal static class V4AnchorGeometry
    {
        internal static V3Point3 PlaneNormal(IList<V3Point3> points,
            V3Point3? distribution = null, double minSegmentMm = 0.1)
        {
            if (points == null || points.Count < 2)
                throw new InvalidOperationException("Thanh neo cần ít nhất hai điểm.");
            if (!Finite(minSegmentMm) || minSegmentMm <= 0)
                throw new ArgumentOutOfRangeException(nameof(minSegmentMm));
            foreach (var p in points)
                if (!Finite(p.X) || !Finite(p.Y) || !Finite(p.Z))
                    throw new InvalidOperationException("Tọa độ thanh neo không phải số hữu hạn.");
            for (int i = 1; i < points.Count; i++)
                if ((points[i] - points[i - 1]).Length <= minSegmentMm)
                    throw new InvalidOperationException("Thanh neo có đoạn quá ngắn hoặc điểm trùng tại " + i + ".");

            var axis = (points[1] - points[0]).Unit;
            V3Point3 normal = new V3Point3();
            for (int i = 2; i < points.Count; i++)
            {
                var cross = axis.Cross(points[i] - points[0]);
                if (cross.Length > 1e-5) { normal = cross.Unit; break; }
            }
            if (normal.Length < 0.5)
            {
                // Collinear two-point paths have infinitely many valid planes.
                // Prefer set distribution when perpendicular to the line.
                if (distribution.HasValue && distribution.Value.Length > 1e-5)
                {
                    var step = distribution.Value.Unit;
                    if (Math.Abs(step.Dot(axis)) > 1e-5)
                        throw new InvalidOperationException("Hướng phân bố Rebar Set không vuông góc với thanh thẳng.");
                    normal = step;
                }
                else
                {
                    var reference = Math.Abs(axis.Dot(new V3Point3(1,0,0))) < .9
                        ? new V3Point3(1,0,0) : new V3Point3(0,1,0);
                    normal = (reference - axis * axis.Dot(reference)).Unit;
                }
            }
            foreach (var point in points)
                if (Math.Abs((point - points[0]).Dot(normal)) > 0.05)
                    throw new InvalidOperationException("Thanh neo có các đoạn không đồng phẳng (>0,05 mm).");

            for (int i = 1; i < points.Count - 1; i++)
            {
                var before = (points[i] - points[i-1]).Unit;
                var after = (points[i+1] - points[i]).Unit;
                if (before.Dot(after) < -0.999999)
                    throw new InvalidOperationException("Thanh neo quay ngược gần 180° tại điểm " + i + ".");
            }

            if (distribution.HasValue && distribution.Value.Length > 1e-5)
            {
                var dir = distribution.Value.Unit;
                // A shape-driven set must translate the entire bar along the plane normal.
                if (Math.Abs(Math.Abs(dir.Dot(normal)) - 1) > 1e-5)
                    throw new InvalidOperationException(
                        "Hướng phân bố Rebar Set không song song pháp tuyến mặt phẳng uốn.");
                if (dir.Dot(normal) < 0) normal = normal * -1;
            }
            return normal;
        }

        private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    }
}
