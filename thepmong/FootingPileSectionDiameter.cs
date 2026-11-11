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

        internal static double ReadPileDiameter(Document doc, FamilyInstance pile, string configuredName)
        {
            // An explicit mapping is authoritative. Auto mode accepts only unambiguous,
            // clearly named diameter Length parameters; never accepts a generic "D".
            string[] names = string.IsNullOrWhiteSpace(configuredName)
                ? new[] { "D Coc", "D Cọc", "Đường kính cọc", "Duong kinh coc",
                    "Pile Diameter", "Diameter", "Đường kính", "Duong Kinh" }
                : new[] { configuredName.Trim() };
            var matches = new System.Collections.Generic.List<Tuple<string, double>>();
            foreach (Element source in new Element[] { pile, doc.GetElement(pile.GetTypeId()) })
            {
                if (source == null) continue;
                foreach (string name in names)
                {
                    var parameters = source.GetParameters(name);
                    if (parameters.Count > 1)
                        throw new InvalidOperationException("Cọc " + pile.Id.Value +
                            " có nhiều tham số cùng tên '" + name + "'. Hãy dùng tham số duy nhất.");
                    if (parameters.Count == 0) continue;
                    Parameter p = parameters[0];
                    if (p.StorageType != StorageType.Double || !p.HasValue ||
                        !p.Definition.GetDataType().Equals(SpecTypeId.Length))
                        throw new InvalidOperationException("Tham số '" + name + "' của cọc " +
                            pile.Id.Value + " không phải Length có giá trị.");
                    double diameter = p.AsDouble();
                    if (!Valid(diameter) || diameter <= 0)
                        throw new InvalidOperationException("Đường kính cọc " + pile.Id.Value +
                            " không hợp lệ.");
                    matches.Add(Tuple.Create(name, diameter));
                }
            }
            if (matches.Count == 0)
                throw new InvalidOperationException("Không tìm thấy tham số đường kính Length của cọc " +
                    pile.Id.Value + ". Chọn tên tham số đường kính thật trong giao diện KStruct; không dùng BoundingBox.");
            double first = matches[0].Item2;
            foreach (var candidate in matches.Skip(1))
                if (!Near(first, candidate.Item2))
                    throw new InvalidOperationException("Cọc " + pile.Id.Value +
                        " có nhiều tham số đường kính mâu thuẫn (" +
                        string.Join(", ", matches.Select(x => x.Item1)) +
                        "). Hãy chỉ định một tên tham số chính xác.");
            return first;
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
            string typeName = FootingPileSectionPolicy.TypeName(mm);
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
            FootingPileSectionPolicy.DiameterMatches(
                UnitUtils.ConvertFromInternalUnits(a, UnitTypeId.Millimeters),
                UnitUtils.ConvertFromInternalUnits(b, UnitTypeId.Millimeters));

        private static bool Valid(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    }
}
