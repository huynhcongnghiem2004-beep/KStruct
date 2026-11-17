using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace SDK_HD.KStruct.NativeRebar
{
    // Strict factory for column and beam commands: existing project Shape/Hook only.
    internal static class ExistingProjectRebar
    {
        internal static Rebar Create(Document doc,RebarStyle style,RebarBarType type,
            RebarHookType start,RebarHookType end,Element host,XYZ normal,IList<Curve> curves,
            RebarHookOrientation startOrientation,RebarHookOrientation endOrientation,string description)
        {
            var shapes=new HashSet<long>(new FilteredElementCollector(doc).OfClass(typeof(RebarShape))
                .Cast<RebarShape>().Select(s=>s.Id.Value));
            if(shapes.Count==0)throw new InvalidOperationException(description+": Project chưa có Rebar Shape. Nạp shape phù hợp trước khi tạo thép.");
            Rebar bar;
            try
            {
                // Only reuse shapes already loaded into this document. Never generate a new shape.
                bar=Rebar.CreateFromCurves(doc,style,type,start,end,host,normal,curves,
                    startOrientation,endOrientation,true,false);
            }
            catch(Exception ex)
            {
                throw new InvalidOperationException(description+": không dùng được Rebar Shape có sẵn với hình học/Hook đã chọn. "+
                    "Móc đầu/cuối: "+(start?.Name??"không")+" / "+(end?.Name??"không")+". "+ex.Message,ex);
            }
            if(bar==null || !shapes.Contains(bar.GetShapeId().Value))
                throw new InvalidOperationException(description+": không có Rebar Shape có sẵn tương thích với hình học và Hook đã chọn; lệnh không tự sinh shape.");
            return bar;
        }

        // Last resort when the project has no loaded Shape matching the curves: free-form bar
        // (not shape-driven, no HookType, no Fixed Number layout). Callers must only use it
        // after a verified zero-match of existing Shapes.
        internal static Rebar CreateFreeForm(Document doc,RebarBarType type,Element host,
            IList<Curve> curves,string description)
        {
            if(curves==null||curves.Count==0)
                throw new InvalidOperationException(description+": thiếu đường thép để vẽ FreeForm.");
            var loop=new CurveLoop();
            foreach(Curve curve in curves)loop.Append(curve);
            Rebar bar;
            RebarFreeFormValidationResult result;
            try
            {
                bar=Rebar.CreateFreeForm(doc,type,host,new List<CurveLoop>{loop},out result);
            }
            catch(Exception ex)
            {
                throw new InvalidOperationException(description+": Revit không vẽ được thép FreeForm. "+ex.Message,ex);
            }
            if(bar==null)
                throw new InvalidOperationException(description+": Revit không vẽ được thép FreeForm ("+result+").");
            return bar;
        }
    }
}
