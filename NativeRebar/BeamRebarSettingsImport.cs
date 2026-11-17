using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SDK_HD.KStruct.NativeRebar
{
    internal sealed class BeamRebarSettingsRow
    {
        internal string BeamId;
        internal string BeamMark;
        internal BeamRebarSettingsFile Settings;
    }

    internal static class BeamRebarSettingsImport
    {
        private static readonly string[] Required =
        {
            "MainTypeName", "TieTypeName", "TopCount", "BottomCount", "CoverMm", "TieSpacingMm"
        };
        private static readonly string[] Optional =
        {
            "UseZonedTies", "TieEndZoneLengthMm", "TieEndSpacingMm", "TieMiddleSpacingMm",
            "TopTypeName", "BottomTypeName", "TopLayer2Count", "TopLayer2TypeName", "BottomLayer2Count", "BottomLayer2TypeName", "TopLayer3Count", "TopLayer3TypeName", "BottomLayer3Count", "BottomLayer3TypeName", "SideBarCount", "SideBarTypeName", "LongitudinalLayerSpacingMm"
            , "TopStartHookTypeName", "TopEndHookTypeName", "BottomStartHookTypeName", "BottomEndHookTypeName"
        };

        internal static BeamRebarSettingsFile Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("Không tìm thấy file Excel/CSV.", path);
            Dictionary<string, string> values = FindKeyValues(ReadFile(path));
            return ParseSettings(values);
        }

        internal static List<BeamRebarSettingsRow> ReadRows(string path)
        {
            List<Dictionary<int, string>> rows = ReadFile(path);
            int headerIndex = -1, beamIdColumn = -1, markColumn = -1;
            Dictionary<string, int> valueColumns = null;
            for (int i = 0; i < rows.Count; i++)
            {
                var columns = new Dictionary<string, int>();
                foreach (var pair in rows[i])
                {
                    string header = Normalize(pair.Value);
                    foreach (string required in Required)
                        if (MatchesHeader(header, Normalize(required))) columns[required] = pair.Key;
                    foreach (string optional in Optional)
                        if (MatchesHeader(header, Normalize(optional))) columns[optional] = pair.Key;
                    if (header == "beamid" || header == "elementid" || header == "rebarhostid") beamIdColumn = pair.Key;
                    if (header == "beammark" || header == "mark" || header == "structuralmark") markColumn = pair.Key;
                }
                if (Required.All(columns.ContainsKey))
                {
                    headerIndex = i;
                    valueColumns = columns;
                    break;
                }
                beamIdColumn = markColumn = -1;
            }

            if (headerIndex < 0 || (beamIdColumn < 0 && markColumn < 0))
                return new List<BeamRebarSettingsRow>
                {
                    new BeamRebarSettingsRow { Settings = Read(path) }
                };

            var result = new List<BeamRebarSettingsRow>();
            for (int i = headerIndex + 1; i < rows.Count; i++)
            {
                string beamId = beamIdColumn < 0 ? "" : GetCell(rows[i], beamIdColumn).Trim();
                string beamMark = markColumn < 0 ? "" : GetCell(rows[i], markColumn).Trim();
                var values = valueColumns.ToDictionary(pair => pair.Key, pair => GetCell(rows[i], pair.Value));
                if (string.IsNullOrWhiteSpace(beamId) && string.IsNullOrWhiteSpace(beamMark) &&
                    values.Values.All(string.IsNullOrWhiteSpace)) continue;
                result.Add(new BeamRebarSettingsRow
                {
                    BeamId = beamId,
                    BeamMark = beamMark,
                    Settings = ParseSettings(values)
                });
            }
            if (result.Count == 0) throw new InvalidOperationException("Không có dòng dữ liệu dầm sau hàng tiêu đề.");
            return result;
        }

        private static List<Dictionary<int, string>> ReadFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("Không tìm thấy file Excel/CSV.", path);
            string extension = Path.GetExtension(path);
            if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)) return ReadXlsx(path);
            if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                return ReadCsv(path);
            throw new InvalidOperationException("Chỉ hỗ trợ file .xlsx, .csv hoặc .txt.");
        }

        private static BeamRebarSettingsFile ParseSettings(Dictionary<string, string> values)
        {
            var result = new BeamRebarSettingsFile
            {
                MainTypeName = Get(values, "MainTypeName"),
                TieTypeName = Get(values, "TieTypeName"),
                TopCount = ParseInt(Get(values, "TopCount"), "TopCount"),
                BottomCount = ParseInt(Get(values, "BottomCount"), "BottomCount"),
                CoverMm = ParseDouble(Get(values, "CoverMm"), "CoverMm"),
                TieSpacingMm = ParseDouble(Get(values, "TieSpacingMm"), "TieSpacingMm"),
                UseZonedTies = ParseOptionalBool(values, "UseZonedTies", false),
                TieEndZoneLengthMm = ParseOptionalDouble(values, "TieEndZoneLengthMm", 600),
                TieEndSpacingMm = ParseOptionalDouble(values, "TieEndSpacingMm", ParseDouble(Get(values, "TieSpacingMm"), "TieSpacingMm")),
                TieMiddleSpacingMm = ParseOptionalDouble(values, "TieMiddleSpacingMm", ParseDouble(Get(values, "TieSpacingMm"), "TieSpacingMm")),
                TopLayer2Count = ParseOptionalInt(values, "TopLayer2Count", 0),
                BottomLayer2Count = ParseOptionalInt(values, "BottomLayer2Count", 0),
                TopLayer3Count = ParseOptionalInt(values, "TopLayer3Count", 0),
                BottomLayer3Count = ParseOptionalInt(values, "BottomLayer3Count", 0),
                SideBarCount = ParseOptionalInt(values, "SideBarCount", 0),
                SideBarTypeName = ParseOptionalString(values, "SideBarTypeName"),
                TopTypeName = ParseOptionalString(values, "TopTypeName"),
                BottomTypeName = ParseOptionalString(values, "BottomTypeName"),
                TopStartHookTypeName = ParseOptionalString(values, "TopStartHookTypeName"),
                TopEndHookTypeName = ParseOptionalString(values, "TopEndHookTypeName"),
                BottomStartHookTypeName = ParseOptionalString(values, "BottomStartHookTypeName"),
                BottomEndHookTypeName = ParseOptionalString(values, "BottomEndHookTypeName"),
                TopLayer2TypeName = ParseOptionalString(values, "TopLayer2TypeName"),
                BottomLayer2TypeName = ParseOptionalString(values, "BottomLayer2TypeName"),
                TopLayer3TypeName = ParseOptionalString(values, "TopLayer3TypeName"),
                BottomLayer3TypeName = ParseOptionalString(values, "BottomLayer3TypeName"),
                LongitudinalLayerSpacingMm = ParseOptionalDouble(values, "LongitudinalLayerSpacingMm", 30)
            };
            if (result.TopCount < 1 || result.TopCount > 12 || result.BottomCount < 1 || result.BottomCount > 12)
                throw new InvalidOperationException("TopCount và BottomCount phải từ 1 đến 12.");
            if (result.TopLayer2Count < 0 || result.TopLayer2Count > 12 || result.BottomLayer2Count < 0 || result.BottomLayer2Count > 12 ||
                result.TopLayer3Count < 0 || result.TopLayer3Count > 12 || result.BottomLayer3Count < 0 || result.BottomLayer3Count > 12 || result.SideBarCount < 0 || result.SideBarCount > 12)
                throw new InvalidOperationException("Số thanh lớp tăng cường phải từ 0 đến 12.");
            if (result.CoverMm <= 0 || result.TieSpacingMm <= 0)
                throw new InvalidOperationException("CoverMm và TieSpacingMm phải lớn hơn 0.");
            return result;
        }

        private static Dictionary<string, string> FindKeyValues(IList<Dictionary<int, string>> rows)
        {
            foreach (Dictionary<int, string> row in rows)
            {
                var normalized = row.ToDictionary(pair => pair.Key, pair => Normalize(pair.Value));
                var columns = new Dictionary<string, int>();
                foreach (string required in Required)
                {
                    string key = Normalize(required);
                    KeyValuePair<int, string> match = normalized.FirstOrDefault(pair => MatchesHeader(pair.Value, key));
                    if (!string.IsNullOrEmpty(match.Value)) columns[required] = match.Key;
                }
                if (Required.All(columns.ContainsKey))
                {
                    int nextIndex = rows.IndexOf(row) + 1;
                    if (nextIndex >= rows.Count) throw new InvalidOperationException("File có hàng tiêu đề nhưng thiếu hàng giá trị.");
                    foreach (string optional in Optional)
                    {
                        KeyValuePair<int, string> match = normalized.FirstOrDefault(pair => MatchesHeader(pair.Value, Normalize(optional)));
                        if (!string.IsNullOrEmpty(match.Value)) columns[optional] = match.Key;
                    }
                    return columns.ToDictionary(pair => pair.Key,
                        pair => GetCell(rows[nextIndex], pair.Value));
                }
            }

            var keyValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Dictionary<int, string> row in rows)
            {
                string key = Normalize(GetCell(row, 0));
                string canonical = Required.Concat(Optional).FirstOrDefault(name => MatchesHeader(key, Normalize(name)));
                if (canonical != null) keyValues[canonical] = GetCell(row, 1);
            }
            if (Required.Any(name => !keyValues.ContainsKey(name)))
                throw new InvalidOperationException("Không đọc đủ cột cấu hình. Cần các trường: " + string.Join(", ", Required) + ".");
            return keyValues;
        }

        private static bool MatchesHeader(string actual, string canonical)
        {
            if (string.Equals(actual, canonical, StringComparison.OrdinalIgnoreCase)) return true;
            string[][] aliases =
            {
                new[] { "maintype", "thepdoc", "thepchu", "mainbar" },
                new[] { "tietype", "thepdai", "dai", "tiebar" },
                new[] { "topbars", "sothanhtren", "thanhtren" },
                new[] { "bottombars", "sothanhduoi", "thanhduoi" },
                new[] { "cover", "lopbaove", "lopbaovebetong" },
                new[] { "tiespacing", "tiepitch", "buocdai", "buocdaithep" }
            };
            int index = Array.IndexOf(Required.Select(Normalize).ToArray(), canonical);
            return index >= 0 && aliases[index].Contains(actual, StringComparer.OrdinalIgnoreCase);
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string decomposed = value.Normalize(NormalizationForm.FormD);
            var result = new StringBuilder(decomposed.Length);
            foreach (char ch in decomposed)
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(ch))
                    result.Append(char.ToLowerInvariant(ch) == 'đ' ? 'd' : char.ToLowerInvariant(ch));
            return result.ToString();
        }

        private static string Get(Dictionary<string, string> values, string key)
        {
            string value;
            if (!values.TryGetValue(key, out value) || string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("Thiếu giá trị " + key + " trong file.");
            return value.Trim();
        }

        private static int ParseInt(string value, string key)
        {
            int result;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
                throw new InvalidOperationException(key + " phải là số nguyên.");
            return result;
        }

        private static double ParseDouble(string value, string key)
        {
            double result;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) &&
                !double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result))
                throw new InvalidOperationException(key + " phải là số hợp lệ.");
            return result;
        }

        private static double ParseOptionalDouble(Dictionary<string, string> values, string key, double fallback)
        {
            string value;
            return values.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value) ? ParseDouble(value, key) : fallback;
        }

        private static int ParseOptionalInt(Dictionary<string, string> values, string key, int fallback)
        {
            string value;
            if (!values.TryGetValue(key, out value) || string.IsNullOrWhiteSpace(value)) return fallback;
            return ParseInt(value, key);
        }

        private static string ParseOptionalString(Dictionary<string, string> values, string key)
        {
            string value;
            return values.TryGetValue(key, out value) ? value?.Trim() ?? string.Empty : string.Empty;
        }

        private static bool ParseOptionalBool(Dictionary<string, string> values, string key, bool fallback)
        {
            string value;
            if (!values.TryGetValue(key, out value) || string.IsNullOrWhiteSpace(value)) return fallback;
            if (bool.TryParse(value, out bool result)) return result;
            if (value == "1") return true;
            if (value == "0") return false;
            throw new InvalidOperationException(key + " phải là true/false hoặc 1/0.");
        }

        private static List<Dictionary<int, string>> ReadCsv(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            char delimiter = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?
                .Count(ch => ch == ';') > text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Count(ch => ch == ',')
                ? ';' : (text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Contains('\t') == true ? '\t' : ',');
            var rows = new List<Dictionary<int, string>>();
            foreach (string line in text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries))
            {
                List<string> fields = ParseCsvLine(line, delimiter);
                rows.Add(fields.Select((value, index) => new { value, index })
                    .ToDictionary(pair => pair.index, pair => pair.value.Trim()));
            }
            return rows;
        }

        private static List<string> ParseCsvLine(string line, char delimiter)
        {
            var fields = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (ch == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = !quoted;
                }
                else if (ch == delimiter && !quoted) { fields.Add(field.ToString()); field.Clear(); }
                else field.Append(ch);
            }
            fields.Add(field.ToString());
            return fields;
        }

        private static List<Dictionary<int, string>> ReadXlsx(string path)
        {
            using (ZipArchive archive = ZipFile.OpenRead(path))
            {
                XDocument workbook = LoadXml(archive, "xl/workbook.xml");
                XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
                XElement firstSheet = workbook.Root.Element(main + "sheets")?.Elements(main + "sheet").FirstOrDefault();
                if (firstSheet == null) throw new InvalidOperationException("Workbook không có sheet.");
                string relationshipId = (string)firstSheet.Attribute(relNs + "id");
                XDocument relationships = LoadXml(archive, "xl/_rels/workbook.xml.rels");
                XNamespace packageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
                XElement relationship = relationships.Root.Elements(packageRel + "Relationship")
                    .FirstOrDefault(item => (string)item.Attribute("Id") == relationshipId);
                if (relationship == null) throw new InvalidOperationException("Không tìm thấy sheet đầu tiên trong workbook.");
                string target = ((string)relationship.Attribute("Target") ?? "").Replace('\\', '/');
                string sheetPath = target.StartsWith("/", StringComparison.Ordinal)
                    ? target.TrimStart('/') : "xl/" + target.TrimStart('.','/');
                XDocument sheet = LoadXml(archive, sheetPath);
                List<string> shared = ReadSharedStrings(archive, main);
                var rows = new List<Dictionary<int, string>>();
                foreach (XElement row in sheet.Descendants(main + "row"))
                {
                    var values = new Dictionary<int, string>();
                    foreach (XElement cell in row.Elements(main + "c"))
                    {
                        string reference = (string)cell.Attribute("r");
                        int column = ColumnIndex(reference);
                        string type = (string)cell.Attribute("t");
                        string value = (string)cell.Element(main + "v") ?? "";
                        if (type == "s")
                        {
                            int index;
                            value = int.TryParse(value, out index) && index >= 0 && index < shared.Count ? shared[index] : "";
                        }
                        else if (type == "inlineStr") value = cell.Element(main + "is")?.Value ?? "";
                        values[column] = value;
                    }
                    if (values.Count > 0) rows.Add(values);
                }
                return rows;
            }
        }

        private static XDocument LoadXml(ZipArchive archive, string path)
        {
            ZipArchiveEntry entry = archive.GetEntry(path);
            if (entry == null) throw new InvalidOperationException("Excel thiếu thành phần " + path + ".");
            using (Stream stream = entry.Open()) return XDocument.Load(stream);
        }

        private static List<string> ReadSharedStrings(ZipArchive archive, XNamespace main)
        {
            ZipArchiveEntry entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null) return new List<string>();
            using (Stream stream = entry.Open())
            {
                XDocument document = XDocument.Load(stream);
                return document.Descendants(main + "si").Select(item =>
                    string.Concat(item.Descendants(main + "t").Select(text => text.Value))).ToList();
            }
        }

        private static int ColumnIndex(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return 0;
            int value = 0;
            foreach (char ch in reference.TakeWhile(char.IsLetter)) value = value * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            return Math.Max(0, value - 1);
        }

        private static string GetCell(Dictionary<int, string> row, int index)
        {
            string value;
            return row != null && row.TryGetValue(index, out value) ? value : "";
        }
    }
}
