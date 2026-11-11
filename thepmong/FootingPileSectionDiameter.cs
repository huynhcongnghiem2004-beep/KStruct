using System;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace SDK_HD.KStruct.NativeDocumentation
{
    // D Coc is a TYPE length parameter in the supplied BS_FM_Detail Cọc 2D family.
    // Never change the user's original Type: duplicate a type for each actual pile diameter.
    internal static class FootingPileSectionDiameter
    {
        private const double ToleranceMm = 0.1;

        internal static double ReadPileDiameter(Document doc, FamilyInstance pile)
        {
            // Only an explicit length parameter is acceptable. Bounding-box widths and
            // footing dimensions cannot certify the diameter of a pile.
            string[] names = { "D Coc", "D Cọc", "Đường kính", "Duong Kinh",
                "Pile Diameter", "Diameter", "DIAMETER", "D" };
            foreach (Element source in new Element[] { pile, doc.GetElement(pile.GetTypeId()) })
            {
                if (source == null) continue;
                foreach (string name in names)
                {
                    var parameters = source.GetParameters(name);
                    if (parameters.Count > 1)
                        throw new InvalidOperationException("Cọc " + pile.Id.Value +
                            " có tham số đường kính trùng tên: " + name);
                    if (parameters.Count == 0) continue;
                    Parameter p = parameters[0];
                    if (p.StorageType != StorageType.Double || !p.HasValue ||
                        !p.Definition.GetDataType().Equals(SpecTypeId.Length))
                        throw new InvalidOperationException("Tham số " + name + " của cọc " +
                            pile.Id.Value + " không phải kích thước Length hợp lệ.");
                    double diameter = p.AsDouble();
                    if (!Valid(diameter) || diameter <= 0)
                        throw new InvalidOperationException("Đường kính cọc " + pile.Id.Value +
                            " bằng 0, âm hoặc không hợp lệ.");
                    return diameter;
                }
            }
            throw new InvalidOperationException("Cọc " + pile.Id.Value +
                " không có tham số đường kính Length (D Coc / Diameter / Đường kính / D). " +
                "Không được suy đoán D Coc từ BoundingBox hoặc mặc định 300 mm.");
        }

        internal static FamilySymbol ResolveSymbol(Document doc, FamilySymbol requested, double diameter)
        {
            if (!Valid(diameter) || diameter <= 0)
                throw new InvalidOperationException("Đường kính cọc không hợp lệ.");
            Parameter current = One(requested, "D Coc");
            if (current == null)
                throw new InvalidOperationException("Family Cọc 2D thiếu Type Parameter 'D Coc'.");
            if (current.StorageType != StorageType.Double ||
                !current.Definition.GetDataType().Equals(SpecTypeId.Length))
                throw new InvalidOperationException("'D Coc' của Family Cọc 2D phải là tham số Length.");
            if (Near(current.AsDouble(), diameter)) return requested;

            double mm = UnitUtils.ConvertFromInternalUnits(diameter, UnitTypeId.Millimeters);
            string suffix = Math.Round(mm, 1, MidpointRounding.AwayFromZero)
                .ToString("0.0", CultureInfo.InvariantCulture).Replace(".", "_");
            string typeName = "KStruct_Coc2D_D" + suffix + "mm";
            FamilySymbol target = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.Family.Id.Value == requested.Family.Id.Value &&
                    s.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                target = requested.Duplicate(typeName) as FamilySymbol;
                if (target == null)
                    throw new InvalidOperationException("Không thể tạo Type Cọc 2D theo đường kính " + mm + " mm.");
                Parameter writable = One(target, "D Coc");
                if (writable == null || writable.IsReadOnly || !writable.Set(diameter))
                    throw new InvalidOperationException("Không thể thiết lập 'D Coc' trên Type mới " + typeName +
                        ". Kiểm tra công thức/ràng buộc của Family.");
                doc.Regenerate();
            }
            Parameter actual = One(target, "D Coc");
            if (actual == null || !Near(actual.AsDouble(), diameter))
                throw new InvalidOperationException("Type " + target.Name +
                    " không khớp đường kính cọc thực. Không thể đặt ký hiệu.");
            return target;
        }

        internal static void Verify(FamilyInstance instance, double diameter)
        {
            Parameter actual = One(instance, "D Coc") ?? One(instance.Symbol, "D Coc");
            if (actual == null || actual.StorageType != StorageType.Double ||
                !Near(actual.AsDouble(), diameter))
                throw new InvalidOperationException("Ký hiệu Cọc 2D " + instance.Id.Value +
                    " có D Coc khác đường kính cọc thực.");
        }

        private static Parameter One(Element element, string name)
        {
            if (element == null) return null;
            var items = element.GetParameters(name);
            if (items.Count > 1)
                throw new InvalidOperationException("Có nhiều tham số tên '" + name + "' trên " + element.Id.Value);
            return items.Count == 1 ? items[0] : null;
        }

        private static bool Near(double a, double b) =>
            Valid(a) && Valid(b) &&
            Math.Abs(UnitUtils.ConvertFromInternalUnits(a - b, UnitTypeId.Millimeters)) <= ToleranceMm;

        private static bool Valid(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    }
}
