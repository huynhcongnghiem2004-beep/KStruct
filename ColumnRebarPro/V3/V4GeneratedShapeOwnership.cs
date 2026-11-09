using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace ColumnRebarPro.V3
{
    // Persistent provenance for RebarShape definitions created by KStruct.
    // Never rename or delete existing project shape definitions.
    internal static class V4GeneratedShapeOwnership
    {
        private static readonly Guid SchemaId =
            new Guid("BC2E8C3C-FDC9-4C11-8DF1-B71A56B9219B");

        private static Schema GetSchema()
        {
            var schema = Schema.Lookup(SchemaId);
            if (schema != null) return schema;
            var builder = new SchemaBuilder(SchemaId);
            builder.SetSchemaName("KStructV4GeneratedRebarShape");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField("Version", typeof(int));
            builder.AddSimpleField("Creator", typeof(string));
            builder.AddSimpleField("ColumnElementId", typeof(int));
            builder.AddSimpleField("Group", typeof(string));
            return builder.Finish();
        }

        internal static void Stamp(RebarShape shape, ElementId columnId, string group)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            var schema = GetSchema();
            var entity = new Entity(schema);
            entity.Set<int>(schema.GetField("Version"), 1);
            entity.Set<string>(schema.GetField("Creator"), "KStruct Column Rebar V4");
            entity.Set<int>(schema.GetField("ColumnElementId"), columnId.Value);
            entity.Set<string>(schema.GetField("Group"), group ?? "");
            shape.SetEntity(entity);
        }
    }
}
