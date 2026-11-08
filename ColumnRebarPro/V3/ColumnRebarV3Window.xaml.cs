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
        private readonly Dictionary<long, V3RebarConfig> _configs = new Dictionary<long, V3RebarConfig>();
        private readonly ObservableCollection<FloorRow> _rows = new ObservableCollection<FloorRow>();
        private readonly List<BarSizeChoice> _sizes;
        private long? _activeId;
        private bool _loading = true;

        public DeploymentSettingsFile DeploymentSettings { get { return DeploymentHost.Children.OfType<V3DeploymentControl>().Single().Capture(); } }
        public bool Accepted { get; private set; }
        internal V3FoundationAnchorSettings FoundationAnchorSettings { get; private set; }
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
            }

            InitializeComponent();
            var workArea = SystemParameters.WorkArea;
            MinWidth = Math.Min(MinWidth, workArea.Width);
            MinHeight = Math.Min(MinHeight, workArea.Height);
            Width = Math.Min(Width, workArea.Width);
            Height = Math.Min(Height, workArea.Height);
            bool anchorEnabled = FoundationAnchorCheck.IsChecked == true;
            FoundationLengthBox.IsEnabled = anchorEnabled;
            FoundationCoverBox.IsEnabled = anchorEnabled;
            var deployment = new V3DeploymentControl();
            DeploymentHost.Children.Add(deployment);
            if (document != null) deployment.Initialize(document);
            RoutedEventHandler updateCaption = (sender, args) =>
                RunButton.Content = deployment.IsDeploymentEnabled ? "Tạo thép và bản vẽ" : "Tạo thép 3D";
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
                (!c.StarterBars || c.StarterLength > 0) &&
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
            StarterLengthBox.Text = c.StarterLength > 0 ? c.StarterLength.ToString(CultureInfo.InvariantCulture) : "";
            CoverBox.Text = c.Cover > 0 ? c.Cover.ToString(CultureInfo.InvariantCulture) : "";
            ZoneBox.Text = c.EndZone.ToString(CultureInfo.InvariantCulture);
            EndStepBox.Text = c.EndSpacing > 0 ? c.EndSpacing.ToString(CultureInfo.InvariantCulture) : "";
            MidStepBox.Text = c.MidSpacing > 0 ? c.MidSpacing.ToString(CultureInfo.InvariantCulture) : "";
            TieLayoutBox.SelectedIndex = c.TieLayout == V3TieLayout.FixedNumber ? 1 :
                c.TieLayout == V3TieLayout.MaximumSpacing ? 0 : -1;
            EndCountBox.Text = c.EndTieCount > 0 ? c.EndTieCount.ToString(CultureInfo.InvariantCulture) : "";
            MidCountBox.Text = c.MidTieCount > 0 ? c.MidTieCount.ToString(CultureInfo.InvariantCulture) : "";
            CrossCheck.IsChecked = c.CrossTies;
            CrossAlternateCheck.IsChecked = c.CrossTiesAlternating;
            JointTiesCheck.IsChecked = c.JointTies;
            JointHeightBox.Text = c.JointBeamHeight > 0 ? c.JointBeamHeight.ToString(CultureInfo.InvariantCulture) : "";
            BeamHeightBox.Text = c.JointBeamHeight > 0 ? c.JointBeamHeight.ToString(CultureInfo.InvariantCulture) : "";
            JointStepBox.Text = c.JointSpacing > 0 ? c.JointSpacing.ToString(CultureInfo.InvariantCulture) : "";
            DesignReferenceBox.Text = c.DesignReference ?? "";
            DesignConfirmedCheck.IsChecked = c.DesignValuesConfirmed;
            HookRadio.IsChecked = c.TieStyle == V3TieStyle.Hook135;
            LoopRadio.IsChecked = c.TieStyle == V3TieStyle.ClosedLoop;
            DetailText.Text = "Tầng " + row.Segment.LevelName + " | ElementId: " + row.Segment.Id.Value +
                " | Cao độ đáy " + Math.Round(row.Segment.BottomMm) + " mm";
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
                    c.StarterLength = ReadPositive(MainStarterLengthBox, "Chiều dài thép chờ theo thiết kế");
                    StarterLengthBox.Text = c.StarterLength.ToString(CultureInfo.InvariantCulture);
                    MainStarterLengthBox.Text = c.StarterLength.ToString(CultureInfo.InvariantCulture);
                }
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
                c.DesignReference = DesignReferenceBox.Text.Trim();
                c.DesignValuesConfirmed = DesignConfirmedCheck.IsChecked == true;
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
            UpdateFoundationAnchorSummary();
        }
        private void TieStyle_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SaveActive(false);
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
            if (_configs.Values.Any(c => !c.DesignValuesConfirmed || string.IsNullOrWhiteSpace(c.DesignReference)))
            {
                MessageBox.Show(this,
                    "Mỗi tầng cần mã/số bản vẽ thiết kế kết cấu đã duyệt và xác nhận đã đối chiếu các thông số. KStruct không tự xác nhận thiết kế.",
                    "KStruct - Thiếu hồ sơ thiết kế", MessageBoxButton.OK, MessageBoxImage.Warning);
                MainTabs.SelectedIndex = MainTabs.Items.Count - 1;
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
            try { FoundationAnchorSettings = CaptureFoundationAnchorSettings(); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "KStruct - Neo thép xuống móng", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Accepted = true;
            DialogResult = true;
        }
        private void FoundationAnchor_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            bool enabled = FoundationAnchorCheck.IsChecked == true;
            FoundationLengthBox.IsEnabled = enabled;
            FoundationCoverBox.IsEnabled = enabled;
            UpdateFoundationAnchorSummary();
        }

        private void UpdateFoundationAnchorSummary()
        {
            if (FoundationAnchorSummary == null) return;
            double length;
            if (!double.TryParse(FoundationLengthBox.Text.Trim().Replace(',', '.'), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out length) || length <= 0)
            {
                FoundationAnchorSummary.Text = "Nhập chiều dài neo chân L theo chi tiết thiết kế đã duyệt. KStruct không tính chiều dài neo.";
                return;
            }
            FoundationAnchorSummary.Text = "Chiều dài neo chân L lấy từ hồ sơ thiết kế: " +
                length.ToString("0.##", CultureInfo.InvariantCulture) + " mm.";
        }

        private V3FoundationAnchorSettings CaptureFoundationAnchorSettings()
        {
            if (FoundationAnchorCheck.IsChecked != true) return new V3FoundationAnchorSettings { Enabled = false };
            double length = ReadPositive(FoundationLengthBox, "Chiều dài neo theo thiết kế");
            double cover = ReadPositive(FoundationCoverBox, "Cover đáy móng");
            return new V3FoundationAnchorSettings
            {
                Enabled = true,
                CoverMm = cover,
                AnchorLengthMm = length
            };
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
            DrawLongSection();
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
            double beamH = c.JointBeamHeight;

            // Chiều cao tổng cộng cần vẽ
            double starterMm = c.StarterBars ? c.StarterLength : 0;
            double totalDrawH = heightMm + Math.Max(beamH, Math.Max(starterMm, upperHeightMm > 0 ? upperHeightMm * 0.4 : 0));
            double totalDrawW = Math.Max(widthMm, upperWidthMm) + 120;

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
            if (c.JointTies && beamH > 0)
            {
                double sTop = mmToY(colH + beamH), sBot = mmToY(colH);
                double sHalfW = Math.Max(colHalfW, upperWidthMm / 2) + 40;
                var slabRect = new Rectangle
                {
                    Width = (mmToX(sHalfW) - mmToX(-sHalfW)), Height = (sBot - sTop),
                    Stroke = new SolidColorBrush(Color.FromRgb(140, 140, 140)), StrokeThickness = 1.5,
                    Fill = new SolidColorBrush(Color.FromArgb(30, 100, 100, 100))
                };
                Canvas.SetLeft(slabRect, mmToX(-sHalfW)); Canvas.SetTop(slabRect, sTop);
                LongSectionCanvas.Children.Add(slabRect);
                addTxt(mmToX(sHalfW) + 3, (sTop + sBot) / 2 - 7, "Sàn/Dầm", Brushes.Gray, 9);
            }

            // === Đoạn cột tầng trên ===
            double upperDrawH = 0;
            if (hasUpper)
            {
                double uHalfW = upperWidthMm / 2;
                upperDrawH = Math.Max(starterMm, upperHeightMm * 0.35);
                double uBot = colH;
                double uTop = uBot + upperDrawH;
                double uLeft = mmToX(-uHalfW), uRight = mmToX(uHalfW);
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
            addLn(mmToX(barXL), mmToY(barBot), mmToX(barXL), mmToY(barTop), barBrush, barThick);
            addLn(mmToX(barXR), mmToY(barBot), mmToX(barXR), mmToY(barTop), barBrush, barThick);

            // === Thép chờ theo chiều dài nhập từ thiết kế ===
            if (c.StarterBars)
            {
                double sEnd = colH + starterMm;
                addLn(mmToX(barXL), mmToY(barTop), mmToX(barXL), mmToY(sEnd), Brushes.OrangeRed, 2.2);
                addLn(mmToX(barXR), mmToY(barTop), mmToX(barXR), mmToY(sEnd), Brushes.OrangeRed, 2.2);

                double dimX = mmToX(colHalfW + 30);
                addLn(dimX, mmToY(colH), dimX, mmToY(sEnd), Brushes.DimGray, 0.8);
                addLn(dimX - 3, mmToY(colH), dimX + 3, mmToY(colH), Brushes.DimGray, 0.8);
                addLn(dimX - 3, mmToY(sEnd), dimX + 3, mmToY(sEnd), Brushes.DimGray, 0.8);
                addTxt(dimX + 4, (mmToY(colH) + mmToY(sEnd)) / 2 - 7, "L=" + Math.Round(starterMm) + "mm", Brushes.DimGray, 8.5);
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
