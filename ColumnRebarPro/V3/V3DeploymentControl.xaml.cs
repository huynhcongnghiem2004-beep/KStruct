using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Serialization;
using Autodesk.Revit.DB;

namespace ColumnRebarPro.V3
{
    public sealed class DeploymentSettingEntry
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    public sealed class DeploymentSettingsFile
    {
        public int Version = 1;
        public List<DeploymentSettingEntry> Entries = new List<DeploymentSettingEntry>();
    }

    public partial class V3DeploymentControl : UserControl
    {
        private sealed class Choice
        {
            public long Id { get; set; }
            public string Label { get; set; }
            public override string ToString() { return Label; }
        }

        private string _settingsPath;
        private bool _ready;

        private readonly string[] _textNames =
        {
            "SheetName", "SheetNumber", "CrossScale", "LongScale", "SheetRightMargin"
        };

        private readonly string[] _checkNames =
        {
            "EnableCheck",
            "PullBars",
            "CrossShowTag",
            "CrossShowValue",
            "LongShowTag",
            "LongShowValue",
            "UseMarkForStackName",
            "UseMarkForSheetName",
            "UseMarkForViewportName"
        };

        private readonly string[] _comboNames =
        {
            "TitleBlock",
            "CrossTemplate", "LongTemplate",
            "CrossSectionType", "LongSectionType",
            "CrossDimension", "LongDimension",
            "CrossTextType", "LongTextType",
            "CrossStirrupTag", "CrossMainTag",
            "LongStirrupTag", "LongMainTag",
            "LongBreakLine"
        };

        public bool IsDeploymentEnabled => EnableCheck.IsChecked == true;

        public V3DeploymentControl() { InitializeComponent(); }

        public void Initialize(Document doc)
        {
            if (_ready) return;

            var sections = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .Where(t => t.ViewFamily == ViewFamily.Section || t.ViewFamily == ViewFamily.Detail)
                .Cast<Element>().ToList();

            var templates = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => v.IsTemplate && (v.ViewType == ViewType.Section || v.ViewType == ViewType.Detail)).Cast<Element>().ToList();

            var dimensions = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>()
                .Where(t => t.StyleType == DimensionStyleType.Linear).Cast<Element>().ToList();

