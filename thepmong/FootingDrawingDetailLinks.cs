using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace SDK_HD.KStruct.NativeDocumentation
{
    internal static class FootingDrawingDetailLinks
    {
        internal static void Refresh(Document doc,FamilyInstance host,ViewPlan callout,IList<ViewSection> details,IList<string> warnings)
        {
            if(!callout.IsCallout)throw new InvalidOperationException("Ký hiệu Detail gốc cần hiển thị trên callout móng.");
            if(details.Count==0 || details.Any(detail=>detail.ViewType!=ViewType.Detail || !FootingDrawingDetailState.OwnsView(detail,host)))
                throw new InvalidOperationException("Cần Detail View do KStruct tạo cho đúng móng.");

            // Migrate old drawings: delete only the extra reference markers owned by this command.
            // Never delete a Detail view or a manually created reference marker.
            FootingDrawingDetailState.Clear(callout,host,"DetailReference");
            if(callout.AreAnnotationCategoriesHidden)callout.AreAnnotationCategoriesHidden=false;
            var category=new ElementId(BuiltInCategory.OST_Sections);
            if(callout.GetCategoryHidden(category))callout.SetCategoryHidden(category,false);

            var plans=new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .Where(plan=>!plan.IsTemplate).ToList();
            foreach(ViewSection detail in details)
            {
                // Both plan callouts (Top/Bot) may share the same native Detail marker.
                // Read exact saved links rather than inferring ownership from names or host alone.
                var allowed=new HashSet<ElementId>{callout.Id};
                foreach(ViewPlan plan in plans.Where(plan=>plan.IsCallout))
                {
                    FootingDrawingProfile options=FootingDrawingDetailState.Read(plan,host,out IList<string> targets);
                    if(options!=null && targets.Contains(detail.UniqueId))allowed.Add(plan.Id);
                }
                foreach(ViewPlan plan in plans)
                {
                    bool hidden=detail.IsHidden(plan);
                    if(allowed.Contains(plan.Id))
                    {
                        if(hidden)plan.UnhideElements(new List<ElementId>{detail.Id});
                    }
                    else if(!hidden && detail.CanBeHidden(plan))
                        plan.HideElements(new List<ElementId>{detail.Id});
                }
                // A template/type can hide the native marker at coarser scales. Adjust only this owned Detail.
                foreach(BuiltInParameter parameter in new[]{BuiltInParameter.SECTION_COARSER_SCALE_PULLDOWN_METRIC,
                    BuiltInParameter.SECTION_COARSER_SCALE_PULLDOWN_IMPERIAL})
                {
                    Parameter scale=detail.get_Parameter(parameter);
                    int required=plans.Where(plan=>allowed.Contains(plan.Id)).Select(plan=>plan.Scale).DefaultIfEmpty(callout.Scale).Max();
                    if(scale!=null && scale.StorageType==StorageType.Integer && scale.AsInteger()>0 && scale.AsInteger()<required)
                    {
                        if(scale.IsReadOnly)warnings.Add(detail.Name+": thuộc tính ẩn ký hiệu theo tỷ lệ đang khóa; kiểm tra Detail Type/template.");
                        else if(!scale.Set(required))warnings.Add(detail.Name+": Revit chưa nhận ngưỡng hiển thị ký hiệu theo tỷ lệ.");
                    }
                }
                if(callout.GetReferenceSections().Any(id=>ReferenceableViewUtils.GetReferencedViewId(doc,id)==detail.Id))
                    warnings.Add(callout.Name+": còn reference thủ công tới "+detail.Name+"; lệnh giữ nguyên, hãy kiểm tra ký hiệu trùng.");
            }
            doc.Regenerate();
            var visible=new HashSet<ElementId>(new FilteredElementCollector(doc,callout.Id).ToElementIds());
            foreach(ViewSection detail in details.Where(detail=>!visible.Contains(detail.Id)))
                warnings.Add(callout.Name+": chưa xác minh được ký hiệu gốc của "+detail.Name+
                    "; kiểm tra Show in/Parent View, crop, template và tỷ lệ trong Revit. Không tạo Reference Other View thay thế.");
        }
    }
}
