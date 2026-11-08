using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class ReviewSlabModulesCommand : IExternalCommand
    {
        private sealed class FloorFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Floor;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class FamilyInstanceFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is FamilyInstance;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class ReviewRow
        {
            public string Kind { get; set; }
            public string Id { get; set; }
            public string Type { get; set; }
            public string Level { get; set; }
            public string Position { get; set; }
            public string Result { get; set; }
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Element> selected = ui.Selection.GetElementIds().Select(doc.GetElement).ToList();
                Floor slab = selected.OfType<Floor>().FirstOrDefault();
                if (slab == null)
                {
                    Reference reference = ui.Selection.PickObject(ObjectType.Element, new FloorFilter(), "Chọn sàn cần review module/drop panel");
                    slab = doc.GetElement(reference) as Floor;
                }
                if (slab == null) return Result.Cancelled;
                PlanData plan = ReadPlan(doc, slab);

                List<FamilyInstance> modules = selected.OfType<FamilyInstance>().Where(instance => !IsColumn(instance)).Distinct().ToList();
                if (modules.Count == 0)
                    modules = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                        .Where(instance => instance.Host != null && instance.Host.Id == slab.Id).ToList();
                if (modules.Count == 0)
                {
                    try
                    {
                        modules = ui.Selection.PickObjects(ObjectType.Element, new FamilyInstanceFilter(),
                            "Chọn các family module cần review (Esc để bỏ qua family module)")
                            .Select(reference => doc.GetElement(reference)).OfType<FamilyInstance>().Distinct().ToList();
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException) { modules = new List<FamilyInstance>(); }
                }

                List<ReviewRow> rows = ReviewModules(modules, slab, plan);
                rows.AddRange(ReviewDropPanels(doc, slab, plan));
                if (rows.Count == 0)
                {
                    TaskDialog.Show("KStruct - Review module sàn", "Không tìm thấy family instance trên sàn hoặc drop panel KStruct để review.");
                    return Result.Cancelled;
                }
                ShowReport(data.Application.MainWindowHandle, rows, slab, modules.Count);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Review module sàn", ex.Message);
                return Result.Failed;
            }
        }

        private sealed class PlanData
        {
            public double TopZ;
            public List<List<XYZ>> Loops;
        }

        private sealed class WorldBounds
        {
            public double MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
        }

        private static PlanData ReadPlan(Document doc, Floor floor)
        {
            PlanarFace face = HostObjectUtils.GetTopFaces(floor)
                .Select(reference => floor.GetGeometryObjectFromReference(reference) as PlanarFace)
                .Where(candidate => candidate != null && candidate.FaceNormal.Normalize().DotProduct(XYZ.BasisZ) > 0.999)
                .OrderByDescending(candidate => candidate.Area).FirstOrDefault();
            if (face == null) throw new InvalidOperationException("Review chỉ hỗ trợ mặt sàn nằm ngang.");
            var loops = new List<List<XYZ>>();
            foreach (CurveLoop curveLoop in face.GetEdgesAsCurveLoops())
            {
                var points = new List<XYZ>();
                foreach (Curve curve in curveLoop)
                foreach (XYZ point in curve.Tessellate())
                    if (points.Count == 0 || points[points.Count - 1].DistanceTo(point) > doc.Application.ShortCurveTolerance / 10.0)
                        points.Add(point);
                if (points.Count > 1 && points[0].DistanceTo(points[points.Count - 1]) <= doc.Application.ShortCurveTolerance / 10.0)
                    points.RemoveAt(points.Count - 1);
                if (points.Count >= 3) loops.Add(points);
            }
            if (loops.Count == 0) throw new InvalidOperationException("Không đọc được biên sàn.");
            return new PlanData { TopZ = face.Origin.Z, Loops = loops };
        }

        private static List<ReviewRow> ReviewModules(IList<FamilyInstance> modules, Floor slab, PlanData plan)
        {
            var bounds = modules.ToDictionary(instance => instance.Id.Value, instance => GetWorldBounds(instance.get_BoundingBox(null)));
            return modules.Select(instance =>
            {
                XYZ point = GetCenter(instance);
                bool inside = point != null && PointInside(plan.Loops, point.X, point.Y);
                bool hosted = instance.Host != null && instance.Host.Id == slab.Id;
                WorldBounds box = bounds[instance.Id.Value];
                string vertical = box == null ? "không có bbox" :
                    (box.MinZ < plan.TopZ - UnitUtils.ConvertToInternalUnits(100, UnitTypeId.Millimeters) ? "cao độ thấp" : "cao độ OK");
                string footprint = DescribeFootprint(box, plan.Loops);
                string overlaps = string.Join(", ", modules.Where(other => other.Id != instance.Id)
                    .Where(other => boxesOverlapInPlan(box, bounds[other.Id.Value]))
                    .Select(other => other.Id.Value.ToString(CultureInfo.InvariantCulture)));
                string status = (inside ? "tâm trong biên" : "tâm ngoài biên") + "; footprint " + footprint +
                    (string.IsNullOrEmpty(overlaps) ? "" : "; bbox chồng 3D với module " + overlaps) +
                    "; " + (hosted ? "host đúng" : "host khác/không host") + "; " + vertical;
                return new ReviewRow
                {
                    Kind = "Family module",
                    Id = instance.Id.Value.ToString(CultureInfo.InvariantCulture),
                    Type = instance.Symbol == null ? instance.Name : instance.Symbol.FamilyName + " : " + instance.Symbol.Name,
                    Level = instance.LevelId == ElementId.InvalidElementId ? "—" : instance.Document.GetElement(instance.LevelId)?.Name ?? "—",
                    Position = point == null ? "—" : UnitUtils.ConvertFromInternalUnits(point.X, UnitTypeId.Millimeters).ToString("0", CultureInfo.CurrentCulture) + ", " +
                        UnitUtils.ConvertFromInternalUnits(point.Y, UnitTypeId.Millimeters).ToString("0", CultureInfo.CurrentCulture),
                    Result = status
                };
            }).ToList();
        }

        private static string DescribeFootprint(WorldBounds box, IList<List<XYZ>> loops)
        {
            if (box == null) return "không có bbox";
            var corners = new[]
            {
                new XYZ(box.MinX, box.MinY, 0), new XYZ(box.MinX, box.MaxY, 0),
                new XYZ(box.MaxX, box.MinY, 0), new XYZ(box.MaxX, box.MaxY, 0)
            };
            return corners.All(point => PointInside(loops, point.X, point.Y))
                ? "bbox nằm trong biên sàn"
                : "bbox có thể vượt mép/lỗ sàn";
        }

        private static bool boxesOverlapInPlan(WorldBounds first, WorldBounds second)
        {
            if (first == null || second == null) return false;
            double tolerance = UnitUtils.ConvertToInternalUnits(1, UnitTypeId.Millimeters);
            return first.MaxX > second.MinX + tolerance && second.MaxX > first.MinX + tolerance &&
                first.MaxY > second.MinY + tolerance && second.MaxY > first.MinY + tolerance &&
                first.MaxZ > second.MinZ + tolerance && second.MaxZ > first.MinZ + tolerance;
        }

        private static WorldBounds GetWorldBounds(BoundingBoxXYZ box)
        {
            if (box == null) return null;
            Transform transform = box.Transform ?? Transform.Identity;
            XYZ[] corners = new[]
            {
                new XYZ(box.Min.X, box.Min.Y, box.Min.Z), new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Min.X, box.Max.Y, box.Min.Z), new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
                new XYZ(box.Max.X, box.Min.Y, box.Min.Z), new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Max.X, box.Max.Y, box.Min.Z), new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
            }.Select(transform.OfPoint).ToArray();
            return new WorldBounds
            {
                MinX = corners.Min(point => point.X), MinY = corners.Min(point => point.Y), MinZ = corners.Min(point => point.Z),
                MaxX = corners.Max(point => point.X), MaxY = corners.Max(point => point.Y), MaxZ = corners.Max(point => point.Z)
            };
        }

        private static List<ReviewRow> ReviewDropPanels(Document doc, Floor slab, PlanData plan)
        {
            var panels = new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>()
                .Where(panel => panel.Id != slab.Id && ReadString(panel, BuiltInParameter.ALL_MODEL_MARK).StartsWith("KSTR_DP_", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var rows = new List<ReviewRow>();
            foreach (Floor panel in panels)
            {
                BoundingBoxXYZ box = panel.get_BoundingBox(null);
                if (box == null) continue;
                XYZ center = (box.Min + box.Max) / 2.0;
                if (Math.Abs(box.Max.Z - plan.TopZ) > UnitUtils.ConvertToInternalUnits(5, UnitTypeId.Millimeters) ||
                    !PointInside(plan.Loops, center.X, center.Y)) continue;
                bool joined = false;
                try { joined = JoinGeometryUtils.AreElementsJoined(doc, slab, panel); } catch { }
                FloorType type = doc.GetElement(panel.GetTypeId()) as FloorType;
                double thickness = type?.GetCompoundStructure()?.GetWidth() ?? 0;
                string mark = ReadString(panel, BuiltInParameter.ALL_MODEL_MARK);
                string columnInfo = ReadLinkedColumn(doc, mark);
                rows.Add(new ReviewRow
                {
                    Kind = "Drop panel",
                    Id = panel.Id.Value.ToString(CultureInfo.InvariantCulture),
                    Type = type?.Name ?? panel.Name,
                    Level = panel.LevelId == ElementId.InvalidElementId ? "—" : doc.GetElement(panel.LevelId)?.Name ?? "—",
                    Position = UnitUtils.ConvertFromInternalUnits(center.X, UnitTypeId.Millimeters).ToString("0", CultureInfo.CurrentCulture) + ", " +
                        UnitUtils.ConvertFromInternalUnits(center.Y, UnitTypeId.Millimeters).ToString("0", CultureInfo.CurrentCulture) +
                        " | dày " + UnitUtils.ConvertFromInternalUnits(thickness, UnitTypeId.Millimeters).ToString("0", CultureInfo.CurrentCulture) + " mm",
                    Result = (joined ? "đã join với sàn" : "chưa join với sàn") + "; " + columnInfo
                        + "; " + DescribeDropRebar(doc, panel)
                });
            }
            return rows;
        }

        private static string ReadLinkedColumn(Document doc, string panelMark)
        {
            const string prefix = "KSTR_DP_";
            if (string.IsNullOrWhiteSpace(panelMark) || !panelMark.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return "Mark panel không hợp lệ";
            string value = panelMark.Substring(prefix.Length);
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
                return "không đọc được ElementId cột";
            FamilyInstance column = doc.GetElement(new ElementId(id)) as FamilyInstance;
            return column == null
                ? "không tìm thấy cột liên kết (Id " + value + ")"
                : "cột " + column.Id.Value.ToString(CultureInfo.InvariantCulture) + " / " + (column.Symbol?.FamilyName ?? column.Name) + " : " + (column.Symbol?.Name ?? "");
        }

        private static string DescribeDropRebar(Document doc, Floor panel)
        {
            List<Rebar> bars = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
                .Where(bar => bar.GetHostId() == panel.Id).ToList();
            string ownedPrefix = "KSTR_DROPSTEEL_" + panel.Id.Value.ToString(CultureInfo.InvariantCulture) + "_";
            int owned = bars.Count(bar => ReadString(bar, BuiltInParameter.NUMBER_PARTITION_PARAM)
                .StartsWith(ownedPrefix, StringComparison.OrdinalIgnoreCase));
            string ownedLayers = string.Join(", ", bars
                .Select(bar => new { Bar = bar, Partition = ReadString(bar, BuiltInParameter.NUMBER_PARTITION_PARAM) })
                .Where(item => item.Partition.StartsWith(ownedPrefix, StringComparison.OrdinalIgnoreCase))
                .GroupBy(item => item.Partition.Substring(ownedPrefix.Length), StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Key + " ×" + group.Count()));
            string types = string.Join(", ", bars.GroupBy(bar =>
            {
                RebarBarType barType = doc.GetElement(bar.GetTypeId()) as RebarBarType;
                if (barType == null) return "Unknown";
                double diameter = UnitUtils.ConvertFromInternalUnits(barType.BarNominalDiameter, UnitTypeId.Millimeters);
                return barType.Name + " Ø" + diameter.ToString("0.##", CultureInfo.CurrentCulture);
            }).OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(group => group.Key + " ×" + group.Count()));
            string summary = "Rebar element " + bars.Count + " (KStruct " + owned + ", khác " + (bars.Count - owned) + ")";
            if (!string.IsNullOrEmpty(ownedLayers)) summary += "; lớp KStruct: " + ownedLayers;
            else summary += "; chưa có lớp thép KStruct";
            return string.IsNullOrEmpty(types) ? summary : summary + "; loại: " + types;
        }

        private static XYZ GetCenter(FamilyInstance instance)
        {
            LocationPoint location = instance.Location as LocationPoint;
            if (location != null) return location.Point;
            BoundingBoxXYZ box = instance.get_BoundingBox(null);
            return box == null ? null : (box.Min + box.Max) / 2.0;
        }

        private static bool PointInside(IList<List<XYZ>> loops, double x, double y)
        {
            bool inside = false;
            foreach (List<XYZ> loop in loops)
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
            {
                XYZ a = loop[i], b = loop[j];
                if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        private static string ReadString(Element element, BuiltInParameter parameter)
        {
            Parameter value = element.get_Parameter(parameter);
            return value != null && value.HasValue ? value.AsString() ?? string.Empty : string.Empty;
        }

        private static bool IsColumn(FamilyInstance instance)
        {
            return instance?.Category != null &&
                (instance.Category.Id.Value == (long)BuiltInCategory.OST_StructuralColumns ||
                 instance.Category.Id.Value == (long)BuiltInCategory.OST_Columns);
        }

        private static void ShowReport(IntPtr owner, IList<ReviewRow> rows, Floor slab, int moduleCount)
        {
            var window = new Window
            {
                Title = "Review module/drop panel - KStruct", Width = 900, Height = 520,
                MinWidth = 720, MinHeight = 400, WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var root = new DockPanel { Margin = new Thickness(12) };
            var footer = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(footer, Dock.Bottom);
            footer.Children.Add(new TextBlock
            {
                Text = "Sàn " + slab.Id.Value + " | " + moduleCount + " family module được review | " +
                    rows.Count(row => row.Kind == "Drop panel") + " drop panel KStruct. Đây là kiểm tra hình học/host, không phải kiểm toán kết cấu.",
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0)
            });
            var export = new Button { Content = "Xuất CSV", Width = 84, Margin = new Thickness(0, 0, 8, 0) };
            var close = new Button { Content = "Đóng", Width = 72, IsCancel = true };
            footer.Children.Add(export); footer.Children.Add(close); root.Children.Add(footer);
            var list = new ListView { ItemsSource = rows };
            var grid = new GridView();
            AddColumn(grid, "Nhóm", "Kind", 100); AddColumn(grid, "Element ID", "Id", 90);
            AddColumn(grid, "Family/Type", "Type", 190); AddColumn(grid, "Level", "Level", 100);
            AddColumn(grid, "Vị trí mm", "Position", 150); AddColumn(grid, "Kết quả", "Result", 360);
            list.View = grid; root.Children.Add(list); window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            export.Click += (sender, args) =>
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Xuất review module sàn", Filter = "CSV UTF-8 (*.csv)|*.csv", FileName = "KStruct_SlabModuleReview.csv",
                    AddExtension = true, DefaultExt = ".csv", OverwritePrompt = true
                };
                if (dialog.ShowDialog(window) != true) return;
                try
                {
                    var csv = new StringBuilder("Kind,ElementId,Type,Level,Position,Result\r\n");
                    foreach (ReviewRow row in rows)
                        csv.Append(Csv(row.Kind)).Append(',').Append(Csv(row.Id)).Append(',').Append(Csv(row.Type)).Append(',')
                            .Append(Csv(row.Level)).Append(',').Append(Csv(row.Position)).Append(',').Append(Csv(row.Result)).Append("\r\n");
                    System.IO.File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
                    TaskDialog.Show("KStruct - Review module sàn", "Đã xuất " + rows.Count + " dòng review.");
                }
                catch (Exception ex) { TaskDialog.Show("KStruct - Review module sàn", ex.Message); }
            };
            window.ShowDialog();
        }

        private static void AddColumn(GridView view, string title, string path, double width)
        {
            view.Columns.Add(new GridViewColumn { Header = title, Width = width, DisplayMemberBinding = new System.Windows.Data.Binding(path) });
        }

        private static string Csv(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
    }
}
