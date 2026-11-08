using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace ColumnRebarPro.V3
{
    internal static class V3AnchorModelView
    {
        // Must run inside the job transaction. Creates a new view; never changes an existing view.
        internal static ElementId Create(Document doc, V3SegmentWork work)
        {
            if (work.FoundationHost == null) return ElementId.InvalidElementId;
            var type = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);
            if (type == null) throw new InvalidOperationException("Project chưa có View Family Type 3D.");
            var bars = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
                .Where(r => V3RebarOwnership.Matches(work.Column.Id.Value,
                    r.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM)?.AsString())).ToList();
            var view = View3D.CreateIsometric(doc, type.Id);
            string prefix = "KSTR_V3_NEO_L_" + work.Column.Id.Value;
            var names = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View))
                .Cast<View>().Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
            string name = prefix;
            for (int i = 2; names.Contains(name); i++) name = prefix + "_" + i;
            view.Name = name;
            view.DetailLevel = ViewDetailLevel.Fine;
            view.DisplayStyle = DisplayStyle.HLR;
            var box = work.FoundationHost.get_BoundingBox(null);
            if (box == null) throw new InvalidOperationException("Không đọc được phạm vi móng cho view 3D.");
            double padding = V3Units.Ft(200);
            var columnBox = work.Column.get_BoundingBox(null);
            if (columnBox == null) throw new InvalidOperationException("Không đọc được phạm vi cột cho view 3D.");
            view.SetSectionBox(new BoundingBoxXYZ
            {
                Min = new XYZ(Math.Min(box.Min.X, columnBox.Min.X)-padding,
                    Math.Min(box.Min.Y, columnBox.Min.Y)-padding,box.Min.Z-padding),
                Max = new XYZ(Math.Max(box.Max.X, columnBox.Max.X)+padding,
                    Math.Max(box.Max.Y, columnBox.Max.Y)+padding,
                    Math.Min(work.Geometry.Top,work.Geometry.Bottom+V3Units.Ft(1200))+padding)
            });
            view.IsSectionBoxActive = true;
            var ids = new List<ElementId> { work.Column.Id, work.FoundationHost.Id };
            ids.AddRange(bars.Select(b => b.Id));
            doc.Regenerate();
            view.IsolateElementsTemporary(ids);
            foreach (var bar in bars) bar.SetUnobscuredInView(view, true);
            return view.Id;
        }
    }
}
