using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace SDK_HD.KStruct.NativeDocumentation
{
    internal static class FootingDrawingDimensions
    {
        private sealed class PlaneReference
        {
            internal Reference Reference;
            internal XYZ Normal;
            internal XYZ Point;
        }
        private sealed class MeasuredReference
        {
            internal Reference Reference;
            internal double Coordinate;
        }
        internal static void Create(Document doc, FamilyInstance host, View view, DimensionType type,
            FootingDrawingProfile profile, IList<string> warnings)
        {
            BoundingBoxXYZ bounds = FootingDrawingGeometry.LocalBounds(host);
            XYZ center = bounds.Transform.OfPoint((bounds.Min + bounds.Max) * 0.5);
            var faces = new List<PlaneReference>();
            Collect(host.get_Geometry(new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine }), Transform.Identity, faces);
            var related = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .Where(f => f.Id != host.Id && f.Category != null &&
                    (f.Category.Id.Value == (long)BuiltInCategory.OST_StructuralColumns || f.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation))
                .Where(f => TouchesHost(f, bounds)).ToList();
            XYZ[] axes = { bounds.Transform.BasisX.Normalize(), bounds.Transform.BasisY.Normalize() };
            for (int axisIndex = 0; axisIndex < 2; axisIndex++)
            {
                XYZ axis = axes[axisIndex], perpendicular = axes[1 - axisIndex];
                var candidates = faces.Where(f => f.Reference != null && Math.Abs(f.Normal.DotProduct(axis)) > 0.9999)
                    .OrderBy(f => f.Point.DotProduct(axis)).ToList();
                if (candidates.Count < 2)
                { warnings.Add("Thiếu reference mép móng để tạo dim hai lớp theo " + (axisIndex == 0 ? "X" : "Y") + " trong " + view.Name); continue; }
                PlaneReference low = candidates.First(), high = candidates.Last();
                double a = low.Point.DotProduct(axis), b = high.Point.DotProduct(axis);
                var inner = new List<MeasuredReference> { new MeasuredReference { Reference = low.Reference, Coordinate = a } };
                foreach (FamilyInstance instance in related)
                {
                    if (!(instance.Location is LocationPoint point)) continue;
                    Transform t = instance.GetTransform();
                    FamilyInstanceReferenceType? kind = Math.Abs(t.BasisX.Normalize().DotProduct(axis)) > 0.9999 ? FamilyInstanceReferenceType.CenterLeftRight :
                        Math.Abs(t.BasisY.Normalize().DotProduct(axis)) > 0.9999 ? (FamilyInstanceReferenceType?)FamilyInstanceReferenceType.CenterFrontBack : null;
                    if (!kind.HasValue) continue;
                    Reference reference = instance.GetReferences(kind.Value).FirstOrDefault();
                    if (reference == null) continue;
                    // Family center planes need not coincide with the insertion point; use the reference plane geometry.
                    double coordinate;
                    try
                    {
                        GeometryObject geometry = instance.GetGeometryObjectFromReference(reference);
                        if (geometry is PlanarFace plane) coordinate = plane.Origin.DotProduct(axis);
                        else
                        {
                            // The API does not expose every family reference plane as a face. Avoid guessed dimensions.
                            continue;
                        }
                    }
                    catch { continue; }
                    if (coordinate > a + 1e-6 && coordinate < b - 1e-6)
                        inner.Add(new MeasuredReference { Reference = reference, Coordinate = coordinate });
                }
                // Add physical side faces of intersecting columns/piles if center references aren't exposed as geometry.
                // These are measured face references, not invented center planes.
                if (inner.Count == 1)
                {
                    foreach (FamilyInstance instance in related)
                    {
                        var detailFaces = new List<PlaneReference>();
                        Collect(instance.get_Geometry(new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine }), Transform.Identity, detailFaces);
                        foreach (PlaneReference face in detailFaces.Where(f => f.Reference != null && Math.Abs(f.Normal.DotProduct(axis)) > 0.9999))
                        {
                            double c = face.Point.DotProduct(axis);
                            if (c > a + 1e-6 && c < b - 1e-6) inner.Add(new MeasuredReference { Reference = face.Reference, Coordinate = c });
                        }
                    }
                    if (inner.Count > 1) warnings.Add("Dim chi tiết " + view.Name + " dùng mép cột/cọc do không đọc được reference tim; cần đối chiếu bản vẽ.");
                }
                inner.Add(new MeasuredReference { Reference = high.Reference, Coordinate = b });
                inner = inner.OrderBy(r => r.Coordinate).GroupBy(r => Math.Round(r.Coordinate, 6)).Select(g => g.First()).ToList();
                double extent = axisIndex == 0 ? (bounds.Max.Y - bounds.Min.Y) / 2 : (bounds.Max.X - bounds.Min.X) / 2;
                foreach (int side in new[] { -1, 1 })
                {
                    double offset = UnitUtils.ConvertToInternalUnits(profile.DimensionOffsetPaperMm * view.Scale, UnitTypeId.Millimeters);
                    double gap = UnitUtils.ConvertToInternalUnits(profile.DimensionGapPaperMm * view.Scale, UnitTypeId.Millimeters);
                    XYZ origin = center + perpendicular * (side * (extent + offset));
                    if (inner.Count > 2) TryCreate(doc, view, type, axis, origin, a, b, inner.Select(r => r.Reference), warnings, "chi tiết");
                    else warnings.Add("Thiếu reference chi tiết cho lớp dim trong " + view.Name + " / " + (axisIndex == 0 ? "X" : "Y") + " / cạnh " + side);
                    TryCreate(doc, view, type, axis, origin + perpendicular * (side * gap), a, b,
                        new[] { low.Reference, high.Reference }, warnings, "tổng thể");
                }
            }
        }
        private static bool TouchesHost(FamilyInstance candidate, BoundingBoxXYZ host)
        {
            BoundingBoxXYZ box = candidate.get_BoundingBox(null);
            if (box == null) return false;
            Transform inverse = host.Transform.Inverse;
            var points = new List<XYZ>();
            for (int x=0;x<2;x++) for(int y=0;y<2;y++) for(int z=0;z<2;z++)
                points.Add(inverse.OfPoint(box.Transform.OfPoint(new XYZ(x==0?box.Min.X:box.Max.X,y==0?box.Min.Y:box.Max.Y,z==0?box.Min.Z:box.Max.Z))));
            double tolerance = UnitUtils.ConvertToInternalUnits(20, UnitTypeId.Millimeters);
            XYZ center = inverse.OfPoint(candidate.Location is LocationPoint location ? location.Point : (box.Min+box.Max)*0.5);
            return center.X > host.Min.X && center.X < host.Max.X && center.Y > host.Min.Y && center.Y < host.Max.Y &&
                points.Max(p=>p.Z) >= host.Min.Z-tolerance && points.Min(p=>p.Z) <= host.Max.Z+tolerance;
        }
        private static void TryCreate(Document doc, View view, DimensionType type, XYZ axis, XYZ origin, double a, double b,
            IEnumerable<Reference> references, IList<string> warnings, string label)
        {
            var array = new ReferenceArray(); foreach (Reference reference in references) array.Append(reference);
            XYZ start = origin + axis * (a-origin.DotProduct(axis)), end = origin + axis * (b-origin.DotProduct(axis));
            // Project the dimension line into the view plane without changing its measured direction.
            XYZ n = view.ViewDirection.Normalize(); start -= n * (start-view.Origin).DotProduct(n); end -= n * (end-view.Origin).DotProduct(n);
            try { doc.Create.NewDimension(view, Line.CreateBound(start,end), array, type); }
            catch (Exception ex) { warnings.Add("Không tạo được dim " + label + " trong " + view.Name + ": " + ex.Message); }
        }
        private static void Collect(GeometryElement geometry, Transform transform, List<PlaneReference> result)
        {
            if (geometry == null) return;
            foreach (GeometryObject obj in geometry)
            {
                if (obj is Solid solid && solid.Volume > 1e-9)
                {
                    foreach (Face face in solid.Faces)
                        if (face is PlanarFace plane)
                            result.Add(new PlaneReference { Reference = plane.Reference, Normal = transform.OfVector(plane.FaceNormal).Normalize(), Point = transform.OfPoint(plane.Origin) });
                }
                else if (obj is GeometryInstance instance)
                    Collect(instance.GetSymbolGeometry(), transform.Multiply(instance.Transform), result);
            }
        }
    }
}
