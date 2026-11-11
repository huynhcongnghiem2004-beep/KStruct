using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace SDK_HD.KStruct.NativeDocumentation
{
    // Audits the real loaded family through Revit FamilyManager, never guesses formulas from .rfa binary.
    // Must be called in the UI before any drawing Transaction starts.
    internal static class FootingPlanFamilyAudit
    {
        internal static string Inspect(Document project, FamilySymbol symbol)
        {
            if (symbol == null) throw new ArgumentNullException(nameof(symbol));
            if (project.IsModifiable)
                throw new InvalidOperationException("Chỉ kiểm tra Family trước khi mở Transaction tạo hồ sơ.");
            Family family = symbol.Family;
            if (family == null || !family.IsEditable)
                throw new InvalidOperationException("Family không hỗ trợ EditFamily; cần Family có thể chỉnh sửa.");
            Document familyDoc = null;
            try
            {
                familyDoc = project.EditFamily(family);
                if (familyDoc == null || !familyDoc.IsFamilyDocument)
                    throw new InvalidOperationException("Không mở được Family Editor.");
                var manager = familyDoc.FamilyManager;
                var parameters = manager.GetParameters().Cast<FamilyParameter>()
                    .OrderBy(p => p.Definition.Name, StringComparer.OrdinalIgnoreCase).ToList();
                var types = manager.Types.Cast<FamilyType>().OrderBy(t => t.Name).ToList();
                var result = new StringBuilder();
                result.AppendLine("KSTRUCT - KIỂM TRA FAMILY GỐC (READ-ONLY)");
                result.AppendLine("Family: " + family.Name);
                result.AppendLine("Định dạng: " + family.FamilyPlacementType);
                result.AppendLine("Type được chọn: " + symbol.Name);
                result.AppendLine("Số Type: " + types.Count + "; tham số: " + parameters.Count);
                result.AppendLine();
                result.AppendLine("THAM SỐ / CÔNG THỨC / NHÃN KÍCH THƯỚC");
                var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (Dimension dimension in new FilteredElementCollector(familyDoc).OfClass(typeof(Dimension)).Cast<Dimension>())
                {
                    try
                    {
                        string name = dimension.FamilyLabel?.Definition.Name;
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        labels[name] = labels.TryGetValue(name, out int count) ? count + 1 : 1;
                    }
                    catch (Autodesk.Revit.Exceptions.InvalidOperationException) { }
                }
                foreach (FamilyParameter p in parameters)
                {
                    string formula = p.Formula;
                    result.AppendLine(p.Definition.Name +
                        " | " + (p.IsInstance ? "Instance" : "Type") +
                        " | " + p.StorageType +
                        " | formula=" + (string.IsNullOrWhiteSpace(formula) ? "<none>" : formula) +
                        " | reporting=" + p.IsReporting +
                        " | readonly=" + p.IsReadOnly +
                        " | dimension-labels=" + (labels.TryGetValue(p.Definition.Name, out int n) ? n : 0));
                }
                result.AppendLine();
                foreach (FamilyType type in types)
                {
                    result.AppendLine("TYPE: " + type.Name);
                    foreach (FamilyParameter p in parameters.Where(p =>
                        FootingPlanDetailComponentSizing.Names.Contains(p.Definition.Name) ||
                        p.Definition.Name == "2D-Top" || p.Definition.Name == "2D-Bot" ||
                        p.Definition.Name == "Bot_Hook 90" ||
                        p.Definition.Name == "Hook-Top" || p.Definition.Name == "Hook-Bot" ||
                        p.Definition.Name == "Abv"))
                    {
                        string value = "<unset>";
                        try
                        {
                            if (p.StorageType == StorageType.Double)
                            {
                                double? d = type.AsDouble(p);
                                if (d.HasValue)
                                    value = UnitUtils.ConvertFromInternalUnits(d.Value, UnitTypeId.Millimeters).ToString("0.###", CultureInfo.InvariantCulture) + " mm";
                            }
                            else if (p.StorageType == StorageType.Integer) value = type.AsInteger(p)?.ToString() ?? "<unset>";
                            else if (p.StorageType == StorageType.String) value = type.AsString(p) ?? "<unset>";
                        }
                        catch (Autodesk.Revit.Exceptions.ArgumentException) { value = "<not available>"; }
                        result.AppendLine("  " + p.Definition.Name + " = " + value);
                    }
                }
                result.AppendLine();
                result.AppendLine("KIỂM TRA ÁNH XẠ KSTRUCT");
                foreach (string name in FootingPlanDetailComponentSizing.Names)
                {
                    var matching = parameters.Where(p => p.Definition.Name == name).ToList();
                    string status = matching.Count != 1 ? "THIẾU/TRÙNG" :
                        !matching[0].IsInstance ? "TYPE - không gán từng móng" :
                        matching[0].StorageType != StorageType.Double ? "SAI KIỂU" :
                        matching[0].IsReporting || matching[0].IsReadOnly ||
                        matching[0].IsDeterminedByFormula ? "BỊ KHÓA/CÓ CÔNG THỨC" : "CÓ THỂ GÁN INSTANCE";
                    result.AppendLine(name + ": " + status);
                }
                result.AppendLine();
                result.AppendLine("LƯU Ý: Đọc được công thức và nhãn kích thước không chứng minh được");
                result.AppendLine("hình học đường thép đã khớp móng. Phải thử 2-3 kích thước và đối chiếu");
                result.AppendLine("đường nét trong Revit Family Editor / View TOP-BOT trước nghiệm thu.");
                return result.ToString();
            }
            finally
            {
                if (familyDoc != null) familyDoc.Close(false);
            }
        }
    }
}
