using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace SDK_HD.KStruct.NativeDocumentation
{
    internal static class FootingDrawingVisibility
    {
        // Copy formatting once so range/visibility can be adjusted locally, without editing the project template.
        internal static void ApplyTemplate(View view, View template, bool snapshot)
        {
            if(snapshot)
            {
                view.ViewTemplateId=ElementId.InvalidElementId;
                if(template!=null) view.ApplyViewTemplateParameters(template);
            }
            else if(template!=null) view.ViewTemplateId=template.Id;
        }
        internal static bool InLayer(Rebar bar, BoundingBoxXYZ host, bool top)
        {
            string group=ManagedGroup(bar);
            if(group!=null)
            {
                if(group.StartsWith("Top-",StringComparison.Ordinal)) return top;
                if(group.StartsWith("Bottom-",StringComparison.Ordinal)) return !top;
                if(group=="SideTies") return true;
            }
            try
            {
                int position=Enumerable.Range(0,bar.NumberOfBarPositions).First(i=>bar.DoesBarExistAtPosition(i));
                var curves=bar.GetTransformedCenterlineCurves(false,false,true,MultiplanarOption.IncludeAllMultiplanarCurves,position);
                double weight=0, weightedZ=0;
                foreach(Curve curve in curves)
                {
                    var points=curve.Tessellate();
                    for(int i=1;i<points.Count;i++)
                    {
                        XYZ delta=points[i]-points[i-1];double horizontal=Math.Sqrt(delta.X*delta.X+delta.Y*delta.Y);
                        if(horizontal>1e-6 && Math.Abs(delta.Z)<=horizontal*0.05)
                        {weight+=horizontal;weightedZ+=horizontal*(points[i].Z+points[i-1].Z)/2;}
                    }
                }
                if(weight<=1e-6) return true; // Upright/ambiguous bars remain visible rather than being hidden by a guessed layer.
                return top ? weightedZ/weight >= (host.Min.Z+host.Max.Z)/2 : weightedZ/weight < (host.Min.Z+host.Max.Z)/2;
            }
            catch {return true;}
        }
        // Strict membership for 2D Family envelope calculations only. Unlike InLayer(),
        // do not admit ties, side bars, vertical-only bars or uncertain layer identity.
        internal static bool IsPrincipalBarInLayer(Rebar bar, BoundingBoxXYZ host, bool top)
        {
            if (host == null) return false;
            string group;
            try { group = ManagedGroup(bar); }
            catch { return false; }
            if (!string.IsNullOrWhiteSpace(group))
            {
                if (group.StartsWith("Top-", StringComparison.Ordinal)) return top;
                if (group.StartsWith("Bottom-", StringComparison.Ordinal)) return !top;
                return false; // SideTies and any nonprincipal group are not mat reinforcement.
            }
            try
            {
                int position = Enumerable.Range(0, bar.NumberOfBarPositions)
                    .Where(i => bar.DoesBarExistAtPosition(i)).DefaultIfEmpty(-1).First();
                if (position < 0) return false;
                double horizontal = 0, total = 0, zWeighted = 0;
                foreach (Curve curve in bar.GetTransformedCenterlineCurves(false, false, true,
                    MultiplanarOption.IncludeAllMultiplanarCurves, position))
                {
                    var pts = curve.Tessellate();
                    for (int i = 1; i < pts.Count; i++)
                    {
                        XYZ a = pts[i], b = pts[i-1], d = a-b;
                        double length = d.GetLength();
                        if (length < 1e-8) continue;
                        total += length;
                        double projected = Math.Sqrt(d.X*d.X + d.Y*d.Y);
                        if (projected > 1e-8 && Math.Abs(d.Z) <= projected * 0.05)
                        {
                            horizontal += projected;
                            zWeighted += projected * (a.Z+b.Z)/2;
                        }
                    }
                }
                if (horizontal <= 1e-8 || total <= 1e-8 || horizontal / total < 0.5)
                    return false;
                double z = zWeighted / horizontal;
                double midpoint = (host.Min.Z + host.Max.Z)/2;
                double margin = UnitUtils.ConvertToInternalUnits(1, UnitTypeId.Millimeters);
                if (Math.Abs(z-midpoint) <= margin) return false; // ambiguous middle layer
                return top ? z > midpoint : z < midpoint;
            }
            catch { return false; }
        }

        private static string ManagedGroup(Rebar bar)
        {
            var host=bar.Document.GetElement(bar.GetHostId()) as FamilyInstance;
            if(host==null || !global::SDK_HD.KStruct.FootingRebar.FootingRebarHistoryStorage.IsOwned(bar,host)) return null;
            var schema=Autodesk.Revit.DB.ExtensibleStorage.Schema.Lookup(new Guid("EF7389B2-91F4-4A7A-BF29-56238C352550"));
            return bar.GetEntity(schema).Get<string>(schema.GetField("Group"));
        }
        internal static void Configure(ViewPlan view, FamilyInstance host, bool top, bool snapshotTemplate, IList<string> warnings)
        {
            Document doc=view.Document;
            if(snapshotTemplate && view.ViewTemplateId!=ElementId.InvalidElementId)
                view.ViewTemplateId=ElementId.InvalidElementId; // Keep the current view formatting when updating, rather than reapplying template defaults.
            var bars=new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>().Where(r=>r.GetHostId()==host.Id).ToList();
            BoundingBoxXYZ hostBox=host.get_BoundingBox(null);
            if(hostBox==null) throw new InvalidOperationException("Không đọc được cao độ móng.");
            var selected=bars.Where(r=>InLayer(r,hostBox,top)).ToList();
            Level level=view.GenLevel ?? doc.GetElement(host.LevelId) as Level;
            if(level==null) throw new InvalidOperationException("Không tìm được level cho view range.");
            var boxes=selected.Select(r=>r.get_BoundingBox(null)).Where(b=>b!=null).ToList();
            double min=boxes.Count>0?boxes.Min(b=>b.Min.Z):hostBox.Min.Z;
            double max=boxes.Count>0?boxes.Max(b=>b.Max.Z):hostBox.Max.Z;
            var limits=FootingDrawingMath.Range(hostBox.Min.Z,hostBox.Max.Z,min,max,level.ProjectElevation,
                UnitUtils.ConvertToInternalUnits(50,UnitTypeId.Millimeters));
            using(PlanViewRange range=view.GetViewRange())
            {
                PlanViewPlane[] planes={PlanViewPlane.TopClipPlane,PlanViewPlane.CutPlane,PlanViewPlane.BottomClipPlane,PlanViewPlane.ViewDepthPlane};
                double[] offsets={limits.Top,limits.Cut,limits.Bottom,limits.Depth};
                bool changed=false;
                for(int i=0;i<planes.Length;i++)
                {
                    ElementId priorLevel=range.GetLevelId(planes[i]);
                    if(priorLevel==PlanViewRange.Current && view.GenLevel!=null)priorLevel=view.GenLevel.Id;
                    changed |= priorLevel!=level.Id || Math.Abs(range.GetOffset(planes[i])-offsets[i])>1e-6;
                    range.SetLevelId(planes[i],level.Id);range.SetOffset(planes[i],offsets[i]);
                }
                var errors=view.CheckPlanViewRangeValidity(range);
                if(errors.Count>0) throw new InvalidOperationException("View range không hợp lệ: "+string.Join(", ",errors));
                if(changed)view.SetViewRange(range);
            }
            if(view.Discipline!=ViewDiscipline.Structural)view.Discipline=ViewDiscipline.Structural;
            if(view.DetailLevel!=ViewDetailLevel.Fine)view.DetailLevel=ViewDetailLevel.Fine;
            if(view.AreModelCategoriesHidden)view.AreModelCategoriesHidden=false;
            if(view.GetCategoryHidden(new ElementId(BuiltInCategory.OST_Rebar)))view.SetCategoryHidden(new ElementId(BuiltInCategory.OST_Rebar),false);
            var hidden=selected.Where(r=>r.IsHidden(view)).Select(r=>r.Id).ToList();
            if(hidden.Count>0) view.UnhideElements(hidden);
            foreach(ElementId filterId in view.GetFilters())
            {
                if(view.GetFilterVisibility(filterId)) continue;
                Element filter=doc.GetElement(filterId);
                bool matches=filter is ParameterFilterElement parameterFilter && selected.Any(r=>parameterFilter.GetElementFilter().PassesFilter(doc,r.Id)) ||
                    filter is SelectionFilterElement selectionFilter && selected.Any(r=>selectionFilter.GetElementIds().Contains(r.Id));
                if(matches){view.SetFilterVisibility(filterId,true);warnings.Add("Đã bật filter đang ẩn thép trong view "+view.Name+"; template gốc giữ nguyên.");}
            }
            foreach(Rebar rebar in selected)
            {
                try {rebar.SetUnobscuredInView(view,true);}
                catch(Exception ex){warnings.Add("Không bật được hiện xuyên bê tông cho thép "+rebar.Id.Value+": "+ex.Message);}
            }
            var excluded=bars.Except(selected).Where(r=>r.CanBeHidden(view)).Select(r=>r.Id).ToList();
            if(excluded.Count>0) view.HideElements(excluded);
            doc.Regenerate();
            using(PlanViewRange actual=view.GetViewRange())
            {
                PlanViewPlane[] planes={PlanViewPlane.TopClipPlane,PlanViewPlane.CutPlane,PlanViewPlane.BottomClipPlane,PlanViewPlane.ViewDepthPlane};
                double[] offsets={limits.Top,limits.Cut,limits.Bottom,limits.Depth};
                for(int i=0;i<planes.Length;i++)
                {
                    ElementId actualLevel=actual.GetLevelId(planes[i]);
                    if(actualLevel==PlanViewRange.Current && view.GenLevel!=null)actualLevel=view.GenLevel.Id;
                    if(actualLevel!=level.Id || Math.Abs(actual.GetOffset(planes[i])-offsets[i])>1e-6)
                        throw new InvalidOperationException("Callout/template không nhận view range riêng. Hãy chọn áp template một lần hoặc template khác.");
                }
            }
            if(selected.Count>0)
            {
                var visible=new HashSet<ElementId>(new FilteredElementCollector(doc,view.Id).OfClass(typeof(Rebar)).ToElementIds());
                int missing=selected.Count(bar=>!visible.Contains(bar.Id));
                if(missing>0) warnings.Add(view.Name+": "+missing+" nhóm thép vẫn không được view trả về; kiểm tra phase, workset/design option và filter đồ họa.");
            }
            if(selected.Count==0) warnings.Add("Không có Rebar thuộc lớp được chọn trong "+view.Name+"; lệnh không tự sinh thép mới.");
        }
    }
}
