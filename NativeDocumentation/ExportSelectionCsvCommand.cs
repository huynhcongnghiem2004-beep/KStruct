using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Microsoft.Win32;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class ExportSelectionCsvCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Element> selected = ui.Selection.GetElementIds().Select(doc.GetElement).Where(x => x != null).Distinct().ToList();
                if (selected.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, "Chọn các phần tử cần xuất dữ liệu");
                    selected = picked.Select(reference => doc.GetElement(reference)).Where(x => x != null).Distinct().ToList();
                }
                if (selected.Count == 0) return Result.Cancelled;

                var dialog = new SaveFileDialog
                {
                    Title = "Xuất dữ liệu phần tử KStruct",
                    Filter = "Excel Workbook (*.xlsx)|*.xlsx|CSV UTF-8 (*.csv)|*.csv",
                    FileName = "KStruct_Elements.xlsx",
                    AddExtension = true,
                    DefaultExt = ".xlsx",
                    OverwritePrompt = true
                };
                if (dialog.ShowDialog() != true) return Result.Cancelled;

                List<string> parameterNames = selected.SelectMany(element => element.Parameters.Cast<Parameter>())
                    .Where(parameter => parameter.Definition != null)
                    .Select(parameter => parameter.Definition.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
                var headers = new List<string> { "ElementId", "UniqueId", "Category", "Name", "Type", "Level", "Mark" };
                headers.AddRange(parameterNames.Select(name => "Parameter: " + name));
                var csv = new StringBuilder();
                csv.AppendLine(string.Join(",", headers.Select(EscapeCsv)));
                var table = new List<List<string>> { headers };

                foreach (Element element in selected.OrderBy(item => item.Id.Value))
                {
                    Element type = doc.GetElement(element.GetTypeId());
                    Element level = element.LevelId != ElementId.InvalidElementId ? doc.GetElement(element.LevelId) : null;
                    Parameter markParameter = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                    var values = new List<string>
                    {
                        element.Id.Value.ToString(CultureInfo.InvariantCulture),
                        element.UniqueId ?? string.Empty,
                        element.Category?.Name ?? string.Empty,
                        element.Name ?? string.Empty,
                        type?.Name ?? string.Empty,
                        level?.Name ?? string.Empty,
                        ReadParameter(markParameter, doc)
                    };
                    foreach (string name in parameterNames)
                    {
                        Parameter parameter = element.Parameters.Cast<Parameter>()
                            .FirstOrDefault(item => item.Definition != null && string.Equals(item.Definition.Name, name, StringComparison.OrdinalIgnoreCase));
                        values.Add(ReadParameter(parameter, doc));
                    }
                    table.Add(values);
                    csv.AppendLine(string.Join(",", values.Select(EscapeCsv)));
                }

                if (string.Equals(Path.GetExtension(dialog.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
                    WriteXlsx(dialog.FileName, table);
                else
                    File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
                TaskDialog.Show("KStruct - Xuất dữ liệu", "Đã xuất " + selected.Count + " phần tử và " + parameterNames.Count + " trường tham số.\n" + dialog.FileName);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Xuất CSV", ex.Message);
                return Result.Failed;
            }
        }

        private static string ReadParameter(Parameter parameter, Document doc)
        {
            if (parameter == null || !parameter.HasValue) return string.Empty;
            switch (parameter.StorageType)
            {
                case StorageType.String: return parameter.AsString() ?? string.Empty;
                case StorageType.Integer: return parameter.AsValueString() ?? parameter.AsInteger().ToString(CultureInfo.InvariantCulture);
                case StorageType.Double: return parameter.AsValueString() ?? parameter.AsDouble().ToString("G17", CultureInfo.InvariantCulture);
                case StorageType.ElementId:
                    ElementId id = parameter.AsElementId();
                    Element referenced = id != ElementId.InvalidElementId ? doc.GetElement(id) : null;
                    return referenced?.Name ?? id.Value.ToString(CultureInfo.InvariantCulture);
                default: return string.Empty;
            }
        }

        private static string EscapeCsv(string value)
        {
            value = value ?? string.Empty;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        internal static void WriteXlsx(string path, IList<List<string>> rows)
        {
            XNamespace contentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
            XNamespace packageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace officeRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace xml = "http://www.w3.org/XML/1998/namespace";
            using (FileStream file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            using (ZipArchive archive = new ZipArchive(file, ZipArchiveMode.Create, false))
            {
                WriteXml(archive, "[Content_Types].xml", new XDocument(
                    new XElement(contentTypes + "Types",
                        new XElement(contentTypes + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                        new XElement(contentTypes + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                        new XElement(contentTypes + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                        new XElement(contentTypes + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));
                WriteXml(archive, "_rels/.rels", new XDocument(
                    new XElement(packageRel + "Relationships",
                        new XElement(packageRel + "Relationship", new XAttribute("Id", "rId1"),
                            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                            new XAttribute("Target", "xl/workbook.xml")))));
                WriteXml(archive, "xl/workbook.xml", new XDocument(
                    new XElement(main + "workbook",
                        new XAttribute(XNamespace.Xmlns + "r", officeRel),
                        new XElement(main + "sheets",
                            new XElement(main + "sheet", new XAttribute("name", "KStruct"), new XAttribute("sheetId", 1), new XAttribute(officeRel + "id", "rId1"))))));
                WriteXml(archive, "xl/_rels/workbook.xml.rels", new XDocument(
                    new XElement(packageRel + "Relationships",
                        new XElement(packageRel + "Relationship", new XAttribute("Id", "rId1"),
                            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                            new XAttribute("Target", "worksheets/sheet1.xml")))));
                IEnumerable<XElement> sheetRows = rows.Select((row, rowIndex) =>
                    new XElement(main + "row", new XAttribute("r", rowIndex + 1),
                        row.Select((value, columnIndex) =>
                        {
                            string text = value ?? string.Empty;
                            XElement content = text.Length == 0
                                ? new XElement(main + "is", new XElement(main + "t", new XAttribute(xml + "space", "preserve"), string.Empty))
                                : new XElement(main + "is", new XElement(main + "t", new XAttribute(xml + "space", "preserve"), text));
                            return new XElement(main + "c", new XAttribute("r", ColumnName(columnIndex) + (rowIndex + 1)),
                                new XAttribute("t", "inlineStr"), content);
                        })));
                WriteXml(archive, "xl/worksheets/sheet1.xml", new XDocument(new XElement(main + "worksheet",
                    new XElement(main + "sheetData", sheetRows))));
            }
        }

        private static void WriteXml(ZipArchive archive, string name, XDocument document)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using (Stream stream = entry.Open()) document.Save(stream);
        }

        private static string ColumnName(int zeroBased)
        {
            int value = zeroBased + 1;
            var result = new StringBuilder();
            while (value > 0)
            {
                value--;
                result.Insert(0, (char)('A' + value % 26));
                value /= 26;
            }
            return result.ToString();
        }
    }
}
