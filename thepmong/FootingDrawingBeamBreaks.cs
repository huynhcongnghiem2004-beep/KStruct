using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace SDK_HD.KStruct.NativeDocumentation
{
    internal static class FootingDrawingBeamBreaks
    {
        internal static FamilySymbol Resolve(Document doc,FootingDrawingProfile p)=>new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_DetailComponents).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .FirstOrDefault(s=>s.FamilyName==p.BreakLineFamily&&s.Name==p.BreakLineType);

        internal static void Refresh(Document doc,FamilyInstance host,View view,FootingDrawingProfile profile,IList<string> warnings)
        {
            if(!profile.CreateBeamBreakLines){FootingDrawingDetailState.Clear(view,host,"BeamBreak");return;}
            if(double.IsNaN(profile.BreakLinePaddingPaperMm) || double.IsInfinity(profile.BreakLinePaddingPaperMm) || profile.BreakLinePaddingPaperMm<0)
                throw new InvalidOperationException("Khoảng dư Breakline không hợp lệ.");
            FamilySymbol symbol=Resolve(doc,profile);
            if(symbol==null){warnings.Add("Chưa chọn/nạp family Breakline cho "+view.Name+"; không tự vẽ ký hiệu thay thế.");return;}
            if(symbol.Family.FamilyPlacementType!=FamilyPlacementType.CurveBasedDetail&&symbol.Family.FamilyPlacementType!=FamilyPlacementType.ViewBased)
                throw new InvalidOperationException("Breakline phải là Detail Item đặt theo đường hoặc điểm.");
            if(!symbol.IsActive){symbol.Activate();doc.Regenerate();}
            IList<CurveLoop> shapes=view.GetCropRegionShapeManager().GetCropShape();
            if(shapes.Count!=1){warnings.Add("Không đặt Breakline cho crop tách vùng: "+view.Name);return;}
            // GetCropShape returns coordinates in the view frame; project beam geometry in model coordinates.
            var edges=shapes[0].Select(curve=>curve.CreateTransformed(view.CropBox.Transform)).ToList();if(edges.Any(e=>!(e is Line))){warnings.Add("Breakline yêu cầu crop có cạnh thẳng: "+view.Name);return;}
            FootingDrawingDetailState.Clear(view,host,"BeamBreak");
            if(view.AreAnnotationCategoriesHidden)view.AreAnnotationCategoriesHidden=false;
            if(view.AreModelCategoriesHidden)view.AreModelCategoriesHidden=false;
            if(view.GetCategoryHidden(new ElementId(BuiltInCategory.OST_DetailComponents)))view.SetCategoryHidden(new ElementId(BuiltInCategory.OST_DetailComponents),false);
            XYZ normal=view.ViewDirection.Normalize(),right=view.RightDirection.Normalize(),up=view.UpDirection.Normalize(),origin=view.Origin;
            Func<XYZ,XYZ> project=p=>p-normal*(p-origin).DotProduct(normal);
            Func<XYZ,DrawingPoint2> screen=p=>new DrawingPoint2((p-origin).DotProduct(right),(p-origin).DotProduct(up));
            XYZ center=edges.Select(e=>e.GetEndPoint(0)).Aggregate(XYZ.Zero,(sum,p)=>sum+p)/edges.Count;
            double pad=UnitUtils.ConvertToInternalUnits(profile.BreakLinePaddingPaperMm*view.Scale,UnitTypeId.Millimeters);
            double inset=UnitUtils.ConvertToInternalUnits(1,UnitTypeId.Millimeters);
            var hostBox=host.get_BoundingBox(null);
            foreach(FamilyInstance beam in new FilteredElementCollector(doc,view.Id).OfCategory(BuiltInCategory.OST_StructuralFraming)
                .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().Where(f=>f.StructuralType==Autodesk.Revit.DB.Structure.StructuralType.Beam))
            {
                BoundingBoxXYZ beamBox=beam.get_BoundingBox(null);
                if(!Touches(hostBox,beamBox,UnitUtils.ConvertToInternalUnits(10,UnitTypeId.Millimeters)))continue;
                var triangles=new List<XYZ[]>();Collect(beam.get_Geometry(new Options{View=view}),triangles);
                foreach(Curve edge in edges)
                {
                    XYZ a=project(edge.GetEndPoint(0)),b=project(edge.GetEndPoint(1));double length=a.DistanceTo(b);
                    if(length<=doc.Application.ShortCurveTolerance)continue;
                    DrawingPoint2 sa=screen(a),sb=screen(b);
                    var spans=new List<DrawingInterval>();
                    foreach(XYZ[] triangle in triangles)
                    {
                        if(view is ViewSection&& !InSectionDepth(view,triangle))continue;
                        DrawingInterval? clipped=FootingDrawingMath.ClipTriangle(sa,sb,screen(triangle[0]),screen(triangle[1]),screen(triangle[2]));
                        if(clipped.HasValue)spans.Add(clipped.Value);
                    }
                    foreach(DrawingInterval span in FootingDrawingMath.Merge(spans))
                    {
                        XYZ tangent=(b-a)/length;
                        XYZ inward=normal.CrossProduct(tangent).Normalize();if(inward.DotProduct(center-(a+b)/2)<0)inward=-inward;
                        XYZ start=a+tangent*Math.Max(0,span.Start*length-pad)+inward*inset;
                        XYZ end=a+tangent*Math.Min(length,span.End*length+pad)+inward*inset;
                        if(start.DistanceTo(end)<=doc.Application.ShortCurveTolerance)continue;
                        bool insideLeft=FootingDrawingMath.InteriorOnLeft(sa,sb,screen(center));
                        if((!insideLeft) ^ profile.ReverseBreakLineMask){XYZ swap=start;start=end;end=swap;}
                        using(var sub=new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                FamilyInstance instance=Place(doc,view,symbol,start,end);
                                FootingDrawingDetailState.Mark(instance,host,view,"BeamBreak",beam.UniqueId);
                                if(sub.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Không commit được Breakline.");
                            }
                            catch(Exception ex){if(sub.GetStatus()==TransactionStatus.Started)sub.RollBack();warnings.Add("Breakline dầm "+beam.Id.Value+" / "+view.Name+": "+ex.Message);}
                        }
                    }
                }
            }
        }
        private static FamilyInstance Place(Document doc,View view,FamilySymbol symbol,XYZ a,XYZ b)
        {
            if(symbol.Family.FamilyPlacementType==FamilyPlacementType.CurveBasedDetail)return doc.Create.NewFamilyInstance(Line.CreateBound(a,b),symbol,view);
            XYZ mid=(a+b)/2;FamilyInstance instance=doc.Create.NewFamilyInstance(mid,symbol,view);
            XYZ normal=view.ViewDirection.Normalize(),direction=(b-a).Normalize(),current=instance.GetTransform().BasisX;
            current=(current-normal*current.DotProduct(normal)).Normalize();
            double angle=Math.Atan2(normal.DotProduct(current.CrossProduct(direction)),current.DotProduct(direction));
            ElementTransformUtils.RotateElement(doc,instance.Id,Line.CreateBound(mid,mid+normal),angle);doc.Regenerate();
            BoundingBoxXYZ box=instance.get_BoundingBox(view);
            if(box==null)throw new InvalidOperationException("Không đọc được phạm vi family Breakline.");
            var points=new List<XYZ>();for(int x=0;x<2;x++)for(int y=0;y<2;y++)for(int z=0;z<2;z++)
                points.Add(box.Transform.OfPoint(new XYZ(x==0?box.Min.X:box.Max.X,y==0?box.Min.Y:box.Max.Y,z==0?box.Min.Z:box.Max.Z)));
            double width=points.Max(p=>p.DotProduct(direction))-points.Min(p=>p.DotProduct(direction));
            if(width+doc.Application.ShortCurveTolerance<a.DistanceTo(b))throw new InvalidOperationException("Family đặt theo điểm chưa đủ rộng che dầm; hãy chọn type lớn hơn hoặc family theo đường.");
            return instance;
        }
        private static bool InSectionDepth(View view,XYZ[] triangle)
        {
            BoundingBoxXYZ crop=view.CropBox;Transform inv=crop.Transform.Inverse;
            var local=triangle.Select(inv.OfPoint).ToList();return local.Max(p=>p.Z)>=crop.Min.Z&&local.Min(p=>p.Z)<=crop.Max.Z;
        }
        private static bool Touches(BoundingBoxXYZ a,BoundingBoxXYZ b,double tol)=>a!=null&&b!=null&&
            b.Max.X>=a.Min.X-tol&&b.Min.X<=a.Max.X+tol&&b.Max.Y>=a.Min.Y-tol&&b.Min.Y<=a.Max.Y+tol&&b.Max.Z>=a.Min.Z-tol&&b.Min.Z<=a.Max.Z+tol;
        private static void Collect(GeometryElement geometry,List<XYZ[]> triangles)
        {
            if(geometry==null)return;
            foreach(GeometryObject item in geometry)
            {
                if(item is Solid solid&&solid.Volume>1e-9)
                {
                    foreach(Face face in solid.Faces){Mesh mesh=face.Triangulate();for(int i=0;i<mesh.NumTriangles;i++)
                    {MeshTriangle triangle=mesh.get_Triangle(i);triangles.Add(new[]{triangle.get_Vertex(0),triangle.get_Vertex(1),triangle.get_Vertex(2)});}}
                }
                else if(item is GeometryInstance instance)Collect(instance.GetInstanceGeometry(),triangles);
            }
        }
    }
}
