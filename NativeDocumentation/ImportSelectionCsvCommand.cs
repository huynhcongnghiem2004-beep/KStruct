using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ImportSelectionCsvCommand : IExternalCommand
    {
        private sealed class ImportRow
        {
            public int Line;
            public Element Element;
            public Dictionary<string, string> Values;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Nhập tham số phần tử KStruct",
                    Filter = "Excel Workbook (*.xlsx)|*.xlsx|CSV UTF-8 (*.csv)|*.csv",
                    CheckFileExists = true,
                    Multiselect = false
                };
                if (dialog.ShowDialog() != true) return Result.Cancelled;

                List<List<string>> table = string.Equals(Path.GetExtension(dialog.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase)
                    ? ReadFirstWorksheet(dialog.FileName)
                    : ParseCsv(File.ReadAllText(dialog.FileName, Encoding.UTF8));
                if (table.Count < 2) throw new InvalidOperationException("Bảng tính phải có dòng tiêu đề và ít nhất một dòng dữ liệu.");
                List<string> headers = table[0];
                int uniqueIdIndex = FindHeader(headers, "UniqueId");
                int elementIdIndex = FindHeader(headers, "ElementId");
                if (uniqueIdIndex < 0 && elementIdIndex < 0)
                    throw new InvalidOperationException("Bảng tính phải có cột UniqueId hoặc ElementId.");

                List<ImportRow> rows = new List<ImportRow>();
                int unresolved = 0;
                for (int i = 1; i < table.Count; i++)
                {
                    List<string> values = table[i];
                    if (values.All(string.IsNullOrWhiteSpace)) continue;
                    Element element = null;
                    string uniqueId = Cell(values, uniqueIdIndex);
                    if (!string.IsNullOrWhiteSpace(uniqueId))
                        element = doc.GetElement(uniqueId.Trim());
                    if (element == null && string.IsNullOrWhiteSpace(uniqueId) && long.TryParse(Cell(values, elementIdIndex), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out long id))
                        element = doc.GetElement(new ElementId(id));
                    if (element == null) { unresolved++; continue; }

                    var mapped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int c = 0; c < headers.Count; c++)
                    {
                        string name = headers[c] ?? string.Empty;
                        if (name.StartsWith("Parameter: ", StringComparison.OrdinalIgnoreCase))
                        {
                            string parameterName = name.Substring("Parameter: ".Length).Trim();
                            if (!string.Equals(parameterName, "Mark", StringComparison.OrdinalIgnoreCase))
                                mapped[parameterName] = Cell(values, c);
                        }
                        else if (string.Equals(name, "Mark", StringComparison.OrdinalIgnoreCase))
                            mapped["\u0001MARK"] = Cell(values, c);
                    }
                    rows.Add(new ImportRow { Line = i + 1, Element = element, Values = mapped });
                }
                if (rows.Count == 0) throw new InvalidOperationException("Không tìm được Element nào trong tài liệu hiện tại khớp với bảng tính.");

                int eligible = 0, readOnly = 0, missing = 0;
                foreach (ImportRow row in rows)
                foreach (KeyValuePair<string, string> value in row.Values)
                {
                    Parameter parameter = FindParameter(row.Element, value.Key);
                    if (parameter == null) { missing++; continue; }
                    if (parameter.IsReadOnly) { readOnly++; continue; }
                    if (CanConvert(parameter, value.Value)) eligible++;
                    else missing++;
                }
                if (eligible == 0) throw new InvalidOperationException("Không có tham số ghi được khớp với dữ liệu bảng tính.");

                string preview = "Tìm thấy " + rows.Count + " phần tử; có thể cập nhật " + eligible + " giá trị tham số.";
                if (unresolved > 0) preview += "\nKhông tìm thấy " + unresolved + " dòng ElementId/UniqueId trong tài liệu.";
                if (readOnly > 0) preview += "\nBỏ qua " + readOnly + " tham số chỉ đọc.";
                if (missing > 0) preview += "\nBỏ qua " + missing + " tham số không tồn tại hoặc giá trị không hợp lệ.";
                preview += "\n\nChỉ cột Mark và Parameter: <tên> được nhập. Bạn muốn cập nhật mô hình?";
                if (TaskDialog.Show("KStruct - Xem trước nhập", preview,
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No) != TaskDialogResult.Yes)
                    return Result.Cancelled;

                int updated = 0, failed = 0;
                List<string> errors = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Nhập tham số bảng tính"))
                {
                    tx.Start();
                    foreach (ImportRow row in rows)
                    foreach (KeyValuePair<string, string> value in row.Values)
                    {
                        Parameter parameter = FindParameter(row.Element, value.Key);
                        if (parameter == null || parameter.IsReadOnly) continue;
                        try
                        {
                            if (SetParameter(parameter, value.Value)) updated++;
                            else { failed++; if (errors.Count < 8) errors.Add("Dòng " + row.Line + " / " + value.Key); }
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            if (errors.Count < 8) errors.Add("Dòng " + row.Line + " / " + value.Key + ": " + ex.Message);
                        }
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các cập nhật tham số.");
                }
                string report = "Đã cập nhật " + updated + " giá trị tham số.";
                if (failed > 0) report += "\nKhông cập nhật được " + failed + ":\n" + string.Join("\n", errors);
                TaskDialog.Show("KStruct - Nhập bảng tính", report);
                return updated > 0 ? Result.Succeeded : Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Nhập bảng tính", ex.Message);
                return Result.Failed;
            }
        }

        private static int FindHeader(IList<string> headers, string name) =>
            Enumerable.Range(0, headers.Count).Where(index => string.Equals(headers[index]?.Trim(), name, StringComparison.OrdinalIgnoreCase))
                .Select(index => (int?)index).FirstOrDefault() ?? -1;

        private static string Cell(IList<string> row, int index) => index >= 0 && index < row.Count ? row[index] ?? string.Empty : string.Empty;

        private static Parameter FindParameter(Element element, string name)
        {
            if (name == "\u0001MARK") return element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            return element.Parameters.Cast<Parameter>().FirstOrDefault(parameter => parameter.Definition != null &&
                string.Equals(parameter.Definition.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static bool CanConvert(Parameter parameter, string value)
        {
            if (parameter == null || parameter.IsReadOnly || string.IsNullOrEmpty(value)) return false;
            switch (parameter.StorageType)
            {
                case StorageType.String: return true;
                case StorageType.Integer:
                    if (parameter.Definition.GetDataType() == SpecTypeId.Boolean.YesNo)
                        return bool.TryParse(value, out _) || value == "0" || value == "1";
                    return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
                case StorageType.Double:
                    return true; // Unit-aware parsing is performed by Revit when the value is committed.
                case StorageType.ElementId:
                    return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
                default: return false;
            }
        }

        private static bool SetParameter(Parameter parameter, string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            switch (parameter.StorageType)
            {
                case StorageType.String: return parameter.Set(value);
                case StorageType.Integer:
                    if (parameter.Definition.GetDataType() == SpecTypeId.Boolean.YesNo && bool.TryParse(value, out bool flag))
                        return parameter.Set(flag ? 1 : 0);
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer))
                        return parameter.Set(integer);
                    return parameter.SetValueString(value);
                case StorageType.Double:
                    if (parameter.SetValueString(value)) return true;
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                        return parameter.Set(number);
                    return false;
                case StorageType.ElementId:
                    if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
                        return parameter.Set(new ElementId(id));
                    return false;
                default: return false;
            }
        }

        private static List<List<string>> ParseCsv(string text)
        {
            List<List<string>> rows = new List<List<string>>();
            List<string> row = new List<string>();
            StringBuilder cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (quoted)
                {
                    if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else if (ch == '"') quoted = false;
                    else cell.Append(ch);
                }
                else if (ch == '"' && cell.Length == 0) quoted = true;
                else if (ch == ',') { row.Add(cell.ToString()); cell.Clear(); }
                else if (ch == '\r' || ch == '\n')
                {
                    if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(cell.ToString()); cell.Clear(); rows.Add(row); row = new List<string>();
                }
                else cell.Append(ch);
            }
            if (quoted) throw new InvalidOperationException("CSV có dấu ngoặc kép chưa đóng.");
            if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
            int width = rows.Count == 0 ? 0 : rows[0].Count;
            if (rows.Any(candidate => candidate.Count != width)) throw new InvalidOperationException("Các dòng CSV không có cùng số cột.");
            return rows;
        }

        private static List<List<string>> ReadFirstWorksheet(string path)
        {
            XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
            using (ZipArchive archive = ZipFile.OpenRead(path))
            {
                XDocument workbook = LoadXml(archive, "xl/workbook.xml");
                XElement firstSheet = workbook.Root?.Element(main + "sheets")?.Elements(main + "sheet").FirstOrDefault();
                if (firstSheet == null) throw new InvalidOperationException("Tệp Excel không có worksheet.");
                string relationshipId = (string)firstSheet.Attribute(rel + "id");
                XDocument relationships = LoadXml(archive, "xl/_rels/workbook.xml.rels");
                XElement relationship = relationships.Root?.Elements(packageRel + "Relationship")
                    .FirstOrDefault(item => string.Equals((string)item.Attribute("Id"), relationshipId, StringComparison.Ordinal));
                if (relationship == null) throw new InvalidOperationException("Không tìm được worksheet đầu tiên trong tệp Excel.");
                string target = ((string)relationship.Attribute("Target") ?? string.Empty).Replace('\\', '/');
                string sheetPath = NormalizeZipPath(target.StartsWith("/", StringComparison.Ordinal) ? target.TrimStart('/') : "xl/" + target);
                XDocument sheet = LoadXml(archive, sheetPath);

                List<string> sharedStrings = new List<string>();
                ZipArchiveEntry sharedEntry = archive.GetEntry("xl/sharedStrings.xml");
                if (sharedEntry != null)
                {
                    using (Stream stream = sharedEntry.Open())
                    {
                        XDocument shared = XDocument.Load(stream);
                        sharedStrings = shared.Root?.Elements(main + "si")
                            .Select(item => string.Concat(item.Descendants(main + "t").Select(text => text.Value))).ToList()
                            ?? new List<string>();
                    }
                }

                var indexedRows = new SortedDictionary<int, List<string>>();
                int maxColumn = -1;
                foreach (XElement rowElement in sheet.Root?.Descendants(main + "row") ?? Enumerable.Empty<XElement>())
                {
                    int rowIndex = int.TryParse((string)rowElement.Attribute("r"), out int parsedRow) ? parsedRow - 1 : indexedRows.Count;
                    if (rowIndex < 0) continue;
                    var cells = new Dictionary<int, string>();
                    foreach (XElement cell in rowElement.Elements(main + "c"))
                    {
                        string reference = (string)cell.Attribute("r") ?? string.Empty;
                        int column = ColumnIndex(reference);
                        if (column < 0) continue;
                        string type = (string)cell.Attribute("t") ?? string.Empty;
                        string value = cell.Element(main + "v")?.Value ?? string.Empty;
                        if (type == "s" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sharedIndex))
                            value = sharedIndex >= 0 && sharedIndex < sharedStrings.Count ? sharedStrings[sharedIndex] : string.Empty;
                        else if (type == "inlineStr")
                            value = string.Concat(cell.Descendants(main + "t").Select(text => text.Value));
                        else if (type == "b") value = value == "1" ? "TRUE" : "FALSE";
                        cells[column] = value;
                        maxColumn = Math.Max(maxColumn, column);
                    }
                    indexedRows[rowIndex] = cells.OrderBy(item => item.Key).Select(item => item.Value).ToList();
                    if (indexedRows[rowIndex].Count > 0)
                    {
                        var dense = Enumerable.Repeat(string.Empty, cells.Keys.Max() + 1).ToList();
                        foreach (KeyValuePair<int, string> item in cells) dense[item.Key] = item.Value;
                        indexedRows[rowIndex] = dense;
                    }
                }
                if (indexedRows.Count == 0) return new List<List<string>>();
                int width = Math.Max(maxColumn + 1, indexedRows.Values.Max(row => row.Count));
                var result = new List<List<string>>();
                for (int i = 0; i <= indexedRows.Keys.Max(); i++)
                {
                    List<string> row;
                    if (!indexedRows.TryGetValue(i, out row)) row = new List<string>();
                    if (row.Count < width) row.AddRange(Enumerable.Repeat(string.Empty, width - row.Count));
                    result.Add(row);
                }
                return result;
            }
        }

        private static XDocument LoadXml(ZipArchive archive, string entryName)
        {
            ZipArchiveEntry entry = archive.GetEntry(entryName);
            if (entry == null) throw new InvalidOperationException("Tệp Excel thiếu thành phần " + entryName + ".");
            using (Stream stream = entry.Open()) return XDocument.Load(stream);
        }

        private static string NormalizeZipPath(string path)
        {
            var parts = new List<string>();
            foreach (string part in path.Split('/'))
            {
                if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
                else if (part.Length > 0 && part != ".") parts.Add(part);
            }
            return string.Join("/", parts);
        }

        private static int ColumnIndex(string cellReference)
        {
            int value = 0, letters = 0;
            foreach (char ch in cellReference)
            {
                if (ch < 'A' || ch > 'Z') break;
                value = value * 26 + (ch - 'A' + 1);
                letters++;
            }
            return letters == 0 ? -1 : value - 1;
        }
    }
}
