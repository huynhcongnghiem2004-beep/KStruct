using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace ColumnRebarPro.V3
{
    internal static class V3AnchorGeometryCheck
    {
        private sealed class Part
        {
            internal Solid Solid;
            internal Transform ToModel;
        }

        internal static void Validate(Document doc, V3SegmentWork work)
        {
            var solids = new List<Part>();
            Collect(work.FoundationHost.get_Geometry(new Options()), Transform.Identity, solids);
            if (solids.Count == 0) throw new InvalidOperationException("Không đọc được solid móng để kiểm tra chân neo.");
            var g = work.Geometry;
            double horizontal = V3Units.Ft(V3AnchorLayoutPlan.HorizontalLength(work.FoundationAnchorLengthMm,
                V3Units.Mm(g.Bottom - work.FoundationBendZ)));
            if (horizontal <= doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Chân neo ngang quá ngắn để Revit tạo hình.");
            double margin = V3Units.Ft(work.FoundationCoverMm + work.Config.MainDiameter / 2 - .05);
            double epsilon = V3Units.Ft(.05);
            foreach (var array in V3RebarSetCreator.AnchorPlan(work))
            for (int i = 0; i < array.Count; i++)
            {
                XYZ bend = g.At(V3Units.Ft(array.X + array.StepX * i),
                    V3Units.Ft(array.Y + array.StepY * i), work.FoundationBendZ);
                XYZ direction = g.X * array.HookX + g.Y * array.HookY;
                XYZ toe = bend + direction * horizontal;
                XYZ side = XYZ.BasisZ.CrossProduct(direction);
                // Probe the bounding envelope of the horizontal leg, including its end clearances.
                foreach (int sideSign in new[] { -1, 0, 1 })
                foreach (int zSign in new[] { -1, 0, 1 })
                {
                    XYZ offset = side * (sideSign * margin) + XYZ.BasisZ * (zSign * margin);
                    RequireInside(solids, Line.CreateBound(toe + direction * margin + offset,
                        bend - direction * margin + offset));
                }
                XYZ embedTop = new XYZ(bend.X, bend.Y, g.Bottom - epsilon);
                foreach (XYZ offset in new[] { XYZ.Zero, g.X * margin, -g.X * margin, g.Y * margin, -g.Y * margin })
                    RequireInside(solids, Line.CreateBound(bend + offset, embedTop + offset));
            }
        }

        private static void Collect(GeometryElement geometry, Transform transform, List<Part> parts)
        {
            if (geometry == null) return;
            foreach (GeometryObject obj in geometry)
            {
                var solid = obj as Solid;
                if (solid != null && solid.Volume > 1e-9) parts.Add(new Part { Solid = solid, ToModel = transform });
                var instance = obj as GeometryInstance;
                if (instance != null) Collect(instance.GetSymbolGeometry(), transform.Multiply(instance.Transform), parts);
            }
        }

        private static void RequireInside(List<Part> solids, Line line)
        {
            var intervals = new List<Tuple<double, double>>();
            XYZ origin = line.GetEndPoint(0), axis = line.Direction;
            foreach (var part in solids)
            {
                Curve local = line.CreateTransformed(part.ToModel.Inverse);
                using (var options = new SolidCurveIntersectionOptions { ResultType = SolidCurveIntersectionMode.CurveSegmentsInside })
                using (var intersection = part.Solid.IntersectWithCurve(local, options))
                    for (int i = 0; i < intersection.SegmentCount; i++)
                    {
                        Curve segment = intersection.GetCurveSegment(i);
                        double a = (part.ToModel.OfPoint(segment.GetEndPoint(0)) - origin).DotProduct(axis);
                        double b = (part.ToModel.OfPoint(segment.GetEndPoint(1)) - origin).DotProduct(axis);
                        intervals.Add(Tuple.Create(Math.Min(a, b), Math.Max(a, b)));
                    }
            }
            double covered = 0, tolerance = V3Units.Ft(.1);
            foreach (var interval in intervals.OrderBy(x => x.Item1))
            {
                if (interval.Item1 > covered + tolerance) break;
                covered = Math.Max(covered, interval.Item2);
            }
            if (covered < line.Length - tolerance)
                throw new InvalidOperationException("Hướng/chiều dài chân L không nằm đủ trong solid móng với cover đã nhập. Đổi hướng neo hoặc đối chiếu lại kích thước thiết kế.");
        }
    }
}
