using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace SDK_HD.KStruct.NativeDocumentation
{
    internal static class FootingDrawingGeometry
    {
        internal static List<XYZ> WorldVertices(FamilyInstance host)
        {
            var result = new List<XYZ>();
            Collect(host.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }), result);
            if (result.Count == 0) throw new InvalidOperationException("Không đọc được solid móng " + host.Id.Value);
            return result;
        }
        private static void Collect(GeometryElement geometry, List<XYZ> result)
        {
            if (geometry == null) return;
            foreach (GeometryObject obj in geometry)
            {
                if (obj is Solid solid && solid.Volume > 1e-9)
                    foreach (Face face in solid.Faces)
                    {
                        Mesh mesh = face.Triangulate();
                        foreach (XYZ vertex in mesh.Vertices) result.Add(vertex);
                    }
                else if (obj is GeometryInstance instance) Collect(instance.GetInstanceGeometry(), result);
            }
        }
        internal static Transform Axes(FamilyInstance host)
        {
            Transform t = host.GetTransform();
            if (Math.Abs(t.BasisZ.Normalize().DotProduct(XYZ.BasisZ)) < 0.999999)
                throw new InvalidOperationException("Chưa hỗ trợ móng nghiêng: " + host.Id.Value);
            return t;
        }
        internal static BoundingBoxXYZ LocalBounds(FamilyInstance host)
        {
            Transform axes = Axes(host);
            List<XYZ> p = WorldVertices(host).Select(axes.Inverse.OfPoint).ToList();
            return new BoundingBoxXYZ { Transform = axes,
                Min = new XYZ(p.Min(x => x.X), p.Min(x => x.Y), p.Min(x => x.Z)),
                Max = new XYZ(p.Max(x => x.X), p.Max(x => x.Y), p.Max(x => x.Z)) };
        }
        internal static List<XYZ> Rectangle(FamilyInstance host, double marginMm)
        {
            BoundingBoxXYZ bounds = LocalBounds(host);
            // Confirm an actual rectangular footprint instead of silently offsetting a world bounding box.
            var edges = new List<List<XYZ>>();
            CollectHorizontalLoops(host.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine }), edges);
            Transform inv = bounds.Transform.Inverse;
            bool valid = edges.Any(loop =>
            {
                List<XYZ> pts = loop.Select(inv.OfPoint).ToList();
                if (pts.Count < 4) return false;
                bool aligned = Enumerable.Range(0, pts.Count).All(i =>
                {
                    XYZ a = pts[i], b = pts[(i + 1) % pts.Count];
                    return Math.Abs(a.X - b.X) < 1e-6 || Math.Abs(a.Y - b.Y) < 1e-6;
                });
                double area = Math.Abs(Enumerable.Range(0, pts.Count).Sum(i => pts[i].X * pts[(i + 1) % pts.Count].Y - pts[(i + 1) % pts.Count].X * pts[i].Y)) / 2;
                double boxArea = (bounds.Max.X - bounds.Min.X) * (bounds.Max.Y - bounds.Min.Y);
                return aligned && Math.Abs(area - boxArea) < Math.Max(1e-6, boxArea * 1e-6);
            });
            if (!valid) throw new InvalidOperationException("Crop chính xác hiện hỗ trợ móng có biên chữ nhật theo trục family. Móng này cần cấu hình biên riêng.");
            double m = UnitUtils.ConvertToInternalUnits(marginMm, UnitTypeId.Millimeters);
            double x0 = bounds.Min.X - m, x1 = bounds.Max.X + m, y0 = bounds.Min.Y - m, y1 = bounds.Max.Y + m;
            return new[] { new XYZ(x0,y0,0), new XYZ(x1,y0,0), new XYZ(x1,y1,0), new XYZ(x0,y1,0) }
                .Select(bounds.Transform.OfPoint).ToList();
        }
        private static void CollectHorizontalLoops(GeometryElement geometry, List<List<XYZ>> loops)
        {
            if (geometry == null) return;
            foreach (GeometryObject obj in geometry)
            {
                if (obj is Solid solid && solid.Volume > 1e-9)
                {
                    foreach (Face face in solid.Faces)
                        if (face is PlanarFace planar && Math.Abs(planar.FaceNormal.DotProduct(XYZ.BasisZ)) > 0.999999)
                            foreach (EdgeArray loop in face.EdgeLoops)
                            {
                                var pts = new List<XYZ>();
                                foreach (Edge edge in loop)
                                {
                                    Curve curve = edge.AsCurveFollowingFace(face);
                                    if (!(curve is Line)) { pts.Clear(); break; }
                                    pts.Add(curve.GetEndPoint(0));
                                }
                                if (pts.Count > 0) loops.Add(pts);
                            }
                }
                else if (obj is GeometryInstance instance) CollectHorizontalLoops(instance.GetInstanceGeometry(), loops);
            }
        }
        internal static void SetPlanCrop(ViewPlan view, FamilyInstance host, double marginMm)
        {
            List<XYZ> points = Rectangle(host, marginMm);
            XYZ normal = view.ViewDirection.Normalize();
            points = points.Select(p => p - normal * (p - view.Origin).DotProduct(normal)).ToList();
            var loop = new CurveLoop();
            for (int i = 0; i < 4; i++) loop.Append(Line.CreateBound(points[i], points[(i + 1) % 4]));
            view.CropBoxActive = true;
            view.GetCropRegionShapeManager().SetCropShape(loop);
            view.CropBoxVisible = false;
        }
    }
}
