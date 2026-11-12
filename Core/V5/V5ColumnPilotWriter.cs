using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using SDK_HD.KStruct.NativeRebar;

namespace SDK_HD.KStruct.Core.V5
{
    /// <summary>
    /// Experimental four-corner straight-bar writer for a single, isolated,
    /// vertical rectangular concrete column. Not a full column reinforcement
    /// design: no ties, hooks, joints, splices, design checks or drawings.
    /// The caller must explicitly approve the trial on a COPY of an RVT.
    /// </summary>
    internal static class V5ColumnPilotWriter
    {
        private const double MmPerFoot=304.8;
        private static double Ft(double mm)=>mm/MmPerFoot;

        internal static int Create(Document doc,FamilyInstance host,V5RebarInput input)
        {
            if(doc==null || host==null || input==null)
                throw new InvalidOperationException("Thiếu Host hoặc cấu hình Pilot.");
            if(doc.IsFamilyDocument || doc.IsReadOnly || host.Document!=doc ||
               host.Category?.Id.Value!=(long)BuiltInCategory.OST_StructuralColumns ||
               input.Module!=V5StructuralModule.Column || input.HostModule!=V5StructuralModule.Column ||
               input.HostUniqueId!=host.UniqueId || input.MainBarCount!=4)
                throw new InvalidOperationException("Pilot chỉ hỗ trợ đúng 1 cột chữ nhật, 4 thanh dọc.");

            var barTypes=new FilteredElementCollector(doc).OfClass(typeof(RebarBarType))
                .Cast<RebarBarType>().ToList();
            var review=V5RebarInputPolicy.Review(input,
                barTypes.Select(t=>AuditV2Policy.Mm(t.BarModelDiameter)));
            if(!review.IsValid) throw new InvalidOperationException(
                "Dữ liệu đầu vào không hợp lệ: "+string.Join("; ",review.Errors));
            var matches=barTypes.Where(t=>Math.Abs(AuditV2Policy.Mm(t.BarModelDiameter)-
                                                input.MainBarDiameterMm)<0.01).ToList();
            if(matches.Count!=1)
                throw new InvalidOperationException("Phải có đúng một RebarBarType thép chủ phù hợp.");
            RebarBarType barType=matches[0];
            if(new FilteredElementCollector(doc).OfClass(typeof(Rebar))
                .Cast<Rebar>().Any(r=>r.GetHostId().Equals(host.Id)))
                throw new InvalidOperationException(
                    "Cột đã có Rebar: Pilot không chèn trùng hoặc thay thép hiện hữu.");

            if(!RevitHostGeometryResolver.TryResolve(host,out var frame,out string reason) ||
               frame.IsMirrored || frame.Z.Z<0.9999 ||
               Math.Abs(frame.X.Z)>0.0001 || Math.Abs(frame.Y.Z)>0.0001)
                throw new InvalidOperationException("Chỉ hỗ trợ cột đứng, không mirror: "+reason);

            var inventory=V5HostSolidInspector.Inspect(host);
            if(inventory.Truncated || inventory.SolidCount!=1 ||
               inventory.ConcreteMaterialCandidateSolids!=1 ||
               inventory.UnknownOrMixedMaterialSolids!=0 ||
               inventory.Warnings.Any(w=>w.Contains("Không đọc được") ||
                                         w.Contains("suy biến") || w.Contains("hai phía")))
                throw new InvalidOperationException(
                    "Solid bê tông không đơn nhất/không xác minh được. Dừng Pilot.");

            // Model geometry is read-only before opening a Revit Transaction.
            var bounds=TryRectangularPrismBounds(host,frame);
            if(bounds==null)
                throw new InvalidOperationException(
                    "Không chứng minh được tiết diện lăng trụ chữ nhật, cột này không hỗ trợ.");
            double width=bounds[1]-bounds[0],depth=bounds[3]-bounds[2],height=bounds[5]-bounds[4];
            if(Math.Abs(width-input.WidthMm)>1 || Math.Abs(depth-input.HeightMm)>1 ||
               height<500 || width<100 || depth<100)
                throw new InvalidOperationException(
                    "Kích thước nhập không khớp tiết diện Solid thực, hoặc chiều cao cột quá nhỏ.");
            var cover=V5HostCoverInspector.Inspect(host);
            if(!cover.CoverByFaceMm.TryGetValue("other",out double actualCover) ||
               Math.Abs(actualCover-input.CoverMm)>0.5)
                throw new InvalidOperationException("Cover nhập khác Rebar Cover Type Other trong Revit.");

            double inset=input.CoverMm+input.TieDiameterMm+input.MainBarDiameterMm/2;
            if(width<2*inset+2*input.MainBarDiameterMm ||
               depth<2*inset+2*input.MainBarDiameterMm ||
               height<2*(input.CoverMm+input.MainBarDiameterMm/2)+100)
                throw new InvalidOperationException("Tiết diện không đủ khoảng hở cơ bản.");
            double bottom=bounds[4]+input.CoverMm+input.MainBarDiameterMm/2;
            double top=bounds[5]-input.CoverMm-input.MainBarDiameterMm/2;
            var xs=new[]{bounds[0]+inset,bounds[1]-inset};
            var ys=new[]{bounds[2]+inset,bounds[3]-inset};
            var centers=new List<XYZ>();
            foreach(double x in xs) foreach(double y in ys)
            {
                double[] p=frame.ToWorld(x,y,bottom);
                centers.Add(new XYZ(Ft(p[0]),Ft(p[1]),Ft(p[2])));
            }
            XYZ normal=new XYZ(frame.X.X,frame.X.Y,frame.X.Z);
            double length=Ft(top-bottom);
            if(length<doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Thanh dọc ngắn hơn ShortCurveTolerance.");

            using(var tx=new Transaction(doc,"KStruct V5 PILOT - 4 thép dọc cột"))
            {
                if(tx.Start()!=TransactionStatus.Started)
                    throw new InvalidOperationException("Không mở được Transaction Pilot.");
                try
                {
                    int count=0;
                    foreach(XYZ p0 in centers)
                    {
                        Rebar r=ExistingProjectRebar.Create(doc,RebarStyle.Standard,barType,
                            null,null,host,normal,
                            new List<Curve>{Line.CreateBound(p0,p0+XYZ.BasisZ*length)},
                            RebarHookOrientation.Right,RebarHookOrientation.Right,
                            "V5 Pilot thanh dọc");
                        if(r==null || !r.GetHostId().Equals(host.Id))
                            throw new InvalidOperationException("Rebar tạo không thuộc đúng cột.");
                        Parameter partition=r.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
                        if(partition==null || partition.IsReadOnly ||
                           !partition.Set("KSTRUCT-V5-PILOT-"+host.Id.Value))
                            throw new InvalidOperationException("Không đóng dấu sở hữu Rebar Pilot.");
                        count++;
                    }
                    doc.Regenerate();
                    if(count!=4) throw new InvalidOperationException("Không đủ 4 thanh thép dọc.");
                    if(tx.Commit()!=TransactionStatus.Committed)
                        throw new InvalidOperationException("Không commit được Rebar Pilot.");
                    return count;
                }
                catch
                {
                    if(tx.GetStatus()==TransactionStatus.Started) tx.RollBack();
                    throw;
                }
            }
        }

        // Require one unmodified six-face axis-aligned concrete prism.
        // Bounding boxes alone are insufficient: also check actual local
        // edge vertices, six outward face normals, and solid volume.
        private static double[] TryRectangularPrismBounds(FamilyInstance host,
            HostCoordinateFrame frame)
        {
            var geometry=host.get_Geometry(new Options{
                DetailLevel=ViewDetailLevel.Fine,ComputeReferences=false,
                IncludeNonVisibleObjects=false});
            var solids=new List<Solid>();
            Gather(geometry,solids,0);
            if(solids.Count!=1) return null;
            Solid solid=solids[0];
            var min=new[]{double.PositiveInfinity,double.PositiveInfinity,double.PositiveInfinity};
            var max=new[]{double.NegativeInfinity,double.NegativeInfinity,double.NegativeInfinity};
            var normals=new HashSet<int>();
            int faces=0;
            foreach(Face face in solid.Faces)
            {
                if(!(face is PlanarFace planar) || face.IsTwoSided) return null;
                faces++;
                var n=frame.LocalDirection(RevitHostGeometryResolver.ToUnitless(planar.FaceNormal));
                int axis=Array.FindIndex(n,a=>Math.Abs(a)>0.9999);
                if(axis<0 || n.Where((a,i)=>i!=axis).Any(a=>Math.Abs(a)>0.0001))
                    return null;
                normals.Add(axis*2+(n[axis]>0?1:0));
                foreach(EdgeArray loop in face.EdgeLoops)
                    foreach(Edge edge in loop)
                    {
                        foreach(XYZ vertex in edge.Tessellate())
                        {
                            var v=frame.ToLocal(RevitHostGeometryResolver.ToMm(vertex));
                            for(int i=0;i<3;i++)
                            {
                                min[i]=Math.Min(min[i],v[i]);
                                max[i]=Math.Max(max[i],v[i]);
                            }
                        }
                    }
            }
            if(faces!=6 || normals.Count!=6) return null;
            double a=max[0]-min[0],b=max[1]-min[1],c=max[2]-min[2];
            if(a<=0 || b<=0 || c<=0) return null;
            double expected=a*b*c;
            double actual=solid.Volume*Math.Pow(MmPerFoot,3);
            if(!AuditV2Policy.IsFinite(expected) || !AuditV2Policy.IsFinite(actual) ||
               Math.Abs(actual-expected)>expected*0.005) return null;
            return new[]{min[0],max[0],min[1],max[1],min[2],max[2]};
        }

        private static void Gather(GeometryElement geometry,List<Solid> solids,int depth)
        {
            if(geometry==null || depth>6)
                throw new InvalidOperationException("Nested Geometry không hợp lệ.");
            foreach(GeometryObject item in geometry)
            {
                if(item is Solid s && s.Volume>1e-10) solids.Add(s);
                else if(item is GeometryInstance gi)
                    Gather(gi.GetInstanceGeometry(),solids,depth+1);
                if(solids.Count>1) return;
            }
        }
    }
}