            var textTypes = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).ToElements();
            var tags = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RebarTags)
                .WhereElementIsElementType().ToElements();

            var breaks = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_DetailComponents)
                .WhereElementIsElementType().ToElements();

            Fill(TitleBlock, new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType().ToElements(), false);

            foreach (string prefix in new[] { "Cross", "Long" })
            {
                Fill((ComboBox)FindName(prefix + "Template"), templates, true);
                Fill((ComboBox)FindName(prefix + "SectionType"), sections, false);

                foreach (string suffix in new[] { "Template", "SectionType" })
                {
                    var picker = (ComboBox)FindName(prefix + suffix);
                    picker.IsEditable = true;
                    picker.IsTextSearchEnabled = true;
                    picker.StaysOpenOnEdit = true;
                    picker.ToolTip = "Chỉ hiển thị loại mặt cắt Section/Detail. Gõ tên để tìm nhanh.";
                }

                Fill((ComboBox)FindName(prefix + "Dimension"), dimensions, true);
                Fill((ComboBox)FindName(prefix + "TextType"), textTypes, true);
                Fill((ComboBox)FindName(prefix + "StirrupTag"), tags, true);
                Fill((ComboBox)FindName(prefix + "MainTag"), tags, true);
            }

            Fill(LongBreakLine, breaks, true);
            LongBreakLine.ToolTip = "Danh sách Detail Item trong project; chọn family break line thích hợp.";

            _settingsPath = SettingsPathFor(doc);

            _ready = true;
            LoadSettings();

            SettingsPanel.IsEnabled = EnableCheck.IsChecked == true;

            if (CrossSectionType.Items.Count == 0 || LongSectionType.Items.Count == 0)
                StatusText.Text = "Project chưa có View Family Type. Nạp loại phù hợp rồi mở lại cửa sổ.";
            if (TitleBlock.Items.Count == 0 || sections.Count == 0)
                StatusText.Text = "Project thiếu Title Block hoặc Section View Family Type. Hãy nạp trước khi lưu cấu hình bật triển khai.";
        }

        public static DeploymentSettingsFile LoadSavedSettings(Document doc)
        {
            string path = SettingsPathFor(doc);
            if (!File.Exists(path)) return null;
            using (var input = File.OpenRead(path))
                return (DeploymentSettingsFile)new XmlSerializer(typeof(DeploymentSettingsFile)).Deserialize(input);
        }

        public static void PersistSettings(Document doc, DeploymentSettingsFile file)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            string path = SettingsPathFor(doc);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            using (var output = File.Create(temporary))
                new XmlSerializer(typeof(DeploymentSettingsFile)).Serialize(output, file);
            File.Copy(temporary, path, true);
            File.Delete(temporary);
        }

        private static string SettingsPathFor(Document doc)
        {
            string key = doc.ProjectInformation.UniqueId;
            using (var sha = SHA256.Create())
                key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "KStruct", "Deployment", key + ".xml");
        }

        private static void Fill(ComboBox box, IEnumerable<Element> elements, bool optional)
        {
            if (optional) box.Items.Add(new Choice { Id = -1, Label = "— Không sử dụng —" });

            foreach (var e in elements
                .OrderBy(x => x is ViewFamilyType && ((ViewFamilyType)x).ViewFamily == ViewFamily.Section ? 0 : 1)
                .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var symbol = e as FamilySymbol;
                box.Items.Add(new Choice
                {
                    Id = e.Id.Value,
                    Label = (symbol == null ? "" : symbol.FamilyName + " : ") + e.Name +
                            (e is ViewFamilyType ? " [" + ((ViewFamilyType)e).ViewFamily + "]" :
                             e is View ? " [" + ((View)e).ViewType + "]" : "")
                });
            }

            if (box.Items.Count > 0) box.SelectedIndex = 0;
        }

        private void Enable_Changed(object sender, RoutedEventArgs e)
        {
            if (SettingsPanel != null) SettingsPanel.IsEnabled = EnableCheck.IsChecked == true;
            if (CrossSectionType.Items.Count == 0 || LongSectionType.Items.Count == 0)
                StatusText.Text = "Project chưa có View Family Type. Nạp loại phù hợp rồi mở lại cửa sổ.";
        }

        private void LoadSettings()
        {
            if (!File.Exists(_settingsPath)) return;

            try
            {
                DeploymentSettingsFile file;
                using (var input = File.OpenRead(_settingsPath))
                    file = (DeploymentSettingsFile)new XmlSerializer(typeof(DeploymentSettingsFile)).Deserialize(input);

                int missing = 0;
                foreach (var entry in file.Entries)
                {
                    string name = entry.Name == "CrossTag" ? "CrossStirrupTag" :
                                  entry.Name == "LongTag" ? "LongMainTag" :
                                  entry.Name;

                    if (_textNames.Contains(name))
                        ((TextBox)FindName(name)).Text = entry.Value;
                    else if (_checkNames.Contains(name))
                        ((CheckBox)FindName(name)).IsChecked = entry.Value == "true";
                    else if (_comboNames.Contains(name))
                    {
                        var box = (ComboBox)FindName(name);
                        var choice = box.Items.Cast<Choice>().FirstOrDefault(c => c.Id.ToString(CultureInfo.InvariantCulture) == entry.Value);
                        if (choice != null) box.SelectedItem = choice;
                        else { box.SelectedIndex = -1; missing++; }
                    }
                }

                StatusText.Text = missing == 0 ? "Đã nạp thiết lập đã lưu của project." :
                    "Có " + missing + " loại đã bị xóa/thay đổi trong project. Vui lòng chọn lại.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Không đọc được thiết lập đã lưu: " + ex.Message;
            }
        }

        public DeploymentSettingsFile Capture()
        {
            var file = new DeploymentSettingsFile();

            foreach (var name in _textNames)
                file.Entries.Add(new DeploymentSettingEntry { Name = name, Value = ((TextBox)FindName(name)).Text.Trim() });

            foreach (var name in _checkNames)
                file.Entries.Add(new DeploymentSettingEntry
                {
                    Name = name,
                    Value = ((CheckBox)FindName(name)).IsChecked == true ? "true" : "false"
                });

            foreach (var name in _comboNames)
            {
                var choice = ((ComboBox)FindName(name)).SelectedItem as Choice;
                if (choice != null)
                    file.Entries.Add(new DeploymentSettingEntry { Name = name, Value = choice.Id.ToString(CultureInfo.InvariantCulture) });
            }

            return file;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_ready) throw new InvalidOperationException("Chưa nạp dữ liệu project Revit.");

                if (EnableCheck.IsChecked == true)
                {
                    if (string.IsNullOrWhiteSpace(SheetName.Text) || string.IsNullOrWhiteSpace(SheetNumber.Text))
                        throw new InvalidOperationException("Nhập tên và số sheet.");

                    foreach (var name in new[] { "TitleBlock", "CrossSectionType", "LongSectionType" })
                        if (((ComboBox)FindName(name)).SelectedItem == null)
                            throw new InvalidOperationException("Chưa chọn Title Block hoặc View Family Type mặt cắt.");

                    foreach (var name in new[] { "CrossScale", "LongScale" })
                    {
                        int scale;
                        if (!int.TryParse(((TextBox)FindName(name)).Text.Trim(), out scale) || scale < 1 || scale > 24000)
                            throw new InvalidOperationException("Tỷ lệ mặt cắt phải là số nguyên từ 1 đến 24000.");
                    }
                }

                var file = new DeploymentSettingsFile();

                foreach (var name in _textNames)
                    file.Entries.Add(new DeploymentSettingEntry { Name = name, Value = ((TextBox)FindName(name)).Text.Trim() });

                foreach (var name in _checkNames)
                    file.Entries.Add(new DeploymentSettingEntry
                    {
                        Name = name,
                        Value = ((CheckBox)FindName(name)).IsChecked == true ? "true" : "false"
                    });

                foreach (var name in _comboNames)
                {
                    var choice = ((ComboBox)FindName(name)).SelectedItem as Choice;
                    if (choice != null)
                        file.Entries.Add(new DeploymentSettingEntry { Name = name, Value = choice.Id.ToString(CultureInfo.InvariantCulture) });
                }

                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath));
                string temporary = _settingsPath + ".tmp";
                using (var output = File.Create(temporary))
                    new XmlSerializer(typeof(DeploymentSettingsFile)).Serialize(output, file);

                File.Copy(temporary, _settingsPath, true);
                File.Delete(temporary);

                StatusText.Text = "Đã lưu cấu hình cho project. Chưa tạo hoặc thay đổi sheet/mặt cắt trong mô hình.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(Window.GetWindow(this), ex.Message, "KStruct - Thiết lập triển khai", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
