using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace ColumnRebarPro.V3
{
    internal static class V4ColumnTagRequirement
    {
        internal static void RequireTagged(Document doc, IList<V3SegmentWork> works)
        {
            if (doc == null || works == null || works.Count == 0) return;

            var taggedColumns = new HashSet<ElementId>();
            foreach (IndependentTag tag in new FilteredElementCollector(doc)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>())
            {
                if (tag.Category == null ||
                    tag.Category.Id.Value != (long)BuiltInCategory.OST_StructuralColumnTags)
                    continue;

                try
                {
                    foreach (ElementId id in tag.GetTaggedLocalElementIds())
                        taggedColumns.Add(id);
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    // A tag can become invalid after its host is deleted. Ignore it;
                    // the corresponding column will still be reported as untagged.
                }
            }

            var missing = works
                .Select(work => work.Column)
                .Where(column => column != null && !taggedColumns.Contains(column.Id))
                .GroupBy(column => column.Id)
                .Select(group => group.First())
                .ToList();

            if (missing.Count == 0) return;

            string details = string.Join(", ", missing.Select(column =>
            {
                string mark = column.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
                return string.IsNullOrWhiteSpace(mark)
                    ? "Id " + column.Id.Value
                    : mark.Trim() + " (Id " + column.Id.Value + ")";
            }));

            throw new InvalidOperationException(
                "Chưa thể tạo bản vẽ: các cột sau chưa được tag cột trong Revit: " + details +
                ". Hãy đặt Structural Column Tag cho tất cả cột trong tuyến rồi chạy lại.");
        }
    }
}
