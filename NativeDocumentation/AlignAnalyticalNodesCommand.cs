using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class AlignAnalyticalNodesCommand : IExternalCommand
    {
        private sealed class Options
        {
            public bool[] Axes;
            public string AxisName;
            public double ToleranceMeters;
        }

        private sealed class OriginRow
        {
            public ReferencePoint Node { get; set; }
            public long NodeId { get; set; }
            public double OriginalX { get; set; }
            public double OriginalY { get; set; }
            public double OriginalZ { get; set; }
            public double NewX { get; set; }
            public double NewY { get; set; }
            public double NewZ { get; set; }
        }

        private sealed class DataRow
        {
            public ReferencePoint Node { get; set; }
            public long NodeId { get; set; }
            public double OriginalX { get; set; }
            public double OriginalY { get; set; }
            public double OriginalZ { get; set; }
            public double DistanceToOriginMeters { get; set; }
            public double NewX { get; set; }
            public double NewY { get; set; }
            public double NewZ { get; set; }
            public string Status { get; set; }
            public bool Apply { get; set; }
        }

        private sealed class AnalyticalNodeFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => IsAnalyticalNode(element as ReferencePoint);
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<ReferencePoint> preselected = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .OfType<ReferencePoint>().Where(IsAnalyticalNode).Distinct().ToList();
                Options options = ChooseOptions(data.Application.MainWindowHandle);
                if (options == null) return Result.Cancelled;

                List<ReferencePoint> origins = preselected.Count > 0 ? preselected : PickNodes(ui,
                    "Chọn các Analytical Node làm mốc rồi nhấn Finish");
                if (origins.Count == 0) return Result.Cancelled;
                List<OriginRow> originRows = origins.Select(node => new OriginRow
                {
                    Node = node, NodeId = node.Id.Value,
                    OriginalX = ToMeters(node.Position.X), OriginalY = ToMeters(node.Position.Y), OriginalZ = ToMeters(node.Position.Z),
                    NewX = ToMeters(node.Position.X), NewY = ToMeters(node.Position.Y), NewZ = ToMeters(node.Position.Z)
                }).OrderBy(row => row.OriginalX).ThenBy(row => row.OriginalY).ThenBy(row => row.OriginalZ).ThenBy(row => row.NodeId).ToList();
                if (!EditOrigins(data.Application.MainWindowHandle, options, originRows)) return Result.Cancelled;
                if (originRows.Count == 0) return Result.Cancelled;

                List<ReferencePoint> imported = PickNodes(ui,
                    "Chọn các Analytical Node cần căn theo mốc rồi nhấn Finish");
                if (imported.Count == 0) return Result.Cancelled;
                List<DataRow> rows = BuildRows(imported, originRows, options);
                if (!ReviewRows(data.Application.MainWindowHandle, options, rows)) return Result.Cancelled;

                List<DataRow> changes = rows.Where(row => row.Apply && row.Status == "Khớp" && HasChanged(row, options)).ToList();
                if (changes.Count == 0)
                {
                    TaskDialog.Show("KStruct - Coordinates", "Không có node nào được chọn để dịch chuyển.");
                    return Result.Cancelled;
                }
                TaskDialogResult confirm = TaskDialog.Show("KStruct - Xác nhận căn node",
                    "Dịch " + changes.Count + " Analytical Node theo các trục " + options.AxisName + "? Revit có thể cập nhật vị trí các đầu Analytical Member/Panel nối với node. Kiểm tra bảng dữ liệu và lưu một bản sao trước khi áp dụng.",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);
                if (confirm != TaskDialogResult.Yes) return Result.Cancelled;

                int moved = 0, failed = 0;
                using (Transaction tx = new Transaction(doc, "KStruct - Căn Analytical Nodes"))
                {
                    tx.Start();
                    foreach (DataRow row in changes)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                XYZ old = row.Node.Position;
                                XYZ target = WithCoordinates(old, row, options);
                                row.Node.Position = target;
                                doc.Regenerate();
                                if (row.Node.Position.DistanceTo(target) > UnitUtils.ConvertToInternalUnits(1, UnitTypeId.Millimeters))
                                    throw new InvalidOperationException("Revit không đặt node về đúng tọa độ yêu cầu.");
                                if (sub.Commit() == TransactionStatus.Committed) moved++;
                                else throw new InvalidOperationException("Revit không chấp nhận thay đổi node.");
                            }
                            catch
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                failed++;
                            }
                        }
                    }
                    if (moved == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Coordinates", "Revit không chấp nhận thay đổi nào.");
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận căn Analytical Node.");
                }

                string report = "Đã căn " + moved + "/" + changes.Count + " Analytical Node theo các trục " + options.AxisName + ".";
                if (failed > 0) report += " Không dịch được " + failed + " node.";
                TaskDialog.Show("KStruct - Coordinates", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Coordinates", ex.Message);
                return Result.Failed;
            }
        }

        private static bool IsAnalyticalNode(ReferencePoint point)
        {
            if (point == null || !point.IsValidObject) return false;
            try
            {
                using (AnalyticalNodeData data = AnalyticalNodeData.GetAnalyticalNodeData(point))
                    return data != null && data.IsValidObject;
            }
            catch { return false; }
        }

        private static List<ReferencePoint> PickNodes(UIDocument ui, string prompt)
        {
            IList<Reference> references = ui.Selection.PickObjects(ObjectType.Element, new AnalyticalNodeFilter(), prompt);
            return references.Select(reference => ui.Document.GetElement(reference)).OfType<ReferencePoint>()
                .Where(IsAnalyticalNode).Distinct().ToList();
        }

        private static List<DataRow> BuildRows(IList<ReferencePoint> imported, IList<OriginRow> origins, Options options)
        {
            double tieTolerance = UnitUtils.ConvertToInternalUnits(1, UnitTypeId.Millimeters);
            var result = new List<DataRow>();
            foreach (ReferencePoint node in imported)
            {
                XYZ position = node.Position;
                double x = ToMeters(position.X), y = ToMeters(position.Y), z = ToMeters(position.Z);
                List<Tuple<OriginRow, double>> distances = origins.Select(origin =>
                    Tuple.Create(origin, DistanceMeters(x, y, z, origin, options.Axes))).OrderBy(item => item.Item2).ToList();
                bool ambiguous = distances.Count > 1 && Math.Abs(distances[0].Item2 - distances[1].Item2) <= ToMeters(tieTolerance);
                bool matched = distances.Count > 0 && distances[0].Item2 <= options.ToleranceMeters && !ambiguous;
                OriginRow origin = distances.Count == 0 ? null : distances[0].Item1;
                result.Add(new DataRow
                {
                    Node = node, NodeId = node.Id.Value, OriginalX = x, OriginalY = y, OriginalZ = z,
                    DistanceToOriginMeters = origin == null ? double.NaN : distances[0].Item2,
                    NewX = matched && options.Axes[0] ? origin.NewX : x,
                    NewY = matched && options.Axes[1] ? origin.NewY : y,
                    NewZ = matched && options.Axes[2] ? origin.NewZ : z,
                    Status = ambiguous ? "Mơ hồ: hai mốc gần như bằng nhau" : matched ? "Khớp" : "Ngoài tolerance",
                    Apply = matched
                });
            }
            return result.OrderBy(row => row.Status == "Khớp" ? 0 : 1).ThenBy(row => row.NodeId).ToList();
        }

        private static double DistanceMeters(double x, double y, double z, OriginRow origin, bool[] axes)
        {
            double dx = axes[0] ? x - origin.OriginalX : 0;
            double dy = axes[1] ? y - origin.OriginalY : 0;
            double dz = axes[2] ? z - origin.OriginalZ : 0;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static XYZ WithCoordinates(XYZ point, DataRow row, Options options) => new XYZ(
            options.Axes[0] ? FromMeters(row.NewX) : point.X,
            options.Axes[1] ? FromMeters(row.NewY) : point.Y,
            options.Axes[2] ? FromMeters(row.NewZ) : point.Z);

        private static bool HasChanged(DataRow row, Options options) =>
            options.Axes[0] && Math.Abs(row.NewX - row.OriginalX) > 0.0001 ||
            options.Axes[1] && Math.Abs(row.NewY - row.OriginalY) > 0.0001 ||
            options.Axes[2] && Math.Abs(row.NewZ - row.OriginalZ) > 0.0001;

        private static double ToMeters(double internalValue) => UnitUtils.ConvertFromInternalUnits(internalValue, UnitTypeId.Meters);
        private static double FromMeters(double meters) => UnitUtils.ConvertToInternalUnits(meters, UnitTypeId.Meters);

        private static Options ChooseOptions(IntPtr owner)
        {
            var window = new Window
            {
                Title = "Coordinates - KStruct", Width = 410, Height = 230,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock { Text = "Chọn các trục tọa độ cần căn (có thể chọn nhiều trục):" });
            var axes = new[]
            {
                new CheckBox { Content = "X", IsChecked = true, Margin = new Thickness(0, 4, 10, 2) },
                new CheckBox { Content = "Y", IsChecked = false, Margin = new Thickness(0, 4, 10, 2) },
                new CheckBox { Content = "Z", IsChecked = false, Margin = new Thickness(0, 4, 10, 2) }
            };
            var axisPanel = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (CheckBox checkBox in axes) axisPanel.Children.Add(checkBox);
            root.Children.Add(axisPanel);
            System.Windows.Controls.TextBox tolerance = AddInput(root, "Tolerance (m):", "0.5");
            root.Children.Add(new TextBlock
            {
                Text = "Luồng: chọn trục → Get mốc và nhập tọa độ đích → Import node cần căn → kiểm tra bảng → Ok. Có thể căn đồng thời X/Y/Z; không thay Shared Coordinates.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 4)
            });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Bắt đầu", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) =>
            {
                double value;
                if (!axes.Any(axisCheck => axisCheck.IsChecked == true) ||
                    !double.TryParse(tolerance.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) || value <= 0)
                {
                    TaskDialog.Show("KStruct - Coordinates", "Chọn ít nhất một trục và nhập Tolerance lớn hơn 0 mét.");
                    return;
                }
                window.DialogResult = true;
            };
            buttons.Children.Add(ok); buttons.Children.Add(cancel); root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            if (window.ShowDialog() != true) return null;
            return new Options
            {
                Axes = axes.Select(axisCheck => axisCheck.IsChecked == true).ToArray(),
                AxisName = string.Join("/", axes.Select((axisCheck, index) => new { axisCheck, index })
                    .Where(item => item.axisCheck.IsChecked == true).Select(item => new[] { "X", "Y", "Z" }[item.index])),
                ToleranceMeters = double.Parse(tolerance.Text, CultureInfo.CurrentCulture)
            };
        }

        private static bool EditOrigins(IntPtr owner, Options options, List<OriginRow> rows)
        {
            var window = new Window
            {
                Title = "Coordinates - Origin Nodes / " + options.AxisName, Width = 650, Height = 430,
                MinWidth = 600, MinHeight = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false
            };
            var root = new DockPanel { Margin = new Thickness(10) };
            var hint = new TextBlock
            {
                Text = "Sửa New X/Y/Z (m) cho các trục đã chọn. Delete chỉ bỏ mốc khỏi bảng.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            };
            DockPanel.SetDock(hint, Dock.Top); root.Children.Add(hint);
            var grid = new DataGrid
            {
                ItemsSource = rows, AutoGenerateColumns = false, CanUserAddRows = false,
                SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow,
                IsReadOnly = false, Margin = new Thickness(0, 0, 0, 8)
            };
            grid.Columns.Add(TextColumn("Node ID", "NodeId", true));
            grid.Columns.Add(TextColumn("X (m)", "OriginalX", true));
            grid.Columns.Add(TextColumn("Y (m)", "OriginalY", true));
            grid.Columns.Add(TextColumn("Z (m)", "OriginalZ", true));
            grid.Columns.Add(TextColumn("New X (m)", "NewX", !options.Axes[0]));
            grid.Columns.Add(TextColumn("New Y (m)", "NewY", !options.Axes[1]));
            grid.Columns.Add(TextColumn("New Z (m)", "NewZ", !options.Axes[2]));
            DockPanel.SetDock(grid, Dock.Top); root.Children.Add(grid);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var remove = new Button { Content = "Delete mốc", Width = 100, Margin = new Thickness(0, 0, 8, 0) };
            var proceed = new Button { Content = "Import node", Width = 100, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var close = new Button { Content = "Close", Width = 72, IsCancel = true };
            remove.Click += (sender, args) =>
            {
                foreach (OriginRow row in grid.SelectedItems.Cast<OriginRow>().ToList()) rows.Remove(row);
                grid.Items.Refresh();
            };
            proceed.Click += (sender, args) =>
            {
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
                if (rows.Count == 0 || rows.Any(row => Invalid(row.NewX) || Invalid(row.NewY) || Invalid(row.NewZ)))
                {
                    TaskDialog.Show("KStruct - Coordinates", "Cần ít nhất một mốc và các tọa độ New hợp lệ.");
                    return;
                }
                window.DialogResult = true;
            };
            buttons.Children.Add(remove); buttons.Children.Add(proceed); buttons.Children.Add(close);
            DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            return window.ShowDialog() == true;
        }

        private static bool ReviewRows(IntPtr owner, Options options, List<DataRow> rows)
        {
            var window = new Window
            {
                Title = "Coordinates - Data / " + options.AxisName, Width = 740, Height = 470,
                MinWidth = 650, MinHeight = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false
            };
            var root = new DockPanel { Margin = new Thickness(10) };
            var hint = new TextBlock
            {
                Text = "Khoảng cách được tính trên các trục đã chọn. Sửa New X/Y/Z trực tiếp để điều chỉnh; bỏ chọn Apply để giữ nguyên node. Hàng ngoài tolerance/mơ hồ không được tự căn.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
            };
            DockPanel.SetDock(hint, Dock.Top); root.Children.Add(hint);
            var grid = new DataGrid
            {
                ItemsSource = rows, AutoGenerateColumns = false, CanUserAddRows = false,
                SelectionMode = DataGridSelectionMode.Extended, SelectionUnit = DataGridSelectionUnit.FullRow,
                IsReadOnly = false, Margin = new Thickness(0, 0, 0, 8)
            };
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Apply", Binding = new System.Windows.Data.Binding("Apply"), Width = 55 });
            grid.Columns.Add(TextColumn("Node ID", "NodeId", true));
            grid.Columns.Add(TextColumn("Original X (m)", "OriginalX", true));
            grid.Columns.Add(TextColumn("Original Y (m)", "OriginalY", true));
            grid.Columns.Add(TextColumn("Original Z (m)", "OriginalZ", true));
            grid.Columns.Add(TextColumn("Distance (m)", "DistanceToOriginMeters", true));
            grid.Columns.Add(TextColumn("New X (m)", "NewX", !options.Axes[0]));
            grid.Columns.Add(TextColumn("New Y (m)", "NewY", !options.Axes[1]));
            grid.Columns.Add(TextColumn("New Z (m)", "NewZ", !options.Axes[2]));
            grid.Columns.Add(TextColumn("Status", "Status", true));
            DockPanel.SetDock(grid, Dock.Top); root.Children.Add(grid);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var view = new Button { Content = "View node", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
            var ok = new Button { Content = "Ok", Width = 72, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var close = new Button { Content = "Close", Width = 72, IsCancel = true };
            view.Click += (sender, args) =>
            {
                DataRow row = grid.SelectedItem as DataRow;
                if (row == null) return;
                try
                {
                    UIDocument uidoc = new UIDocument(row.Node.Document);
                    uidoc.ShowElements(row.Node.Id);
                }
                catch { }
            };
            ok.Click += (sender, args) =>
            {
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
                window.DialogResult = true;
            };
            buttons.Children.Add(view); buttons.Children.Add(ok); buttons.Children.Add(close);
            DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            return window.ShowDialog() == true;
        }

        private static DataGridTextColumn TextColumn(string title, string path, bool readOnly) =>
            new DataGridTextColumn { Header = title, Binding = new System.Windows.Data.Binding(path), IsReadOnly = readOnly, Width = new DataGridLength(1, DataGridLengthUnitType.Star) };

        private static bool Invalid(double value) => double.IsNaN(value) || double.IsInfinity(value);

        private static System.Windows.Controls.TextBox AddInput(System.Windows.Controls.Panel root, string label, string value)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 5, 0, 2) });
            var box = new System.Windows.Controls.TextBox { Text = value, MinWidth = 355, MinHeight = 22 };
            root.Children.Add(box);
            return box;
        }
    }
}
