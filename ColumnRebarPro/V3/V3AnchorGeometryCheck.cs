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
            // The current UI defines FoundationAnchorLengthMm as the horizontal L-leg (Ln).
            double horizontal = V3Units.Ft(work.FoundationAnchorLengthMm);
            if (horizontal <= doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Chân neo ngang quá ngắn để Revit tạo hình.");
            double margin = V3Units.Ft(work.FoundationCoverMm + work.Config.MainDiameter / 2 - .05);
            double epsilon = V3Units.Ft(.05);
            var errors = new List<V3AnchorErrorRow>();
            int barIndex = 0;
            foreach (var array in V3RebarSetCreator.AnchorPlan(work))
            for (int i = 0; i < array.Count; i++)
            {
                barIndex++;
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
                    Inspect(solids, Line.CreateBound(toe + direction * margin + offset,
                        bend - direction * margin + offset), errors, barIndex, "Chân ngang",
                        (sideSign < 0 ? "Bên phải" : sideSign > 0 ? "Bên trái" : "Giữa") +
                        "; " + (zSign < 0 ? "bao đáy" : zSign > 0 ? "bao trên" : "cao độ tim"));
                }
                XYZ embedTop = new XYZ(bend.X, bend.Y, g.Bottom - epsilon);
                var offsets = new[] { XYZ.Zero, g.X * margin, -g.X * margin, g.Y * margin, -g.Y * margin };
                string[] sides = { "Tim", "Bao +X cột", "Bao -X cột", "Bao +Y cột", "Bao -Y cột" };
                for (int j = 0; j < offsets.Length; j++)
                    Inspect(solids, Line.CreateBound(bend + offsets[j], embedTop + offsets[j]), errors,
                        barIndex, "Đoạn đứng", sides[j]);
            }
            if (errors.Count > 0)
            {
                string mark = work.Column.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
                string context = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " | Cột " + mark + " (ID " + work.Column.Id.Value + "); móng ID " + work.FoundationHost.Id.Value +
                    "; Lₙ=" + work.FoundationAnchorLengthMm + " mm; cover=" + work.FoundationCoverMm +
                    " mm; D=" + work.Config.MainDiameter + " mm; hướng=" + work.Config.FoundationAnchorDirection +
                    "; đáy cột Z=" + V3Units.Mm(g.Bottom).ToString("0.###") +
                    "; tim chân neo Z=" + V3Units.Mm(work.FoundationBendZ).ToString("0.###") + " mm.";
                throw new V3AnchorDiagnosticException(context, errors);
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

        private static void Inspect(List<Part> solids, Line line, List<V3AnchorErrorRow> errors,
            int barIndex, string segmentName, string probe)
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
            var gap = V3AnchorGap.FirstGap(intervals, line.Length, V3Units.Ft(.1));
            if (gap != null) errors.Add(new V3AnchorErrorRow {
                Bar = barIndex, Segment = segmentName, Probe = probe,
                Start = Coordinates(origin + axis * gap.Item1), End = Coordinates(origin + axis * gap.Item2),
                GapMm = V3Units.Mm(gap.Item2 - gap.Item1)
            });
        }

        private static string Coordinates(XYZ point) => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0:0.###}; {1:0.###}; {2:0.###}", V3Units.Mm(point.X), V3Units.Mm(point.Y), V3Units.Mm(point.Z));
    }
}
