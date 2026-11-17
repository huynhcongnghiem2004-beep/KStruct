using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace ColumnRebarPro.V3
{
    internal static class V3ColumnReferences
    {
        private sealed class FaceCandidate
        {
            internal Reference Reference;
            internal double Position;
        }

        internal static ReferenceArray Find(FamilyInstance column, XYZ direction,
            FamilyInstanceReferenceType firstType, FamilyInstanceReferenceType secondType)
        {
            var first = column.GetReferences(firstType).FirstOrDefault();
            var second = column.GetReferences(secondType).FirstOrDefault();
            if (first != null && second != null)
            {
                var named = new ReferenceArray(); named.Append(first); named.Append(second);
                return named;
            }
            // GetInstanceGeometry returns copies whose references cannot be used to dimension.
            // Read original symbol geometry and transform only the coordinates used for sorting.
            var faces = new List<FaceCandidate>();
            XYZ axis = direction.Normalize();
            var options = new Options { ComputeReferences = true, IncludeNonVisibleObjects = false,
                DetailLevel = ViewDetailLevel.Fine };
            Visit(column.get_Geometry(options), Transform.Identity, axis, faces);
            if (faces.Count < 2)
                throw new InvalidOperationException("Cột " + column.Id.Value +
                    " không có hai mặt phẳng tham chiếu theo phương kích thước. Kiểm tra geometry/reference trong family.");
            var ordered = faces.OrderBy(f => f.Position).ToList();
            if (ordered.Last().Position - ordered.First().Position < column.Document.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Hai mặt tham chiếu cột trùng nhau, không thể tạo kích thước.");
            var result = new ReferenceArray();
            result.Append(ordered.First().Reference); result.Append(ordered.Last().Reference);
            return result;
        }

        private static void Visit(GeometryElement geometry, Transform transform, XYZ axis, List<FaceCandidate> faces)
        {
            if (geometry == null) return;
            foreach (GeometryObject item in geometry)
            {
                var instance = item as GeometryInstance;
                if (instance != null)
                {
                    Visit(instance.GetSymbolGeometry(), transform.Multiply(instance.Transform), axis, faces);
                    continue;
                }
                var solid = item as Solid;
                if (solid == null || solid.Faces.Size == 0 || solid.Volume < 1e-9) continue;
                foreach (Face face in solid.Faces)
                {
                    var plane = face as PlanarFace;
                    if (plane == null || plane.Reference == null) continue;
                    XYZ normal = transform.OfVector(plane.FaceNormal).Normalize();
                    if (Math.Abs(normal.DotProduct(axis)) < .9999) continue;
                    faces.Add(new FaceCandidate { Reference = plane.Reference,
                        Position = transform.OfPoint(plane.Origin).DotProduct(axis) });
                }
            }
        }
    }
}
