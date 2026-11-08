using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace SDK_HD.KStruct.NativeDocumentation
{
    internal static class FootingDrawingSectionLinks
    {
        internal static void Refresh(Document doc,FamilyInstance host,ViewPlan callout,IList<ViewSection> sections)
        {
            if(!callout.IsCallout)throw new InvalidOperationException("Mặt cắt cần tham chiếu từ callout móng, không phải mặt bằng độc lập.");
            FootingDrawingDetailState.Clear(callout,host,"SectionReference");
            if(callout.AreAnnotationCategoriesHidden)callout.AreAnnotationCategoriesHidden=false;
            if(callout.GetCategoryHidden(new ElementId(BuiltInCategory.OST_Sections)))callout.SetCategoryHidden(new ElementId(BuiltInCategory.OST_Sections),false);
            List<XYZ> vertices=FootingDrawingGeometry.WorldVertices(host);
            foreach(ViewSection section in sections)
            {
                // Hide the automatically visible original marker here; the explicitly placed reference belongs to this callout.
                if(section.CanBeHidden(callout) && !section.IsHidden(callout)) callout.HideElements(new List<ElementId>{section.Id});
                // Respect manual references already present; create only missing references in this exact callout.
                if(callout.GetReferenceSections().Any(id=>ReferenceableViewUtils.GetReferencedViewId(doc,id)==section.Id))continue;
                XYZ normal=callout.ViewDirection.Normalize();
                XYZ origin=section.Origin-normal*(section.Origin-callout.Origin).DotProduct(normal);
                XYZ direction=XYZ.BasisZ.CrossProduct(section.ViewDirection).Normalize();
                double pad=UnitUtils.ConvertToInternalUnits(2*callout.Scale,UnitTypeId.Millimeters);
                double first=vertices.Min(p=>(p-origin).DotProduct(direction))-pad,last=vertices.Max(p=>(p-origin).DotProduct(direction))+pad;
                XYZ head=origin+direction*first,tail=origin+direction*last;
                var before=new HashSet<ElementId>(callout.GetReferenceSections());
                ViewSection.CreateReferenceSection(doc,callout.Id,section.Id,head,tail);doc.Regenerate();
                var added=callout.GetReferenceSections().Where(id=>!before.Contains(id)).ToList();
                if(added.Count==0)throw new InvalidOperationException("Revit không trả về ký hiệu mặt cắt trong callout "+callout.Name);
                foreach(ElementId id in added)
                {
                    if(ReferenceableViewUtils.GetReferencedViewId(doc,id)!=section.Id)throw new InvalidOperationException("Ký hiệu mặt cắt không liên kết đúng view.");
                    FootingDrawingDetailState.Mark(doc.GetElement(id),host,callout,"SectionReference",callout.UniqueId,section.UniqueId);
                }
            }
        }
    }
}
