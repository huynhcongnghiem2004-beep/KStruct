using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace ColumnTransition
{
    /// <summary>
    /// Independent entry point for the new multi-storey column transition workflow.
    /// This command deliberately does not call or modify the V3 column command.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public sealed class ColumnTransitionCommand : IExternalCommand
    {
        private const double StackToleranceMm = 50.0;

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;

            Document doc = ui.Document;
            try
            {
                Reference picked = ui.Selection.PickObject(ObjectType.Element,
                    new StructuralColumnFilter(), "Chọn một cột thuộc tuyến nhiều tầng cần xử lý");
                FamilyInstance selected = doc.GetElement(picked) as FamilyInstance;
                if (selected == null) throw new InvalidOperationException("Không đọc được cột đã chọn.");

                List<TransitionColumnSegment> stack = FindStack(doc, selected);
                if (stack.Count == 0) throw new InvalidOperationException("Không tìm thấy cột nào trong tuyến.");
                List<double> diameters = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType))
                    .Cast<RebarBarType>()
                    .Select(t => t.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER))
                    .Where(p => p != null && p.HasValue)
                    .Select(p => Math.Round(UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Millimeters), 2))
                    .Distinct().OrderBy(x => x).ToList();
                if (diameters.Count == 0)
                    throw new InvalidOperationException("Project chưa có RebarBarType. Hãy Load loại thép vào Project trước.");

                var window = new ColumnTransitionWindow(stack, diameters);
                new WindowInteropHelper(window).Owner = data.Application.MainWindowHandle;
                if (window.ShowDialog() != true || !window.Accepted) return Result.Cancelled;

                int transitions = stack.Zip(stack.Skip(1), (a, b) =>
                    Math.Abs(a.WidthMm - b.WidthMm) > 1 || Math.Abs(a.DepthMm - b.DepthMm) > 1).Count(x => x);
                Dictionary<long, TransitionRebarConfig> configurations = window.Configurations;
                string configSummary = string.Join("\n", stack.Select(segment =>
                {
                    TransitionRebarConfig config = configurations[segment.Id.Value];
                    int barCount = 2 * (config.BarsX + config.BarsY) - 4;
                    return string.Format("{0}: {1}Ø{2:0.##}; đai Ø{3:0.##} a{4:0} mm",
                        segment.Level, barCount, config.MainDiameter, config.TieDiameter, config.TieSpacing);
                }));
                TaskDialog.Show("KStruct - Cấu hình tuyến",
                    "Đã cấu hình " + stack.Count + " đoạn cột, gồm " + transitions +
                    " vị trí thay đổi tiết diện.\n\n" + configSummary +
                    "\n\nThông số hiện chỉ áp dụng trong phiên này. Sơ đồ chuyển thanh là minh họa; lệnh chưa tạo Rebar.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Thép cột chuyển tiếp", ex.Message);
                return Result.Failed;
            }
        }

        private static List<TransitionColumnSegment> FindStack(Document doc, FamilyInstance selected)
        {
            LocationPoint selectedLocation = selected.Location as LocationPoint;
            if (selectedLocation == null)
                throw new InvalidOperationException("Lệnh hiện hỗ trợ cột kết cấu đặt theo LocationPoint.");

            double tolerance = UnitUtils.ConvertToInternalUnits(StackToleranceMm, UnitTypeId.Millimeters);
            var results = new List<TransitionColumnSegment>();
            foreach (FamilyInstance column in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralColumns)
                .WhereElementIsNotElementType().OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
            {
                if (column.CreatedPhaseId != selected.CreatedPhaseId ||
                    column.DemolishedPhaseId != selected.DemolishedPhaseId ||
                    !SameDesignOption(column, selected)) continue;

                LocationPoint location = column.Location as LocationPoint;
                if (location == null) continue;
                XYZ selectedXY = new XYZ(selectedLocation.Point.X, selectedLocation.Point.Y, 0);
                XYZ columnXY = new XYZ(location.Point.X, location.Point.Y, 0);
                if (selectedXY.DistanceTo(columnXY) > tolerance) continue;

                BoundingBoxXYZ box = column.get_BoundingBox(null);
                if (box == null) continue;
                Level level = doc.GetElement(column.LevelId) as Level;
                results.Add(new TransitionColumnSegment
                {
                    Column = column,
                    Id = column.Id,
                    Level = level == null ? "Không rõ tầng" : level.Name,
                    Mark = column.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "",
                    BottomMm = UnitUtils.ConvertFromInternalUnits(box.Min.Z, UnitTypeId.Millimeters),
                    TopMm = UnitUtils.ConvertFromInternalUnits(box.Max.Z, UnitTypeId.Millimeters),
                    WidthMm = UnitUtils.ConvertFromInternalUnits(box.Max.X - box.Min.X, UnitTypeId.Millimeters),
                    DepthMm = UnitUtils.ConvertFromInternalUnits(box.Max.Y - box.Min.Y, UnitTypeId.Millimeters)
                });
            }

            return results.OrderBy(x => x.BottomMm).ThenBy(x => x.Id.Value).ToList();
        }

        private static bool SameDesignOption(FamilyInstance a, FamilyInstance b)
        {
            ElementId idA = a.DesignOption == null ? ElementId.InvalidElementId : a.DesignOption.Id;
            ElementId idB = b.DesignOption == null ? ElementId.InvalidElementId : b.DesignOption.Id;
            return idA == idB;
        }

        private sealed class StructuralColumnFilter : ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                return element is FamilyInstance && element.Category != null &&
                    element.Category.Id.Value == (long)BuiltInCategory.OST_StructuralColumns;
            }

            public bool AllowReference(Reference reference, XYZ position) { return false; }
        }
    }
}
