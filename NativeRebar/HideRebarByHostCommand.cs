using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class HideRebarByHostCommand : IExternalCommand
    {
        private enum FilterMode { SelectedHosts, HostCategory }

        private sealed class HostFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => IsSupportedHost(element);
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class CategoryChoice
        {
            public BuiltInCategory Category { get; }
            public string Name { get; }
            public CategoryChoice(BuiltInCategory category, string name) { Category = category; Name = name; }
            public override string ToString() => Name;
        }

        private sealed class ModeChoice
        {
            public FilterMode Mode { get; }
            public string Name { get; }
            public ModeChoice(FilterMode mode, string name) { Mode = mode; Name = name; }
            public override string ToString() => Name;
        }

        private sealed class Options
        {
            public FilterMode Mode;
            public BuiltInCategory HostCategory;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            View view = ui.ActiveView;
            try
            {
                Options options = ChooseOptions(data.Application.MainWindowHandle);
                if (options == null) return Result.Cancelled;
                HashSet<ElementId> selectedHostIds = new HashSet<ElementId>();
                if (options.Mode == FilterMode.SelectedHosts)
                {
                    foreach (Element element in ui.Selection.GetElementIds().Select(doc.GetElement).Where(IsSupportedHost))
                        selectedHostIds.Add(element.Id);
                    if (selectedHostIds.Count == 0)
                    {
                        IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new HostFilter(),
                            "Chọn host cần giữ lại Rebar trong view");
                        foreach (Element element in picked.Select(reference => doc.GetElement(reference)).Where(IsSupportedHost))
                            selectedHostIds.Add(element.Id);
                    }
                    if (selectedHostIds.Count == 0) return Result.Cancelled;
                }

                List<Rebar> visibleRebars = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(Rebar)).WhereElementIsNotElementType().Cast<Rebar>().ToList();
                if (visibleRebars.Count == 0)
                {
                    TaskDialog.Show("KStruct - Ẩn Rebar theo host", "View hiện tại không có Rebar để lọc.");
                    return Result.Cancelled;
                }
                List<ElementId> hideIds = visibleRebars.Where(rebar =>
                {
                    Element host = doc.GetElement(rebar.GetHostId());
                    bool keep = options.Mode == FilterMode.SelectedHosts
                        ? host != null && selectedHostIds.Contains(host.Id)
                        : host?.Category != null && host.Category.Id.Value == (long)options.HostCategory;
                    return !keep;
                }).Select(rebar => rebar.Id).Distinct().ToList();
                if (hideIds.Count == 0)
                {
                    TaskDialog.Show("KStruct - Ẩn Rebar theo host", "Không có Rebar nào ngoài phạm vi host mục tiêu cần ẩn.");
                    return Result.Succeeded;
                }

                string target = options.Mode == FilterMode.SelectedHosts
                    ? selectedHostIds.Count + " host được chọn"
                    : new FilteredElementCollector(doc).OfCategory(options.HostCategory).WhereElementIsNotElementType()
                        .Select(element => element.Category?.Name).FirstOrDefault() ?? options.HostCategory.ToString();
                if (TaskDialog.Show("Xác nhận ẩn Rebar",
                    "View: " + view.Name + "\nHost mục tiêu: " + target +
                    "\nRebar trong view: " + visibleRebars.Count +
                    "\nRebar sẽ bị ẩn: " + hideIds.Count + "\n\nThao tác ẩn theo view hiện hành. Tiếp tục?",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No) != TaskDialogResult.Yes)
                    return Result.Cancelled;

                using (Transaction transaction = new Transaction(doc, "KStruct - Ẩn Rebar theo host"))
                {
                    transaction.Start();
                    view.HideElements(hideIds);
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi hiển thị Rebar.");
                }
                ui.RefreshActiveView();
                TaskDialog.Show("KStruct - Ẩn Rebar theo host", "Đã ẩn " + hideIds.Count + " Rebar ngoài phạm vi " + target + " trong view hiện hành.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Ẩn Rebar theo host", ex.Message);
                return Result.Failed;
            }
        }

        private static bool IsSupportedHost(Element element)
        {
            if (element?.Category == null || element is Rebar || element is RevitLinkInstance) return false;
            long category = element.Category.Id.Value;
            return category == (long)BuiltInCategory.OST_StructuralColumns ||
                category == (long)BuiltInCategory.OST_StructuralFraming ||
                category == (long)BuiltInCategory.OST_StructuralFoundation ||
                category == (long)BuiltInCategory.OST_Walls || category == (long)BuiltInCategory.OST_Floors;
        }

        private static Options ChooseOptions(IntPtr owner)
        {
            var window = new Window
            {
                Title = "Ẩn Rebar theo host - KStruct",
                Width = 480,
                Height = 230,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            new WindowInteropHelper(window).Owner = owner;
            var root = new System.Windows.Controls.Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(new TextBlock
            {
                Text = "Ẩn các Rebar không thuộc host mục tiêu trong view hiện hành. Có thể chọn các host cụ thể hoặc lọc theo category.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            var mode = new WpfComboBox
            {
                ItemsSource = new[]
                {
                    new ModeChoice(FilterMode.SelectedHosts, "Giữ Rebar của host được chọn"),
                    new ModeChoice(FilterMode.HostCategory, "Giữ Rebar theo category host")
                },
                SelectedIndex = 0,
                MinHeight = 27,
                Margin = new Thickness(0, 0, 0, 8)
            };
            System.Windows.Controls.Grid.SetRow(mode, 1); root.Children.Add(mode);
            var category = new WpfComboBox
            {
                ItemsSource = new[]
                {
                    new CategoryChoice(BuiltInCategory.OST_StructuralColumns, "Cột kết cấu"),
                    new CategoryChoice(BuiltInCategory.OST_StructuralFraming, "Dầm kết cấu"),
                    new CategoryChoice(BuiltInCategory.OST_StructuralFoundation, "Móng kết cấu"),
                    new CategoryChoice(BuiltInCategory.OST_Walls, "Tường"),
                    new CategoryChoice(BuiltInCategory.OST_Floors, "Sàn")
                },
                SelectedIndex = 0,
                MinHeight = 27,
                Margin = new Thickness(0, 0, 0, 8),
                IsEnabled = false
            };
            System.Windows.Controls.Grid.SetRow(category, 2); root.Children.Add(category);
            mode.SelectionChanged += (sender, args) =>
                category.IsEnabled = (mode.SelectedItem as ModeChoice)?.Mode == FilterMode.HostCategory;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Lọc / ẩn", MinWidth = 85, IsDefault = true, Margin = new Thickness(0, 0, 7, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 70, IsCancel = true };
            Options result = null;
            ok.Click += (sender, args) =>
            {
                ModeChoice selectedMode = mode.SelectedItem as ModeChoice;
                CategoryChoice selectedCategory = category.SelectedItem as CategoryChoice;
                if (selectedMode == null || selectedMode.Mode == FilterMode.HostCategory && selectedCategory == null)
                {
                    MessageBox.Show(window, "Chọn chế độ lọc hợp lệ.", "KStruct - Ẩn Rebar theo host", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                result = new Options { Mode = selectedMode.Mode, HostCategory = selectedCategory?.Category ?? BuiltInCategory.OST_StructuralColumns };
                window.DialogResult = true;
            };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            System.Windows.Controls.Grid.SetRow(buttons, 4); root.Children.Add(buttons);
            window.Content = root;
            return window.ShowDialog() == true ? result : null;
        }
    }
}
