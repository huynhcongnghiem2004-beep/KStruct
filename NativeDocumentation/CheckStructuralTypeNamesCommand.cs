using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class CheckStructuralTypeNamesCommand : IExternalCommand
    {
        private static readonly BuiltInCategory[] StructuralCategories =
        {
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_StructuralFoundation,
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors
        };

        private sealed class AuditRow
        {
            public string Category, Family, TypeName, Width, Height, Material;
            public string DimensionCheck, MaterialCheck, Result;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                HashSet<long> categories = new HashSet<long>(StructuralCategories.Select(category => (long)category));
                ICollection<ElementId> selectedIds = ui.Selection.GetElementIds();
                List<ElementType> types = selectedIds.Select(doc.GetElement)
                    .Select(element => element is ElementType ? element as ElementType :
                        element != null && element.GetTypeId() != ElementId.InvalidElementId ? doc.GetElement(element.GetTypeId()) as ElementType : null)
                    .Where(type => type != null && type.Category != null && categories.Contains(type.Category.Id.Value))
                    .GroupBy(type => type.Id).Select(group => group.First()).ToList();
                string scope = "các cấu kiện đã chọn";
                if (types.Count == 0)
                {
                    if (selectedIds.Count > 0)
                    {
                        TaskDialog.Show("KStruct - Naming Checker", "Lựa chọn hiện tại không chứa cột, dầm, tường, sàn hoặc móng kết cấu.");
                        return Result.Cancelled;
                    }
                    scope = "toàn bộ type kết cấu đang được sử dụng trong project";
                    types = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                        .Where(element => element.Category != null && categories.Contains(element.Category.Id.Value))
                        .Select(element => doc.GetElement(element.GetTypeId()) as ElementType)
                        .Where(type => type != null).GroupBy(type => type.Id).Select(group => group.First()).ToList();
                }
                if (types.Count == 0)
                {
                    TaskDialog.Show("KStruct - Naming Checker", "Không tìm thấy type cột, dầm, tường, sàn hoặc móng trong phạm vi.");
                    return Result.Cancelled;
                }

                List<AuditRow> rows = types.Select(type => AuditType(doc, type)).OrderBy(row => row.Category)
                    .ThenBy(row => row.Family).ThenBy(row => row.TypeName).ToList();
                var dialog = new SaveFileDialog
                {
                    Title = "Xuất báo cáo Naming Checker - KStruct",
                    Filter = "CSV UTF-8 (*.csv)|*.csv",
                    FileName = "KStruct_StructuralTypeNameAudit_" + DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture) + ".csv",
                    AddExtension = true,
                    DefaultExt = ".csv",
                    OverwritePrompt = true
                };
                if (dialog.ShowDialog() != true) return Result.Cancelled;

                var csv = new StringBuilder();
                csv.AppendLine("Category,Family,TypeName,Width_mm,Height_mm,Material,DimensionNameCheck,MaterialNameCheck,Result");
                foreach (AuditRow row in rows)
                    csv.AppendLine(string.Join(",", new[] { Csv(row.Category), Csv(row.Family), Csv(row.TypeName), Csv(row.Width),
                        Csv(row.Height), Csv(row.Material), Csv(row.DimensionCheck), Csv(row.MaterialCheck), Csv(row.Result) }));
                File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));

                int passed = rows.Count(row => row.Result == "PASS");
                int mismatch = rows.Count(row => row.Result == "MISMATCH");
                int incomplete = rows.Count(row => row.Result == "REVIEW");
                TaskDialog.Show("KStruct - Naming Checker",
                    "Đã rà " + rows.Count + " type trong " + scope + ".\nKhớp: " + passed +
                    "\nKhông khớp theo quy tắc kiểm tra: " + mismatch +
                    "\nThiếu tham số để kết luận: " + incomplete +
                    "\n\nBáo cáo: " + dialog.FileName +
                    "\n\nKStruct không tự đổi tên type; hãy xem từng dòng CSV trước khi chỉnh quy ước đặt tên.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Naming Checker", ex.Message);
                return Result.Failed;
            }
        }

        private static AuditRow AuditType(Document doc, ElementType type)
        {
            double? width = ReadLength(type, BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH,
                BuiltInParameter.FAMILY_WIDTH_PARAM, "Width", "B", "b", "Width X");
            double? height = ReadLength(type, BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT,
                BuiltInParameter.FAMILY_HEIGHT_PARAM, "Height", "H", "h", "Depth", "D");
            string material = ReadMaterial(doc, type);
            string normalizedName = Normalize(type.Name);
            string widthToken = width.HasValue ? FormatMillimeters(width.Value) : null;
            string heightToken = height.HasValue ? FormatMillimeters(height.Value) : null;
            string dimensionCheck = widthToken == null || heightToken == null ? "UNKNOWN" :
                (normalizedName.Contains(Normalize(widthToken + "x" + heightToken)) ||
                 normalizedName.Contains(Normalize(heightToken + "x" + widthToken)) ? "MATCH" : "MISMATCH");
            string materialCheck = string.IsNullOrWhiteSpace(material) ? "UNKNOWN" :
                (Normalize(material).Length >= 2 && normalizedName.Contains(Normalize(material)) ? "MATCH" : "MISMATCH");
            string result = dimensionCheck == "MISMATCH" || materialCheck == "MISMATCH" ? "MISMATCH" :
                dimensionCheck == "MATCH" && materialCheck == "MATCH" ? "PASS" : "REVIEW";
            FamilySymbol symbol = type as FamilySymbol;
            return new AuditRow
            {
                Category = type.Category?.Name ?? "",
                Family = symbol?.FamilyName ?? "",
                TypeName = type.Name ?? "",
                Width = width.HasValue ? width.Value.ToString("0.##", CultureInfo.InvariantCulture) : "",
                Height = height.HasValue ? height.Value.ToString("0.##", CultureInfo.InvariantCulture) : "",
                Material = material,
                DimensionCheck = dimensionCheck,
                MaterialCheck = materialCheck,
                Result = result
            };
        }

        private static double? ReadLength(Element element, BuiltInParameter first, BuiltInParameter second, params string[] names)
        {
            Parameter parameter = element.get_Parameter(first) ?? element.get_Parameter(second);
            if (!IsLength(parameter))
                parameter = element.Parameters.Cast<Parameter>().FirstOrDefault(candidate => candidate.Definition != null &&
                    names.Any(name => string.Equals(candidate.Definition.Name, name, StringComparison.OrdinalIgnoreCase)) && IsLength(candidate));
            return IsLength(parameter) && parameter.HasValue ?
                (double?)UnitUtils.ConvertFromInternalUnits(parameter.AsDouble(), UnitTypeId.Millimeters) : null;
        }

        private static bool IsLength(Parameter parameter) => parameter != null && parameter.StorageType == StorageType.Double &&
            string.Equals(parameter.Definition?.GetDataType()?.TypeId, SpecTypeId.Length.TypeId, StringComparison.Ordinal);

        private static string ReadMaterial(Document doc, ElementType type)
        {
            Parameter parameter = type.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM) ??
                type.Parameters.Cast<Parameter>().FirstOrDefault(candidate => candidate.Definition != null &&
                    (string.Equals(candidate.Definition.Name, "Material", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(candidate.Definition.Name, "Structural Material", StringComparison.OrdinalIgnoreCase)));
            if (parameter == null || !parameter.HasValue) return "";
            if (parameter.StorageType == StorageType.ElementId)
                return (doc.GetElement(parameter.AsElementId()) as Material)?.Name ?? "";
            return parameter.AsString() ?? parameter.AsValueString() ?? "";
        }

        private static string FormatMillimeters(double value) =>
            Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

        private static string Normalize(string value) => new string((value ?? "").ToUpperInvariant()
            .Where(char.IsLetterOrDigit).ToArray());

        private static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    }
}
