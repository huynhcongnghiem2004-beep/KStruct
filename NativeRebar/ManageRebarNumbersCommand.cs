using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ManageRebarNumbersCommand : IExternalCommand
    {
        private sealed class RebarFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Rebar;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class RebarNumberRow : INotifyPropertyChanged
        {
            private string _mark;
            public Rebar Bar { get; private set; }
            public string Partition { get; private set; }
            public string Host { get; private set; }
            public string BarType { get; private set; }
            public double LengthMm { get; private set; }
            public string Mark
            {
                get => _mark;
                set
                {
                    if (_mark == value) return;
                    _mark = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Mark)));
                }
            }
            public event PropertyChangedEventHandler PropertyChanged;

            public RebarNumberRow(Rebar bar, string partition, string host, string type, double lengthMm)
            {
                Bar = bar;
                Partition = partition;
                Host = host;
                BarType = type;
                LengthMm = lengthMm;
                Mark = bar.ScheduleMark ?? string.Empty;
            }
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                var selected = ui.Selection.GetElementIds().Select(doc.GetElement).Where(x => x != null).ToList();
                if (selected.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element,
                        "Chọn các cấu kiện hoặc thanh Rebar cần quản lý mã");
                    selected = picked.Select(doc.GetElement).Where(x => x != null).ToList();
                }
                HashSet<ElementId> hostIds = new HashSet<ElementId>(selected.Where(x => !(x is Rebar)).Select(x => x.Id));
                List<Rebar> directBars = selected.OfType<Rebar>().ToList();
                foreach (Rebar bar in directBars) hostIds.Add(bar.GetHostId());
                List<Rebar> bars = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).WhereElementIsNotElementType()
                    .Cast<Rebar>().Where(x => hostIds.Contains(x.GetHostId())).ToList();
                bars.AddRange(directBars.Where(x => !bars.Any(y => y.Id == x.Id)));
                bars = bars.Distinct().OrderBy(x => x.Id.Value).ToList();
                if (bars.Count == 0)
                {
                    TaskDialog.Show("KStruct - Quản lý mã thép", "Không tìm thấy Rebar trên các cấu kiện đã chọn.");
                    return Result.Cancelled;
                }

                var rows = new ObservableCollection<RebarNumberRow>(bars.Select(bar =>
                {
                    Element host = doc.GetElement(bar.GetHostId());
                    RebarBarType type = doc.GetElement(bar.GetTypeId()) as RebarBarType;
                    Parameter length = bar.get_Parameter(BuiltInParameter.REBAR_ELEM_TOTAL_LENGTH);
                    double lengthMm = length != null && length.HasValue
                        ? UnitUtils.ConvertFromInternalUnits(length.AsDouble(), UnitTypeId.Millimeters) : 0;
                    Parameter partitionParameter = bar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
                    string partition = partitionParameter == null || !partitionParameter.HasValue
                        ? "" : partitionParameter.AsString() ?? "";
                    return new RebarNumberRow(bar, partition, host == null ? "?" : host.Id.Value.ToString(),
                        type == null ? "?" : type.Name, lengthMm);
                }));

                if (!EditRows(data.Application.MainWindowHandle, doc, rows)) return Result.Cancelled;
                using (Transaction tx = new Transaction(doc, "KStruct - Quản lý mã Rebar"))
                {
                    tx.Start();
                    int changed = 0;
                    foreach (RebarNumberRow row in rows)
                    {
                        string mark = (row.Mark ?? string.Empty).Trim();
                        if (string.Equals(row.Bar.ScheduleMark, mark, StringComparison.Ordinal)) continue;
                        row.Bar.ScheduleMark = mark;
                        changed++;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các mã Rebar.");
                    TaskDialog.Show("KStruct - Quản lý mã thép", "Đã cập nhật " + changed + " mã trên " + rows.Count + " Rebar.");
                }
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Quản lý mã thép", ex.Message);
                return Result.Failed;
            }
        }

        private static bool EditRows(IntPtr owner, Document doc, ObservableCollection<RebarNumberRow> rows)
        {
            var window = new Window
            {
                Title = "Quản lý mã Rebar theo cấu kiện - KStruct",
                Width = 880,
                Height = 570,
                MinWidth = 720,
                MinHeight = 420,
                ResizeMode = ResizeMode.CanResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new DockPanel { Margin = new Thickness(10) };
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            controls.Children.Add(new TextBlock { Text = "Tiền tố:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
            var prefix = new System.Windows.Controls.TextBox { Text = "R-", Width = 75, Margin = new Thickness(0, 0, 10, 0) };
            controls.Children.Add(prefix);
            controls.Children.Add(new TextBlock { Text = "Bắt đầu:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
            var startNumber = new System.Windows.Controls.TextBox { Text = "1", Width = 55, Margin = new Thickness(0, 0, 10, 0) };
            controls.Children.Add(startNumber);
            var numberingOrder = new System.Windows.Controls.ComboBox { Width = 180, SelectedIndex = 0, Margin = new Thickness(0, 0, 8, 0) };
            numberingOrder.Items.Add("Theo chiều dài");
            numberingOrder.Items.Add("Theo loại thép");
            numberingOrder.Items.Add("Theo mã hiện tại");
            controls.Children.Add(numberingOrder);
            var numberButton = new Button { Content = "Đánh số theo Partition", Padding = new Thickness(8, 3, 8, 3) };
            controls.Children.Add(numberButton);
            DockPanel.SetDock(controls, Dock.Top);
            root.Children.Add(controls);

            var grid = new DataGrid
            {
                ItemsSource = rows,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                IsReadOnly = false,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HeadersVisibility = DataGridHeadersVisibility.Column
            };
            grid.Columns.Add(new DataGridTextColumn { Header = "Schedule Mark", Binding = new System.Windows.Data.Binding(nameof(RebarNumberRow.Mark)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Partition", Binding = new System.Windows.Data.Binding(nameof(RebarNumberRow.Partition)), IsReadOnly = true, Width = 150 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Loại thép", Binding = new System.Windows.Data.Binding(nameof(RebarNumberRow.BarType)), IsReadOnly = true, Width = 190 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Dài (mm)", Binding = new System.Windows.Data.Binding(nameof(RebarNumberRow.LengthMm)) { StringFormat = "N0" }, IsReadOnly = true, Width = 100 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Host ID", Binding = new System.Windows.Data.Binding(nameof(RebarNumberRow.Host)), IsReadOnly = true, Width = 90 });
            DockPanel.SetDock(grid, Dock.Top);
            root.Children.Add(grid);

            var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var summary = new TextBlock { Text = rows.Count + " Rebar", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DimGray };
            footer.Children.Add(summary);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var apply = new Button { Content = "Áp dụng", Width = 85, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            buttons.Children.Add(apply);
            buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Right);
            footer.Children.Add(buttons);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            numberButton.Click += (sender, args) =>
            {
                int start;
                if (!int.TryParse(startNumber.Text, out start) || start < 0)
                {
                    TaskDialog.Show("KStruct - Quản lý mã thép", "Số bắt đầu phải là số nguyên không âm.");
                    return;
                }
                List<RebarNumberRow> ordered = rows.ToList();
                if (numberingOrder.SelectedIndex == 0) ordered = ordered.OrderBy(x => x.LengthMm).ThenBy(x => x.Bar.Id.Value).ToList();
                else if (numberingOrder.SelectedIndex == 1) ordered = ordered.OrderBy(x => x.BarType, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.LengthMm).ToList();
                else ordered = ordered.OrderBy(x => x.Mark, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Bar.Id.Value).ToList();
                HashSet<string> usedMarks = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
                    .Where(x => !rows.Any(r => r.Bar.Id == x.Id) && !string.IsNullOrWhiteSpace(x.ScheduleMark))
                    .Select(x => x.ScheduleMark).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (IGrouping<string, RebarNumberRow> partitionRows in ordered.GroupBy(x => x.Partition ?? string.Empty))
                {
                    int sequence = start;
                    foreach (RebarNumberRow row in partitionRows)
                    {
                        string candidate;
                        do { candidate = prefix.Text + sequence.ToString("D3"); sequence++; }
                        while (usedMarks.Contains(candidate));
                        row.Mark = candidate;
                        usedMarks.Add(candidate);
                    }
                }
                grid.Items.Refresh();
            };
            apply.Click += (sender, args) => { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); window.DialogResult = true; };
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            return window.ShowDialog() == true;
        }
    }
}
