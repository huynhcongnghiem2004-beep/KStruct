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
            new Guid("A3CB7F4E-6AF2-40F8-AC62-365E78E14743");

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

        internal static void Stamp(ViewSheet sheet, IList<V3SegmentWork> works)
        {
            if (sheet == null) throw new ArgumentNullException(nameof(sheet));
            var placed = sheet.GetAllPlacedViews().ToList();
            if (placed.Count == 0) throw new InvalidOperationException("Sheet chưa có View để ghi metadata.");
            var schema = GetSchema();
            var entity = new Entity(schema);
            entity.Set<int>(schema.GetField("Version"), 1);
            entity.Set<string>(schema.GetField("StackKey"), StackKey(works));
            entity.Set<string>(schema.GetField("ViewIds"),
                string.Join(";", placed.Select(id => id.Value.ToString(CultureInfo.InvariantCulture))));
            sheet.SetEntity(entity);
        }

        internal static bool IsOwned(Document doc, ViewSheet sheet, IList<V3SegmentWork> works)
        {
            if (doc == null || sheet == null || works == null || works.Count == 0) return false;
            try
            {
                var schema = GetSchema();
                var entity = sheet.GetEntity(schema);
                if (!entity.IsValid() || entity.Get<int>(schema.GetField("Version")) != 1) return false;
                string storedStack = entity.Get<string>(schema.GetField("StackKey"));
                string storedIds = entity.Get<string>(schema.GetField("ViewIds"));
                if (string.IsNullOrWhiteSpace(storedIds)) return false;
                var stored = storedIds.Split(';');
                var current = sheet.GetAllPlacedViews()
                    .Select(id => id.Value.ToString(CultureInfo.InvariantCulture)).ToList();
                if (!V4SheetOwnershipRules.Matches(StackKey(works), storedStack, stored, current))
                    return false;
                return sheet.GetAllPlacedViews().Select(doc.GetElement).OfType<View>()
                    .All(view => view.Name.StartsWith("KSTR V3 MCD ", StringComparison.OrdinalIgnoreCase) ||
                                 view.Name.StartsWith("KSTR V3 MCN ", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception) { return false; }
        }
    }
}
