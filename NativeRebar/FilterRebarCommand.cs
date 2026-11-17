using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class FilterRebarCommand : IExternalCommand
    {
        private sealed class Choice
        {
            public string Name { get; private set; }
            public object Value { get; private set; }
            public Choice(string name, object value) { Name = name; Value = value; }
            public override string ToString() => Name;
        }

        private sealed class Criterion
        {
            public CheckBox Enabled;
            public System.Windows.Controls.ComboBox Values;
            public object SelectedValue => (Values.SelectedItem as Choice)?.Value;
            public bool IsEnabled => Enabled.IsChecked.GetValueOrDefault();
        }

        private sealed class Criteria
        {
            public Criterion Diameter;
            public Criterion Partition;
            public Criterion Shape;
            public Criterion HostMark;
            public Criterion Style;
            public Criterion Number;
            public Criterion HostCategory;
            public ActionKind Action;
        }

        private enum ActionKind { Select, Isolate, ResetIsolate }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Rebar> rebars = new FilteredElementCollector(doc)
                    .OfClass(typeof(Rebar)).WhereElementIsNotElementType().Cast<Rebar>().ToList();
                if (rebars.Count == 0)
                {
                    TaskDialog.Show("KStruct - Lọc Rebar", "Project chưa có Rebar.");
                    return Result.Cancelled;
                }

                Criteria criteria = ChooseCriteria(data.Application.MainWindowHandle, doc, rebars);
                if (criteria == null) return Result.Cancelled;
                if (criteria.Action == ActionKind.ResetIsolate)
                {
                    if (ui.ActiveView.IsTemporaryHideIsolateActive())
                        ui.ActiveView.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                    ui.RefreshActiveView();
                    return Result.Succeeded;
                }

                if (!new[] { criteria.Diameter, criteria.Partition, criteria.Shape, criteria.HostMark,
                    criteria.Style, criteria.Number, criteria.HostCategory }.Any(c => c.IsEnabled))
                {
                    TaskDialog.Show("KStruct - Lọc Rebar", "Bật ít nhất một tiêu chí lọc.");
                    return Result.Cancelled;
                }

                List<ElementId> matches = rebars.Where(rebar => Matches(doc, rebar, criteria))
                    .Select(rebar => rebar.Id).ToList();
                if (matches.Count == 0)
                {
                    TaskDialog.Show("KStruct - Lọc Rebar", "Không tìm thấy Rebar khớp với tiêu chí.");
                    return Result.Succeeded;
                }

                ui.Selection.SetElementIds(matches);
                if (criteria.Action == ActionKind.Isolate)
                {
                    if (ui.ActiveView.IsTemporaryHideIsolateActive())
                        ui.ActiveView.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                    ui.ActiveView.IsolateElementsTemporary(matches);
                    ui.ShowElements(matches);
                }
                TaskDialog.Show("KStruct - Lọc Rebar",
                    "Đã tìm thấy và chọn " + matches.Count.ToString("N0", CultureInfo.CurrentCulture) +
                    " Rebar trong project." +
                    (criteria.Action == ActionKind.Isolate ? "\nĐã cô lập tạm thời trong view hiện hành." : ""));
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Lọc Rebar", ex.Message);
                return Result.Failed;
            }
        }

        private static bool Matches(Document doc, Rebar rebar, Criteria c)
        {
            if (c.Diameter.IsEnabled)
            {
                Parameter diameter = rebar.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER);
                if (diameter == null || !diameter.HasValue ||
                    Math.Abs(diameter.AsDouble() - (double)c.Diameter.SelectedValue) > 1e-7) return false;
            }
            if (c.Partition.IsEnabled &&
                !String.Equals(ParameterText(rebar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM)),
                    (string)c.Partition.SelectedValue, StringComparison.Ordinal)) return false;
            if (c.Shape.IsEnabled && rebar.GetShapeId() != (ElementId)c.Shape.SelectedValue) return false;
            if (c.HostMark.IsEnabled &&
                !String.Equals(ParameterText(rebar.get_Parameter(BuiltInParameter.REBAR_ELEM_HOST_MARK)),
                    (string)c.HostMark.SelectedValue, StringComparison.Ordinal)) return false;
            if (c.Style.IsEnabled)
            {
                RebarShape shape = doc.GetElement(rebar.GetShapeId()) as RebarShape;
                if (shape == null || shape.RebarStyle != (RebarStyle)c.Style.SelectedValue) return false;
            }
            if (c.Number.IsEnabled)
            {
                Parameter number = rebar.get_Parameter(BuiltInParameter.REBAR_NUMBER);
                int actual;
                if (number == null || !number.HasValue ||
                    !Int32.TryParse(number.AsValueString() ?? number.AsString() ?? number.AsInteger().ToString(CultureInfo.InvariantCulture), out actual) ||
                    actual != (int)c.Number.SelectedValue) return false;
            }
            if (c.HostCategory.IsEnabled)
            {
                Element host = doc.GetElement(rebar.GetHostId());
                if (host == null || host.Category == null ||
                    host.Category.Id.Value != (long)c.HostCategory.SelectedValue) return false;
            }
            return true;
        }

        private static string ParameterText(Parameter parameter)
        {
            if (parameter == null || !parameter.HasValue) return "";
            return parameter.AsString() ?? parameter.AsValueString() ?? "";
        }

        private static Criteria ChooseCriteria(IntPtr owner, Document doc, IList<Rebar> bars)
        {
            var diameters = bars.Select(b => b.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER))
                .Where(p => p != null && p.HasValue).Select(p => p.AsDouble()).Distinct()
                .OrderBy(x => x).Select(x => new Choice(
                    "Ø" + UnitUtils.ConvertFromInternalUnits(x, UnitTypeId.Millimeters).ToString("0.#", CultureInfo.CurrentCulture) + " mm",
                    (object)x)).ToList();

            var partitions = bars.Select(b => ParameterText(b.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM)))
                .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => new Choice(String.IsNullOrEmpty(x) ? "(trống)" : x, (object)x)).ToList();

            var shapeIds = new HashSet<ElementId>(bars.Select(b => b.GetShapeId()));
            var shapes = new FilteredElementCollector(doc).OfClass(typeof(RebarShape)).Cast<RebarShape>()
                .Where(x => shapeIds.Contains(x.Id))
                .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => new Choice(x.Name, (object)x.Id)).ToList();

            var hostMarks = bars.Select(b => ParameterText(b.get_Parameter(BuiltInParameter.REBAR_ELEM_HOST_MARK)))
                .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => new Choice(String.IsNullOrEmpty(x) ? "(trống)" : x, (object)x)).ToList();

            var styles = bars.Select(b => doc.GetElement(b.GetShapeId()) as RebarShape)
                .Where(s => s != null).Select(s => s.RebarStyle).Distinct()
                .OrderBy(s => s.ToString()).Select(s => new Choice(
                    s == RebarStyle.Standard ? "Thanh tiêu chuẩn" : "Đai / thép móc", (object)s)).ToList();

            var numbers = bars.Select(b => ParseNumber(b.get_Parameter(BuiltInParameter.REBAR_NUMBER)))
                .Where(n => n.HasValue).Select(n => n.Value).Distinct().OrderBy(x => x)
                .Select(x => new Choice(x.ToString(CultureInfo.CurrentCulture), (object)x)).ToList();

            var hostCategories = bars.Select(b => doc.GetElement(b.GetHostId()))
                .Where(h => h != null && h.Category != null)
                .GroupBy(h => h.Category.Id.Value).Select(g => g.First().Category)
                .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(c => new Choice(c.Name, (object)c.Id.Value)).ToList();

            var window = new Window
            {
                Title = "Lọc Rebar - KStruct",
                Width = 540,
                Height = 585,
                MinWidth = 500,
                MinHeight = 530,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false
            };
            new WindowInteropHelper(window).Owner = owner;
            var root = new DockPanel { Margin = new Thickness(14) };
            var title = new TextBlock
            {
                Text = "Chọn một hoặc nhiều tiêu chí. Các tiêu chí bật sẽ được kết hợp theo điều kiện AND.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            DockPanel.SetDock(title, Dock.Top);
            root.Children.Add(title);

            var rows = new StackPanel();
            Criterion diameter = AddRow(rows, "Đường kính", diameters);
            Criterion partition = AddRow(rows, "Partition", partitions);
            Criterion shape = AddRow(rows, "Hình dạng Rebar", shapes);
            Criterion hostMark = AddRow(rows, "Host Mark", hostMarks);
            Criterion style = AddRow(rows, "Rebar Style", styles);
            Criterion number = AddRow(rows, "Số thống kê", numbers);
            Criterion hostCategory = AddRow(rows, "Loại cấu kiện chủ", hostCategories);
            var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            root.Children.Add(scroll);

            Criteria result = null;
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            DockPanel.SetDock(buttons, Dock.Bottom);
            var select = new Button { Content = "Chọn", MinWidth = 75, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
            var isolate = new Button { Content = "Cô lập", MinWidth = 75, Margin = new Thickness(0, 0, 6, 0) };
            var reset = new Button { Content = "Bỏ cô lập", MinWidth = 85, Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 70, IsCancel = true };
            Action<ActionKind> accept = action =>
            {
                result = new Criteria
                {
                    Diameter = diameter, Partition = partition, Shape = shape, HostMark = hostMark,
                    Style = style, Number = number, HostCategory = hostCategory, Action = action
                };
                window.DialogResult = true;
            };
            select.Click += (s, e) => accept(ActionKind.Select);
            isolate.Click += (s, e) => accept(ActionKind.Isolate);
            reset.Click += (s, e) => accept(ActionKind.ResetIsolate);
            buttons.Children.Add(select);
            buttons.Children.Add(isolate);
            buttons.Children.Add(reset);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            return window.ShowDialog() == true ? result : null;
        }

        private static Criterion AddRow(System.Windows.Controls.Panel root, string label, IList<Choice> choices)
        {
            var row = new System.Windows.Controls.Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(155) });
            row.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            var check = new CheckBox { Content = label, VerticalAlignment = VerticalAlignment.Center, IsEnabled = choices != null && choices.Count > 0 };
            var combo = new System.Windows.Controls.ComboBox
            {
                ItemsSource = choices,
                SelectedItem = choices.FirstOrDefault(),
                IsEnabled = false,
                MinHeight = 24
            };
            check.Checked += (s, e) => combo.IsEnabled = true;
            check.Unchecked += (s, e) => combo.IsEnabled = false;
            System.Windows.Controls.Grid.SetColumn(check, 0);
            System.Windows.Controls.Grid.SetColumn(combo, 1);
            row.Children.Add(check);
            row.Children.Add(combo);
            root.Children.Add(row);
            return new Criterion { Enabled = check, Values = combo };
        }

        private static int? ParseNumber(Parameter parameter)
        {
            if (parameter == null || !parameter.HasValue) return null;
            int number;
            string text = parameter.AsValueString() ?? parameter.AsString() ??
                parameter.AsInteger().ToString(CultureInfo.InvariantCulture);
            return Int32.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out number) ? (int?)number : null;
        }
    }
}