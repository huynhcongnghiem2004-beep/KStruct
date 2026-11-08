using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace SDK_HD.KStruct.NativeRebar
{
    internal static class RebarShapeDrawingSettings
    {
        private static readonly Guid LegacySchemaId = new Guid("B1CEFE1D-861A-4895-B28C-8527CE85DF52");
        private static readonly Guid SchemaId = new Guid("C8BF7B8B-7DF4-4A89-A517-3D00D9A77004");

        internal sealed class Values
        {
            public string TextTypeName { get; set; }
            public bool ShowSegmentLength { get; set; }
            public bool ShowBarInfo { get; set; }
            public double GapMm { get; set; }
            public bool CreateRebarTag { get; set; }
            public string StandardTagTypeName { get; set; }
            public string StirrupTagTypeName { get; set; }
        }

        internal static Values Defaults()
        {
            return new Values
            {
                ShowSegmentLength = false,
                ShowBarInfo = true,
                GapMm = 60.0,
                CreateRebarTag = true
            };
        }

        internal static Values Load(Document doc)
        {
            Schema schema = Schema.Lookup(SchemaId);
            DataStorage storage = FindStorage(doc, schema);
            if (schema != null && storage != null)
            {
                Entity entity = storage.GetEntity(schema);
                return new Values
                {
                    TextTypeName = entity.Get<string>(schema.GetField("TextTypeName")),
                    ShowSegmentLength = entity.Get<bool>(schema.GetField("ShowSegmentLength")),
                    ShowBarInfo = entity.Get<bool>(schema.GetField("ShowBarInfo")),
                    GapMm = entity.Get<double>(schema.GetField("GapMm")),
                    CreateRebarTag = entity.Get<bool>(schema.GetField("CreateRebarTag")),
                    StandardTagTypeName = entity.Get<string>(schema.GetField("StandardTagTypeName")),
                    StirrupTagTypeName = entity.Get<string>(schema.GetField("StirrupTagTypeName"))
                };
            }

            Schema legacy = Schema.Lookup(LegacySchemaId);
            DataStorage legacyStorage = FindStorage(doc, legacy);
            if (legacy != null && legacyStorage != null)
            {
                Entity entity = legacyStorage.GetEntity(legacy);
                return new Values
                {
                    TextTypeName = entity.Get<string>(legacy.GetField("TextTypeName")),
                    ShowSegmentLength = entity.Get<bool>(legacy.GetField("ShowSegmentLength")),
                    ShowBarInfo = entity.Get<bool>(legacy.GetField("ShowBarInfo")),
                    GapMm = entity.Get<double>(legacy.GetField("GapMm")),
                    CreateRebarTag = false
                };
            }
            return Defaults();
        }

        internal static void Save(Document doc, DrawRebarShape2DCommand.Options options)
        {
            Schema schema = Schema.Lookup(SchemaId) ?? CreateSchema();
            DataStorage storage = FindStorage(doc, schema);
            if (storage == null) storage = DataStorage.Create(doc);
            Entity entity = new Entity(schema);
            entity.Set(schema.GetField("TextTypeName"), options.TextType == null ? "" : options.TextType.Name);
            entity.Set(schema.GetField("ShowSegmentLength"), options.ShowSegmentLength);
            entity.Set(schema.GetField("ShowBarInfo"), options.ShowBarInfo);
            entity.Set(schema.GetField("GapMm"), options.GapMm);
            entity.Set(schema.GetField("CreateRebarTag"), options.CreateRebarTag);
            entity.Set(schema.GetField("StandardTagTypeName"), options.StandardTagType == null ? "" : options.StandardTagType.Name);
            entity.Set(schema.GetField("StirrupTagTypeName"), options.StirrupTagType == null ? "" : options.StirrupTagType.Name);
            storage.SetEntity(entity);
        }

        private static DataStorage FindStorage(Document doc, Schema schema)
        {
            if (schema == null) return null;
            return new FilteredElementCollector(doc).OfClass(typeof(DataStorage)).Cast<DataStorage>()
                .FirstOrDefault(x => x.GetEntity(schema).IsValid());
        }

        private static Schema CreateSchema()
        {
            var builder = new SchemaBuilder(SchemaId);
            builder.SetSchemaName("KStructRebarShapeDrawingSettingsV2");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField("TextTypeName", typeof(string));
            builder.AddSimpleField("ShowSegmentLength", typeof(bool));
            builder.AddSimpleField("ShowBarInfo", typeof(bool));
            builder.AddSimpleField("GapMm", typeof(double));
            builder.AddSimpleField("CreateRebarTag", typeof(bool));
            builder.AddSimpleField("StandardTagTypeName", typeof(string));
            builder.AddSimpleField("StirrupTagTypeName", typeof(string));
            return builder.Finish();
        }
    }
}