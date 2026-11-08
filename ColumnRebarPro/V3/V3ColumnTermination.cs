using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
namespace ColumnRebarPro.V3
{
    internal static class V3ColumnTermination
    {
        // Recognize horizontal beams meeting the top of this physical column.
        // For sloped/curved framing there is no single soffit elevation: fall back to column top.
        internal static double? BeamSoffitMm(V3SegmentWork work)
        {
            var host=work.Column;var box=host.get_BoundingBox(null);if(box==null)return null;
            double tolerance=V3Units.Ft(2);double? lowest=null;
            foreach(var beam in new FilteredElementCollector(host.Document).OfCategory(BuiltInCategory.OST_StructuralFraming)
                .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().Where(b=>b.StructuralType==StructuralType.Beam))
            {
                if(beam.CreatedPhaseId!=host.CreatedPhaseId||beam.DemolishedPhaseId!=host.DemolishedPhaseId||
                    (beam.DesignOption?.Id??ElementId.InvalidElementId)!=(host.DesignOption?.Id??ElementId.InvalidElementId))continue;
                if(!(beam.Location is LocationCurve location)||!(location.Curve is Line line)||
                    Math.Abs(line.GetEndPoint(0).Z-line.GetEndPoint(1).Z)>tolerance)continue;
                var b=beam.get_BoundingBox(null);if(b==null)continue;
                if(b.Max.X<box.Min.X-tolerance||b.Min.X>box.Max.X+tolerance||
                    b.Max.Y<box.Min.Y-tolerance||b.Min.Y>box.Max.Y+tolerance||
                    b.Max.Z<work.Geometry.Top-tolerance||b.Min.Z>work.Geometry.Top+tolerance||b.Min.Z<=work.Geometry.Bottom)continue;
                var triangles=new List<V3Point3[]>();Collect(beam.get_Geometry(new Options {DetailLevel=ViewDetailLevel.Fine}),work.Geometry,triangles);
                double? soffit=V3ColumnTerminationRules.SoffitInRectangle(triangles,
                    V3Units.Mm(work.Geometry.Width/2+tolerance),V3Units.Mm(work.Geometry.Depth/2+tolerance));
                if(soffit.HasValue&&soffit.Value>V3Units.Mm(work.Geometry.Bottom))
                    lowest=lowest.HasValue?Math.Min(lowest.Value,soffit.Value):soffit.Value;
            }
            return lowest;
        }
        private static void Collect(GeometryElement geometry,V3Geometry column,List<V3Point3[]> triangles)
        {
            if(geometry==null)return;
            foreach(GeometryObject obj in geometry)
            {
                if(obj is Solid solid&&solid.Volume>1e-9)
                    foreach(Face face in solid.Faces)
                    {
                        Mesh mesh=face.Triangulate();
                        for(int i=0;i<mesh.NumTriangles;i++)
                        {
                            MeshTriangle triangle=mesh.get_Triangle(i);var points=new V3Point3[3];
                            for(int j=0;j<3;j++)
                            {
                                XYZ p=triangle.get_Vertex(j),delta=p-column.Center;
                                points[j]=new V3Point3(V3Units.Mm(delta.DotProduct(column.X)),V3Units.Mm(delta.DotProduct(column.Y)),V3Units.Mm(p.Z));
                            }
                            triangles.Add(points);
                        }
                    }
                else if(obj is GeometryInstance instance)Collect(instance.GetInstanceGeometry(),column,triangles);
            }
        }
    }
}
