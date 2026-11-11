using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace ColumnRebarPro.V3
{
    // V4-owned sheets are tied to a column stack and the exact placed views.
    // Unstamped V3 sheets are deliberately NOT auto-adopted by view names.
    internal static class V4SheetOwnership
    {
        private static readonly Guid SchemaId =
            new Guid("D5829E37-AC28-4D8D-95C2-5BE332AD6AF1");

        private static Schema GetSchema()
        {
            Schema schema = Schema.Lookup(SchemaId);
            if (schema != null) return schema;
            var builder = new SchemaBuilder(SchemaId);
            builder.SetSchemaName("KStructColumnV4SheetOwnership");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField("Version", typeof(int));
            builder.AddSimpleField("StackKey", typeof(string));
            builder.AddSimpleField("ViewIds", typeof(string));
            builder.AddSimpleField("ViewContents", typeof(string));
            return builder.Finish();
        }

        private static string StackKey(IList<V3SegmentWork> works)
        {
            if (works == null || works.Count == 0)
                throw new InvalidOperationException("Không có tuyến cột để xác thực Sheet.");
            var ids = works.Select(x => x.Column?.UniqueId).ToList();
            if (ids.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("Không đọc được UniqueId của cột.");
            return string.Join("|", ids.OrderBy(x => x, StringComparer.Ordinal));
        }

        // Only elements owned by a view (tags, dimensions, text/detail lines) are tracked.
        // Model geometry visible through the view is not captured.
        private static string ViewContents(Document doc, IEnumerable<ElementId> viewIds)
        {
            var snapshots = new List<string>();
            foreach (ElementId viewId in viewIds.OrderBy(id => id.Value))
            {
                string[] content = new FilteredElementCollector(doc, viewId)
                    .WhereElementIsNotElementType()
                    .Where(element => element.OwnerViewId == viewId)
                    .Select(element => element.Id.Value.ToString(CultureInfo.InvariantCulture))
                    .OrderBy(x => x, StringComparer.Ordinal).ToArray();
                snapshots.Add(viewId.Value.ToString(CultureInfo.InvariantCulture) +
                    ":" + string.Join(",", content));
            }
            return string.Join(";", snapshots);
        }

        private static string SheetSchedules(Document doc, ViewSheet sheet)
        {
            var placed = new FilteredElementCollector(doc)
                .OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>()
                .Where(s => s.OwnerViewId == sheet.Id)
                .OrderBy(s => s.Id.Value).ToList();
            if (placed.Count == 0) return "";
            return "#BBS:" + string.Join(";", placed.Select(s =>
            {
                var schedule = doc.GetElement(s.ScheduleId) as ViewSchedule;
                return s.Id.Value.ToString(CultureInfo.InvariantCulture) + ":" +
                    s.ScheduleId.Value.ToString(CultureInfo.InvariantCulture) + ":" +
                    (schedule == null ? "" : schedule.Name);
            }));
        }

        private static string DrawingSnapshot(Document doc, ViewSheet sheet,
            IEnumerable<ElementId> viewIds)
        {
            return ViewContents(doc, viewIds) + SheetSchedules(doc, sheet);
        }

        internal static void Stamp(Document doc, ViewSheet sheet, IList<V3SegmentWork> works)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (sheet == null) throw new ArgumentNullException(nameof(sheet));
            var placed = sheet.GetAllPlacedViews().ToList();
            if (placed.Count == 0) throw new InvalidOperationException("Sheet chưa có View để ghi metadata.");
            var schema = GetSchema();
            var entity = new Entity(schema);
            entity.Set<int>(schema.GetField("Version"), 2);
            entity.Set<string>(schema.GetField("StackKey"), StackKey(works));
            entity.Set<string>(schema.GetField("ViewIds"),
                string.Join(";", placed.Select(id => id.Value.ToString(CultureInfo.InvariantCulture))));
            entity.Set<string>(schema.GetField("ViewContents"), DrawingSnapshot(doc, sheet, placed));
            sheet.SetEntity(entity);
        }

        internal static bool IsOwned(Document doc, ViewSheet sheet, IList<V3SegmentWork> works)
        {
            if (doc == null || sheet == null || works == null || works.Count == 0) return false;
            try
            {
                var schema = GetSchema();
                var entity = sheet.GetEntity(schema);
                if (!entity.IsValid() || entity.Get<int>(schema.GetField("Version")) != 2) return false;
                string storedStack = entity.Get<string>(schema.GetField("StackKey"));
                string storedIds = entity.Get<string>(schema.GetField("ViewIds"));
                if (string.IsNullOrWhiteSpace(storedIds)) return false;
                var stored = storedIds.Split(';');
                var current = sheet.GetAllPlacedViews()
                    .Select(id => id.Value.ToString(CultureInfo.InvariantCulture)).ToList();
                if (!V4SheetOwnershipRules.Matches(StackKey(works), storedStack, stored, current))
                    return false;
                if (!V4SheetOwnershipRules.SameViewContents(
                    entity.Get<string>(schema.GetField("ViewContents")),
                    DrawingSnapshot(doc, sheet, sheet.GetAllPlacedViews())))
                    return false;
                if (new FilteredElementCollector(doc).OfClass(typeof(ScheduleSheetInstance))
                    .Cast<ScheduleSheetInstance>().Where(item => item.OwnerViewId == sheet.Id)
                    .Any(item => !(doc.GetElement(item.ScheduleId) is ViewSchedule schedule) ||
                        !schedule.Name.StartsWith("KSTR V4 BBS ", StringComparison.OrdinalIgnoreCase)))
                    return false;
                // A user can annotate the sheet itself, outside every generated view.
                // Such additions are not covered by per-view ViewContents metadata.
                // Never delete a sheet with those user-owned annotations.
                if (new FilteredElementCollector(doc, sheet.Id)
                    .WhereElementIsNotElementType()
                    .Any(element => element.OwnerViewId == sheet.Id &&
                        (element is TextNote || element is IndependentTag ||
                         element is Dimension || element is CurveElement ||
                         element is FilledRegion)))
                    return false;
                return sheet.GetAllPlacedViews().Select(doc.GetElement).OfType<View>()
                    .All(view => view.Name.StartsWith("KSTR V3 MCD ", StringComparison.OrdinalIgnoreCase) ||
                                 view.Name.StartsWith("KSTR V3 MCN ", StringComparison.OrdinalIgnoreCase) ||
                                 view.Name.StartsWith("KSTR V3 3D ", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception) { return false; }
        }
    }
}
