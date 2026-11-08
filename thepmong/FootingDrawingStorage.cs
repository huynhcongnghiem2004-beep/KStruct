using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace SDK_HD.KStruct.NativeDocumentation
{
    internal static class FootingDrawingStorage
    {
        private static readonly Guid SchemaGuid = new Guid("5798295B-9EF3-41CE-9A09-1A36563FCD3B");
        private static Schema schema;

        private static Schema GetSchema()
        {
            if (schema != null) return schema;
            schema = Schema.Lookup(SchemaGuid);
            if (schema != null) return schema;
            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName("KStructFoundationDrawing");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField("HostUniqueId", typeof(string));
            builder.AddSimpleField("Role", typeof(string));
            builder.AddSimpleField("Version", typeof(int));
            FieldBuilder cropMarginField = builder.AddSimpleField("CropMarginMm", typeof(double));
            cropMarginField.SetSpec(SpecTypeId.Length);
            schema = builder.Finish();
            return schema;
        }

        internal static void Mark(Element element, string hostUniqueId, string role, double cropMarginMm = 300, int version = 1)
        {
            Schema current = GetSchema();
            var entity = new Entity(current);
            entity.Set(current.GetField("HostUniqueId"), hostUniqueId ?? string.Empty);
            entity.Set(current.GetField("Role"), role ?? string.Empty);
            entity.Set(current.GetField("Version"), version);
            entity.Set(current.GetField("CropMarginMm"), cropMarginMm, UnitTypeId.Millimeters);
            element.SetEntity(entity);
        }

        internal static int Version(Element element)
        {
            if (element == null) return 0;
            Entity entity = element.GetEntity(GetSchema());
            return entity.IsValid() ? entity.Get<int>(GetSchema().GetField("Version")) : 0;
        }

        internal static bool TryRead(Element element, out string hostUniqueId, out string role, out double cropMarginMm)
        {
            hostUniqueId = role = null; cropMarginMm = 300;
            if (element == null) return false;
            Schema current = GetSchema();
            Entity entity = element.GetEntity(current);
            if (!entity.IsValid()) return false;
            hostUniqueId = entity.Get<string>(current.GetField("HostUniqueId"));
            role = entity.Get<string>(current.GetField("Role"));
            Field cropMarginField = current.GetField("CropMarginMm");
            if (cropMarginField != null) cropMarginMm = entity.Get<double>(cropMarginField, UnitTypeId.Millimeters);
            return !string.IsNullOrWhiteSpace(hostUniqueId) && !string.IsNullOrWhiteSpace(role);
        }
    }
}


