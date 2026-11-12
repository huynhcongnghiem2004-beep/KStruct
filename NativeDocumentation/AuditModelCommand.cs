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

namespace SDK_HD.KStruct.NativeDocumentation
{
    /// <summary>Read-only, streaming project inventory for reproducible KStruct development.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class AuditModelCommand : IExternalCommand
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly BuiltInCategory[] StructuralCategories =
        {
            BuiltInCategory.OST_StructuralFoundation,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Stairs
        };

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null)
            {
                message = "Cần mở project Revit trước khi kiểm tra.";
                return Result.Failed;
            }
            Document document = ui.Document;
            if (document.IsFamilyDocument)
            {
                TaskDialog.Show("KStruct - Kiểm tra mô hình",
                    "Lệnh dành cho Project RVT. Hãy mở file dự án thay vì Family RFA.");
                return Result.Cancelled;
            }
            string outputPath;
            using (var dialog = new WinForms.SaveFileDialog())
            {
                dialog.Title = "KStruct - Lưu báo cáo kiểm tra mô hình (JSON + CSV)";
                dialog.Filter = "JSON (*.json)|*.json";
                dialog.DefaultExt = "json";
                dialog.AddExtension = true;
                dialog.OverwritePrompt = true;
                dialog.FileName = SafeFilename(Path.GetFileNameWithoutExtension(document.Title)) + "_KStruct_Audit.json";
                if (dialog.ShowDialog(new RevitOwner(data.Application.MainWindowHandle)) != WinForms.DialogResult.OK)
                    return Result.Cancelled;
                outputPath = dialog.FileName;
            }
            string csvPath = Path.ChangeExtension(outputPath, ".csv");
            if (File.Exists(csvPath))
            {
                TaskDialog overwrite = new TaskDialog("KStruct - Kiểm tra mô hình")
                {
                    MainInstruction = "File CSV cùng tên đã tồn tại.",
                    MainContent = csvPath + "\nLệnh chỉ ghi file báo cáo, không sửa mô hình Revit. Ghi đè CSV?",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.No
                };
                if (overwrite.Show() != TaskDialogResult.Yes) return Result.Cancelled;
            }

            try
            {
                var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var warnings = new List<string>();
                int recorded = 0;
                int failed = 0;
                using (var json = new StreamWriter(outputPath, false, new UTF8Encoding(false)))
                using (var csv = new StreamWriter(csvPath, false, new UTF8Encoding(true)))
                {
                    csv.WriteLine("Class,Category,ElementId,UniqueId,Name,Family,Type,Level,HostId,DiameterMm,RebarCount,MinXmm,MinYmm,MinZmm,MaxXmm,MaxYmm,MaxZmm,LocationXmm,LocationYmm,LocationZmm,Notes");
                    json.Write("{\n\"schema\":\"kstruct.model-audit.v1\",\"generatedUtc\":");
                    String(json, DateTime.UtcNow.ToString("o", Invariant));
                    json.Write(",\"document\":{");
                    Pair(json, "title", document.Title, true);
                    Pair(json, "path", document.PathName, true);
                    Pair(json, "revitVersion", data.Application.Application.VersionNumber, true);
                    Pair(json, "isWorkshared", document.IsWorkshared, true);
                    Pair(json, "isFamilyDocument", document.IsFamilyDocument, false);
                    json.Write("},\"elements\":[\n");
                    bool first = true;

                    // Only model instances and active types are enumerated: no transaction,
                    // temporary graphics or model mutations are made.
                    var categories = new HashSet<long>(StructuralCategories.Select(x => (long)x));
                    var model = new FilteredElementCollector(document)
                        .WhereElementIsNotElementType()
                        .WherePasses(new ElementMulticategoryFilter(StructuralCategories.ToList()))
                        .ToElements()
                        .OrderBy(e => e.Id.Value);
                    foreach (Element element in model)
                        Record(element, document, json, csv, counts, warnings,
                            ref first, ref recorded, ref failed);
                    foreach (Rebar bar in new FilteredElementCollector(document)
                        .OfClass(typeof(Rebar)).Cast<Rebar>().OrderBy(x => x.Id.Value))
                        Record(bar, document, json, csv, counts, warnings,
                            ref first, ref recorded, ref failed);
                    foreach (FamilyInstance detail in new FilteredElementCollector(document)
                        .OfCategory(BuiltInCategory.OST_DetailComponents)
                        .WhereElementIsNotElementType().OfClass(typeof(FamilyInstance))
                        .Cast<FamilyInstance>().OrderBy(x => x.Id.Value))
                        Record(detail, document, json, csv, counts, warnings,
                            ref first, ref recorded, ref failed);
                    foreach (View view in new FilteredElementCollector(document)
                        .OfClass(typeof(View)).Cast<View>().Where(x => !x.IsTemplate)
                        .OrderBy(x => x.Id.Value))
                        Record(view, document, json, csv, counts, warnings,
                            ref first, ref recorded, ref failed);
                    foreach (Viewport port in new FilteredElementCollector(document)
                        .OfClass(typeof(Viewport)).Cast<Viewport>().OrderBy(x => x.Id.Value))
                        Record(port, document, json, csv, counts, warnings,
                            ref first, ref recorded, ref failed);
                    foreach (RevitLinkInstance link in new FilteredElementCollector(document)
                        .OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>()
                        .OrderBy(x => x.Id.Value))
                        Record(link, document, json, csv, counts, warnings,
                            ref first, ref recorded, ref failed);
                    foreach (ImportInstance cad in new FilteredElementCollector(document)
                        .OfClass(typeof(ImportInstance)).Cast<ImportInstance>()
                        .OrderBy(x => x.Id.Value))
                        Record(cad, document, json, csv, counts, warnings,
                            ref first, ref recorded, ref failed);
                    json.Write("],\"summary\":{\"total\":" + recorded.ToString(Invariant)
                        + ",\"failed\":" + failed.ToString(Invariant) + ",\"counts\":{");
                    bool comma = false;
                    foreach (KeyValuePair<string, int> item in counts)
                    {
                        if (comma) json.Write(",");
                        comma = true;
                        String(json, item.Key);
                        json.Write(":" + item.Value.ToString(Invariant));
                    }
                    json.Write("}},\"warnings\":[");
                    for (int i = 0; i < warnings.Count; i++)
                    {
                        if (i > 0) json.Write(",");
                        String(json, warnings[i]);
                    }
                    json.Write("]}\n");
                }
                TaskDialog.Show("KStruct - Kiểm tra mô hình",
                    "Đã xuất dữ liệu ở chế độ chỉ đọc.\nSố bản ghi: " + recorded +
                    "\nLỗi bản ghi: " + failed +
                    "\nJSON: " + outputPath + "\nCSV: " + csvPath +
                    "\n\nCó thể gửi 2 file báo cáo để phân tích và viết KStruct.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Kiểm tra mô hình", ex.Message);
                return Result.Failed;
            }
        }

        private static void Record(Element element, Document doc, TextWriter json,
            TextWriter csv, IDictionary<string, int> counts, List<string> warnings,
            ref bool first, ref int recorded, ref int failed)
        {
            try
            {
                string category = element.Category?.Name ?? "";
                string klass = element.GetType().Name;
                string family = "", type = "", level = "", host = "", notes = "";
                XYZ locationPoint = (element.Location as LocationPoint)?.Point;
                double? diameter = null;
                int? rebarCount = null;
                if (element is FamilyInstance instance)
                {
                    family = instance.Symbol?.FamilyName ?? "";
                    type = instance.Symbol?.Name ?? "";
                    level = NameOf(doc, instance.LevelId);
                }
                if (element is Rebar bar)
                {
                    host = bar.GetHostId().Value.ToString(Invariant);
                    var barType = doc.GetElement(bar.GetTypeId()) as RebarBarType;
                    type = barType?.Name ?? "";
                    diameter = barType?.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER) is Parameter diameterParameter && diameterParameter.HasValue
                        ? Mm(diameterParameter.AsDouble()) : (double?)null;
                    rebarCount = bar.NumberOfBarPositions;
                    try { notes = "ShapeId=" + bar.GetShapeId().Value.ToString(Invariant); }
                    catch { notes = "ShapeId=UNAVAILABLE"; }
                }
                if (element is View view)
                {
                    type = view.ViewType.ToString();
                    level = NameOf(doc, view.GenLevel?.Id ?? ElementId.InvalidElementId);
                    notes = "Scale=" + view.Scale + ";Template=" + NameOf(doc, view.ViewTemplateId);
                }
                if (element is Viewport port)
                {
                    host = port.ViewId.Value.ToString(Invariant);
                    notes = "SheetId=" + port.SheetId.Value.ToString(Invariant);
                }
                if (element is RevitLinkInstance rvtLink)
                    notes = "LinkedDocument=" + (rvtLink.GetLinkDocument()?.Title ?? "UNLOADED");
                if (element is ImportInstance cad)
                    notes = "IsLinked=" + cad.IsLinked;
                BoundingBoxXYZ bounds = null;
                // BoundingBox may be absent for some valid elements (e.g. non-graphical views).
                try { bounds = element.get_BoundingBox(null); }
                catch (Exception ex) { notes += ";BBoxUnavailable=" + ex.GetType().Name; }

                if (!first) json.Write(",\n");
                first = false;
                json.Write("{");
                Pair(json, "class", klass, true);
                Pair(json, "category", category, true);
                Pair(json, "id", element.Id.Value, true);
                Pair(json, "uniqueId", element.UniqueId, true);
                Pair(json, "name", SafeName(element), true);
                Pair(json, "family", family, true);
                Pair(json, "type", type, true);
                Pair(json, "level", level, true);
                Pair(json, "hostId", host, true);
                Pair(json, "diameterMm", diameter, true);
                Pair(json, "rebarPositions", rebarCount, true);
                json.Write("\"locationMm\":");
                if (locationPoint == null) json.Write("null,");
                else
                {
                    json.Write("{");
                    Pair(json, "x", Mm(locationPoint.X), true);
                    Pair(json, "y", Mm(locationPoint.Y), true);
                    Pair(json, "z", Mm(locationPoint.Z), false);
                    json.Write("},");
                }
                json.Write("\"boundsMm\":");
                if (bounds == null) json.Write("null");
                else
                {
                    json.Write("{");
                    Pair(json, "minX", Mm(bounds.Min.X), true);
                    Pair(json, "minY", Mm(bounds.Min.Y), true);
                    Pair(json, "minZ", Mm(bounds.Min.Z), true);
                    Pair(json, "maxX", Mm(bounds.Max.X), true);
                    Pair(json, "maxY", Mm(bounds.Max.Y), true);
                    Pair(json, "maxZ", Mm(bounds.Max.Z), false);
                    json.Write("}");
                }
                json.Write(",\"parameters\":");
                WriteRelevantParameters(json, element);
                json.Write(",");
                Pair(json, "notes", notes, false);
                json.Write("}");
                var row = new[]
                {
                    klass, category, element.Id.Value.ToString(Invariant), element.UniqueId,
                    SafeName(element), family, type, level, host,
                    Number(diameter), rebarCount?.ToString(Invariant) ?? "",
                    bounds != null ? Number(Mm(bounds.Min.X)) : "",
                    bounds != null ? Number(Mm(bounds.Min.Y)) : "",
                    bounds != null ? Number(Mm(bounds.Min.Z)) : "",
                    bounds != null ? Number(Mm(bounds.Max.X)) : "",
                    bounds != null ? Number(Mm(bounds.Max.Y)) : "",
                    bounds != null ? Number(Mm(bounds.Max.Z)) : "",
                    locationPoint != null ? Number(Mm(locationPoint.X)) : "",
                    locationPoint != null ? Number(Mm(locationPoint.Y)) : "",
                    locationPoint != null ? Number(Mm(locationPoint.Z)) : "", notes
                };
                csv.WriteLine(string.Join(",", row.Select(Csv)));
                counts[klass] = counts.TryGetValue(klass, out int n) ? n + 1 : 1;
                recorded++;
            }
            catch (Exception e)
            {
                failed++;
                if (warnings.Count < 500)
                    warnings.Add("Element " + element.Id.Value + ": " + e.GetType().Name + " - " + e.Message);
                // A failed element must not corrupt JSON; all risky reads are before opening the record.
            }
        }

        // Audit only dimensions/identifiers needed for KStruct diagnosis. Other project
        // parameters can contain private client data and should not be exported implicitly.
        private static void WriteRelevantParameters(TextWriter writer, Element element)
        {
            string[] names =
            {
                "D Coc", "D Cọc", "Đường kính cọc", "Pile Diameter", "Diameter",
                "L", "W", "B", "H", "L1", "L2", "X1", "X2", "Y1", "Y2",
                "Mark", "Comments", "2D-Top", "2D-Bot"
            };
            writer.Write("{");
            bool comma = false;
            foreach (string name in names)
            {
                Parameter p;
                try { p = element.LookupParameter(name); } catch { continue; }
                if (p == null || !p.HasValue) continue;
                string value;
                try
                {
                    switch (p.StorageType)
                    {
                        case StorageType.Double:
                            value = p.AsDouble().ToString("R", Invariant);
                            if (p.Definition?.GetDataType() == SpecTypeId.Length)
                                value = Mm(p.AsDouble()).ToString("0.###", Invariant) + " mm";
                            break;
                        case StorageType.Integer:
                            value = p.AsInteger().ToString(Invariant);
                            break;
                        case StorageType.ElementId:
                            value = p.AsElementId()?.Value.ToString(Invariant) ?? "";
                            break;
                        case StorageType.String:
                            value = p.AsString() ?? "";
                            break;
                        default: continue;
                    }
                }
                catch { continue; }
                if (comma) writer.Write(",");
                comma = true;
                String(writer, name);
                writer.Write(":");
                String(writer, value);
            }
            writer.Write("}");
        }

        private static string SafeName(Element e)
        {
            try { return e.Name ?? ""; } catch { return ""; }
        }
        private static string NameOf(Document doc, ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId) return "";
            try { return doc.GetElement(id)?.Name ?? ""; } catch { return ""; }
        }
        private static double Mm(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
        private static string Number(double? value) =>
            value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
                ? value.Value.ToString("0.###", Invariant) : "";
        private static string Csv(string text)
        {
            text = (text ?? "").Replace("\r", " ").Replace("\n", " ");
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
        private static string SafeFilename(string text) =>
            new string((text ?? "Model").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        private static void Pair(TextWriter writer, string key, string value, bool comma)
        {
            String(writer, key); writer.Write(":"); String(writer, value);
            if (comma) writer.Write(",");
        }
        private static void Pair(TextWriter writer, string key, bool value, bool comma)
        {
            String(writer, key); writer.Write(value ? ":true" : ":false");
            if (comma) writer.Write(",");
        }
        private static void Pair(TextWriter writer, string key, long value, bool comma)
        {
            String(writer, key); writer.Write(":" + value.ToString(Invariant));
            if (comma) writer.Write(",");
        }
        private static void Pair(TextWriter writer, string key, int? value, bool comma)
        {
            String(writer, key); writer.Write(value.HasValue ? ":" + value.Value.ToString(Invariant) : ":null");
            if (comma) writer.Write(",");
        }
        private static void Pair(TextWriter writer, string key, double? value, bool comma)
        {
            String(writer, key);
            writer.Write(value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value)
                ? ":" + value.Value.ToString("0.###", Invariant) : ":null");
            if (comma) writer.Write(",");
        }
        private static void String(TextWriter writer, string value)
        {
            writer.Write('"');
            foreach (char c in value ?? "")
            {
                switch (c)
                {
                    case '"': writer.Write("\\\""); break;
                    case '\\': writer.Write("\\\\"); break;
                    case '\n': writer.Write("\\n"); break;
                    case '\r': writer.Write("\\r"); break;
                    case '\t': writer.Write("\\t"); break;
                    default:
                        if (c < 32) writer.Write("\\u" + ((int)c).ToString("x4", Invariant));
                        else writer.Write(c);
                        break;
                }
            }
            writer.Write('"');
        }
        private sealed class RevitOwner : WinForms.IWin32Window
        {
            internal RevitOwner(IntPtr handle) { Handle = handle; }
            public IntPtr Handle { get; }
        }
    }
}
