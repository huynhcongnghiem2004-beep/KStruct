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

        // Use reference Stirrup/Tie bars only. Same column takes priority over
        // other columns with the same RebarBarType. Conflicting samples stay ambiguous.
        private static void SampleHookIds(Document doc, RebarBarType type, ElementId hostId,
            out List<string> hostSamples, out List<string> projectSamples)
        {
            hostSamples = new List<string>();
            projectSamples = new List<string>();
            if (doc == null || type == null) return;
            var valid = new HashSet<string>(Hooks(doc, type)
                .Where(h => h.TieStyle && h.Allowed).Select(h => h.Id), StringComparer.Ordinal);
            foreach (var bar in new FilteredElementCollector(doc)
                .OfClass(typeof(Rebar)).Cast<Rebar>())
            {
                if (!bar.GetTypeId().Equals(type.Id)) continue;
                var shape = doc.GetElement(bar.GetShapeId()) as RebarShape;
                if (shape == null || shape.RebarStyle != RebarStyle.StirrupTie) continue;
                bool onHost = hostId != null && hostId != ElementId.InvalidElementId &&
                    bar.GetHostId().Equals(hostId);
                for (int end = 0; end < 2; end++)
                {
                    var hook = doc.GetElement(bar.GetHookTypeId(end)) as RebarHookType;
                    if (hook == null || !valid.Contains(hook.UniqueId)) continue;
                    projectSamples.Add(hook.UniqueId);
                    if (onHost) hostSamples.Add(hook.UniqueId);
                }
            }
        }

        internal static string SuggestHook(Document doc, RebarBarType type, double degrees, ElementId hostId=null)
        {
            List<string> hostSamples, projectSamples;
            SampleHookIds(doc, type, hostId, out hostSamples, out projectSamples);
            return V3HookSelection.SuggestFromSamples(Hooks(doc,type),
                degrees,hostSamples,projectSamples);
        }

        internal static RebarHookType Hook(Document doc,RebarBarType type,string selected,double degrees,bool required,
            ElementId hostId=null)
        {
            if (!required) return null;
            // Do not scan the project if the user already specified an ID or there is
            // only one matching type; Resolve validates saved IDs and permissions.
            var choices = Hooks(doc,type);
            var matching = choices.Where(h => h.TieStyle && h.Allowed &&
                Math.Abs(h.Angle-degrees*Math.PI/180)<1e-6).ToList();
            List<string> hostSamples=null, projectSamples=null;
            if (string.IsNullOrEmpty(selected) && matching.Count > 1)
                SampleHookIds(doc,type,hostId,out hostSamples,out projectSamples);
            var choice=V3HookSelection.Resolve(choices,selected,degrees,required,
                hostSamples,projectSamples);
            return choice==null?null:doc.GetElement(choice.Id) as RebarHookType;
        }

    }
}
