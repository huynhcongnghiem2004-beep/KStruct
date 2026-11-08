using System;
using System.IO;
using System.Xml.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace ColumnRebarPro.V3
{
    internal static class V3RebarConfigStorage
    {
        private static readonly Guid SchemaGuid = new Guid("A5B45BB5-6593-4D8C-92D3-4E3D8377C1A2");
        private const string LastUsedConfigFile = "last-used-column-rebar.xml";

        internal static V3RebarConfig ReadLastUsed()
        {
            string path = LastUsedConfigPath();
            if (!File.Exists(path)) return null;
            try
            {
                using (var input = File.OpenText(path))
                    return V3LegacySplice.Read(input);
            }
            catch
            {
                return null; // An invalid saved profile must not prevent opening the editor.
            }
        }

        internal static void StoreLastUsed(V3RebarConfig config)
        {
            if (config == null) return;
            string path = LastUsedConfigPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            using (var output = File.Create(temporary))
                new XmlSerializer(typeof(V3RebarConfig)).Serialize(output, config);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }

        private static string LastUsedConfigPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "KStruct", "ColumnRebar", LastUsedConfigFile);
        }
        private const string FieldName = "ConfigurationXml";

        internal static void Store(FamilyInstance column, V3RebarConfig config)
        {
            if (column == null || config == null) throw new ArgumentNullException();
            Schema schema = GetOrCreateSchema();
            var entity = new Entity(schema);
            entity.Set(schema.GetField(FieldName), Serialize(config));
            column.SetEntity(entity);
        }

        internal static V3RebarConfig Read(FamilyInstance column)
        {
            if (column == null) return null;
            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema == null) return null;
            Entity entity = column.GetEntity(schema);
            if (!entity.IsValid()) return null;
            string xml = entity.Get<string>(schema.GetField(FieldName));
            if (string.IsNullOrWhiteSpace(xml)) return null;
            using (var reader = new StringReader(xml))
                return V3LegacySplice.Read(reader);
        }

        private static Schema GetOrCreateSchema()
        {
            Schema schema = Schema.Lookup(SchemaGuid);
            if (schema != null) return schema;
            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName("KStructColumnRebarConfig");
            builder.SetDocumentation("KStruct V3 column reinforcement configuration used to regenerate native detailing views.");
            builder.SetVendorId("KSTR");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(FieldName, typeof(string));
            return builder.Finish();
        }

        private static string Serialize(V3RebarConfig config)
        {
            using (var writer = new StringWriter())
            {
                new XmlSerializer(typeof(V3RebarConfig)).Serialize(writer, config);
                return writer.ToString();
            }
        }
    }
}
