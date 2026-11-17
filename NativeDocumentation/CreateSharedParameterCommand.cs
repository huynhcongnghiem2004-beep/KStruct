using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CreateSharedParameterCommand : IExternalCommand
    {
        private sealed class DataTypeChoice
        {
            public string Name { get; }
            public ForgeTypeId Spec { get; }
            public DataTypeChoice(string name, ForgeTypeId spec) { Name = name; Spec = spec; }
            public override string ToString() => Name;
        }

        private sealed class BindingChoice
        {
            public bool IsType { get; }
            public string Name { get; }
            public BindingChoice(bool isType, string name) { IsType = isType; Name = name; }
            public override string ToString() => Name;
        }

        private sealed class Options
        {
            public string Name;
            public string Group;
            public DataTypeChoice DataType;
            public BindingChoice Binding;
            public List<Category> Categories;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Category> categories = doc.Settings.Categories.Cast<Category>()
                    .Where(category => category != null && category.AllowsBoundParameters &&
                        category.CategoryType == CategoryType.Model)
                    .OrderBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                Options options = ChooseOptions(data.Application.MainWindowHandle, categories);
                if (options == null) return Result.Cancelled;

                Definition existingDefinition = null;
                var existingCategories = new List<Category>();
                DefinitionBindingMapIterator iterator = doc.ParameterBindings.ForwardIterator();
                iterator.Reset();
                while (iterator.MoveNext())
                {
                    Definition definition = iterator.Key;
                    if (!string.Equals(definition?.Name, options.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    existingDefinition = definition;
                    ElementBinding binding = iterator.Current as ElementBinding;
                    if (binding != null) existingCategories.AddRange(binding.Categories.Cast<Category>());
                    break;
                }
                if (existingDefinition != null && !(existingDefinition is ExternalDefinition))
                    throw new InvalidOperationException("Đã có parameter không phải Shared Parameter tên '" + options.Name + "'. Hãy chọn tên khác.");

                ExternalDefinition definitionToBind = existingDefinition as ExternalDefinition ?? GetOrCreateExternalDefinition(
                    data.Application.Application, options.Name, options.Group, options.DataType.Spec);
                if (!string.Equals(definitionToBind.GetDataType()?.TypeId, options.DataType.Spec.TypeId, StringComparison.Ordinal))
                    throw new InvalidOperationException("Shared Parameter '" + options.Name + "' đã tồn tại với kiểu dữ liệu khác.");
                CategorySet categorySet = data.Application.Application.Create.NewCategorySet();
                foreach (Category category in existingCategories.Concat(options.Categories).GroupBy(item => item.Id.Value).Select(group => group.First()))
                    categorySet.Insert(category);
                if (categorySet.Size == 0) throw new InvalidOperationException("Chọn ít nhất một category.");

                Binding bindingToApply = options.Binding.IsType
                    ? (Binding)data.Application.Application.Create.NewTypeBinding(categorySet)
                    : data.Application.Application.Create.NewInstanceBinding(categorySet);
                using (Transaction transaction = new Transaction(doc, "KStruct - Tạo Shared Parameter"))
                {
                    transaction.Start();
                    bool bound = existingDefinition != null
                        ? doc.ParameterBindings.ReInsert(definitionToBind, bindingToApply, GroupTypeId.Data)
                        : doc.ParameterBindings.Insert(definitionToBind, bindingToApply, GroupTypeId.Data);
                    if (!bound) throw new InvalidOperationException("Revit không bind được Shared Parameter. Kiểm tra tên, kiểu binding và category.");
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận Shared Parameter.");
                }

                TaskDialog.Show("KStruct - Shared Parameter",
                    "Đã tạo/bind Shared Parameter '" + options.Name + "'.\nKiểu dữ liệu: " + options.DataType.Name +
                    "\nBinding: " + options.Binding.Name + "\nCategory được chọn: " + options.Categories.Count +
                    "\nCategory binding trước đó được giữ lại.\n\nFile định nghĩa KStruct:\n" + SharedParameterFilePath);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Shared Parameter", ex.Message);
                return Result.Failed;
            }
        }

        private static string SharedParameterFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KStruct", "KStructSharedParameters.txt");

        private static ExternalDefinition GetOrCreateExternalDefinition(Autodesk.Revit.ApplicationServices.Application app,
            string name, string groupName, ForgeTypeId spec)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SharedParameterFilePath));
            if (!File.Exists(SharedParameterFilePath))
            {
                using (File.Create(SharedParameterFilePath)) { }
            }
            string originalPath = app.SharedParametersFilename;
            try
            {
                app.SharedParametersFilename = SharedParameterFilePath;
                DefinitionFile file = app.OpenSharedParameterFile();
                if (file == null) throw new InvalidOperationException("Revit không mở được file shared parameter của KStruct.");
                DefinitionGroup group = file.Groups.get_Item(groupName) ?? file.Groups.Create(groupName);
                ExternalDefinition definition = group.Definitions.get_Item(name) as ExternalDefinition;
                if (definition != null) return definition;
                var creation = new ExternalDefinitionCreationOptions(name, spec) { Visible = true, UserModifiable = true };
                definition = group.Definitions.Create(creation) as ExternalDefinition;
                return definition ?? throw new InvalidOperationException("Không tạo được external definition '" + name + "'.");
            }
            finally { app.SharedParametersFilename = originalPath; }
        }

        private static Options ChooseOptions(IntPtr owner, IList<Category> categories)
        {
            var window = new Window
            {
                Title = "Tạo Shared Parameter - KStruct",
                Width = 590,
                Height = 650,
                MinWidth = 520,
                MinHeight = 560,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            new WindowInteropHelper(window).Owner = owner;
            var root = new WpfGrid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var name = new WpfTextBox { MinHeight = 26, Margin = new Thickness(0, 3, 0, 8) };
            var group = new WpfTextBox { Text = "KStruct", MinHeight = 26, Margin = new Thickness(0, 3, 0, 8) };
            var dataType = new WpfComboBox
            {
                ItemsSource = new[]
                {
                    new DataTypeChoice("Text", SpecTypeId.String.Text),
                    new DataTypeChoice("Yes/No", SpecTypeId.Boolean.YesNo),
                    new DataTypeChoice("Integer", SpecTypeId.Int.Integer),
                    new DataTypeChoice("Number", SpecTypeId.Number),
                    new DataTypeChoice("Length", SpecTypeId.Length),
                    new DataTypeChoice("Area", SpecTypeId.Area),
                    new DataTypeChoice("Volume", SpecTypeId.Volume),
                    new DataTypeChoice("Angle", SpecTypeId.Angle)
                },
                SelectedIndex = 0,
                MinHeight = 26,
                Margin = new Thickness(0, 3, 0, 8)
            };
            var bindingType = new WpfComboBox
            {
                ItemsSource = new[] { new BindingChoice(false, "Instance"), new BindingChoice(true, "Type") },
                SelectedIndex = 0,
                MinHeight = 26,
                Margin = new Thickness(0, 3, 0, 8)
            };
            root.Children.Add(new TextBlock { Text = "Tên parameter:", FontWeight = FontWeights.SemiBold });
            WpfGrid.SetRow(name, 1); root.Children.Add(name);
            var groupLabel = new TextBlock { Text = "Nhóm định nghĩa:", FontWeight = FontWeights.SemiBold };
            WpfGrid.SetRow(groupLabel, 2); root.Children.Add(groupLabel);
            WpfGrid.SetRow(group, 3); root.Children.Add(group);
            var typePanel = new WpfGrid();
            typePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            typePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var dataPanel = new StackPanel();
            dataPanel.Children.Add(new TextBlock { Text = "Kiểu dữ liệu:", FontWeight = FontWeights.SemiBold });
            dataPanel.Children.Add(dataType);
            var bindingPanel = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
            bindingPanel.Children.Add(new TextBlock { Text = "Binding:", FontWeight = FontWeights.SemiBold });
            bindingPanel.Children.Add(bindingType);
            typePanel.Children.Add(dataPanel);
            WpfGrid.SetColumn(bindingPanel, 1); typePanel.Children.Add(bindingPanel);
            WpfGrid.SetRow(typePanel, 4); root.Children.Add(typePanel);

            var categoryPanel = new StackPanel();
            var categoryHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
            categoryHeader.Children.Add(new TextBlock { Text = "Bind vào categories:", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var all = new Button { Content = "Chọn tất cả", MinWidth = 80, Margin = new Thickness(12, 0, 5, 0) };
            var none = new Button { Content = "Bỏ chọn", MinWidth = 70 };
            categoryHeader.Children.Add(all); categoryHeader.Children.Add(none);
            categoryPanel.Children.Add(categoryHeader);
            var categoryChecks = new List<CheckBox>();
            var defaultIds = new HashSet<long>
            {
                (long)BuiltInCategory.OST_StructuralColumns, (long)BuiltInCategory.OST_StructuralFraming,
                (long)BuiltInCategory.OST_StructuralFoundation, (long)BuiltInCategory.OST_Walls, (long)BuiltInCategory.OST_Floors
            };
            foreach (Category category in categories)
            {
                var check = new CheckBox
                {
                    Content = category.Name,
                    Tag = category,
                    IsChecked = defaultIds.Contains(category.Id.Value),
                    Margin = new Thickness(2, 2, 2, 2)
                };
                categoryChecks.Add(check);
                categoryPanel.Children.Add(check);
            }
            all.Click += (sender, args) => categoryChecks.ForEach(check => check.IsChecked = true);
            none.Click += (sender, args) => categoryChecks.ForEach(check => check.IsChecked = false);
            var scroll = new ScrollViewer { Content = categoryPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            WpfGrid.SetRow(scroll, 5); root.Children.Add(scroll);

            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "Tạo / cập nhật binding", MinWidth = 155, IsDefault = true, Margin = new Thickness(0, 0, 7, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 70, IsCancel = true };
            footer.Children.Add(ok); footer.Children.Add(cancel);
            WpfGrid.SetRow(footer, 6); root.Children.Add(footer);
            Options result = null;
            ok.Click += (sender, args) =>
            {
                DataTypeChoice selectedDataType = dataType.SelectedItem as DataTypeChoice;
                BindingChoice selectedBinding = bindingType.SelectedItem as BindingChoice;
                List<Category> selectedCategories = categoryChecks.Where(check => check.IsChecked == true)
                    .Select(check => check.Tag as Category).Where(category => category != null).ToList();
                if (string.IsNullOrWhiteSpace(name.Text) || name.Text.IndexOfAny(new[] { '\r', '\n', '|', '\t' }) >= 0 ||
                    string.IsNullOrWhiteSpace(group.Text) || selectedDataType == null || selectedBinding == null || selectedCategories.Count == 0)
                {
                    MessageBox.Show(window, "Nhập tên/nhóm hợp lệ, chọn kiểu dữ liệu, binding và ít nhất một category.",
                        "KStruct - Shared Parameter", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                result = new Options { Name = name.Text.Trim(), Group = group.Text.Trim(), DataType = selectedDataType,
                    Binding = selectedBinding, Categories = selectedCategories };
                window.DialogResult = true;
            };
            window.Content = root;
            return window.ShowDialog() == true ? result : null;
        }
    }
}
