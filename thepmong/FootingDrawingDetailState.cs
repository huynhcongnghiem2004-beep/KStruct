using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace SDK_HD.KStruct.NativeDocumentation
{
    internal static class FootingDrawingDetailState
    {
        private static readonly Guid OptionsId=new Guid("A975C04E-90B0-4ECC-AB1D-AEB73D10EF20");
        private static readonly Guid AnnotationId=new Guid("BFC4D73A-2EFC-48BD-A5B9-C52CD6AE34F8");
        private static Schema GetSchema(bool annotation)
        {
            Guid id=annotation?AnnotationId:OptionsId;Schema schema=Schema.Lookup(id);if(schema!=null)return schema;
            var builder=new SchemaBuilder(id);builder.SetSchemaName(annotation?"KStructFootingDetailAnnotation":"KStructFootingDetailOptions");
            builder.SetReadAccessLevel(AccessLevel.Public);builder.SetWriteAccessLevel(AccessLevel.Public);
            foreach(string name in new[]{"ElementUniqueId","HostUniqueId","OwnerViewUniqueId"})builder.AddSimpleField(name,typeof(string));
            builder.AddSimpleField("Version",typeof(int));
            if(annotation){foreach(string name in new[]{"Kind","SourceUniqueId","TargetUniqueId"})builder.AddSimpleField(name,typeof(string));}
            else {builder.AddSimpleField("ProfileXml",typeof(string));builder.AddArrayField("SectionUniqueIds",typeof(string));}
            return builder.Finish();
        }
        internal static void Save(View view,FamilyInstance host,FootingDrawingProfile profile,IEnumerable<ViewSection> sections=null)
        {
            Schema schema=GetSchema(false);var entity=Base(schema,view,host,view);
            RecordPose(profile,host);
            string xml;using(var writer=new StringWriter()){new XmlSerializer(typeof(FootingDrawingProfile)).Serialize(writer,profile);xml=writer.ToString();}
            entity.Set(schema.GetField("ProfileXml"),xml);
            entity.Set<IList<string>>(schema.GetField("SectionUniqueIds"),sections?.Select(s=>s.UniqueId).ToList()??new List<string>());
            view.SetEntity(entity);
        }
        internal static void RecordPose(FootingDrawingProfile profile,FamilyInstance host)
        {
            var location=host.Location as LocationPoint;
            if(location==null)throw new InvalidOperationException("Không đọc được vị trí móng.");
            profile.HostPoseX=location.Point.X;profile.HostPoseY=location.Point.Y;profile.HostPoseZ=location.Point.Z;
            profile.HostPoseAngle=location.Rotation;profile.HostPoseRecorded=true;
        }
        internal static bool OwnsView(View view,FamilyInstance host)
        {
            Schema schema=Schema.Lookup(OptionsId);return schema!=null&&Matches(view.GetEntity(schema),schema,view,host,view);
        }
        internal static FootingDrawingProfile Read(View view,FamilyInstance host,out IList<string> sections)
        {
            sections=new List<string>();Schema schema=Schema.Lookup(OptionsId);if(schema==null)return null;
            Entity e=view.GetEntity(schema);if(!Matches(e,schema,view,host,view))return null;
            sections=e.Get<IList<string>>(schema.GetField("SectionUniqueIds"));
            using(var source=new StringReader(e.Get<string>(schema.GetField("ProfileXml"))))
            using(var reader=XmlReader.Create(source,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1024*1024}))
                return (FootingDrawingProfile)new XmlSerializer(typeof(FootingDrawingProfile)).Deserialize(reader);
        }
        internal static void Mark(Element element,FamilyInstance host,View view,string kind,string source,string target="")
        {
            Schema schema=GetSchema(true);Entity e=Base(schema,element,host,view);
            e.Set(schema.GetField("Kind"),kind);e.Set(schema.GetField("SourceUniqueId"),source??string.Empty);e.Set(schema.GetField("TargetUniqueId"),target??string.Empty);
            element.SetEntity(e);
        }
        internal static IList<Element> Annotations(View view,FamilyInstance host,string kind)
        {
            Schema schema=Schema.Lookup(AnnotationId);if(schema==null)return new List<Element>();
            return new FilteredElementCollector(view.Document).WherePasses(new ExtensibleStorageFilter(AnnotationId))
                .WhereElementIsNotElementType().Where(e=>{Entity data=e.GetEntity(schema);return Matches(data,schema,e,host,view)&&data.Get<string>(schema.GetField("Kind"))==kind;}).ToList();
        }
        internal static void Clear(View view,FamilyInstance host,string kind)
        {
            var owned=new HashSet<ElementId>(Annotations(view,host,kind).Select(e=>e.Id));if(owned.Count==0)return;
            var removed=view.Document.Delete(owned.ToList());
            if(removed.Any(id=>!owned.Contains(id)))throw new InvalidOperationException("Cập nhật ký hiệu sẽ ảnh hưởng đối tượng khác; đã hủy để giữ hồ sơ.");
        }
        private static Entity Base(Schema schema,Element element,FamilyInstance host,View view)
        {
            var e=new Entity(schema);e.Set(schema.GetField("ElementUniqueId"),element.UniqueId);e.Set(schema.GetField("HostUniqueId"),host.UniqueId);
            e.Set(schema.GetField("OwnerViewUniqueId"),view.UniqueId);e.Set(schema.GetField("Version"),1);return e;
        }
        private static bool Matches(Entity e,Schema schema,Element element,FamilyInstance host,View view)=>e.IsValid()&&e.Get<int>(schema.GetField("Version"))==1&&
            e.Get<string>(schema.GetField("ElementUniqueId"))==element.UniqueId&&e.Get<string>(schema.GetField("HostUniqueId"))==host.UniqueId&&
            e.Get<string>(schema.GetField("OwnerViewUniqueId"))==view.UniqueId;
    }
}
