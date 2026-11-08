using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
    public sealed class SplitZoneElementSettingsCommand : IExternalCommand
    {
        private sealed class LinkFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is RevitLinkInstance;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            try
            {
                RevitLinkInstance link = ui.Selection.GetElementIds().Select(ui.Document.GetElement)
                    .OfType<RevitLinkInstance>().FirstOrDefault();
                if (link == null)
                {
                    Reference reference = ui.Selection.PickObject(ObjectType.Element, new LinkFilter(),
                        "Chọn Revit Link để cấu hình category được sao chép vào Split Zone");
                    link = ui.Document.GetElement(reference) as RevitLinkInstance;
                }
                Document source = link?.GetLinkDocument();
                if (source == null) throw new InvalidOperationException("Link chưa được nạp hoặc không thể đọc.");

                List<Category> categories = new FilteredElementCollector(source).WhereElementIsNotElementType()
                    .Where(element => element.Category != null && element.Category.CategoryType == CategoryType.Model &&
                        !element.ViewSpecific && !(element is RevitLinkInstance))
                    .Select(element => element.Category).GroupBy(category => category.Id.Value)
                    .Select(group => group.First()).OrderBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (categories.Count == 0)
                {
                    TaskDialog.Show("KStruct - Cài đặt Split Zone", "Link không có category mô hình để cấu hình.");
                    return Result.Cancelled;
                }

                HashSet<long> saved = SplitZoneElementSettings.Load();
                bool copySameNameParameters = SplitZoneElementSettings.LoadCopySameNameParameters();
                List<SplitZoneParameterMapping> parameterMappings = SplitZoneElementSettings.LoadParameterMappings();
                var dialog = new Window
                {
                    Title = "Split Zone Element - KStruct",
                    Width = 420,
                    Height = 560,
                    MinWidth = 360,
                    MinHeight = 400,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    WindowStyle = WindowStyle.ToolWindow,
                    ShowInTaskbar = false
                };
                new WindowInteropHelper(dialog).Owner = data.Application.MainWindowHandle;
                var root = new System.Windows.Controls.Grid { Margin = new Thickness(12) };
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var header = new StackPanel();
                header.Children.Add(new TextBlock { Text = "Nguồn: " + link.Name, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
                header.Children.Add(new TextBlock
                {
                    Text = "Chọn category được sao chép. Thiết lập dùng chung cho lệnh Split Zone Element trên máy này.",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8)
                });
                var selectButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                var selectAll = new Button { Content = "Chọn tất cả", MinWidth = 90, Margin = new Thickness(0, 0, 6, 0) };
                var selectNone = new Button { Content = "Bỏ chọn", MinWidth = 80 };
                selectButtons.Children.Add(selectAll);
                selectButtons.Children.Add(selectNone);
                header.Children.Add(selectButtons);
                var parameterCheck = new CheckBox
                {
                    Content = "Chép parameter instance cùng tên tương thích từ link sang bản sao",
                    IsChecked = copySameNameParameters,
                    Margin = new Thickness(2, 2, 2, 8)
                };
                header.Children.Add(parameterCheck);
                var mappingButton = new Button
                {
                    Content = "Ánh xạ parameter đích khác (" + parameterMappings.Count + ")",
                    MinWidth = 220,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(2, 0, 2, 8)
                };
                mappingButton.Click += (sender, args) =>
                {
                    List<SplitZoneParameterMapping> edited = SplitZoneParameterMappingDialog.Show(
                        data.Application.MainWindowHandle, source, ui.Document, categories, parameterMappings);
                    if (edited == null) return;
                    parameterMappings = edited;
                    mappingButton.Content = "Ánh xạ parameter đích khác (" + parameterMappings.Count + ")";
                };
                header.Children.Add(mappingButton);
                System.Windows.Controls.Grid.SetRow(header, 0);
                root.Children.Add(header);

                var list = new StackPanel();
                var checks = new List<CheckBox>();
                foreach (Category category in categories)
                {
                    bool isChecked = saved == null || saved.Contains(category.Id.Value);
                    var check = new CheckBox
                    {
                        Content = category.Name,
                        Tag = category.Id.Value,
                        IsChecked = isChecked,
                        Margin = new Thickness(2, 3, 2, 3)
                    };
                    checks.Add(check);
                    list.Children.Add(check);
                }
                selectAll.Click += (sender, args) => checks.ForEach(check => check.IsChecked = true);
                selectNone.Click += (sender, args) => checks.ForEach(check => check.IsChecked = false);
                var categoryScroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                System.Windows.Controls.Grid.SetRow(categoryScroll, 1);
                root.Children.Add(categoryScroll);

                var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
                System.Windows.Controls.Grid.SetRow(actions, 2);
                var save = new Button { Content = "Lưu thiết lập", MinWidth = 100, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
                var cancel = new Button { Content = "Hủy", MinWidth = 75, IsCancel = true };
                save.Click += (sender, args) => dialog.DialogResult = true;
                actions.Children.Add(save);
                actions.Children.Add(cancel);
                root.Children.Add(actions);
                dialog.Content = root;
                if (dialog.ShowDialog() != true) return Result.Cancelled;

                long[] selected = checks.Where(check => check.IsChecked == true)
                    .Select(check => (long)check.Tag).ToArray();
                SplitZoneElementSettings.Save(selected, parameterCheck.IsChecked == true, parameterMappings);
                TaskDialog.Show("KStruct - Cài đặt Split Zone",
                    "Đã lưu " + selected.Length + "/" + categories.Count + " category cho Split Zone Element.\n" +
                    "Chép tham số cùng tên: " + (parameterCheck.IsChecked == true ? "Có" : "Không") + "\n" +
                    "Ánh xạ đích tùy chọn: " + parameterMappings.Count + "\n" +
                    "Thiết lập được lưu tại:\n" + SplitZoneElementSettings.FilePath);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Cài đặt Split Zone", ex.Message);
                return Result.Failed;
            }
        }
    }

    internal static class SplitZoneElementSettings
    {
        internal static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KStruct", "SplitZoneElementCategories.txt");

        // Null means no settings have been saved, so the first run includes every model category.
        internal static HashSet<long> Load()
        {
            if (!File.Exists(FilePath)) return null;
            return File.ReadAllLines(FilePath).Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
                .Select(line => { long id; return long.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) ? (long?)id : null; })
                .Where(id => id.HasValue).Select(id => id.Value).ToHashSet();
        }

        internal static List<SplitZoneParameterMapping> LoadParameterMappings()
        {
            if (!File.Exists(FilePath)) return new List<SplitZoneParameterMapping>();
            var result = new List<SplitZoneParameterMapping>();
            foreach (string raw in File.ReadAllLines(FilePath))
            {
                string line = raw.Trim();
                if (!line.StartsWith("ParameterMap=", StringComparison.OrdinalIgnoreCase)) continue;
                string[] parts = line.Substring(line.IndexOf("=") + 1).Split('|');
                long categoryId;
                if ((parts.Length != 3 && parts.Length != 4) || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out categoryId)) continue;
                try
                {
                    // Three-part records are the previous instance-only format.
                    bool isType = parts.Length == 4 && string.Equals(parts[1], "T", StringComparison.OrdinalIgnoreCase);
                    int nameIndex = parts.Length == 4 ? 2 : 1;
                    string sourceName = Encoding.UTF8.GetString(Convert.FromBase64String(parts[nameIndex]));
                    string targetName = Encoding.UTF8.GetString(Convert.FromBase64String(parts[nameIndex + 1]));
                    if (!string.IsNullOrWhiteSpace(sourceName) && !string.IsNullOrWhiteSpace(targetName))
                        result.Add(new SplitZoneParameterMapping { CategoryId = categoryId, SourceName = sourceName, TargetName = targetName, IsType = isType });
                }
                catch (FormatException) { }
            }
            return result;
        }
        internal static bool LoadCopySameNameParameters()
        {
            if (!File.Exists(FilePath)) return false;
            string value = File.ReadAllLines(FilePath).Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith("CopySameNameParameters=", StringComparison.OrdinalIgnoreCase));
            bool enabled;
            return value != null && bool.TryParse(value.Substring(value.IndexOf("=") + 1), out enabled) && enabled;
        }

        internal static void Save(IEnumerable<long> ids, bool copySameNameParameters, IEnumerable<SplitZoneParameterMapping> parameterMappings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var lines = new List<string>
            {
                "# KStruct Split Zone category and parameter settings",
                "CopySameNameParameters=" + copySameNameParameters.ToString(CultureInfo.InvariantCulture)
            };
            lines.AddRange(ids.Distinct().OrderBy(id => id).Select(id => id.ToString(CultureInfo.InvariantCulture)));
            foreach (SplitZoneParameterMapping mapping in parameterMappings ?? Enumerable.Empty<SplitZoneParameterMapping>())
                lines.Add("ParameterMap=" + mapping.CategoryId.ToString(CultureInfo.InvariantCulture) + "|" + (mapping.IsType ? "T" : "I") + "|" +
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(mapping.SourceName ?? "")) + "|" +
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(mapping.TargetName ?? "")));
            File.WriteAllLines(FilePath, lines, new UTF8Encoding(false));
        }
    }
}
