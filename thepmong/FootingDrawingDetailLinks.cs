using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace SDK_HD.KStruct.NativeDocumentation
{
    internal static class FootingDrawingDetailLinks
    {
        internal static void Refresh(Document doc,FamilyInstance host,ViewPlan callout,IList<ViewSection> details)
        {
            if(!callout.IsCallout)throw new InvalidOperationException("Ký hiệu mặt cắt tham chiếu Detail cần đặt trên callout móng.");
            if(details.Count==0 || details.Any(detail=>detail.ViewType!=ViewType.Detail))
                throw new InvalidOperationException("View đích phải là Detail View; không dùng Section View.");
            FootingDrawingDetailState.Clear(callout,host,"DetailReference");
            if(callout.AreAnnotationCategoriesHidden)callout.AreAnnotationCategoriesHidden=false;
            if(callout.GetCategoryHidden(new ElementId(BuiltInCategory.OST_Sections)))callout.SetCategoryHidden(new ElementId(BuiltInCategory.OST_Sections),false);
            List<XYZ> vertices=FootingDrawingGeometry.WorldVertices(host);
            foreach(ViewSection detail in details)
            {
                if(detail.CanBeHidden(callout) && !detail.IsHidden(callout))callout.HideElements(new List<ElementId>{detail.Id});
                if(callout.GetReferenceSections().Any(id=>ReferenceableViewUtils.GetReferencedViewId(doc,id)==detail.Id))continue;
                XYZ normal=callout.ViewDirection.Normalize();
                XYZ origin=detail.Origin-normal*(detail.Origin-callout.Origin).DotProduct(normal);
                XYZ direction=XYZ.BasisZ.CrossProduct(detail.ViewDirection).Normalize();
                double pad=UnitUtils.ConvertToInternalUnits(2*callout.Scale,UnitTypeId.Millimeters);
                double first=vertices.Min(p=>(p-origin).DotProduct(direction))-pad,last=vertices.Max(p=>(p-origin).DotProduct(direction))+pad;
                var before=new HashSet<ElementId>(callout.GetReferenceSections());
                // SDK name describes the cut marker on the plan. The destination created by CreateDetail is ViewType.Detail.
                ViewSection.CreateReferenceSection(doc,callout.Id,detail.Id,origin+direction*first,origin+direction*last);
                doc.Regenerate();
                var added=callout.GetReferenceSections().Where(id=>!before.Contains(id)).ToList();
                if(added.Count==0)throw new InvalidOperationException("Revit không trả về ký hiệu tham chiếu Detail trên "+callout.Name);
                foreach(ElementId id in added)
                {
                    if(ReferenceableViewUtils.GetReferencedViewId(doc,id)!=detail.Id)throw new InvalidOperationException("Ký hiệu mặt cắt không liên kết đúng Detail View.");
                    FootingDrawingDetailState.Mark(doc.GetElement(id),host,callout,"DetailReference",callout.UniqueId,detail.UniqueId);
                }
            }
        }
    }
}
