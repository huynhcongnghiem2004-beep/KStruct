using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.DB.Structure;

namespace SDK_HD.KStruct.FootingRebar
{
    internal static class FootingRebarHistoryStorage
    {
        private static readonly Guid BarSchemaId = new Guid("EF7389B2-91F4-4A7A-BF29-56238C352550");
        private static readonly Guid HostSchemaId = new Guid("6C7CFAB8-0BA5-40A9-8DA9-2DD621AD684B");
        private static Schema SchemaFor(bool host)
        {
            Guid id = host ? HostSchemaId : BarSchemaId;
            Schema existing = Schema.Lookup(id);
            if (existing != null) return existing;
            var builder = new SchemaBuilder(id);
            builder.SetSchemaName(host ? "KStructFootingRebarHistory" : "KStructFootingRebarOwnership");
            builder.SetReadAccessLevel(AccessLevel.Public); builder.SetWriteAccessLevel(AccessLevel.Public);
            foreach (string name in new[] { "HostUniqueId", "ToolId", "RunId" }) builder.AddSimpleField(name, typeof(string));
            builder.AddSimpleField("Version", typeof(int));
            if (host)
            { builder.AddSimpleField("SettingsXml", typeof(string)); builder.AddSimpleField("SavedUtc", typeof(string)); }
            else
            { builder.AddSimpleField("ElementUniqueId", typeof(string)); builder.AddSimpleField("Group", typeof(string)); }
            return builder.Finish();
        }

        internal static FootingRebarPreference ReadHost(FamilyInstance host)
        {
            Schema schema = Schema.Lookup(HostSchemaId);
            if (schema == null) return null;
            Entity entity = host.GetEntity(schema);
            if (!entity.IsValid()) return null;
            if (entity.Get<string>(schema.GetField("HostUniqueId")) != host.UniqueId ||
                entity.Get<string>(schema.GetField("ToolId")) != FootingRebarOwnership.ToolId) return null;
            if (entity.Get<int>(schema.GetField("Version")) != 1)
                throw new InvalidOperationException("Lịch sử thép móng dùng phiên bản mới hơn; không thể khôi phục tự động.");
            return FootingRebarPreferenceStore.Deserialize(entity.Get<string>(schema.GetField("SettingsXml")));
        }
        internal static bool IsOwned(Rebar bar, FamilyInstance host)
        {
            if (bar.GetHostId() != host.Id) return false;
            Schema schema = Schema.Lookup(BarSchemaId);
            if (schema == null) return false;
            Entity e = bar.GetEntity(schema);
            return e.IsValid() && FootingRebarOwnership.IsOwned(bar.UniqueId, host.UniqueId,
                e.Get<string>(schema.GetField("ElementUniqueId")), e.Get<string>(schema.GetField("HostUniqueId")),
                e.Get<string>(schema.GetField("ToolId")), e.Get<int>(schema.GetField("Version")));
        }
        internal static IList<Rebar> Owned(Document doc, FamilyInstance host) =>
            new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>().Where(r => IsOwned(r, host)).ToList();

        internal static void Record(Document doc, FamilyInstance host, IEnumerable<CreatedSet> created, FootingRebarFullSettings settings)
        {
            string run = Guid.NewGuid().ToString("N");
            Schema barSchema = SchemaFor(false), hostSchema = SchemaFor(true);
            foreach (CreatedSet item in created)
            {
                var rebar = doc.GetElement(item.RebarId) as Rebar;
                if (rebar == null || rebar.GetHostId() != host.Id) throw new InvalidOperationException("Không thể nhận diện nhóm thép vừa tạo.");
                Entity e = BaseEntity(barSchema, host, run);
                e.Set(barSchema.GetField("ElementUniqueId"), rebar.UniqueId);
                e.Set(barSchema.GetField("Group"), item.Set.Name ?? string.Empty);
                rebar.SetEntity(e);
            }
            Entity history = BaseEntity(hostSchema, host, run);
            history.Set(hostSchema.GetField("SettingsXml"), FootingRebarPreferenceStore.Serialize(FootingRebarPreference.From(settings)));
            history.Set(hostSchema.GetField("SavedUtc"), DateTime.UtcNow.ToString("o"));
            host.SetEntity(history);
        }
        private static Entity BaseEntity(Schema schema, FamilyInstance host, string run)
        {
            var e = new Entity(schema);
            e.Set(schema.GetField("HostUniqueId"), host.UniqueId); e.Set(schema.GetField("ToolId"), FootingRebarOwnership.ToolId);
            e.Set(schema.GetField("RunId"), run); e.Set(schema.GetField("Version"), 1); return e;
        }

        internal static void DeleteOwned(Document doc, FamilyInstance host, IEnumerable<Rebar> bars)
        {
            var ids = new HashSet<ElementId>();
            foreach (Rebar bar in bars)
            {
                if (!IsOwned(bar, host)) throw new InvalidOperationException("Nhóm thép không còn thuộc lệnh/móng này; đã hủy cập nhật.");
                ids.Add(bar.Id);
            }
            if (ids.Count == 0) return;
            ICollection<ElementId> removed = doc.Delete(ids.ToList());
            // Caller owns the transaction: fail and rollback if Revit would cascade-delete linked drawings or other content.
            if (removed.Any(id => !ids.Contains(id)))
                throw new InvalidOperationException("Thay thép sẽ xóa thêm tag, kích thước hoặc đối tượng liên quan. Đã hủy để giữ hồ sơ hiện có; hãy xử lý các liên kết này trước.");
        }
    }
}
