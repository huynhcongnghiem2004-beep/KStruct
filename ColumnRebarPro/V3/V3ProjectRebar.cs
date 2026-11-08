using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ColumnRebarPro.V3
{
    internal static class V3ProjectRebar
    {
        internal static List<V3HookChoice> Hooks(Document doc,RebarBarType type=null) =>
            new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>()
            .Select(h=>new V3HookChoice {Id=h.UniqueId,Label=h.Name+" ["+h.Id.Value+"]",
                Angle=h.HookAngle,TieStyle=h.Style==RebarStyle.StirrupTie,
                Allowed=type==null || type.GetHookPermission(h.Id)})
            .OrderBy(h=>h.Label,StringComparer.OrdinalIgnoreCase).ToList();

        internal static RebarHookType Hook(Document doc,RebarBarType type,string selected,double degrees,bool required)
        {
            var choice=V3HookSelection.Resolve(Hooks(doc,type),selected,degrees,required);
            return choice==null?null:doc.GetElement(choice.Id) as RebarHookType;
        }

    }
}
