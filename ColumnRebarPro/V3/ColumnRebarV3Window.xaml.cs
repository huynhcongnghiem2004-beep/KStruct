using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ColumnRebarPro.V3
{
    /// <summary>
    /// WPF UI for KStruct V3.2. This is a replacement for ColumnRebarV3Window.cs
    /// from V3.1: do NOT keep the original class in the same project.
    /// </summary>
    public partial class ColumnRebarV3Window : Window
    {
        private sealed class BarSizeChoice
        {
            public double Mm { get; set; }
            public string Label { get { return "D" + Mm.ToString("0.##", CultureInfo.InvariantCulture); } }
            public override string ToString() { return Label; }
        }

        public sealed class FloorRow : INotifyPropertyChanged
        {
            public ColumnSegment Segment { get; set; }
            public string FloorTitle { get { return "▼ " + Segment.LevelName; } }
            public string HeightText { get { return "H = " + Math.Round(Segment.TopMm - Segment.BottomMm) + " (mm)"; } }
            public string SizeText { get { return "S = " + Math.Round(Segment.WidthMm) + "×" + Math.Round(Segment.DepthMm); } }
            private string _barText;
            public string BarText
            {
                get { return _barText; }
                set { _barText = value; if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("BarText")); }
            }
            public event PropertyChangedEventHandler PropertyChanged;
        }

        private readonly Autodesk.Revit.DB.Document _document;
        private readonly List<ColumnSegment> _segments;
        private V3ColumnConnectionPlan _previewConnectionPlan;
        private readonly Dictionary<long, V3Geometry> _geometry = new Dictionary<long, V3Geometry>();
        private readonly Dictionary<long, V3RebarConfig> _configs = new Dictionary<long, V3RebarConfig>();
        private readonly ObservableCollection<FloorRow> _rows = new ObservableCollection<FloorRow>();
        private readonly List<BarSizeChoice> _sizes;
        private long? _activeId;
        private bool _loading = true;

        public DeploymentSettingsFile DeploymentSettings { get { return DeploymentHost.Children.OfType<V3DeploymentControl>().Single().Capture(); } }
        public bool Accepted { get; private set; }
        internal V3FoundationAnchorSettings FoundationAnchorSettings { get; private set; }
        public bool OpenAnchorModel => FoundationAnchorCheck.IsChecked == true && OpenAnchorModelCheck.IsChecked == true;
        public bool ReplaceOld { get { return OverwriteCheck.IsChecked == true; } }
        public Dictionary<long, V3RebarConfig> Configurations
        {
            get { return _configs.ToDictionary(kv => kv.Key, kv => kv.Value.Copy()); }
        }

        public ColumnRebarV3Window(List<ColumnSegment> stack, IList<double> loadedSizes, Autodesk.Revit.DB.Document document = null)
        {
            if (stack == null || stack.Count == 0)
                throw new InvalidOperationException("Chưa chọn được tuyến cột hợp lệ.");
            _document = document;
            _segments = stack.OrderBy(s => s.BottomMm).ToList();
            _sizes = loadedSizes.Where(n => n >= 6 && n <= 40).Distinct().OrderBy(n => n)
                .Select(n => new BarSizeChoice { Mm = n }).ToList();
            if (_sizes.Count == 0)
                throw new InvalidOperationException("Project chưa có RebarBarType phù hợp. Hãy nạp thép vào Project.");
            for (int i = 0; i < _segments.Count; i++)
            {
                ColumnSegment segment = _segments[i];
                var initial = new V3RebarConfig();
                // Match BimSpeed's per-column saved profiles, which override the last-used fallback.
                // Ignore stale/invalid stored data so the editor can still open.
                try
                {
                    var column = document == null ? null : document.GetElement(segment.Id) as Autodesk.Revit.DB.FamilyInstance;
                    var saved = V3RebarConfigStorage.Read(column);
                    if (saved != null && IsUsable(saved)) initial = saved.Copy();
                }
                catch { /* A bad legacy profile must not prevent opening the editor. */ }
                _configs[segment.Id.Value] = initial;
                if (document != null)
                {
                    var column = document.GetElement(segment.Id) as Autodesk.Revit.DB.FamilyInstance;
                    _geometry[segment.Id.Value] = V3GeometryReader.Read(column);
                }
            }

            InitializeComponent();
            var workArea = SystemParameters.WorkArea;
            MinWidth = Math.Min(MinWidth, workArea.Width);
            MinHeight = Math.Min(MinHeight, workArea.Height);
            Width = Math.Min(Width, workArea.Width);
            Height = Math.Min(Height, workArea.Height);
            var bottomConfig = _configs[_segments[0].Id.Value];
            FoundationAnchorCheck.IsChecked = bottomConfig.FoundationAnchorEnabled;
            FoundationLengthBox.Text = bottomConfig.FoundationAnchorLength > 0 ? bottomConfig.FoundationAnchorLength.ToString(CultureInfo.InvariantCulture) : "";
            FoundationCoverBox.Text = bottomConfig.FoundationAnchorCover > 0 ? bottomConfig.FoundationAnchorCover.ToString(CultureInfo.InvariantCulture) : "";
            FoundationDirectionBox.SelectedIndex = Enum.IsDefined(typeof(V3AnchorDirection), bottomConfig.FoundationAnchorDirection)
                ? (int)bottomConfig.FoundationAnchorDirection : 0;
            SetFoundationControlState();
            var deployment = new V3DeploymentControl();
            DeploymentHost.Children.Add(deployment);
            if (document != null) deployment.Initialize(document);
            RoutedEventHandler updateCaption = (sender, args) =>
                RunButton.Content = deployment.IsDeploymentEnabled ? "Kiểm tra & tạo thép + bản vẽ" : "Kiểm tra & tạo thép 3D";
            deployment.AddHandler(CheckBox.CheckedEvent, updateCaption);
            deployment.AddHandler(CheckBox.UncheckedEvent, updateCaption);
            updateCaption(null, null);
            for (int n = 2; n <= 20; n++) { CxBox.Items.Add(n); CyBox.Items.Add(n); }
            foreach (BarSizeChoice size in _sizes) { MainBarBox.Items.Add(size); TieBarBox.Items.Add(size); }
            foreach (ColumnSegment s in _segments.OrderByDescending(s => s.BottomMm))
            {
                V3RebarConfig c = _configs[s.Id.Value];
                _rows.Add(new FloorRow { Segment = s, BarText = FormatBars(c) });
            }
            FloorList.ItemsSource = _rows;
            CountLabel.Text = "Số lượng: " + _rows.Count;
            _loading = false;
            FloorList.SelectedIndex = Math.Max(0, _rows.Count - 1);
        }

        private FloorRow ActiveRow
        {
            get { return _rows.FirstOrDefault(r => _activeId.HasValue && r.Segment.Id.Value == _activeId.Value); }
        }
        private V3RebarConfig ActiveConfig
        {
            get { return _activeId.HasValue ? _configs[_activeId.Value] : null; }
        }

        private static string FormatBars(V3RebarConfig config)
        {
            if (config.Cx < 2 || config.Cy < 2 || config.MainDiameter <= 0)
                return "Thiếu dữ liệu thiết kế";
            return config.BarCount + "Ø" + config.MainDiameter.ToString("0.##", CultureInfo.InvariantCulture) + (config.StarterBars ? " + Chờ" : "");
        }

        private static bool IsUsable(V3RebarConfig c)
        {
            if (c == null || new[] { c.MainDiameter, c.TieDiameter, c.Cover, c.EndZone,
                c.EndSpacing, c.MidSpacing, c.StarterLength, c.JointBeamHeight, c.JointSpacing }
                .Any(v => double.IsNaN(v) || double.IsInfinity(v))) return false;
            return c.Cx >= 2 && c.Cx <= 20 && c.Cy >= 2 && c.Cy <= 20 &&
                c.MainDiameter >= 6 && c.MainDiameter <= 40 && c.TieDiameter >= 6 && c.TieDiameter <= 40 &&
                c.Cover > 0 && c.EndZone >= 0 && c.EndSpacing > 0 && c.MidSpacing > 0 &&
                (c.TieLayout != V3TieLayout.FixedNumber ||
                    (c.EndTieCount >= 2 && c.EndTieCount <= 1002 && c.MidTieCount >= 2 && c.MidTieCount <= 1002)) &&
                (!c.StarterBars || c.StarterLength > 0 || V3LegacySplice.NeedsLength(c)) &&
                (!c.JointTies || (c.JointBeamHeight > 0 && c.JointSpacing > 0));
        }

        private static double ReadPositive(TextBox box, string caption, bool zeroAllowed = false)
        {
            double value;
            if (!double.TryParse(box.Text.Trim().Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value) || double.IsNaN(value) || double.IsInfinity(value) ||
                (zeroAllowed ? value < 0 : value <= 0))
                throw new InvalidOperationException("Giá trị '" + caption + "' không hợp lệ.");
            return value;
        }
        private static int ReadInteger(TextBox box, string caption)
        {
            int value;
            if (!int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < 1)
                throw new InvalidOperationException("Giá trị '" + caption + "' phải là số nguyên dương.");
            return value;
        }
        private void SetDiameter(ComboBox combo, double diameter)
        {
            combo.SelectedItem = diameter > 0
                ? _sizes.FirstOrDefault(x => Math.Abs(x.Mm - diameter) <= .01)
                : null;
        }
        private void LoadHookBox(ComboBox box,string selected,double degrees)
        {
            var hooks = V3ProjectRebar.Hooks(_document, SelectedTieBarType());
            var choices=new List<V3HookChoice> {new V3HookChoice {Id="",Label="Tự chọn nếu chỉ có một loại phù hợp"}};
            choices.AddRange(hooks.Where(h=>h.TieStyle && h.Allowed &&
                Math.Abs(h.Angle-degrees*Math.PI/180)<1e-6));
            if(!string.IsNullOrEmpty(selected) && !choices.Any(h=>h.Id==selected))
                choices.Add(new V3HookChoice {Id=selected,Label="Móc đã lưu không còn hợp lệ — chọn lại"});
            box.ItemsSource=choices;
            box.SelectedItem=choices.FirstOrDefault(h=>h.Id==(selected??"")) ?? choices[0];
        }

        private Autodesk.Revit.DB.Structure.RebarBarType SelectedBarType(ComboBox diameterBox)
        {
            if (_document == null) return null;
            var size = diameterBox.SelectedItem as BarSizeChoice;
            if (size == null) return null;
            var matches = new Autodesk.Revit.DB.FilteredElementCollector(_document)
                .OfClass(typeof(Autodesk.Revit.DB.Structure.RebarBarType))
                .Cast<Autodesk.Revit.DB.Structure.RebarBarType>()
                .Where(type => !type.Name.StartsWith(V3BottleneckBarType.Prefix, StringComparison.Ordinal) &&
                    Math.Abs(V3Units.Mm(type.BarNominalDiameter) - size.Mm) <= .01)
                .ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        private Autodesk.Revit.DB.Structure.RebarBarType SelectedTieBarType()
        {
            return SelectedBarType(TieBarBox);
        }

        private void RefreshTypeChoices(V3RebarConfig config)
        {
            if (config == null) return;
            var mainType = SelectedBarType(MainBarBox);
            var tieType = SelectedTieBarType();
            LoadHookBox(TieHook135Box, config.TieHook135UniqueId, 135);
            LoadHookBox(TieHook90Box, config.TieHook90UniqueId, 90);
            LoadShapeBox(LongitudinalShapeBox, V3RebarShapeFactory.Choices(_document, mainType,
                V3RebarShapeGroup.Longitudinal, config.TieStyle, false), config.LongitudinalShapeUniqueId);
            LoadShapeBox(AnchorShapeBox, V3RebarShapeFactory.Choices(_document, mainType,
                V3RebarShapeGroup.Anchor, config.TieStyle, false), config.AnchorShapeUniqueId);
            LoadShapeBox(OuterTieShapeBox, V3RebarShapeFactory.Choices(_document, tieType,
                V3RebarShapeGroup.OuterTie, config.TieStyle, false), config.OuterTieShapeUniqueId);
            LoadShapeBox(CrossTieShapeBox, V3RebarShapeFactory.Choices(_document, tieType,
                V3RebarShapeGroup.CrossTie, config.TieStyle, config.CrossTiesAlternating),
                config.CrossTieShapeUniqueId);
        }

        private static void LoadShapeBox(ComboBox box, IEnumerable<V3RebarShapeChoice> shapes, string selected)
        {
            var choices = new List<V3RebarShapeChoice> {
                new V3RebarShapeChoice { UniqueId = "", Label = "— Chọn Rebar Shape có sẵn —" }
            };
            choices.AddRange(shapes);
            if (!string.IsNullOrEmpty(selected) && !choices.Any(s => s.UniqueId == selected))
                choices.Add(new V3RebarShapeChoice { UniqueId = selected,
                    Label = "Shape đã lưu không còn phù hợp — chọn lại" });
            box.ItemsSource = choices;
            box.SelectedItem = choices.FirstOrDefault(s => s.UniqueId == (selected ?? "")) ?? choices[0];
        }

        private void LoadActive()
        {
            var c = ActiveConfig;
            var row = ActiveRow;
            if (c == null || row == null) return;
            _loading = true;
            CxBox.SelectedItem = c.Cx;
            CyBox.SelectedItem = c.Cy;
            SetDiameter(MainBarBox, c.MainDiameter);
            SetDiameter(TieBarBox, c.TieDiameter);
            GroupLongCheck.IsChecked = c.GroupLongBars;
            GroupTiesCheck.IsChecked = c.GroupTies;
            MainStarterCheck.IsChecked = c.StarterBars;
            MainStarterLengthBox.Text = c.StarterLength > 0 ? c.StarterLength.ToString(CultureInfo.InvariantCulture) : "";
            StarterCheck.IsChecked = c.StarterBars;
            BottleneckRibbedCheck.IsChecked = c.BottleneckRibbed;
            BottleneckClearanceBox.Text = c.BottleneckClearanceMm.ToString(CultureInfo.InvariantCulture);
            BottleneckSlopeBox.Text = c.BottleneckSlope.ToString(CultureInfo.InvariantCulture);
            StarterLengthBox.Text = c.StarterLength > 0 ? c.StarterLength.ToString(CultureInfo.InvariantCulture) : "";
            CoverBox.Text = c.Cover > 0 ? c.Cover.ToString(CultureInfo.InvariantCulture) : "";
            ZoneBox.Text = c.EndZone.ToString(CultureInfo.InvariantCulture);
            EndStepBox.Text = c.EndSpacing > 0 ? c.EndSpacing.ToString(CultureInfo.InvariantCulture) : "";
            MidStepBox.Text = c.MidSpacing > 0 ? c.MidSpacing.ToString(CultureInfo.InvariantCulture) : "";
            TieLayoutBox.SelectedIndex = c.TieLayout == V3TieLayout.FixedNumber ? 1 :
                c.TieLayout == V3TieLayout.MaximumSpacing ? 0 : -1;
            EndCountBox.Text = c.EndTieCount > 0 ? c.EndTieCount.ToString(CultureInfo.InvariantCulture) : "";
            MidCountBox.Text = c.MidTieCount > 0 ? c.MidTieCount.ToString(CultureInfo.InvariantCulture) : "";
            RefreshTypeChoices(c);
            CrossCheck.IsChecked = c.CrossTies;
            CrossAlternateCheck.IsChecked = c.CrossTiesAlternating;
            JointTiesCheck.IsChecked = c.JointTies;
            JointHeightBox.Text = c.JointBeamHeight > 0 ? c.JointBeamHeight.ToString(CultureInfo.InvariantCulture) : "";
            BeamHeightBox.Text = c.JointBeamHeight > 0 ? c.JointBeamHeight.ToString(CultureInfo.InvariantCulture) : "";
            JointStepBox.Text = c.JointSpacing > 0 ? c.JointSpacing.ToString(CultureInfo.InvariantCulture) : "";
            HookRadio.IsChecked = c.TieStyle == V3TieStyle.Hook135;
            LoopRadio.IsChecked = c.TieStyle == V3TieStyle.ClosedLoop;
            _loading = false;
            TieLayoutControlState();
            UpdateSummary();
            UpdateFoundationAnchorSummary();
        }

        private bool SaveActive(bool showError)
        {
            if (_loading || !_activeId.HasValue) return true;
            try
            {
                var main = MainBarBox.SelectedItem as BarSizeChoice;
                var tie = TieBarBox.SelectedItem as BarSizeChoice;
                if (main == null || tie == null || CxBox.SelectedItem == null || CyBox.SelectedItem == null)
                    throw new InvalidOperationException("Vui lòng chọn đầy đủ Cx, Cy và loại thép.");
                var c = _configs[_activeId.Value].Copy();
                c.Cx = (int)CxBox.SelectedItem;
                c.Cy = (int)CyBox.SelectedItem;
                c.MainDiameter = main.Mm;
                c.TieDiameter = tie.Mm;
                c.Cover = ReadPositive(CoverBox, "Lớp bảo vệ");
                c.EndZone = ReadPositive(ZoneBox, "Chiều dài vùng đai đầu", true);
                if (TieLayoutBox.SelectedIndex < 0)
                    throw new InvalidOperationException("Chọn quy tắc bố trí đai theo thiết kế đã duyệt.");
                c.TieLayout = TieLayoutBox.SelectedIndex == 1 ? V3TieLayout.FixedNumber : V3TieLayout.MaximumSpacing;
                if (c.TieLayout == V3TieLayout.FixedNumber)
                {
                    c.EndTieCount = ReadInteger(EndCountBox, "Số đai vùng đầu");
                    c.MidTieCount = ReadInteger(MidCountBox, "Số đai vùng giữa");
                }
                else
                {
                    c.EndSpacing = ReadPositive(EndStepBox, "Bước đai hai đầu");
                    c.MidSpacing = ReadPositive(MidStepBox, "Bước đai giữa");
                }
                // Inactive spacing values are unused in Fixed Number, but keep the stored profile valid.
                if (c.EndSpacing <= 0) c.EndSpacing = 1;
                if (c.MidSpacing <= 0) c.MidSpacing = 1;
                c.StarterBars = (MainStarterCheck.IsChecked == true || StarterCheck.IsChecked == true);
                if (c.StarterBars)
                {
                    c.StarterLength = ReadPositive(MainStarterLengthBox, "Chiều dài chờ từ đỉnh cột dưới");
                    c.BottleneckClearanceMm = ReadPositive(BottleneckClearanceBox, "Khe hở cổ chai", true);
                    c.BottleneckSlope = ReadPositive(BottleneckSlopeBox, "Tỷ lệ xiên 1:n");
                    c.BottleneckRibbed = BottleneckRibbedCheck.IsChecked == true;
                    StarterLengthBox.Text = c.StarterLength.ToString(CultureInfo.InvariantCulture);
                    MainStarterLengthBox.Text = c.StarterLength.ToString(CultureInfo.InvariantCulture);
                }
                // The editor explicitly supplies a new length or disables the old connection.
                // LapLength has a different datum and is never copied to StarterLength.
                if (c.Splice == V3SpliceStyle.AtStoreyJoint)
                {
                    c.Splice = V3SpliceStyle.None;
                    c.LegacyConnectionNotice = c.StarterBars
                        ? "Đã chuyển cấu hình nối cũ sang nhấn cổ chai; dùng chiều dài chờ đã nhập."
                        : "Đã tắt nối tầng theo lựa chọn trong giao diện.";
                }
                c.TieHook135UniqueId = (TieHook135Box.SelectedItem as V3HookChoice)?.Id ?? "";
                c.TieHook90UniqueId = (TieHook90Box.SelectedItem as V3HookChoice)?.Id ?? "";
                c.LongitudinalShapeUniqueId = (LongitudinalShapeBox.SelectedItem as V3RebarShapeChoice)?.UniqueId ?? "";
                c.AnchorShapeUniqueId = (AnchorShapeBox.SelectedItem as V3RebarShapeChoice)?.UniqueId ?? "";
                c.OuterTieShapeUniqueId = (OuterTieShapeBox.SelectedItem as V3RebarShapeChoice)?.UniqueId ?? "";
                c.CrossTieShapeUniqueId = (CrossTieShapeBox.SelectedItem as V3RebarShapeChoice)?.UniqueId ?? "";
                c.CrossTies = CrossCheck.IsChecked == true;
                c.CrossTiesAlternating = CrossAlternateCheck.IsChecked == true;
                c.JointTies = JointTiesCheck.IsChecked == true;
                if (c.JointTies)
                {
                    c.JointBeamHeight = ReadPositive(JointHeightBox, "Chiều cao nút dầm sàn");
                    c.JointSpacing = ReadPositive(JointStepBox, "Bước đai trong nút dầm");
                }
                else
                {
                    c.JointBeamHeight = 0;
                    c.JointSpacing = 0;
                }
                BeamHeightBox.Text = c.JointBeamHeight > 0
                    ? c.JointBeamHeight.ToString(CultureInfo.InvariantCulture)
                    : "";
                c.GroupLongBars = GroupLongCheck.IsChecked == true;
                c.GroupTies = GroupTiesCheck.IsChecked == true;
                if (LoopRadio.IsChecked != true && HookRadio.IsChecked != true)
                    throw new InvalidOperationException("Chọn hình dạng đai theo chi tiết thiết kế đã duyệt.");
                c.TieStyle = LoopRadio.IsChecked == true ? V3TieStyle.ClosedLoop : V3TieStyle.Hook135;
                _configs[_activeId.Value] = c;
                var row = ActiveRow;
                if (row != null) row.BarText = FormatBars(c);
                TieLayoutControlState();
                UpdateSummary();
                return true;
            }
            catch (Exception ex)
            {
                if (showError) MessageBox.Show(this, ex.Message, "KStruct - Kiểm tra dữ liệu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        private void WithoutEvents(Action action)
        {
            bool previous = _loading;
            _loading = true;
            try { action(); }
            finally { _loading = previous; }
        }

        private void TieLayoutControlState()
        {
            StarterLengthBox.IsEnabled = StarterCheck.IsChecked == true;
            MainStarterLengthBox.IsReadOnly = false;
            MainStarterLengthBox.IsEnabled = MainStarterCheck.IsChecked == true;
            bool enabled = TieLayoutBox.SelectedIndex == 1;
            EndStepBox.IsEnabled = !enabled;
            MidStepBox.IsEnabled = !enabled;
            EndCountBox.IsEnabled = enabled;
            MidCountBox.IsEnabled = enabled;
            TieHook135Box.IsEnabled = HookRadio.IsChecked == true || CrossCheck.IsChecked == true;
            TieHook90Box.IsEnabled = CrossCheck.IsChecked == true && CrossAlternateCheck.IsChecked == true;
            CrossAlternateCheck.IsEnabled = CrossCheck.IsChecked == true;
            JointHeightBox.IsEnabled = JointTiesCheck.IsChecked == true;
            JointStepBox.IsEnabled = JointTiesCheck.IsChecked == true;
        }

        private void FloorList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var target = FloorList.SelectedItem as FloorRow;
            if (target == null) return;
            long targetId = target.Segment.Id.Value;
            if (_activeId.HasValue && _activeId.Value == targetId) return;
            if (_activeId.HasValue && !SaveActive(true))
            {
                _loading = true;
                FloorList.SelectedItem = ActiveRow;
                _loading = false;
                return;
            }
            _activeId = targetId;
            LoadActive();
        }
        private void Edit_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            TieLayoutControlState();
            WithoutEvents(() =>
            {
                // Hai checkbox "thép chờ" ở hai tab là cùng một thiết lập: giữ đồng bộ để bỏ chọn được.
                bool starterSender = ReferenceEquals(sender, MainStarterCheck) || ReferenceEquals(sender, StarterCheck);
                if (ReferenceEquals(sender, MainStarterCheck)) StarterCheck.IsChecked = MainStarterCheck.IsChecked;
                else if (ReferenceEquals(sender, StarterCheck)) MainStarterCheck.IsChecked = StarterCheck.IsChecked;

            });
            SaveActive(false);
            if (ReferenceEquals(sender, TieBarBox) || ReferenceEquals(sender, MainBarBox) ||
                ReferenceEquals(sender, HookRadio) || ReferenceEquals(sender, LoopRadio) ||
                ReferenceEquals(sender, CrossAlternateCheck))
                RefreshTypeChoices(ActiveConfig);
            UpdateFoundationAnchorSummary();
        }
        private void TieStyle_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SaveActive(false);
            RefreshTypeChoices(ActiveConfig);
        }
        private void Text_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (ReferenceEquals(sender, BeamHeightBox)) JointHeightBox.Text = BeamHeightBox.Text;
            else if (ReferenceEquals(sender, JointHeightBox)) BeamHeightBox.Text = JointHeightBox.Text;
            else if (ReferenceEquals(sender, MainStarterLengthBox)) StarterLengthBox.Text = MainStarterLengthBox.Text;
            else if (ReferenceEquals(sender, StarterLengthBox)) MainStarterLengthBox.Text = StarterLengthBox.Text;
            SaveActive(false);
        }
        private void SectionCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawSection();
            DrawLongSection();
        }
        private void ApplyAll_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveActive(true)) return;
            var c = ActiveConfig;
            if (c == null) return;
            foreach (ColumnSegment s in _segments)
                _configs[s.Id.Value] = c.Copy();
            foreach (FloorRow row in _rows)
                row.BarText = FormatBars(_configs[row.Segment.Id.Value]);
            LoadActive();
            MessageBox.Show(this, "Đã áp dụng cấu hình cho " + _segments.Count + " tầng trong cửa sổ. Chưa thay đổi mô hình Revit.",
                "KStruct", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void Run_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveActive(true)) return;
            if (DistributedRadio.IsChecked != true)
            {
                MessageBox.Show(this, "Chỉ hỗ trợ phân bố đều. Chọn kiểu này sau khi đối chiếu với thiết kế đã duyệt.",
                    "KStruct - Kiểm tra cấu hình", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                if(_document != null)new V3DrawingBuilder(DeploymentSettings).Validate(_document,new List<V3SegmentWork>());
            }
            catch(Exception ex)
            {
                MessageBox.Show(this,ex.Message,"KStruct - Thiết lập bản vẽ",MessageBoxButton.OK,MessageBoxImage.Warning);
                return;
            }
            try
            {
                FoundationAnchorSettings = CaptureFoundationAnchorSettings();
                var segment = _segments[0];
                if (_document != null)
                {
                    var column = _document.GetElement(segment.Id) as Autodesk.Revit.DB.FamilyInstance;
                    var work = new V3SegmentWork { Column = column, Segment = segment,
                        Geometry = V3GeometryReader.Read(column), Config = _configs[segment.Id.Value].Copy() };
                    V3FoundationAnchor.Attach(_document, work, FoundationAnchorSettings);
                    _configs[segment.Id.Value] = work.Config;
                    var works = _segments.Select(s => s.Id.Value == segment.Id.Value ? work : new V3SegmentWork {
                        Column = _document.GetElement(s.Id) as Autodesk.Revit.DB.FamilyInstance,
                        Segment = s, Geometry = _geometry[s.Id.Value], Config = _configs[s.Id.Value].Copy() }).ToList();
                    V3StarterConnection.Resolve(works);
                    V3RebarValidator.Preflight(works);
                    V3RebarBuilder.ValidateProjectTypes(_document,works);
                }
            }
            catch (Exception ex)
            {
                MainTabs.SelectedIndex = ex.Message.IndexOf("móc",StringComparison.OrdinalIgnoreCase)>=0 ||
                    ex.Message.Contains("Hook") || ex.Message.Contains("Shape") ? 2 : 1;
                MessageBox.Show(this, ex.Message, "KStruct - Kiểm tra tuyến cột", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Accepted = true;
            DialogResult = true;
        }
        private void SetFoundationControlState()
        {
            bool enabled = FoundationAnchorCheck.IsChecked == true;
            FoundationLengthBox.IsEnabled = enabled;
            FoundationCoverBox.IsEnabled = enabled;
            FoundationDirectionBox.IsEnabled = enabled;
            OpenAnchorModelCheck.IsEnabled = enabled;
        }

        private void FoundationAnchor_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SetFoundationControlState();
            UpdateFoundationAnchorSummary();
            UpdateStarterConnectionSummary();
            DrawLongSection();
        }

        private void UpdateFoundationAnchorSummary()
        {
            if (FoundationAnchorSummary == null) return;
            var bottom = _configs[_segments[0].Id.Value];
            FoundationAnchorSummary.Text = "Áp dụng tại " + _segments[0].LevelName + ": " + FormatBars(bottom) +
                (bottom.GroupLongBars ? " — nhóm Fixed Number theo cạnh (1 thanh dùng Single)." : " — tạo từng thanh.") +
                " Không lặp thanh góc; nhóm vuông góc hướng chân neo; chân ngang = tổng kích thước nhập trừ đoạn đứng trong móng. Kiểm tra solid móng trước khi tạo.";
            DrawFoundationPreview();
        }

        private V3FoundationAnchorSettings CaptureFoundationAnchorSettings()
        {
            if (FoundationAnchorCheck.IsChecked != true) return new V3FoundationAnchorSettings { Enabled = false };
            if (FoundationDirectionBox.SelectedIndex < 0)
                throw new InvalidOperationException("Chọn hướng chân neo theo trục cột.");
            return new V3FoundationAnchorSettings
            {
                Enabled = true,
                CoverMm = ReadPositive(FoundationCoverBox, "Cover móng"),
                AnchorLengthMm = ReadPositive(FoundationLengthBox, "Tổng kích thước neo chân L"),
                Direction = (V3AnchorDirection)FoundationDirectionBox.SelectedIndex
            };
        }

        private void FoundationCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawFoundationPreview();

        private void DrawFoundationPreview()
        {
            if (FoundationCanvas == null || _loading) return;
            FoundationCanvas.Children.Clear();
            if (FoundationAnchorCheck.IsChecked != true || FoundationDirectionBox.SelectedIndex < 0) return;
            var segment = _segments[0];
            var config = _configs[segment.Id.Value];
            double width = FoundationCanvas.ActualWidth, height = FoundationCanvas.ActualHeight;
            if (width < 100 || height < 100) return;
            try
            {
                var plan = V3AnchorLayoutPlan.Create(config.Cx, config.Cy, segment.WidthMm, segment.DepthMm,
                    config.Cover, config.TieDiameter, config.MainDiameter, (V3AnchorDirection)FoundationDirectionBox.SelectedIndex);
                double scale = Math.Min((width - 100) / segment.WidthMm, (height - 90) / segment.DepthMm);
                Func<double, double, Point> point = (x, y) => new Point(width / 2 + x * scale, height / 2 - y * scale);
                Action<Point, Point, Brush> line = (a, b, brush) => FoundationCanvas.Children.Add(
                    new Line { X1=a.X,Y1=a.Y,X2=b.X,Y2=b.Y,Stroke=brush,StrokeThickness=2 });
                Point lower = point(-segment.WidthMm / 2, -segment.DepthMm / 2);
                Point upper = point(segment.WidthMm / 2, segment.DepthMm / 2);
                var outline = new Rectangle { Width=upper.X-lower.X,Height=lower.Y-upper.Y,
                    Stroke=Brushes.SeaGreen,StrokeThickness=2 };
                Canvas.SetLeft(outline,lower.X);Canvas.SetTop(outline,upper.Y);FoundationCanvas.Children.Add(outline);
                foreach (var array in plan)
                {
                    for (int i=0;i<array.Count;i++)
                    {
                        Point p=point(array.X+array.StepX*i,array.Y+array.StepY*i);
                        var dot=new Ellipse {Width=6,Height=6,Fill=Brushes.Firebrick};
                        Canvas.SetLeft(dot,p.X-3);Canvas.SetTop(dot,p.Y-3);FoundationCanvas.Children.Add(dot);
                    }
                    Point start=point(array.X+array.StepX*(array.Count-1)/2,array.Y+array.StepY*(array.Count-1)/2);
                    var end=new Point(start.X+array.HookX*24,start.Y-array.HookY*24);
                    line(start,end,Brushes.RoyalBlue);
                    var v=end-start;v.Normalize();var side=new Vector(-v.Y,v.X);
                    line(end,end-v*7+side*4,Brushes.RoyalBlue);line(end,end-v*7-side*4,Brushes.RoyalBlue);
                }
                var text=new TextBlock {Text="+X →     +Y ↑",Foreground=Brushes.DimGray,FontSize=11};
                Canvas.SetLeft(text,8);Canvas.SetTop(text,5);FoundationCanvas.Children.Add(text);
            }
            catch (InvalidOperationException)
            {
                FoundationCanvas.Children.Add(new TextBlock { Text="Nhập đủ cấu hình thép tại tầng chân tuyến.",TextWrapping=TextWrapping.Wrap,Width=280 });
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Accepted = false;
            DialogResult = false;
        }

        private void UpdateSummary()
        {
            var c = ActiveConfig;
            var row = ActiveRow;
            if (c == null || row == null) return;
            if (c.Cx < 2 || c.Cy < 2 || c.MainDiameter <= 0 || c.TieDiameter <= 0)
            {
                TotalBarsText.Text = "Thiếu dữ liệu thiết kế";
                AreaText.Text = "—";
                RatioText.Text = "—";
                DrawSection();
                DrawLongSection();
                return;
            }
            double area = c.BarCount * Math.PI * c.MainDiameter * c.MainDiameter / 4.0;
            double concreteArea = row.Segment.WidthMm * row.Segment.DepthMm;
            TotalBarsText.Text = FormatBars(c);
            AreaText.Text = (area / 100.0).ToString("0.000", CultureInfo.InvariantCulture) + " cm²";
            RatioText.Text = concreteArea > 0
                ? (100.0 * area / concreteArea).ToString("0.000", CultureInfo.InvariantCulture) + "%"
                : "—";
            DrawSection();
            UpdateFoundationAnchorSummary();
            UpdateStarterConnectionSummary();
            DrawLongSection();
        }

        private void UpdateStarterConnectionSummary()
        {
            _previewConnectionPlan = null;
            var row = ActiveRow; var c = ActiveConfig;
            LegacyConnectionSummary.Text = c == null ? "" : c.LegacyConnectionNotice ?? "";
            if (row == null || c == null || StarterConnectionSummary == null) return;
            int index = _segments.FindIndex(s => s.Id.Value == row.Segment.Id.Value);
            if (!c.StarterBars || index + 1 >= _segments.Count)
            {
                StarterConnectionSummary.Text = "Không có cặp nối tầng trên đang bật tại tầng này.";
                ConnectionComparisonGrid.ItemsSource = null; ConnectionPairingStatus.Text = "";
                return;
            }
            var upper = _segments[index + 1]; var u = _configs[upper.Id.Value];
            if (u.MainDiameter <= 0 || u.Cover <= 0 || c.MainDiameter <= 0)
            {
                StarterConnectionSummary.Text = "Nhập cấu hình thép tầng trên để tính phần chờ vào cột.";
                ConnectionPairingStatus.Text = "Chưa đủ dữ liệu ghép thanh."; ConnectionComparisonGrid.ItemsSource = null;
                return;
            }
            if (_geometry.ContainsKey(row.Segment.Id.Value) && _geometry.ContainsKey(upper.Id.Value))
            {
                var lowerWork = new V3SegmentWork { Segment = row.Segment, Config = c, Geometry = _geometry[row.Segment.Id.Value] };
                var upperWork = new V3SegmentWork { Segment = upper, Config = u, Geometry = _geometry[upper.Id.Value] };
                var lowerProfile = V3ConnectionAdapter.Profile(lowerWork);
                var upperProfile = V3ConnectionAdapter.Profile(upperWork);
                lowerProfile.HasFoot = index == 0 && FoundationAnchorCheck.IsChecked == true;
                if (lowerProfile.HasFoot && FoundationDirectionBox.SelectedIndex >= 0)
                    lowerProfile.FootDirection = (V3AnchorDirection)FoundationDirectionBox.SelectedIndex;
                ConnectionComparisonGrid.ItemsSource = V3ColumnPairing.Compare(lowerProfile, upperProfile);
                try
                {
                    var plan = V3ColumnPairing.Create(lowerProfile, upperProfile, c.BottleneckClearanceMm, c.BottleneckSlope);
                    _previewConnectionPlan = plan;
                    ConnectionPairingStatus.Text = "Ghép " + plan.Bars.Count + " cặp; dịch ngang lớn nhất " + Math.Round(plan.MaximumShift, 1) +
                        " mm; chiều cao nhấn lớn nhất " + Math.Round(plan.MaximumHeight, 1) + " mm. Nhóm chỉ khi cùng hình dạng và mặt phẳng.";
                }
                catch (InvalidOperationException ex) { ConnectionPairingStatus.Text = ex.Message; }
            }
            double gap = upper.BottomMm - row.Segment.TopMm;
            double lap = c.StarterLength - gap - u.Cover - u.MainDiameter / 2;
            double minimum = gap + u.Cover + u.MainDiameter / 2 + V3BottleneckRules.MinimumLap(Math.Max(c.MainDiameter, u.MainDiameter));
            StarterConnectionSummary.Text = "Khoảng hở lên " + upper.LevelName + ": " + Math.Round(gap, 1) +
                " mm. Phần nối thẳng còn lại: " + Math.Round(lap, 1) + " mm. Chiều dài chờ tối thiểu theo giới hạn 20d/250 mm: " +
                Math.Round(minimum, 1) + " mm; chiều dài tính toán có thể lớn hơn.";
        }
        private void LongSectionCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawLongSection();
        }
        private void DrawSection()
        {
            if (SectionCanvas == null) return;
            SectionCanvas.Children.Clear();
            var c = ActiveConfig;
            var row = ActiveRow;
            if (c == null || row == null) return;
            double canvasW = SectionCanvas.ActualWidth;
            double canvasH = SectionCanvas.ActualHeight;
            if (canvasW < 120 || canvasH < 100) return;
            double widthMm = row.Segment.WidthMm, depthMm = row.Segment.DepthMm;
            if (widthMm <= 0 || depthMm <= 0 || c.Cx < 2 || c.Cy < 2) return;
            double factor = Math.Min((canvasW - 82) / widthMm, (canvasH - 78) / depthMm);
            if (factor <= 0) return;
            double w = widthMm * factor, h = depthMm * factor;
            double left = (canvasW - w) / 2, top = (canvasH - h) / 2;
            AddOutline(left, top, w, h, Color.FromRgb(47, 143, 86), 5, 0);
            double inset = (c.Cover + c.TieDiameter / 2) * factor;
            double tieLeft = left + inset, tieTop = top + inset;
            double tieW = w - 2 * inset, tieH = h - 2 * inset;
            if (tieW < 12 || tieH < 12) return;
            AddOutline(tieLeft, tieTop, tieW, tieH, Color.FromRgb(0, 153, 157), 2.5, 17);
            double barInset = (c.MainDiameter / 2 + c.TieDiameter / 2) * factor;
            double bx1 = tieLeft + barInset, bx2 = tieLeft + tieW - barInset;
            double by1 = tieTop + barInset, by2 = tieTop + tieH - barInset;
            if (bx2 <= bx1 || by2 <= by1) return;
            if (c.CrossTies)
            {
                for (int i = 1; i < c.Cx - 1; i++)
                {
                    double xx = bx1 + (bx2 - bx1) * i / (c.Cx - 1);
                    AddLine(xx, by1, xx, by2, Brushes.Firebrick);
                }
                for (int i = 1; i < c.Cy - 1; i++)
                {
                    double yy = by1 + (by2 - by1) * i / (c.Cy - 1);
                    AddLine(bx1, yy, bx2, yy, Brushes.Firebrick);
                }
            }
            for (int i = 0; i < c.Cx; i++)
            {
                double xx = bx1 + (bx2 - bx1) * i / (c.Cx - 1);
                AddBar(xx, by1); AddBar(xx, by2);
            }
            for (int j = 1; j < c.Cy - 1; j++)
            {
                double yy = by1 + (by2 - by1) * j / (c.Cy - 1);
                AddBar(bx1, yy); AddBar(bx2, yy);
            }
            var sizeLabel = new TextBlock {
                Text = Math.Round(widthMm) + " × " + Math.Round(depthMm) + " mm",
                FontSize = 11, Foreground = Brushes.DimGray
            };
            Canvas.SetLeft(sizeLabel, Math.Max(4, left));
            Canvas.SetTop(sizeLabel, Math.Max(4, top - 23));
            SectionCanvas.Children.Add(sizeLabel);
        }
        private void AddOutline(double x, double y, double w, double h, Color color, double stroke, double radius)
        {
            var rect = new Rectangle {
                Width = w, Height = h, Stroke = new SolidColorBrush(color), StrokeThickness = stroke,
                RadiusX = radius, RadiusY = radius, Fill = Brushes.Transparent
            };
            Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y);
            SectionCanvas.Children.Add(rect);
        }
        private void AddLine(double x1, double y1, double x2, double y2, Brush brush)
        {
            SectionCanvas.Children.Add(new Line { X1 = x1, X2 = x2, Y1 = y1, Y2 = y2,
                Stroke = brush, StrokeThickness = 2.1 });
        }
        private void AddBar(double x, double y)
        {
            const double r = 6.5;
            var circle = new Ellipse { Width = r * 2, Height = r * 2, Stroke = Brushes.DarkRed,
                StrokeThickness = 1.2, Fill = Brushes.Red };
            Canvas.SetLeft(circle, x - r); Canvas.SetTop(circle, y - r);
            SectionCanvas.Children.Add(circle);
        }
    
        // ========== MẶT CẮT DỌC MINH HỌA ==========
        private void DrawLongSection()
        {
            if (LongSectionCanvas == null) return;
            LongSectionCanvas.Children.Clear();
            var c = ActiveConfig;
            var row = ActiveRow;
            if (c == null || row == null) return;
            double cW = LongSectionCanvas.ActualWidth, cH = LongSectionCanvas.ActualHeight;
            if (cW < 80 || cH < 80) return;

            double widthMm = row.Segment.WidthMm;
            double heightMm = row.Segment.TopMm - row.Segment.BottomMm;
            if (widthMm <= 0 || heightMm <= 0) return;

            // Tìm tầng trên nếu có (để vẽ thép chờ / nối)
            int segIdx = _segments.FindIndex(s => s.Id.Value == row.Segment.Id.Value);
            bool hasUpper = segIdx + 1 < _segments.Count;
            double upperWidthMm = hasUpper ? _segments[segIdx + 1].WidthMm : widthMm;
            double upperHeightMm = hasUpper ? (_segments[segIdx + 1].TopMm - _segments[segIdx + 1].BottomMm) : 0;
            var preview = _previewConnectionPlan;
            double upperCenterX = 0;
            if (preview != null)
            {
                upperCenterX = (preview.Upper.Center-preview.Lower.Center).Dot(preview.Lower.AxisX);
                upperWidthMm = Math.Abs(preview.Upper.AxisX.Dot(preview.Lower.AxisX))*preview.Upper.Width +
                    Math.Abs(preview.Upper.AxisY.Dot(preview.Lower.AxisX))*preview.Upper.Depth;
            }
            double beamH = c.JointBeamHeight;
            double upperGapMm = hasUpper ? _segments[segIdx + 1].BottomMm - row.Segment.TopMm : 0;

            // Chiều cao tổng cộng cần vẽ
            double starterMm = c.StarterBars ? c.StarterLength : 0;
            double totalDrawH = heightMm + Math.Max(beamH, Math.Max(starterMm, hasUpper ? upperGapMm + upperHeightMm * 0.4 : 0));
            double totalDrawW = Math.Max(widthMm, upperWidthMm + 2*Math.Abs(upperCenterX)) + 120;

            double pad = 18;
            double scale = Math.Min((cW - pad * 2) / totalDrawW, (cH - pad * 2) / totalDrawH);
            if (scale <= 0) return;

            double baseX = cW / 2;
            double baseY = cH - pad - 10;

            Action<double, double, double, double, Brush, double> addLn = (x1, y1, x2, y2, brush, thick) =>
            {
                LongSectionCanvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                    Stroke = brush, StrokeThickness = thick });
            };
            Action<double, double, string, Brush, double> addTxt = (x, y, text, brush, size) =>
            {
                var tb = new TextBlock { Text = text, FontSize = size, Foreground = brush };
                Canvas.SetLeft(tb, x); Canvas.SetTop(tb, y);
                LongSectionCanvas.Children.Add(tb);
            };
            Func<double, double> mmToY = mm => baseY - mm * scale;
            Func<double, double> mmToX = mm => baseX + mm * scale;

            double colHalfW = widthMm / 2;
            double colH = heightMm;

            // === Thân cột ===
            double cLeft = mmToX(-colHalfW), cRight = mmToX(colHalfW);
            double cTop = mmToY(colH), cBot = mmToY(0);
            var colRect = new Rectangle
            {
                Width = (cRight - cLeft), Height = (cBot - cTop),
                Stroke = new SolidColorBrush(Color.FromRgb(47, 143, 86)), StrokeThickness = 2.5,
                Fill = new SolidColorBrush(Color.FromArgb(25, 47, 143, 86))
            };
            Canvas.SetLeft(colRect, cLeft); Canvas.SetTop(colRect, cTop);
            LongSectionCanvas.Children.Add(colRect);

            // === Vùng dầm sàn ===
            double jointDrawHeight = hasUpper ? Math.Max(0, upperGapMm) : (c.JointTies ? beamH : 0);
            if (jointDrawHeight > 0)
            {
                double sTop = mmToY(colH + jointDrawHeight), sBot = mmToY(colH);
                double sHalfW = Math.Max(colHalfW, upperWidthMm / 2) + 40;
                var slabRect = new Rectangle
                {
                    Width = (mmToX(sHalfW) - mmToX(-sHalfW)), Height = (sBot - sTop),
                    Stroke = new SolidColorBrush(Color.FromRgb(140, 140, 140)), StrokeThickness = 1.5,
                    Fill = new SolidColorBrush(Color.FromArgb(30, 100, 100, 100))
                };
                Canvas.SetLeft(slabRect, mmToX(-sHalfW)); Canvas.SetTop(slabRect, sTop);
                LongSectionCanvas.Children.Add(slabRect);
                addTxt(mmToX(sHalfW) + 3, (sTop + sBot) / 2 - 7, hasUpper ? "Hở " + Math.Round(upperGapMm) + " mm" : "Nút dầm/sàn", Brushes.Gray, 9);
            }

            // === Đoạn cột tầng trên ===
            double upperDrawH = 0;
            if (hasUpper)
            {
                double uHalfW = upperWidthMm / 2;
                upperDrawH = Math.Min(upperHeightMm, Math.Max(starterMm - upperGapMm, upperHeightMm * 0.35));
                double uBot = colH + upperGapMm;
                double uTop = uBot + upperDrawH;
                double uLeft = mmToX(upperCenterX-uHalfW), uRight = mmToX(upperCenterX+uHalfW);
                double uTopY = mmToY(uTop), uBotY = mmToY(uBot);
                var upRect = new Rectangle
                {
                    Width = (uRight - uLeft), Height = (uBotY - uTopY),
                    Stroke = new SolidColorBrush(Color.FromRgb(47, 143, 86)), StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection(new[] { 6.0, 3.0 }),
                    Fill = new SolidColorBrush(Color.FromArgb(12, 47, 143, 86))
                };
                Canvas.SetLeft(upRect, uLeft); Canvas.SetTop(upRect, uTopY);
                LongSectionCanvas.Children.Add(upRect);
                addTxt(uRight + 3, uTopY + 3, "Tầng trên", Brushes.DimGray, 9);
            }

            // === Thép dọc chính thân cột ===
            double cover = c.Cover + c.TieDiameter + c.MainDiameter / 2;
            double barXL = -colHalfW + cover, barXR = colHalfW - cover;
            Brush barBrush = Brushes.Red;
            double barThick = 2.8;

            double barBot = c.Cover + c.MainDiameter / 2;
            double barTop = colH - c.Cover - c.MainDiameter / 2;
            if (preview != null)
            {
                Func<V3Point2,double> project=p=>(p-preview.Lower.Center).Dot(preview.Lower.AxisX);
                foreach(var pair in preview.Bars)
                {
                    double x=project(pair.Lower), endX=project(pair.End), upperX=project(pair.Upper);
                    addLn(mmToX(x),mmToY(barBot),mmToX(x),mmToY(colH-pair.Height),barBrush,1.5);
                    if(pair.Height>0)
                        addLn(mmToX(x),mmToY(colH-pair.Height),mmToX(endX),mmToY(colH),barBrush,1.5);
                    addLn(mmToX(endX),mmToY(colH),mmToX(endX),mmToY(colH+starterMm),Brushes.OrangeRed,1.5);
                    addLn(mmToX(upperX),mmToY(colH+upperGapMm+preview.Upper.Cover+preview.Upper.Diameter/2),
                        mmToX(upperX),mmToY(colH+upperGapMm+upperDrawH),Brushes.Firebrick,1.2);
                }
                addTxt(4,4,"Chiếu theo trục X cột dưới; các thanh có thể chồng hình.",Brushes.DimGray,9);
            }
            else
            {
                foreach(double x in new[]{barXL,barXR})
                    addLn(mmToX(x),mmToY(barBot),mmToX(x),mmToY(barTop),barBrush,barThick);
                if(hasUpper && c.StarterBars)
                    addTxt(4,4,"Chưa có phương án ghép hợp lệ — xem bảng so sánh.",Brushes.Firebrick,9);
            }
            if (c.StarterBars)
            {
                double sEnd = colH + starterMm, dimX = mmToX(colHalfW + 30);
                addLn(dimX, mmToY(colH), dimX, mmToY(sEnd), Brushes.DimGray, 0.8);
                addTxt(dimX + 4, (mmToY(colH) + mmToY(sEnd)) / 2 - 7,
                    "L=" + Math.Round(starterMm) + "mm", Brushes.DimGray, 8.5);
            }

            // === Thép đai ===
            Brush tieBrush = new SolidColorBrush(Color.FromRgb(0, 153, 157));
            try
            {
                var bands = V3TieLayoutPlan.Create(heightMm, c.Cover, c.TieDiameter,
                    c.EndZone, c.EndSpacing, c.MidSpacing, c.TieLayout == V3TieLayout.FixedNumber,
                    c.EndTieCount, c.MidTieCount, c.JointTies, c.JointBeamHeight, c.JointSpacing);
                foreach (var band in bands)
                    for (int i = 0; i < band.Count; i++)
                    {
                        double z = band.Position(i);
                        addLn(mmToX(-colHalfW + c.Cover), mmToY(z),
                            mmToX(colHalfW - c.Cover), mmToY(z), tieBrush, 0.8);
                    }
            }
            catch (InvalidOperationException ex)
            {
                addTxt(4, 4, ex.Message, Brushes.Firebrick, 9);
            }

            addTxt(cLeft + 3, cBot + 2, row.Segment.LevelName, Brushes.DarkGreen, 9.5);
            addTxt(cLeft + 3, cTop - 15, Math.Round(widthMm) + "×" + Math.Round(row.Segment.DepthMm) + " | H=" + Math.Round(heightMm) + "mm", Brushes.DimGray, 8.5);
        }
    }
}
