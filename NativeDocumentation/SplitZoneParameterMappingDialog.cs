using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.DB;

namespace SDK_HD.KStruct.NativeDocumentation
{
    internal sealed class SplitZoneParameterMapping
    {
        public long CategoryId;
        public string SourceName;
        public string TargetName;
        public bool IsType;
        public override string ToString() => CategoryId + " | " + (IsType ? "Type" : "Instance") + " | " + SourceName + " → " + TargetName;
    }

    internal static class SplitZoneParameterMappingDialog
    {
        public static List<SplitZoneParameterMapping> Show(IntPtr owner, Document source, Document host,
            IList<Category> categories, IList<SplitZoneParameterMapping> current)
        {
            var mappings = current.Select(item => new SplitZoneParameterMapping
            {
                CategoryId = item.CategoryId, SourceName = item.SourceName, TargetName = item.TargetName, IsType = item.IsType
            }).ToList();
            var window = new Window
            {
                Title = "Ánh xạ parameter Split Zone - KStruct",
                Width = 650,
                Height = 640,
                MinWidth = 560,
                MinHeight = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            new WindowInteropHelper(window).Owner = owner;
            var root = new System.Windows.Controls.Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var top = new StackPanel();
            top.Children.Add(new TextBlock
            {
                Text = "Ánh xạ parameter từ Revit Link sang project KStruct theo category. Giá trị chỉ được chép khi kiểu lưu trữ và spec trùng nhau. Type đích sẽ được nhân bản riêng trước khi ghi để không sửa các cấu kiện hiện hữu.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            var categoryBox = new ComboBox { ItemsSource = categories, DisplayMemberPath = "Name", MinHeight = 26, Margin = new Thickness(0, 0, 0, 6) };
            top.Children.Add(new TextBlock { Text = "Category:" });
            top.Children.Add(categoryBox);
            var typeCheck = new CheckBox
            {
                Content = "Ánh xạ parameter Type (nhân bản type KStruct trước khi ghi)",
                Margin = new Thickness(2, 2, 2, 8)
            };
            top.Children.Add(typeCheck);
            var sourceBox = new ComboBox { MinHeight = 26, Margin = new Thickness(0, 0, 0, 6) };
            var targetBox = new ComboBox { MinHeight = 26, Margin = new Thickness(0, 0, 0, 8) };
            top.Children.Add(new TextBlock { Text = "Parameter nguồn:" });
            top.Children.Add(sourceBox);
            top.Children.Add(new TextBlock { Text = "Parameter đích:" });
            top.Children.Add(targetBox);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var add = new Button { Content = "Thêm ánh xạ", MinWidth = 100, Margin = new Thickness(0, 0, 8, 0) };
            var remove = new Button { Content = "Xóa ánh xạ", MinWidth = 90 };
            actions.Children.Add(add);
            actions.Children.Add(remove);
            top.Children.Add(actions);
            System.Windows.Controls.Grid.SetRow(top, 0);
            root.Children.Add(top);

            var mapList = new ListBox { MinHeight = 120 };
            System.Windows.Controls.Grid.SetRow(mapList, 1);
            root.Children.Add(mapList);
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            System.Windows.Controls.Grid.SetRow(footer, 2);
            var ok = new Button { Content = "Dùng các ánh xạ này", MinWidth = 130, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 75, IsCancel = true };
            footer.Children.Add(ok);
            footer.Children.Add(cancel);
            root.Children.Add(footer);

            Action refreshMaps = () =>
            {
                mapList.Items.Clear();
                foreach (SplitZoneParameterMapping mapping in mappings.OrderBy(item => item.CategoryId)
                    .ThenBy(item => item.IsType).ThenBy(item => item.SourceName, StringComparer.CurrentCultureIgnoreCase))
                    mapList.Items.Add(mapping);
            };
            Action refreshParameters = () =>
            {
                sourceBox.ItemsSource = null;
                targetBox.ItemsSource = null;
                Category category = categoryBox.SelectedItem as Category;
                if (category == null) return;
                sourceBox.ItemsSource = GetParameterNames(source, category.Id.Value, false, typeCheck.IsChecked == true);
                targetBox.ItemsSource = GetParameterNames(host, category.Id.Value, true, typeCheck.IsChecked == true);
            };
            categoryBox.SelectionChanged += (sender, args) => refreshParameters();
            typeCheck.Checked += (sender, args) => refreshParameters();
            typeCheck.Unchecked += (sender, args) => refreshParameters();
            add.Click += (sender, args) =>
            {
                Category category = categoryBox.SelectedItem as Category;
                string sourceName = sourceBox.SelectedItem as string;
                string targetName = targetBox.SelectedItem as string;
                bool isType = typeCheck.IsChecked == true;
                if (category == null || string.IsNullOrWhiteSpace(sourceName) || string.IsNullOrWhiteSpace(targetName))
                {
                    MessageBox.Show(window, "Chọn category, parameter nguồn và parameter đích.", "KStruct - Ánh xạ parameter", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (!mappings.Any(item => item.CategoryId == category.Id.Value && item.IsType == isType &&
                    string.Equals(item.SourceName, sourceName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.TargetName, targetName, StringComparison.OrdinalIgnoreCase)))
                    mappings.Add(new SplitZoneParameterMapping { CategoryId = category.Id.Value, SourceName = sourceName, TargetName = targetName, IsType = isType });
                refreshMaps();
            };
            remove.Click += (sender, args) =>
            {
                SplitZoneParameterMapping selected = mapList.SelectedItem as SplitZoneParameterMapping;
                if (selected == null) return;
                mappings.Remove(selected);
                refreshMaps();
            };
            ok.Click += (sender, args) => window.DialogResult = true;
            categoryBox.SelectedIndex = categories.Count > 0 ? 0 : -1;
            refreshMaps();
            window.Content = root;
            return window.ShowDialog() == true ? mappings : null;
        }

        private static List<string> GetParameterNames(Document doc, long categoryId, bool writableOnly, bool typeScope)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<Element> instances = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                .WhereElementIsViewIndependent().Where(item => item.Category != null && item.Category.Id.Value == categoryId).Take(1000);
            IEnumerable<Element> elements = typeScope
                ? instances.Select(element => doc.GetElement(element.GetTypeId())).Where(element => element != null).GroupBy(element => element.Id).Select(group => group.First())
                : instances;
            foreach (Element element in elements)
            {
                foreach (Parameter parameter in element.Parameters.Cast<Parameter>())
                {
                    string name = parameter.Definition?.Name;
                    if (string.IsNullOrWhiteSpace(name) || (writableOnly && parameter.IsReadOnly)) continue;
                    if (parameter.StorageType == StorageType.String || parameter.StorageType == StorageType.Integer || parameter.StorageType == StorageType.Double)
                        names.Add(name);
                }
            }
            if (writableOnly)
            {
                DefinitionBindingMapIterator iterator = doc.ParameterBindings.ForwardIterator();
                iterator.Reset();
                while (iterator.MoveNext())
                {
                    ElementBinding binding = iterator.Current as ElementBinding;
                    bool scopeMatches = typeScope ? binding is TypeBinding : binding is InstanceBinding;
                    if (!scopeMatches || !binding.Categories.Cast<Category>().Any(category => category.Id.Value == categoryId)) continue;
                    string name = iterator.Key?.Name;
                    if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                }
            }
            return names.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }
}
