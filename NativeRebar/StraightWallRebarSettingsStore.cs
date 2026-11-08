using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace SDK_HD.KStruct.NativeRebar
{
    internal static class StraightWallRebarSettingsStore
    {
        private static readonly Guid V1SchemaId = new Guid("764C5D86-1767-4F17-AD21-6F8350E39766");
        private static readonly Guid V2SchemaId = new Guid("DD6B5027-49F5-4A7B-8F4A-81A8C8AA5D02");
        private static readonly Guid V3SchemaId = new Guid("A7C66F84-D0AE-4268-AB08-D3F8FD4702FE");
        private static readonly Guid V4SchemaId = new Guid("E90445C0-2E31-47C4-8AC4-D49F610D9F8E");
        private static readonly Guid SchemaId = new Guid("B667415A-6B0D-4DF4-9AFE-629F3EEA2B13");

        internal sealed class Values
        {
            public string HorizontalTypeName { get; set; }
            public string VerticalTypeName { get; set; }
            public double CoverMm { get; set; }
            public double HorizontalSpacingMm { get; set; }
            public double VerticalSpacingMm { get; set; }
            public bool UseLeftEdge { get; set; }
            public string LeftTypeName { get; set; }
            public double LeftLengthMm { get; set; }
            public bool UseRightEdge { get; set; }
            public string RightTypeName { get; set; }
            public double RightLengthMm { get; set; }
            public bool LapAdjacentWalls { get; set; }
            public double VerticalLapMm { get; set; }
            public bool DrawTransverseTies { get; set; }
            public string TieTypeName { get; set; }
            public string TieHookName { get; set; }
            public double TieSpacingMm { get; set; }
            public bool UseTieO { get; set; }
            public bool UseTieC { get; set; }
            public bool UseTieU { get; set; }
            public string TieOShapeName { get; set; }
            public string TieCShapeName { get; set; }
            public string TieUShapeName { get; set; }
            public string ShapeTieTypeName { get; set; }
            public double ShapeTieZoneLengthMm { get; set; }
            public double ShapeTieSpacingMm { get; set; }
        }

        internal static Values Load(Wall wall)
        {
            Schema current = Schema.Lookup(SchemaId);
            if (current != null)
            {
                Entity entity = wall.GetEntity(current);
                if (entity.IsValid()) return ReadCurrent(entity, current);
            }

            Schema v4 = Schema.Lookup(V4SchemaId);
            if (v4 != null)
            {
                Entity entity = wall.GetEntity(v4);
                if (entity.IsValid()) return ReadV4(entity, v4);
            }

            Schema v3 = Schema.Lookup(V3SchemaId);
            if (v3 != null)
            {
                Entity entity = wall.GetEntity(v3);
                if (entity.IsValid()) return ReadV3(entity, v3);
            }

            Schema v2 = Schema.Lookup(V2SchemaId);
            if (v2 != null)
            {
                Entity entity = wall.GetEntity(v2);
                if (entity.IsValid())
                {
                    Values values = ReadBase(entity, v2);
                    values.UseLeftEdge = entity.Get<bool>(v2.GetField("UseLeftEdge"));
                    values.LeftTypeName = entity.Get<string>(v2.GetField("LeftTypeName"));
                    values.LeftLengthMm = entity.Get<double>(v2.GetField("LeftLengthMm"));
                    values.UseRightEdge = entity.Get<bool>(v2.GetField("UseRightEdge"));
                    values.RightTypeName = entity.Get<string>(v2.GetField("RightTypeName"));
                    values.RightLengthMm = entity.Get<double>(v2.GetField("RightLengthMm"));
                    return values;
                }
            }

            Schema v1 = Schema.Lookup(V1SchemaId);
            if (v1 == null) return null;
            Entity old = wall.GetEntity(v1);
            return old.IsValid() ? ReadBase(old, v1) : null;
        }

        internal static void Save(Wall wall, Values values)
        {
            Schema schema = Schema.Lookup(SchemaId) ?? CreateSchema();
            var entity = new Entity(schema);
            entity.Set(schema.GetField("HorizontalTypeName"), values.HorizontalTypeName ?? "");
            entity.Set(schema.GetField("VerticalTypeName"), values.VerticalTypeName ?? "");
            entity.Set(schema.GetField("CoverMm"), values.CoverMm);
            entity.Set(schema.GetField("HorizontalSpacingMm"), values.HorizontalSpacingMm);
            entity.Set(schema.GetField("VerticalSpacingMm"), values.VerticalSpacingMm);
            entity.Set(schema.GetField("UseLeftEdge"), values.UseLeftEdge);
            entity.Set(schema.GetField("LeftTypeName"), values.LeftTypeName ?? "");
            entity.Set(schema.GetField("LeftLengthMm"), values.LeftLengthMm);
            entity.Set(schema.GetField("UseRightEdge"), values.UseRightEdge);
            entity.Set(schema.GetField("RightTypeName"), values.RightTypeName ?? "");
            entity.Set(schema.GetField("RightLengthMm"), values.RightLengthMm);
            entity.Set(schema.GetField("LapAdjacentWalls"), values.LapAdjacentWalls);
            entity.Set(schema.GetField("VerticalLapMm"), values.VerticalLapMm);
            entity.Set(schema.GetField("DrawTransverseTies"), values.DrawTransverseTies);
            entity.Set(schema.GetField("TieTypeName"), values.TieTypeName ?? "");
            entity.Set(schema.GetField("TieHookName"), values.TieHookName ?? "");
            entity.Set(schema.GetField("TieSpacingMm"), values.TieSpacingMm);
            entity.Set(schema.GetField("UseTieO"), values.UseTieO);
            entity.Set(schema.GetField("UseTieC"), values.UseTieC);
            entity.Set(schema.GetField("UseTieU"), values.UseTieU);
            entity.Set(schema.GetField("TieOShapeName"), values.TieOShapeName ?? "");
            entity.Set(schema.GetField("TieCShapeName"), values.TieCShapeName ?? "");
            entity.Set(schema.GetField("TieUShapeName"), values.TieUShapeName ?? "");
            entity.Set(schema.GetField("ShapeTieTypeName"), values.ShapeTieTypeName ?? "");
            entity.Set(schema.GetField("ShapeTieZoneLengthMm"), values.ShapeTieZoneLengthMm);
            entity.Set(schema.GetField("ShapeTieSpacingMm"), values.ShapeTieSpacingMm);
            wall.SetEntity(entity);
        }

        private static Values ReadCurrent(Entity entity, Schema schema)
        {
            Values values = ReadV4(entity, schema);
            values.UseTieO = entity.Get<bool>(schema.GetField("UseTieO"));
            values.UseTieC = entity.Get<bool>(schema.GetField("UseTieC"));
            values.UseTieU = entity.Get<bool>(schema.GetField("UseTieU"));
            values.TieOShapeName = entity.Get<string>(schema.GetField("TieOShapeName"));
            values.TieCShapeName = entity.Get<string>(schema.GetField("TieCShapeName"));
            values.TieUShapeName = entity.Get<string>(schema.GetField("TieUShapeName"));
            values.ShapeTieTypeName = entity.Get<string>(schema.GetField("ShapeTieTypeName"));
            values.ShapeTieZoneLengthMm = entity.Get<double>(schema.GetField("ShapeTieZoneLengthMm"));
            values.ShapeTieSpacingMm = entity.Get<double>(schema.GetField("ShapeTieSpacingMm"));
            return values;
        }

        private static Values ReadV4(Entity entity, Schema schema)
        {
            Values values = ReadV3(entity, schema);
            values.DrawTransverseTies = entity.Get<bool>(schema.GetField("DrawTransverseTies"));
            values.TieTypeName = entity.Get<string>(schema.GetField("TieTypeName"));
            values.TieHookName = entity.Get<string>(schema.GetField("TieHookName"));
            values.TieSpacingMm = entity.Get<double>(schema.GetField("TieSpacingMm"));
            return values;
        }

        private static Values ReadV3(Entity entity, Schema schema)
        {
            Values values = ReadBase(entity, schema);
            values.UseLeftEdge = entity.Get<bool>(schema.GetField("UseLeftEdge"));
            values.LeftTypeName = entity.Get<string>(schema.GetField("LeftTypeName"));
            values.LeftLengthMm = entity.Get<double>(schema.GetField("LeftLengthMm"));
            values.UseRightEdge = entity.Get<bool>(schema.GetField("UseRightEdge"));
            values.RightTypeName = entity.Get<string>(schema.GetField("RightTypeName"));
            values.RightLengthMm = entity.Get<double>(schema.GetField("RightLengthMm"));
            values.LapAdjacentWalls = entity.Get<bool>(schema.GetField("LapAdjacentWalls"));
            values.VerticalLapMm = entity.Get<double>(schema.GetField("VerticalLapMm"));
            return values;
        }

        private static Values ReadBase(Entity entity, Schema schema)
        {
            return new Values
            {
                HorizontalTypeName = entity.Get<string>(schema.GetField("HorizontalTypeName")),
                VerticalTypeName = entity.Get<string>(schema.GetField("VerticalTypeName")),
                CoverMm = entity.Get<double>(schema.GetField("CoverMm")),
                HorizontalSpacingMm = entity.Get<double>(schema.GetField("HorizontalSpacingMm")),
                VerticalSpacingMm = entity.Get<double>(schema.GetField("VerticalSpacingMm"))
            };
        }

        private static Schema CreateSchema()
        {
            var builder = new SchemaBuilder(SchemaId);
            builder.SetSchemaName("KStructStraightWallRebarSettingsV5");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField("HorizontalTypeName", typeof(string));
            builder.AddSimpleField("VerticalTypeName", typeof(string));
            builder.AddSimpleField("CoverMm", typeof(double));
            builder.AddSimpleField("HorizontalSpacingMm", typeof(double));
            builder.AddSimpleField("VerticalSpacingMm", typeof(double));
            builder.AddSimpleField("UseLeftEdge", typeof(bool));
            builder.AddSimpleField("LeftTypeName", typeof(string));
            builder.AddSimpleField("LeftLengthMm", typeof(double));
            builder.AddSimpleField("UseRightEdge", typeof(bool));
            builder.AddSimpleField("RightTypeName", typeof(string));
            builder.AddSimpleField("RightLengthMm", typeof(double));
            builder.AddSimpleField("LapAdjacentWalls", typeof(bool));
            builder.AddSimpleField("VerticalLapMm", typeof(double));
            builder.AddSimpleField("DrawTransverseTies", typeof(bool));
            builder.AddSimpleField("TieTypeName", typeof(string));
            builder.AddSimpleField("TieHookName", typeof(string));
            builder.AddSimpleField("TieSpacingMm", typeof(double));
            builder.AddSimpleField("UseTieO", typeof(bool));
            builder.AddSimpleField("UseTieC", typeof(bool));
            builder.AddSimpleField("UseTieU", typeof(bool));
            builder.AddSimpleField("TieOShapeName", typeof(string));
            builder.AddSimpleField("TieCShapeName", typeof(string));
            builder.AddSimpleField("TieUShapeName", typeof(string));
            builder.AddSimpleField("ShapeTieTypeName", typeof(string));
            builder.AddSimpleField("ShapeTieZoneLengthMm", typeof(double));
            builder.AddSimpleField("ShapeTieSpacingMm", typeof(double));
            return builder.Finish();
        }
    }
}
