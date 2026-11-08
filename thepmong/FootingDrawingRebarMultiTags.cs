using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace SDK_HD.KStruct.NativeDocumentation
{
    internal static class FootingDrawingRebarMultiTags
    {
        private const string Kind="RebarMultiTag";
        internal static IList<MultiReferenceAnnotationType> Types(Document doc)=>new FilteredElementCollector(doc)
            .OfClass(typeof(MultiReferenceAnnotationType)).Cast<MultiReferenceAnnotationType>()
            .Where(type=>type.ReferenceCategoryId==new ElementId(BuiltInCategory.OST_Rebar) &&
                doc.GetElement(type.TagTypeId) is ElementType && doc.GetElement(type.DimensionStyleId) is DimensionType dimension &&
                (dimension.StyleType==DimensionStyleType.Linear || dimension.StyleType==DimensionStyleType.LinearFixed))
            .OrderBy(type=>type.Name).ToList();

        private static MultiReferenceAnnotationType Resolve(Document doc,FootingDrawingProfile profile)
        {
            var types=Types(doc);
            var exact=types.FirstOrDefault(type=>type.UniqueId==profile.RebarMultiTagTypeUniqueId);
            if(exact!=null)return exact;
            if(!string.IsNullOrWhiteSpace(profile.RebarMultiTagTypeName))
                return types.SingleOrDefault(type=>type.Name==profile.RebarMultiTagTypeName);
            // Only migrate an old drawing automatically if there is a single compatible type.
            return types.Count==1?types[0]:null;
        }
        internal static void Refresh(Document doc,FamilyInstance host,View view,string role,FootingDrawingProfile profile,
            IList<string> warnings,MultiReferenceAnnotationType chosen=null)
        {
            if(!profile.CreateTags){FootingDrawingDetailState.Clear(view,host,Kind);return;}
            MultiReferenceAnnotationType type=chosen??Resolve(doc,profile);
            if(type==null)
            {warnings.Add(view.Name+": chưa chọn Multi-Rebar Annotation Type có Rebar Tag và Dimension Type. Không dùng tag thép đơn thay thế.");return;}
            profile.RebarMultiTagTypeName=type.Name;profile.RebarMultiTagTypeUniqueId=type.UniqueId;
            if(view.AreAnnotationCategoriesHidden)view.AreAnnotationCategoriesHidden=false;
            var categories=new List<ElementId>{new ElementId(BuiltInCategory.OST_RebarTags),new ElementId(BuiltInCategory.OST_Dimensions)};
            if(type.Category!=null)categories.Add(type.Category.Id);
            foreach(ElementId category in categories.Distinct())
                if(view.CanCategoryBeHidden(category)&&view.GetCategoryHidden(category))view.SetCategoryHidden(category,false);
            doc.Regenerate();
            bool top=role!=null&&role.EndsWith("Top",StringComparison.OrdinalIgnoreCase);
            bool bot=role!=null&&role.EndsWith("Bot",StringComparison.OrdinalIgnoreCase);
            var hostBox=host.get_BoundingBox(null);
            var bars=new FilteredElementCollector(doc,view.Id).OfClass(typeof(Rebar)).Cast<Rebar>()
                .Where(bar=>bar.GetHostId()==host.Id&&!bar.IsHidden(view)&&(!top&&!bot||FootingDrawingVisibility.InLayer(bar,hostBox,top)))
                .OrderBy(bar=>bar.Id.Value).ToList();
            if(bars.Count==0){warnings.Add(view.Name+": không có thép móng hiển thị để đặt MultiTag.");return;}
            ExpandAnnotationCrop(view,bars.Count);
            var hostVertices=FootingDrawingGeometry.WorldVertices(host);
            for(int index=0;index<bars.Count;index++)
            {
                Rebar bar=bars[index];string problem="Hướng thanh/tham chiếu không phù hợp Multi-Rebar Annotation trong view này.";
                using(var group=new SubTransaction(doc))
                {
                    group.Start();bool created=false;
                    try
                    {
                        bar.SetUnobscuredInView(view,true);
                        FootingDrawingDetailState.Clear(view,host,Kind,bar.UniqueId);
                        XYZ normal=view.ViewDirection.Normalize();
                        var points=BarPoints(bar);
                        XYZ mid=Project(view,points.Aggregate(XYZ.Zero,(sum,p)=>sum+p)/points.Count);
                        foreach(XYZ direction in Directions(view,bar,points))
                        {
                            using(var attempt=new SubTransaction(doc))
                            {
                                attempt.Start();
                                try
                                {
                                    XYZ outward=normal.CrossProduct(direction).Normalize();
                                    if(outward.DotProduct(view.RightDirection)>.01 ||
                                        Math.Abs(outward.DotProduct(view.RightDirection))<=.01&&outward.DotProduct(view.UpDirection)>0)outward=-outward;
                                    double padding=UnitUtils.ConvertToInternalUnits((8+index*6)*view.Scale,UnitTypeId.Millimeters);
                                    XYZ origin=mid+outward*(hostVertices.Max(p=>p.DotProduct(outward))-mid.DotProduct(outward)+padding);
                                    using(var options=new MultiReferenceAnnotationOptions(type))
                                    {
                                        var dimensionType=(DimensionType)doc.GetElement(type.DimensionStyleId);
                                        options.DimensionStyleType=dimensionType.StyleType;
                                        options.SetElementsToDimension(new List<ElementId>{bar.Id});
                                        options.DimensionPlaneNormal=normal;options.DimensionLineDirection=direction;options.DimensionLineOrigin=Project(view,origin);
                                        options.TagHeadPosition=Project(view,origin+outward*UnitUtils.ConvertToInternalUnits(4*view.Scale,UnitTypeId.Millimeters));
                                        options.TagHasLeader=true;
                                        bool valid=options.DimensionStyleType==DimensionStyleType.LinearFixed?
                                            MultiReferenceAnnotation.IsLinearFixedDimensionDirectionValid(doc,view.Id,options)&&
                                                MultiReferenceAnnotation.AreReferencesValidForLinearFixedDimension(doc,view.Id,options):
                                            MultiReferenceAnnotation.AreReferencesValidForLinearDimension(doc,view.Id,options);
                                        if(!valid){attempt.RollBack();continue;}
                                        MultiReferenceAnnotation annotation=MultiReferenceAnnotation.Create(doc,view.Id,options);
                                        doc.Regenerate();
                                        if(annotation==null||!(doc.GetElement(annotation.TagId) is IndependentTag)||!(doc.GetElement(annotation.DimensionId) is Dimension))
                                            throw new InvalidOperationException("MultiTag chưa có đủ tag và dimension con.");
                                        foreach(Element element in new[]{(Element)annotation,doc.GetElement(annotation.TagId),doc.GetElement(annotation.DimensionId)})
                                            FootingDrawingDetailState.Mark(element,host,view,Kind,bar.UniqueId);
                                    }
                                    if(attempt.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Không commit được MultiTag.");
                                    created=true;break;
                                }
                                catch(Exception ex)
                                {if(attempt.GetStatus()==TransactionStatus.Started)attempt.RollBack();problem=ex.Message;}
                            }
                        }
                        if(created)
                        {
                            if(group.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Không commit được nhóm MultiTag.");
                        }
                        else group.RollBack();
                    }
                    catch(Exception ex){if(group.GetStatus()==TransactionStatus.Started)group.RollBack();created=false;problem=ex.Message;}
                    if(!created)warnings.Add(view.Name+" / thép "+bar.Id.Value+": chưa tạo MultiTag; giữ annotation cũ nếu có. "+problem);
                }
            }
        }
        private static XYZ Project(View view,XYZ point)=>point-view.ViewDirection*(point-view.Origin).DotProduct(view.ViewDirection);
        private static IList<XYZ> BarPoints(Rebar bar)
        {
            int position=Enumerable.Range(0,bar.NumberOfBarPositions).First(i=>bar.DoesBarExistAtPosition(i));
            var points=bar.GetTransformedCenterlineCurves(false,false,true,MultiplanarOption.IncludeAllMultiplanarCurves,position)
                .SelectMany(curve=>curve.Tessellate()).ToList();
            if(points.Count==0)throw new InvalidOperationException("Không đọc được đường tâm thép.");return points;
        }
        private static IList<XYZ> Directions(View view,Rebar bar,IList<XYZ> first)
        {
            var result=new List<XYZ>();
            Action<XYZ> add=vector=>{
                vector=vector-view.ViewDirection*vector.DotProduct(view.ViewDirection);
                if(vector.GetLength()<1e-7)return;vector=vector.Normalize();
                if(result.All(prior=>Math.Abs(prior.DotProduct(vector))<.9999))result.Add(vector);
            };
            int last=Enumerable.Range(0,bar.NumberOfBarPositions).Last(i=>bar.DoesBarExistAtPosition(i));
            var curves=bar.GetTransformedCenterlineCurves(false,false,true,MultiplanarOption.IncludeAllMultiplanarCurves,last);
            if(curves.Count>0)add(curves[0].GetEndPoint(0)-first[0]);
            XYZ longest=XYZ.Zero;
            for(int i=1;i<first.Count;i++)
            {
                XYZ delta=first[i]-first[i-1];delta-=view.ViewDirection*delta.DotProduct(view.ViewDirection);
                if(delta.GetLength()>longest.GetLength())longest=delta;
            }
            if(longest.GetLength()>1e-7)add(view.ViewDirection.CrossProduct(longest));
            add(view.RightDirection);add(view.UpDirection);return result;
        }
        private static void ExpandAnnotationCrop(View view,int groups)
        {
            var manager=view.GetCropRegionShapeManager();if(!manager.CanHaveAnnotationCrop)return;
            Parameter active=view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
            if(active!=null&&!active.IsReadOnly)active.Set(1);
            double space=UnitUtils.ConvertToInternalUnits(8+Math.Max(0,groups-1)*6+10,UnitTypeId.Millimeters);
            manager.LeftAnnotationCropOffset=Math.Max(manager.LeftAnnotationCropOffset,space);
            manager.RightAnnotationCropOffset=Math.Max(manager.RightAnnotationCropOffset,space);
            manager.TopAnnotationCropOffset=Math.Max(manager.TopAnnotationCropOffset,space);
            manager.BottomAnnotationCropOffset=Math.Max(manager.BottomAnnotationCropOffset,space);
        }
    }
}
