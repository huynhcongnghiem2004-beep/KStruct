using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Microsoft.Win32;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class ExportPileCoordinatesCommand : IExternalCommand
    {
        private sealed class FoundationFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element.Category != null &&
                element.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation &&
                element is FamilyInstance;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class CoordinateRow
        {
            public Element Element;
            public XYZ Point;
            public ProjectPosition ProjectPosition;
            public string Mark;
            public string Family;
            public string Type;
            public string Level;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Element> piles = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .Where(element => element.Category != null &&
                        element.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation && element is FamilyInstance)
                    .Distinct().ToList();
                if (piles.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new FoundationFilter(),
                        "Chọn các cấu kiện móng/cọc cần xuất tọa độ tim");
                    piles = picked.Select(reference => doc.GetElement(reference)).Where(element => element != null).Distinct().ToList();
                }
                if (piles.Count == 0) return Result.Cancelled;

                ProjectLocation projectLocation = doc.ActiveProjectLocation;
                if (projectLocation == null) throw new InvalidOperationException("Project chưa có Project Location hoạt động.");
                var rows = new List<CoordinateRow>();
                var skipped = new List<string>();
                foreach (Element pile in piles)
                {
                    try
                    {
                        XYZ point = GetCenterPoint(pile);
                        ProjectPosition projected = projectLocation.GetProjectPosition(point);
                        if (projected == null) throw new InvalidOperationException("Không đổi được sang tọa độ Project Location.");
                        FamilyInstance instance = pile as FamilyInstance;
                        Element type = instance != null ? doc.GetElement(instance.GetTypeId()) : null;
                        Element level = pile.LevelId != ElementId.InvalidElementId ? doc.GetElement(pile.LevelId) : null;
                        rows.Add(new CoordinateRow
                        {
                            Element = pile,
                            Point = point,
                            ProjectPosition = projected,
                            Mark = ReadParameter(pile, BuiltInParameter.ALL_MODEL_MARK),
                            Family = (instance?.Symbol?.FamilyName) ?? "",
                            Type = type?.Name ?? pile.Name ?? "",
                            Level = level?.Name ?? ""
                        });
                    }
                    catch (Exception ex) { skipped.Add(pile.Id.Value + ": " + ex.Message); }
                }
                if (rows.Count == 0)
                {
                    TaskDialog.Show("KStruct - Tọa độ cọc", "Không đọc được tọa độ cấu kiện nào.\n" + string.Join("\n", skipped.Take(8)));
                    return Result.Cancelled;
                }

                var dialog = new SaveFileDialog
                {
                    Title = "Xuất tọa độ tim cọc - KStruct",
                    Filter = "CSV UTF-8 (*.csv)|*.csv",
                    FileName = "KStruct_PileCoordinates_" + DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture) + ".csv",
                    AddExtension = true,
                    DefaultExt = ".csv",
                    OverwritePrompt = true
                };
                if (dialog.ShowDialog() != true) return Result.Cancelled;

                StringBuilder csv = new StringBuilder();
                csv.AppendLine("Mark,ElementId,Family,Type,Level,ProjectEastWest_m,ProjectNorthSouth_m,ProjectElevation_m,InternalX_m,InternalY_m,InternalZ_m,ProjectLocation,ProjectAngle_deg");
                foreach (CoordinateRow row in rows.OrderBy(item => item.Mark, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(item => item.Element.Id.Value))
                {
                    csv.AppendLine(string.Join(",", new[]
                    {
                        Csv(row.Mark), Csv(row.Element.Id.Value.ToString(CultureInfo.InvariantCulture)), Csv(row.Family), Csv(row.Type), Csv(row.Level),
                        Meters(row.ProjectPosition.EastWest), Meters(row.ProjectPosition.NorthSouth), Meters(row.ProjectPosition.Elevation),
                        Meters(row.Point.X), Meters(row.Point.Y), Meters(row.Point.Z), Csv(projectLocation.Name),
                        (row.ProjectPosition.Angle * 180.0 / Math.PI).ToString("0.######", CultureInfo.InvariantCulture)
                    }));
                }
                File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));

                string report = "Đã xuất " + rows.Count + " cấu kiện móng/cọc sang:\n" + dialog.FileName +
                    "\n\nTọa độ Project East/West, North/South và Elevation lấy từ Project Location: " + projectLocation.Name +
                    ". Tọa độ Internal X/Y/Z được ghi kèm để đối chiếu.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " phần tử không đọc được tọa độ.";
                TaskDialog.Show("KStruct - Tọa độ cọc", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Tọa độ cọc", ex.Message);
                return Result.Failed;
            }
        }

        private static XYZ GetCenterPoint(Element element)
        {
            if (element.Location is LocationPoint location) return location.Point;
            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null) throw new InvalidOperationException("Không có LocationPoint hoặc BoundingBox.");
            XYZ center = (box.Min + box.Max) * 0.5;
            return (box.Transform ?? Transform.Identity).OfPoint(center);
        }

        private static string ReadParameter(Element element, BuiltInParameter parameterId)
        {
            Parameter parameter = element.get_Parameter(parameterId);
            if (parameter == null || !parameter.HasValue) return "";
            return parameter.AsString() ?? parameter.AsValueString() ?? "";
        }

        private static string Meters(double internalLength) =>
            UnitUtils.ConvertFromInternalUnits(internalLength, UnitTypeId.Meters).ToString("0.000", CultureInfo.InvariantCulture);

        private static string Csv(string value)
        {
            value = value ?? "";
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
