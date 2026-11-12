using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using WinForms = System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using SDK_HD.KStruct.Core.V5;

namespace SDK_HD.KStruct.NativeDocumentation
{
    // V2 is strictly additive. Original AuditModelCommand (V1) is unchanged.
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class AuditModelV2Command : IExternalCommand
    {
        private static readonly BuiltInCategory[] Hosts = {
            BuiltInCategory.OST_StructuralFoundation,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Walls
        };
        private static readonly string[] Parameters = {
            "D Coc","D Cọc","Đường kính cọc","Diameter","Pile Diameter",
            "L","W","B","H","L1","L2","X1","X2","Y1","Y2",
            "Mark","2D-Top","2D-Bot"
        };
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        public Result Execute(ExternalCommandData data,ref string message,ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if(ui==null || ui.Document.IsFamilyDocument) {
                TaskDialog.Show("KStruct Audit V2","Hãy mở Project RVT (không phải RFA).");
                return Result.Cancelled;
            }
            Document doc=ui.Document;
            string path;
            using(var save=new WinForms.SaveFileDialog())
            {
                save.Title="KStruct V5 - Audit V2 (chỉ đọc)";
                save.Filter="JSON (*.json)|*.json";save.DefaultExt="json";save.AddExtension=true;
                save.OverwritePrompt=true;
                save.FileName="KStruct_Audit_V2.json";
                if(save.ShowDialog(new RevitOwner(data.Application.MainWindowHandle))!=WinForms.DialogResult.OK)
                    return Result.Cancelled;
                path=save.FileName;
            }
            string csv=Path.ChangeExtension(path,".csv");
            if(File.Exists(csv))
            {
                var confirm=new TaskDialog("KStruct Audit V2"){
                    MainInstruction="CSV đã tồn tại, ghi đè?",
                    MainContent=csv,
                    CommonButtons=TaskDialogCommonButtons.Yes|TaskDialogCommonButtons.No,
                    DefaultButton=TaskDialogResult.No};
                if(confirm.Show()!=TaskDialogResult.Yes)return Result.Cancelled;
            }
            var issues=new List<Dictionary<string,object>>();
            var counts=new SortedDictionary<string,int>(StringComparer.Ordinal);
            int written=0;
            try
            {
                // Build to temp paths, move results only after successful closing.
                string tmpJson=path+".partial",tmpCsv=csv+".partial";
                try
                {
                    using(var json=new StreamWriter(tmpJson,false,new UTF8Encoding(false)))
                    using(var table=new StreamWriter(tmpCsv,false,new UTF8Encoding(true)))
                    {
                        table.WriteLine("Class,Category,ElementId,UniqueId,HostId,Family,Type,Level,RebarDiameterMm,RebarPositions,CenterXmm,CenterYmm,CenterZmm,Status");
                        json.Write("{\"schema\":");
                        json.Write(AuditV2Policy.Json(AuditV2Policy.Schema));
                        json.Write(",\"generatedUtc\":"+AuditV2Policy.Json(DateTime.UtcNow.ToString("o",Culture)));
                        json.Write(",\"document\":"+AuditV2Policy.Json(new Dictionary<string,object>{
                            {"title",doc.Title},{"revitVersion",data.Application.Application.VersionNumber},
                            {"isWorkshared",doc.IsWorkshared},
                            {"path",doc.PathName}
                        }));
                        json.Write(",\"elements\":[");
                        bool first=true;
                        foreach(Element e in Collect(doc))
                        {
                            try
                            {
                                var record=Describe(doc,e,issues);
                                string serialized=AuditV2Policy.Json(record);
                                if(!first)json.Write(",");
                                first=false;
                                json.Write(serialized);
                                var row=new[]{
                                    Text(record,"class"),Text(record,"category"),Text(record,"elementId"),
                                    Text(record,"uniqueId"),Text(record,"hostId"),
                                    Text(record,"family"),Text(record,"type"),Text(record,"level"),
                                    Text(record,"diameterMm"),Text(record,"barPositions"),
                                    Text(record,"xMm"),Text(record,"yMm"),Text(record,"zMm"),
                                    Text(record,"status")};
                                table.WriteLine(string.Join(",",row.Select(Csv)));
                                string type=Text(record,"category");
                                counts[type]=counts.TryGetValue(type,out int old)?old+1:1;
                                written++;
                            }
                            catch(Exception ex)
                            {
                                Warn(issues,e.Id.Value,"RECORD_ERROR",ex.GetType().Name+": "+ex.Message);
                            }
                        }
                        json.Write("],\"summary\":"+AuditV2Policy.Json(new Dictionary<string,object>{
                            {"records",written},{"warnings",issues.Count},
                            {"counts",counts.ToDictionary(x=>x.Key,x=>(object)x.Value)}
                        }));
                        json.Write(",\"warnings\":"+AuditV2Policy.Json(issues)+"}");
                    }
                    // JSON and CSV are produced from the same successful records.
                    // Avoid leaving partial outputs after exceptions.
                    if(File.Exists(path))File.Delete(path);
                    if(File.Exists(csv))File.Delete(csv);
                    File.Move(tmpJson,path);File.Move(tmpCsv,csv);
                }
                finally
                {
                    if(File.Exists(tmpJson))File.Delete(tmpJson);
                    if(File.Exists(tmpCsv))File.Delete(tmpCsv);
                }
                TaskDialog.Show("KStruct Audit V2","Xuất xong (không sửa mô hình): "+
                    written+" bản ghi, "+issues.Count+" cảnh báo.\nJSON: "+path+"\nCSV: "+csv);
                return Result.Succeeded;
            }
            catch(Exception ex)
            {
                message=ex.ToString();
                TaskDialog.Show("KStruct Audit V2","Không xuất được báo cáo: "+ex.Message);
                return Result.Failed;
            }
        }

        private static IEnumerable<Element> Collect(Document doc)
        {
            // Element IDs are unique across collectors; dedup prevents repeated rows.
            var all=new SortedDictionary<long,Element>();
            Action<IEnumerable<Element>> add=items=>{
                foreach(Element e in items)if(e!=null)all[e.Id.Value]=e;
            };
            add(new FilteredElementCollector(doc).WhereElementIsNotElementType()
                .WherePasses(new ElementMulticategoryFilter(Hosts.ToList())).ToElements());
            add(new FilteredElementCollector(doc).OfClass(typeof(Rebar)).ToElements());
            add(new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_DetailComponents)
                .WhereElementIsNotElementType().ToElements());
            add(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v=>!v.IsTemplate).Cast<Element>());
            add(new FilteredElementCollector(doc).OfClass(typeof(Viewport)).ToElements());
            add(new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).ToElements());
            add(new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).ToElements());
            return all.Values;
        }

        private static Dictionary<string,object> Describe(Document doc,Element e,
            List<Dictionary<string,object>> issues)
        {
            var result=new Dictionary<string,object>{
                {"class",e.GetType().Name},{"category",e.Category?.Name??""},
                {"elementId",e.Id.Value},{"uniqueId",e.UniqueId},
                {"name",SafeName(e)},{"status","read"},{"hostId",null},
                {"family",null},{"type",null},{"level",null},
                {"diameterMm",null},{"barPositions",null},
                {"xMm",null},{"yMm",null},{"zMm",null},
                {"instanceParameters",ReadParameters(e)},
                {"typeParameters",ReadParameters(doc.GetElement(e.GetTypeId()))}
            };
            if(e is FamilyInstance instance)
            {
                result["family"]=instance.Symbol?.FamilyName;
                result["type"]=instance.Symbol?.Name;
                result["level"]=doc.GetElement(instance.LevelId)?.Name;
            }
            XYZ center=(e.Location as LocationPoint)?.Point;
            if(center!=null)
            {
                result["xMm"]=AuditV2Policy.Mm(center.X);
                result["yMm"]=AuditV2Policy.Mm(center.Y);
                result["zMm"]=AuditV2Policy.Mm(center.Z);
            }
            try
            {
                BoundingBoxXYZ bbox=e.get_BoundingBox(null);
                if(bbox!=null)
                {
                    var min=Point(bbox.Min);var max=Point(bbox.Max);
                    result["boundsMm"]=new Dictionary<string,object>{{"min",min},{"max",max},
                        {"isValid",AuditV2Policy.IsValidBounds(min,max)}};
                }
            }
            catch(Exception ex){Warn(issues,e.Id.Value,"BOUNDS_UNAVAILABLE",ex.Message);}

            if(e is Rebar bar) DescribeRebar(doc,bar,result,issues);
            else if(e is View view) DescribeView(doc,view,result);
            else if(e is Viewport port)
            {
                result["hostId"]=port.ViewId.Value;
                result["sheetId"]=port.SheetId.Value;
            }
            else if(e is RevitLinkInstance link)
                result["linkLoaded"]=link.GetLinkDocument()!=null;
            else if(e is ImportInstance cad)
                result["isLinkedCad"]=cad.IsLinked;

            // Solid metrics are diagnostic only: nested graphics may include piles.
            if(e is FamilyInstance fi &&
                fi.Category?.Id.Value==(long)BuiltInCategory.OST_StructuralFoundation)
                DescribeSolids(fi,result,issues);
            return result;
        }

        private static void DescribeRebar(Document doc,Rebar rebar,
            Dictionary<string,object> record,List<Dictionary<string,object>> issues)
        {
            record["hostId"]=rebar.GetHostId().Value;
            record["barPositions"]=rebar.NumberOfBarPositions;
            record["shapeId"]=SafeElementId(()=>rebar.GetShapeId());
            record["startHookTypeId"]=SafeElementId(()=>rebar.GetHookTypeId(0));
            record["endHookTypeId"]=SafeElementId(()=>rebar.GetHookTypeId(1));
            var type=doc.GetElement(rebar.GetTypeId());
            record["type"]=type?.Name;
            var diameter=type?.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER);
            if(diameter!=null && diameter.HasValue && diameter.StorageType==StorageType.Double)
            {
                double mm=AuditV2Policy.Mm(diameter.AsDouble());
                record["diameterMm"]=AuditV2Policy.IsPositiveLength(mm)?(object)mm:null;
            }
            record["shapeDriven"]=rebar.IsRebarShapeDriven();
            var positions=new List<object>();
            for(int i=0;i<rebar.NumberOfBarPositions;i++)
                if(rebar.DoesBarExistAtPosition(i)) positions.Add(i);
            record["existingPositions"]=positions;
            if(positions.Count==0)
            {
                Warn(issues,rebar.Id.Value,"NO_BAR_POSITION","No visible model bar position.");
                return;
            }
            var segments=new List<object>();
            try
            {
                int first=Convert.ToInt32(positions[0],Culture);
                var curves=rebar.GetTransformedCenterlineCurves(false,false,true,
                    MultiplanarOption.IncludeAllMultiplanarCurves,first);
                foreach(Curve curve in curves)
                {
                    var p0=Point(curve.GetEndPoint(0));var p1=Point(curve.GetEndPoint(1));
                    segments.Add(new Dictionary<string,object>{
                        {"fromMm",p0},{"toMm",p1},
                        {"lengthMm",AuditV2Policy.Mm(curve.Length)},
                        {"curveType",curve.GetType().Name},
                        {"direction",AuditV2Policy.Direction(p1[0]-p0[0],p1[1]-p0[1],p1[2]-p0[2])}
                    });
                }
                record["centerlineFirstPosition"]=segments;
                record["centerlinePositionIndex"]=first;
            }
            catch(Exception ex)
            {
                Warn(issues,rebar.Id.Value,"CENTERLINE_UNAVAILABLE",ex.Message);
                record["centerlineError"]=ex.GetType().Name;
            }
            // Full layout state is model-specific; expose available shape-driven fields
            // but avoid calling unsupported setter APIs in the read-only audit.
            if(rebar.IsRebarShapeDriven())
            {
                try
                {
                    var accessor=rebar.GetShapeDrivenAccessor();
                    record["layoutRule"]=accessor.LayoutRule.ToString();
                    record["arrayLengthMm"]=AuditV2Policy.Mm(accessor.ArrayLength);
                }
                catch(Exception ex) {Warn(issues,rebar.Id.Value,"LAYOUT_UNAVAILABLE",ex.Message);}
            }
        }

        private static void DescribeView(Document doc,View view,Dictionary<string,object> result)
        {
            result["viewType"]=view.ViewType.ToString();
            result["scale"]=view.Scale;
            result["viewTemplateId"]=view.ViewTemplateId.Value;
            result["isSheet"]=view is ViewSheet;
            if(view is ViewSection section)
            {
                result["originMm"]=Point(section.Origin);
                result["rightDirection"]=RawPoint(section.RightDirection);
                result["upDirection"]=RawPoint(section.UpDirection);
                result["viewDirection"]=RawPoint(section.ViewDirection);
            }
            try
            {
                if(view.CropBoxActive)
                {
                    BoundingBoxXYZ crop=view.CropBox;
                    if(crop!=null)result["crop"]=new Dictionary<string,object>{
                        {"minMm",Point(crop.Min)},{"maxMm",Point(crop.Max)},
                        {"originMm",Point(crop.Transform.Origin)},
                        {"basisX",RawPoint(crop.Transform.BasisX)},
                        {"basisY",RawPoint(crop.Transform.BasisY)},
                        {"basisZ",RawPoint(crop.Transform.BasisZ)}
                    };
                }
            }
            catch {result["crop"]="not-available";}
            if(FootingDrawingStorage.TryRead(view,out string host,out string role,out double margin))
                result["kstructFootingDrawing"]=new Dictionary<string,object>{
                    {"hostUniqueId",host},{"role",role},{"cropMarginMm",margin},
                    {"ownership",AuditOwnershipPolicy.Classify(true,host,view.UniqueId)}
                };
        }

        private static void DescribeSolids(FamilyInstance instance,
            Dictionary<string,object> record,List<Dictionary<string,object>> warnings)
        {
            try
            {
                var values=new List<object>();
                var options=new Options{ComputeReferences=false,IncludeNonVisibleObjects=false,
                    DetailLevel=ViewDetailLevel.Fine};
                GeometryElement geometry=instance.get_Geometry(options);
                if(geometry!=null) CollectSolids(geometry,Transform.Identity,values,0);
                record["solidCandidates"]=values;
                record["solidWarning"]="Solid metrics may include nested pile or symbolic body; not certified concrete.";
            }
            catch(Exception ex){Warn(warnings,instance.Id.Value,"SOLID_UNAVAILABLE",ex.Message);}
        }
        private static void CollectSolids(GeometryElement geometry,Transform transform,
            List<object> values,int depth)
        {
            if(depth>6||values.Count>=100)return;
            foreach(GeometryObject item in geometry)
            {
                if(item is Solid solid && solid.Volume>1e-9)
                {
                    values.Add(new Dictionary<string,object>{
                        {"volumeCubicMm",solid.Volume*304.8*304.8*304.8},
                        {"faces",solid.Faces.Size},{"depth",depth}
                    });
                    if(values.Count>=100)break;
                }
                else if(item is GeometryInstance nested)
                {
                    GeometryElement nestedGeometry=nested.GetSymbolGeometry();
                    if(nestedGeometry!=null)
                        CollectSolids(nestedGeometry,transform.Multiply(nested.Transform),values,depth+1);
                }
            }
        }

        private static Dictionary<string,object> ReadParameters(Element e)
        {
            var values=new Dictionary<string,object>(StringComparer.Ordinal);
            if(e==null)return values;
            foreach(string name in Parameters)
            {
                try
                {
                    Parameter p=e.LookupParameter(name);
                    if(p==null||!p.HasValue)continue;
                    object v=null;
                    if(p.StorageType==StorageType.String)v=p.AsString();
                    else if(p.StorageType==StorageType.Integer)v=p.AsInteger();
                    else if(p.StorageType==StorageType.ElementId)v=p.AsElementId().Value;
                    else if(p.StorageType==StorageType.Double)
                    {
                        double x=p.AsDouble();
                        v=p.Definition.GetDataType()==SpecTypeId.Length?
                            (object)AuditV2Policy.Mm(x):x;
                    }
                    if(v!=null)values[name]=v;
                }
                catch { /* invalid/unsupported parameter is not used */ }
            }
            return values;
        }
        private static string Text(Dictionary<string,object> record,string key) =>
            record.TryGetValue(key,out object v)&&v!=null?
                Convert.ToString(v,Culture):"";
        private static string Csv(string value) =>
            "\"" + (value??"").Replace("\r"," ").Replace("\n"," ").Replace("\"","\"\"") + "\"";
        private static string SafeName(Element e) {try{return e.Name;}catch{return "";}}
        private static object SafeElementId(Func<ElementId> id)
        {try{return id()?.Value;}catch{return null;}}
        private static double[] Point(XYZ value) => new[]{
            AuditV2Policy.Mm(value.X),AuditV2Policy.Mm(value.Y),AuditV2Policy.Mm(value.Z)};
        private static double[] RawPoint(XYZ value) => new[]{value.X,value.Y,value.Z};
        private static void Warn(List<Dictionary<string,object>> list,long element,string code,string text)
        {
            if(list.Count>=2000)return;
            list.Add(new Dictionary<string,object>{
                {"elementId",element},{"code",code},{"message",text??""}});
        }
        private sealed class RevitOwner : WinForms.IWin32Window
        {
            internal RevitOwner(IntPtr hwnd){Handle=hwnd;}
            public IntPtr Handle{get;}
        }
    }
}
