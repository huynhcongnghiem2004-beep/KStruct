using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CopyBasicWallsToLevelsCommand : IExternalCommand
    {
        private sealed class WallFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Wall;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class LevelChoice
        {
            public Level Level { get; }
            public string Label { get; }
            public LevelChoice(Level level) { Level = level; Label = level.Name + " | " + UnitUtils.ConvertFromInternalUnits(level.Elevation, UnitTypeId.Millimeters).ToString("0.##") + " mm"; }
            public override string ToString() => Label;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Wall> walls = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Wall>().Distinct().ToList();
                if (walls.Count == 0)
                    walls = ui.Selection.PickObjects(ObjectType.Element, new WallFilter(), "Chọn các tường cơ bản thẳng cần sao chép sang Level khác")
                        .Select(reference => doc.GetElement(reference)).OfType<Wall>().Distinct().ToList();
                if (walls.Count == 0) return Result.Cancelled;

                List<Level> levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(level => level.Elevation).ThenBy(level => level.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                List<Level> targets = ChooseLevels(data.Application.MainWindowHandle, levels);
                if (targets == null || targets.Count == 0) return Result.Cancelled;

                int created = 0, skipped = 0;
                var details = new List<string>();
                using (Transaction transaction = new Transaction(doc, "KStruct - Sao chép tường sang Level"))
                {
                    transaction.Start();
                    foreach (Wall wall in walls)
                    {
                        try
                        {
                            ValidateSourceWall(doc, wall);
                            ElementId baseId = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT).AsElementId();
                            Level sourceLevel = doc.GetElement(baseId) as Level;
                            if (sourceLevel == null) throw new InvalidOperationException("Không xác định được Base Constraint.");
                            Line sourceLine = ((LocationCurve)wall.Location).Curve as Line;
                            double baseOffset = ReadDouble(wall, BuiltInParameter.WALL_BASE_OFFSET, 0);
                            double topOffset = ReadDouble(wall, BuiltInParameter.WALL_TOP_OFFSET, 0);
                            ElementId topId = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.AsElementId() ?? ElementId.InvalidElementId;
                            Level sourceTop = doc.GetElement(topId) as Level;
                            double height = sourceTop != null
                                ? sourceTop.Elevation + topOffset - sourceLevel.Elevation - baseOffset
                                : ReadDouble(wall, BuiltInParameter.WALL_USER_HEIGHT_PARAM, 0);
                            if (height <= doc.Application.ShortCurveTolerance)
                                throw new InvalidOperationException("Chiều cao tường không hợp lệ.");

                            bool structural = ReadInteger(wall, BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT, 0) != 0;
                            foreach (Level target in targets.Where(level => level.Id != sourceLevel.Id))
                            {
                                using (SubTransaction sub = new SubTransaction(doc))
                                {
                                    sub.Start();
                                    try
                                    {
                                        double dz = target.Elevation - sourceLevel.Elevation;
                                        Curve translated = sourceLine.CreateTransformed(Transform.CreateTranslation(XYZ.BasisZ * dz));
                                        Wall copy = Wall.Create(doc, translated, wall.WallType.Id, target.Id, height, baseOffset, wall.Flipped, structural);
                                        if (copy == null) throw new InvalidOperationException("Revit không tạo được tường mới.");
                                        CopyTextParameter(wall, copy, BuiltInParameter.ALL_MODEL_MARK);
                                        CopyTextParameter(wall, copy, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                                        if (sourceTop != null)
                                        {
                                            Level targetTop = levels.Where(level => level.Id != target.Id)
                                                .OrderBy(level => Math.Abs(level.Elevation - (target.Elevation + sourceTop.Elevation - sourceLevel.Elevation)))
                                                .FirstOrDefault(level => Math.Abs(level.Elevation - (target.Elevation + sourceTop.Elevation - sourceLevel.Elevation))
                                                    <= UnitUtils.ConvertToInternalUnits(2, UnitTypeId.Millimeters));
                                            if (targetTop != null)
                                            {
                                                Parameter topConstraint = copy.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
                                                Parameter newTopOffset = copy.get_Parameter(BuiltInParameter.WALL_TOP_OFFSET);
                                                if (topConstraint != null && !topConstraint.IsReadOnly) topConstraint.Set(targetTop.Id);
                                                if (newTopOffset != null && !newTopOffset.IsReadOnly) newTopOffset.Set(topOffset);
                                            }
                                        }
                                        if (sub.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không chấp nhận bản sao.");
                                        created++;
                                    }
                                    catch (Exception ex)
                                    {
                                        if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                        skipped++;
                                        if (details.Count < 8) details.Add("Tường " + wall.Id.Value + " → " + target.Name + ": " + ex.Message);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            skipped++;
                            if (details.Count < 8) details.Add("Tường " + wall.Id.Value + ": " + ex.Message);
                        }
                    }
                    if (created == 0)
                    {
                        transaction.RollBack();
                        TaskDialog.Show("KStruct - Sao chép tường", "Không tạo được bản sao nào.\n" + string.Join("\n", details));
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các tường mới.");
                }

                string report = "Đã tạo " + created + " tường cơ bản trên " + targets.Count + " Level đích.";
                if (skipped > 0) report += "\nBỏ qua " + skipped + " trường hợp:\n" + string.Join("\n", details);
                report += "\nLệnh chỉ xử lý Basic Wall thẳng, đứng và không có cửa/lỗ mở hoặc embedded insert.";
                TaskDialog.Show("KStruct - Sao chép tường", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Sao chép tường", ex.Message);
                return Result.Failed;
            }
        }

        private static void ValidateSourceWall(Document doc, Wall wall)
        {
            if (wall.WallType == null || wall.WallType.Kind != WallKind.Basic)
                throw new InvalidOperationException("Chỉ hỗ trợ Basic Wall.");
            LocationCurve location = wall.Location as LocationCurve;
            Line line = location?.Curve as Line;
            if (line == null || Math.Abs(line.Direction.Normalize().DotProduct(XYZ.BasisZ)) > 1e-6)
                throw new InvalidOperationException("Chỉ hỗ trợ tường thẳng đứng có trục ngang.");
            IList<ElementId> inserts = wall.FindInserts(true, true, true, true);
            if (inserts != null && inserts.Count > 0)
                throw new InvalidOperationException("Tường có cửa/lỗ mở hoặc insert; cần sao chép insert riêng để giữ liên kết host.");
            if (wall.Pinned) throw new InvalidOperationException("Bỏ ghim tường trước khi sao chép.");
        }

        private static double ReadDouble(Element element, BuiltInParameter parameter, double fallback)
        {
            Parameter value = element.get_Parameter(parameter);
            return value != null && value.HasValue ? value.AsDouble() : fallback;
        }

        private static int ReadInteger(Element element, BuiltInParameter parameter, int fallback)
        {
            Parameter value = element.get_Parameter(parameter);
            return value != null && value.HasValue ? value.AsInteger() : fallback;
        }

        private static void CopyTextParameter(Element source, Element target, BuiltInParameter parameter)
        {
            Parameter from = source.get_Parameter(parameter), to = target.get_Parameter(parameter);
            if (from != null && to != null && !to.IsReadOnly && from.StorageType == StorageType.String && from.HasValue)
                to.Set(from.AsString());
        }

        private static List<Level> ChooseLevels(IntPtr owner, IList<Level> levels)
        {
            var choices = levels.Select(level => new LevelChoice(level)).ToList();
            var window = new Window
            {
                Title = "Chọn Level đích - KStruct", Width = 390, Height = 440,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false
            };
            var root = new DockPanel { Margin = new Thickness(12) };
            var footer = new StackPanel();
            DockPanel.SetDock(footer, Dock.Bottom);
            footer.Children.Add(new TextBlock
            {
                Text = "Chỉ sao chép Basic Wall thẳng không có insert. Chọn một hoặc nhiều Level đích.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Sao chép", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            buttons.Children.Add(ok); buttons.Children.Add(cancel); footer.Children.Add(buttons); root.Children.Add(footer);
            var list = new ListBox { ItemsSource = choices, SelectionMode = SelectionMode.Extended };
            root.Children.Add(list);
            new WindowInteropHelper(window).Owner = owner;
            window.Content = root;
            ok.Click += (sender, args) =>
            {
                if (list.SelectedItems.Count == 0)
                {
                    TaskDialog.Show("KStruct - Sao chép tường", "Chọn ít nhất một Level đích.");
                    return;
                }
                window.DialogResult = true;
            };
            return window.ShowDialog() == true
                ? list.SelectedItems.Cast<LevelChoice>().Select(choice => choice.Level).Distinct().ToList()
                : null;
        }
    }
}
