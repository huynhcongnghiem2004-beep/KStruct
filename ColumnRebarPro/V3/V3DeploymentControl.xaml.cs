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
        // Value remains the legacy ElementId representation. Identity fields
        // allow settings to resolve in projects created from the RTE template.
        public string UniqueId { get; set; }
        public string FamilyName { get; set; }
        public string TypeName { get; set; }
        public string Category { get; set; }
        public string ViewType { get; set; }
    }

    public sealed class DeploymentSettingsFile
    {
        public int Version = 1;
        public List<DeploymentSettingEntry> Entries = new List<DeploymentSettingEntry>();
    }

    public partial class V3DeploymentControl : UserControl
    {
        private enum StatusSeverity
        {
            Info,
            Review,
            Blocker
        }

        private sealed class Choice
        {
            public long Id { get; set; }
            public string Label { get; set; }
            public string UniqueId { get; set; }
            public string FamilyName { get; set; }
            public string TypeName { get; set; }
            public string Category { get; set; }
            public string ViewType { get; set; }
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
        // Invoked by compact V4 front page. The deployment tab remains the
        // source of truth for all Sheet/Tag/Dimension preferences.
        public void SetDeploymentEnabled(bool enabled)
        {
            EnableCheck.IsChecked = enabled;
        }

        public V3DeploymentControl() { InitializeComponent(); }

        private void SetStatus(string message, StatusSeverity severity)
        {
            StatusText.Text = message;
            switch (severity)
            {
                case StatusSeverity.Blocker:
                    StatusLabel.Text = "KHÔNG THỂ TIẾP TỤC";
                    StatusBanner.Background = System.Windows.Media.Brushes.MistyRose;
                    StatusBanner.BorderBrush = System.Windows.Media.Brushes.IndianRed;
                    StatusLabel.Foreground = System.Windows.Media.Brushes.DarkRed;
                    StatusText.Foreground = System.Windows.Media.Brushes.DarkRed;
                    break;
                case StatusSeverity.Review:
                    StatusLabel.Text = "CẦN XÁC MINH";
                    StatusBanner.Background = System.Windows.Media.Brushes.LemonChiffon;
                    StatusBanner.BorderBrush = System.Windows.Media.Brushes.Goldenrod;
                    StatusLabel.Foreground = System.Windows.Media.Brushes.DarkGoldenrod;
                    StatusText.Foreground = System.Windows.Media.Brushes.SaddleBrown;
                    break;
                default:
                    StatusLabel.Text = "THÔNG TIN";
                    StatusBanner.Background = System.Windows.Media.Brushes.AliceBlue;
                    StatusBanner.BorderBrush = System.Windows.Media.Brushes.LightSteelBlue;
                    StatusLabel.Foreground = System.Windows.Media.Brushes.SteelBlue;
                    StatusText.Foreground = System.Windows.Media.Brushes.DarkSlateBlue;
                    break;
            }
        }

        private bool UpdateResourceStatus()
        {
            var missingResources = new List<string>();
            if (CrossSectionType.Items.Count == 0 || LongSectionType.Items.Count == 0)
                missingResources.Add("Project chưa có View Family Type.");
            if (TitleBlock.Items.Count == 0)
                missingResources.Add("Project chưa có Title Block.");
            if (missingResources.Count == 0) return false;

            bool enabled = EnableCheck.IsChecked == true;
            string prefix = enabled
                ? "Không thể triển khai bản vẽ. "
                : "Cần bổ sung trước khi bật triển khai. ";
            SetStatus(prefix + string.Join(" ", missingResources) + " Nạp loại phù hợp rồi mở lại cửa sổ.",
                enabled ? StatusSeverity.Blocker : StatusSeverity.Review);
            return true;
        }

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
            ApplyDrawingDefaults();
            LoadSettings();

            SettingsPanel.IsEnabled = EnableCheck.IsChecked == true;

            UpdateResourceStatus();
        }

        public static DeploymentSettingsFile LoadSavedSettings(Document doc)
        {
            string path = SettingsPathFor(doc);
            if (!File.Exists(path)) return null;
            using (var input = File.OpenRead(path))
                return (DeploymentSettingsFile)new XmlSerializer(typeof(DeploymentSettingsFile)).Deserialize(input);
        }

        internal static DeploymentSettingsFile ForExistingRebar(Document doc)
        {
            // Resolve saved family/type identities through the same UI loader, including
            // projects created from templates. The standalone command itself enables drawing.
            var control = new V3DeploymentControl();
            control.Initialize(doc);
            control.PrepareForExistingRebar();
            return control.Capture();
        }

        internal void PrepareForExistingRebar()
        {
            SetDeploymentEnabled(true);
            EnableCheck.IsEnabled = false;
            foreach (string prefix in new[] { "Cross", "Long" })
            {
                foreach (string suffix in new[] { "Dimension", "TextType", "StirrupTag", "MainTag" })
                {
                    var picker = (ComboBox)FindName(prefix + suffix);
                    bool needed = suffix == "Dimension" || suffix == "TextType"
                        ? ((CheckBox)FindName(prefix + "ShowValue")).IsChecked == true
                        : ((CheckBox)FindName(prefix + "ShowTag")).IsChecked == true;
                    if (needed && (!(picker.SelectedItem is Choice selected) || selected.Id < 0))
                        picker.SelectedItem = picker.Items.Cast<Choice>().FirstOrDefault(c => c.Id >= 0);
                }
            }
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
                    UniqueId = e.UniqueId,
                    FamilyName = symbol == null ? "" : symbol.FamilyName,
                    TypeName = e.Name,
                    Category = e.Category == null ? "" : e.Category.Name,
                    ViewType = e is ViewFamilyType ? ((ViewFamilyType)e).ViewFamily.ToString() :
                               e is View ? ((View)e).ViewType.ToString() : "",
                    Label = (symbol == null ? "" : symbol.FamilyName + " : ") + e.Name +
                            (e is ViewFamilyType ? " [" + ((ViewFamilyType)e).ViewFamily + "]" :
                             e is View ? " [" + ((View)e).ViewType + "]" : "")
                });
            }

            if (box.Items.Count > 0) box.SelectedIndex = 0;
        }

        private static bool Same(string left, string right)
        {
            return string.IsNullOrWhiteSpace(left) ||
                string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyDrawingDefaults()
        {
            var defaults = new Dictionary<string, string>
            {
                { "LongSectionType", "BS-04-MCD-CT-Column" },
                { "LongTemplate", "BS-19-MCD-CT-Cột" },
                { "CrossTemplate", "BS-20-MCN-CT-Cột" },
                { "LongStirrupTag", "A3_T_RT_DK&KC_MID" },
                { "CrossStirrupTag", "A3_P_RT_DK&KC_MID" },
                { "CrossMainTag", "A3_P_RT_NhomThepBS_MID" },
                { "CrossDimension", "@BS-Dim A3" },
                { "LongDimension", "@BS-Dim A3" }
            };

            foreach (var setting in defaults)
            {
                var box = (ComboBox)FindName(setting.Key);
                var matches = box.Items.Cast<Choice>().Where(c => c.Id >= 0 &&
                    (string.Equals(c.TypeName, setting.Value, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(c.FamilyName, setting.Value, StringComparison.OrdinalIgnoreCase))).ToList();
                if (matches.Count == 1) box.SelectedItem = matches[0];
            }
        }

        private static bool MatchesIdentity(Choice choice, DeploymentSettingEntry entry)
        {
            return choice != null && entry != null &&
                Same(entry.FamilyName, choice.FamilyName) &&
                Same(entry.TypeName, choice.TypeName) &&
                Same(entry.Category, choice.Category) &&
                Same(entry.ViewType, choice.ViewType);
        }

        private static Choice ResolveChoice(ComboBox box, DeploymentSettingEntry entry)
        {
            var choices = box.Items.Cast<Choice>().ToList();
            if (entry == null) return null;

            long id;
            if (long.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id < 0)
                return choices.FirstOrDefault(c => c.Id < 0);

            if (!string.IsNullOrWhiteSpace(entry.UniqueId))
            {
                var byUnique = choices.FirstOrDefault(c =>
                    string.Equals(c.UniqueId, entry.UniqueId, StringComparison.Ordinal));
                if (byUnique != null && MatchesIdentity(byUnique, entry)) return byUnique;
            }

            if (long.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
            {
                var byId = choices.FirstOrDefault(c => c.Id == id);
                if (byId != null && MatchesIdentity(byId, entry)) return byId;
            }

            // Projects created from the RTE have different ElementIds and
            // UniqueIds. Family + Type is the stable deployment identity.
            return choices.FirstOrDefault(c => MatchesIdentity(c, entry));
        }

        private static DeploymentSettingEntry Entry(string name, Choice choice)
        {
            if (choice == null) return new DeploymentSettingEntry { Name = name };
            return new DeploymentSettingEntry
            {
                Name = name,
                Value = choice.Id.ToString(CultureInfo.InvariantCulture),
                UniqueId = choice.UniqueId,
                FamilyName = choice.FamilyName,
                TypeName = choice.TypeName,
                Category = choice.Category,
                ViewType = choice.ViewType
            };
        }

        private void Enable_Changed(object sender, RoutedEventArgs e)
        {
            if (SettingsPanel != null) SettingsPanel.IsEnabled = EnableCheck.IsChecked == true;
            if (UpdateResourceStatus()) return;
            if (EnableCheck.IsChecked == true)
                SetStatus("Kiểm tra template, tỷ lệ, Tag và Dimension trước khi triển khai.",
                    StatusSeverity.Review);
            else
                SetStatus("Triển khai bản vẽ đang tắt.", StatusSeverity.Info);
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
                        var choice = ResolveChoice(box, entry);
                        if (choice != null) box.SelectedItem = choice;
                        else { box.SelectedIndex = -1; missing++; }
                    }
                }

                SetStatus(missing == 0 ? "Đã nạp thiết lập đã lưu của project." :
                    "Có " + missing + " loại đã bị xóa/thay đổi trong project. Vui lòng chọn lại.",
                    missing == 0 ? StatusSeverity.Info : StatusSeverity.Review);
            }
            catch (Exception ex)
            {
                SetStatus("Không đọc được thiết lập đã lưu: " + ex.Message, StatusSeverity.Review);
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
                file.Entries.Add(Entry(name, choice));
            }

            return file;
        }

        internal string SelectedChoiceLabel(string name)
        {
            var box = FindName(name) as ComboBox;
            var choice = box == null ? null : box.SelectedItem as Choice;
            return choice == null || choice.Id < 0 ? "— Không sử dụng —" : choice.Label;
        }

        internal bool IsOptionChecked(string name)
        {
            var option = FindName(name) as CheckBox;
            return option != null && option.IsChecked == true;
        }

        internal DeploymentSettingsFile CaptureValidated()
        {
            ValidateSettings();
            return Capture();
        }

        private void ValidateSettings()
        {
            if (!_ready) throw new InvalidOperationException("Chưa nạp dữ liệu project Revit.");
            if (EnableCheck.IsChecked != true) return;

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

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var file = CaptureValidated();

                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath));
                string temporary = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var output = File.Create(temporary))
                        new XmlSerializer(typeof(DeploymentSettingsFile)).Serialize(output, file);

                    if (File.Exists(_settingsPath)) File.Replace(temporary, _settingsPath, null);
                    else File.Move(temporary, _settingsPath);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }

                SetStatus("Đã lưu cấu hình cho project. Chưa tạo hoặc thay đổi sheet/mặt cắt trong mô hình.",
                    StatusSeverity.Info);
            }
            catch (Exception ex)
            {
                SetStatus("Không thể lưu thiết lập: " + ex.Message, StatusSeverity.Blocker);
                MessageBox.Show(Window.GetWindow(this), ex.Message, "KStruct - Thiết lập triển khai", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
