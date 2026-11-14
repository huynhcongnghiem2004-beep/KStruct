using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
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
            public string UniqueId { get; set; }
            public string TypeName { get; set; }
            public string Label { get { return "D" + Mm.ToString("0.##", CultureInfo.InvariantCulture) +
                (string.IsNullOrWhiteSpace(TypeName) ? "" : " · " + TypeName); } }
            public override string ToString() { return Label; }
        }

        public sealed class FloorRow : INotifyPropertyChanged
        {
            public ColumnSegment Segment { get; set; }
            public string FloorTitle { get { return "▼ " + Segment.LevelName; } }
            public string HeightText { get { return "H = " + Math.Round(Segment.TopMm - Segment.BottomMm) + " (mm)"; } }
            public string SizeText { get { return "S = " + Math.Round(Segment.WidthMm) + "×" + Math.Round(Segment.DepthMm); } }
            private ImageSource _sectionPreview;
            public ImageSource SectionPreview
            {
                get { return _sectionPreview; }
                set { _sectionPreview = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("SectionPreview")); }
            }
            private string _tieTag;
            public string TieTag
            {
                get { return _tieTag; }
                set { _tieTag = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TieTag")); }
            }
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
        private readonly Dictionary<long, Tuple<bool, V3BeamJointInfo, string>> _beamDetectionCache =
            new Dictionary<long, Tuple<bool, V3BeamJointInfo, string>>();
        private readonly ObservableCollection<FloorRow> _rows = new ObservableCollection<FloorRow>();
        private readonly List<BarSizeChoice> _sizes;
        private long? _activeId;
        private bool _loading = true;
        private bool _compactSync;
        private double _longSectionZoom = 1.0;
        private double _longSectionPanX, _longSectionPanY;
        private Point _longSectionLastPanPoint;
        private bool _longSectionPanning;
        private V3DeploymentControl _deployment;
        private sealed class QuickBarChoice
        {
            public BarSizeChoice Size;
            public int Count;
            public override string ToString()
            {
                return Count + "Ø" + Size.Mm.ToString("0.##", CultureInfo.InvariantCulture) +
                    (string.IsNullOrWhiteSpace(Size.TypeName) ? "" : " (" + Size.TypeName + ")");
            }
        }


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
            // The dropdown is populated from actual RebarBarTypes in this RVT,
            // never from an unbound nominal diameter list.
            var loaded = new HashSet<double>(loadedSizes.Where(n => n >= 6 && n <= 40)
                .Select(n => Math.Round(n, 2)));
            _sizes = document == null
                ? loaded.Select(n => new BarSizeChoice { Mm = n }).OrderBy(x => x.Mm).ToList()
                : new Autodesk.Revit.DB.FilteredElementCollector(document)
                    .OfClass(typeof(Autodesk.Revit.DB.Structure.RebarBarType))
                    .Cast<Autodesk.Revit.DB.Structure.RebarBarType>()
                    .Where(t => !t.Name.StartsWith(V3BottleneckBarType.Prefix, StringComparison.Ordinal))
                    .Select(t => new BarSizeChoice {
                        Mm = Math.Round(V3Units.Mm(t.BarNominalDiameter), 2),
                        UniqueId = t.UniqueId, TypeName = t.Name
                    })
                    .Where(x => loaded.Contains(x.Mm))
                    .OrderBy(x => x.Mm).ThenBy(x => x.TypeName).ToList();
            if (_sizes.Count == 0)
                throw new InvalidOperationException("Project chưa có RebarBarType phù hợp. Hãy nạp thép vào Project.");
            var lastUsed = V3RebarConfigStorage.ReadLastUsed();
            for (int i = 0; i < _segments.Count; i++)
            {
                ColumnSegment segment = _segments[i];
                var initial = lastUsed != null && IsUsable(lastUsed) ? lastUsed.Copy() : new V3RebarConfig();
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
            Closing += (sender, args) =>
            {
                // Store the most recently edited floor profile for the next session.
                // This is user AppData only; it does not modify the RVT on Cancel.
                try
                {
                    if (SaveActive(false) && ActiveConfig != null && IsUsable(ActiveConfig))
                        V3RebarConfigStorage.StoreLastUsed(ActiveConfig.Copy());
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("KStruct column settings autosave: " + ex.Message);
                }
            };
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
            _deployment = deployment;
            DeploymentHost.Children.Add(deployment);
            if (document != null) deployment.Initialize(document);
            // Keep the mockup's primary-button caption stable. The drawing flag
            // is controlled by the deployment settings, not by changing the label.
            for (int n = 2; n <= 20; n++) { CxBox.Items.Add(n); CyBox.Items.Add(n); }
            foreach (BarSizeChoice size in _sizes) { MainBarBox.Items.Add(size); TieBarBox.Items.Add(size); }
            for (int n = 2; n <= 20; n++) { QuickCxBox.Items.Add(n); QuickCyBox.Items.Add(n); }
            foreach (BarSizeChoice size in _sizes) QuickTieBarBox.Items.Add(size);
            foreach (var label in new[] { "B20", "B25", "B30", "B35", "B40" }) QuickConcreteBox.Items.Add(label);
            foreach (var label in new[] { "CB300-V", "CB400-V", "CB500-V" }) QuickSteelGradeBox.Items.Add(label);
            QuickConcreteBox.SelectedItem = "B30";
            QuickSteelGradeBox.SelectedItem = "CB400-V";
            foreach (ColumnSegment s in _segments.OrderByDescending(s => s.BottomMm))
            {
                V3RebarConfig c = _configs[s.Id.Value];
                var floorRow = new FloorRow { Segment = s, BarText = FormatBars(c) };
                RefreshFloorPreview(floorRow, c);
                _rows.Add(floorRow);
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
        private static double ReadOptionalPositive(TextBox box, string caption)
        {
            return string.IsNullOrWhiteSpace(box.Text) ? 0 : ReadPositive(box, caption);
        }
        private static int ReadInteger(TextBox box, string caption)
        {
            int value;
            if (!int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < 1)
                throw new InvalidOperationException("Giá trị '" + caption + "' phải là số nguyên dương.");
            return value;
        }
        private void SetDiameter(ComboBox combo, double diameter, string uniqueId)
        {
            combo.SelectedItem = !string.IsNullOrWhiteSpace(uniqueId)
                ? _sizes.FirstOrDefault(x => x.UniqueId == uniqueId)
                : diameter > 0 && _sizes.Count(x => Math.Abs(x.Mm-diameter) <= .01) == 1
                    ? _sizes.First(x => Math.Abs(x.Mm-diameter) <= .01)
                    : null;
        }
        private void LoadHookBox(ComboBox box,string selected,double degrees)
        {
            var type = SelectedTieBarType();
            var hooks = V3ProjectRebar.Hooks(_document, type);
            var compatible = hooks.Where(h => h.TieStyle && h.Allowed &&
                Math.Abs(h.Angle-degrees*Math.PI/180)<1e-6).ToList();
            var choices=new List<V3HookChoice> {new V3HookChoice {Id="",Label="Tự nhận Hook từ thép đai mẫu trong project"}};
            choices.AddRange(compatible);
            bool stale = !string.IsNullOrEmpty(selected) && !compatible.Any(h=>h.Id==selected);
            if(stale)
                choices.Add(new V3HookChoice {Id=selected,Label="Móc đã lưu không còn hợp lệ — chọn lại"});

            string suggested = !string.IsNullOrEmpty(selected) ? selected :
                compatible.Count == 1 ? compatible[0].Id :
                type == null ? null :
                V3ProjectRebar.SuggestHook(_document, type, degrees,
                    ActiveRow?.Segment?.Id);

            box.ItemsSource=choices;
            box.SelectedItem=choices.FirstOrDefault(h=>h.Id==suggested) ?? choices[0];
            box.ToolTip = stale
                ? "Hook đã lưu không phù hợp; hãy xác nhận lại."
                : compatible.Count > 1 && string.IsNullOrEmpty(suggested)
                    ? "Có nhiều Hook khác nhau và chưa có mẫu thống nhất. Hãy chọn đúng Hook theo thiết kế."
                    : string.IsNullOrEmpty(suggested)
                        ? "Không có Hook phù hợp trong project."
                        : "Đã xác định Hook từ cấu hình/mẫu Rebar hợp lệ của project.";
        }

        private Autodesk.Revit.DB.Structure.RebarBarType SelectedBarType(ComboBox diameterBox)
        {
            if (_document == null) return null;
            var size = diameterBox.SelectedItem as BarSizeChoice;
            if (size == null) return null;
            return _document.GetElement(size.UniqueId) as Autodesk.Revit.DB.Structure.RebarBarType;
        }

        private Autodesk.Revit.DB.Structure.RebarBarType SelectedTieBarType()
        {
            return SelectedBarType(TieBarBox);
        }

        private void RefreshTypeChoices(V3RebarConfig config)
        {
            if (config == null) return;
            // Replacing ComboBox.ItemsSource raises SelectionChanged. Suppress those
            // intermediate events so they cannot overwrite a saved Hook ID with "".
            bool wasLoading = _loading;
            _loading = true;
            try
            {
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
            finally { _loading = wasLoading; }
        }

        private static void LoadShapeBox(ComboBox box, IEnumerable<V3RebarShapeChoice> shapes, string selected)
        {
            var available = shapes.ToList();
            string chosenId = V4ShapeAutoSelection.Choose(selected,
                available.Select(choice => choice.UniqueId));
            var choices = new List<V3RebarShapeChoice> {
                new V3RebarShapeChoice { UniqueId = "",
                    Label = available.Count == 0 ? "— Không có Shape phù hợp trong project —" :
                        available.Count == 1 ? "— Chọn Shape có sẵn —" :
                        "— Có " + available.Count + " Shape phù hợp; hãy chọn rõ —" }
            };
            choices.AddRange(available);
            bool stale = !string.IsNullOrWhiteSpace(selected) && chosenId == null;
            if (stale)
                choices.Add(new V3RebarShapeChoice { UniqueId = selected,
                    Label = "Shape đã lưu không còn phù hợp — chọn lại" });
            box.ItemsSource = choices;
            // Never auto-select when the saved identity is stale: silently replacing the
            // existing shape can change the approved reinforcement geometry.
            box.SelectedItem = stale
                ? choices.Last()
                : choices.FirstOrDefault(choice => choice.UniqueId == chosenId) ?? choices[0];
            box.ToolTip = stale
                ? "Shape đã lưu không còn phù hợp với loại thép/kiểu đai hiện tại."
                : available.Count == 0
                    ? "Không tìm được Shape đáp ứng đồng thời kiểu thanh, số đoạn, Hook và RebarBarType."
                    : available.Count > 1 && chosenId == null
                        ? "Có nhiều Shape hợp lệ trong project. Phải chọn một Shape rõ ràng."
                        : "Dùng RebarShape có sẵn trong project, không tạo Shape mới.";
        }

        private void LoadActive()
        {
            var c = ActiveConfig;
            var row = ActiveRow;
            if (c == null || row == null) return;
            _loading = true;
            CxBox.SelectedItem = c.Cx;
            CyBox.SelectedItem = c.Cy;
            SetDiameter(MainBarBox, c.MainDiameter, c.MainBarTypeUniqueId);
            SetDiameter(TieBarBox, c.TieDiameter, c.TieBarTypeUniqueId);
            GroupLongCheck.IsChecked = c.GroupLongBars;
            GroupTiesCheck.IsChecked = true;
            MainStarterCheck.IsChecked = c.StarterBars;
            MainStarterLengthBox.Text = c.StarterLength > 0 ? c.StarterLength.ToString(CultureInfo.InvariantCulture) : "";
            StarterCheck.IsChecked = c.StarterBars;
            BottleneckRibbedCheck.IsChecked = c.BottleneckRibbed;
            AllowExistingBendCheck.IsChecked = c.AllowExistingUndersizeBend;
            BottleneckClearanceBox.Text = (c.BottleneckClearanceMm >= 0 ? c.BottleneckClearanceMm : 2).ToString(CultureInfo.InvariantCulture);
            BottleneckSlopeBox.Text = (c.BottleneckSlope > 0 ? c.BottleneckSlope : 6).ToString(CultureInfo.InvariantCulture);
            StarterLengthBox.Text = c.StarterLength > 0 ? c.StarterLength.ToString(CultureInfo.InvariantCulture) : "";
            SupplementalAnchorLengthBox.Text = c.SupplementalAnchorLengthMm > 0
                ? c.SupplementalAnchorLengthMm.ToString(CultureInfo.InvariantCulture) : "";
            BeamTopCoverBox.Text = c.BeamTopCoverMm > 0
                ? c.BeamTopCoverMm.ToString(CultureInfo.InvariantCulture) : "";
            CoverBox.Text = c.Cover > 0 ? c.Cover.ToString(CultureInfo.InvariantCulture) : "";
            ZoneBox.Text = c.EndZone.ToString(CultureInfo.InvariantCulture);
            EndStepBox.Text = c.EndSpacing > 0 ? c.EndSpacing.ToString(CultureInfo.InvariantCulture) : "";
            MidStepBox.Text = c.MidSpacing > 0 ? c.MidSpacing.ToString(CultureInfo.InvariantCulture) : "";
            TieLayoutBox.SelectedIndex = 0;
            EndCountBox.Text = c.EndTieCount > 0 ? c.EndTieCount.ToString(CultureInfo.InvariantCulture) : "";
            MidCountBox.Text = c.MidTieCount > 0 ? c.MidTieCount.ToString(CultureInfo.InvariantCulture) : "";
            RefreshTypeChoices(c);
            CrossCheck.IsChecked = c.CrossTies;
            CrossAlternateCheck.IsChecked = c.CrossTiesAlternating;
            JointTiesCheck.IsChecked = c.JointTies;
            QuickJointTiesCheck.IsChecked = c.ShowJointTies;
            JointHeightBox.Text = c.JointBeamHeight > 0 ? c.JointBeamHeight.ToString(CultureInfo.InvariantCulture) : "";
            BeamHeightBox.Text = c.JointBeamHeight > 0 ? c.JointBeamHeight.ToString(CultureInfo.InvariantCulture) : "";
            JointStepBox.Text = c.JointSpacing > 0 ? c.JointSpacing.ToString(CultureInfo.InvariantCulture) : "";
            HookRadio.IsChecked = c.TieStyle == V3TieStyle.Hook135;
            LoopRadio.IsChecked = c.TieStyle == V3TieStyle.ClosedLoop;
            _loading = false;
            TieLayoutControlState();
            UpdateSummary();
            UpdateFoundationAnchorSummary();
            SyncCompactFromLegacy();
            RefreshLivePreviews();
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
                c.MainBarTypeUniqueId = main.UniqueId ?? "";
                c.TieDiameter = tie.Mm;
                c.TieBarTypeUniqueId = tie.UniqueId ?? "";
                c.Cover = ReadPositive(CoverBox, "Lớp bảo vệ");
                c.EndZone = ReadPositive(ZoneBox, "Chiều dài vùng đai đầu", true);
                c.TieLayout = V3TieLayout.MaximumSpacing;
                c.EndSpacing = ReadPositive(EndStepBox, "Bước đai hai đầu");
                c.MidSpacing = ReadPositive(MidStepBox, "Bước đai giữa");
                // Inactive spacing values are unused in Fixed Number, but keep the stored profile valid.
                if (c.EndSpacing <= 0) c.EndSpacing = 1;
                if (c.MidSpacing <= 0) c.MidSpacing = 1;
                c.StarterBars = (MainStarterCheck.IsChecked == true || StarterCheck.IsChecked == true);
                c.SupplementalAnchorLengthMm = ReadOptionalPositive(SupplementalAnchorLengthBox,
                    "Chiều dài neo thép bổ sung trong dầm");
                c.BeamTopCoverMm = ReadOptionalPositive(BeamTopCoverBox,
                    "Lớp bảo vệ phía trên dầm");
                if (c.StarterBars)
                {
                    c.StarterLength = ReadPositive(MainStarterLengthBox, "Chiều dài chờ từ đỉnh cột dưới");
                    c.BottleneckClearanceMm = string.IsNullOrWhiteSpace(BottleneckClearanceBox.Text)
                        ? 2
                        : ReadPositive(BottleneckClearanceBox, "Khe hở cổ chai", true);
                    c.BottleneckSlope = string.IsNullOrWhiteSpace(BottleneckSlopeBox.Text)
                        ? 6
                        : ReadPositive(BottleneckSlopeBox, "Tỷ lệ xiên 1:n");
                    c.BottleneckRibbed = BottleneckRibbedCheck.IsChecked == true;
                    c.AllowExistingUndersizeBend = AllowExistingBendCheck.IsChecked == true;
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
                c.ShowJointTies = QuickJointTiesCheck.IsChecked == true;
                c.JointTies = false; // Legacy starter-zone setting remains disabled.
                double beamHeight = ReadOptionalPositive(QuickBeamHeightBox, "Cao dầm sàn");
                if (c.JointTies)
                {
                    c.JointBeamHeight = ReadPositive(JointHeightBox, "Chiều cao nút dầm sàn");
                    c.JointSpacing = ReadPositive(JointStepBox, "Bước đai trong nút dầm");
                }
                else
                {
                    c.JointBeamHeight = beamHeight;
                    c.JointSpacing = 0;
                }
                BeamHeightBox.Text = c.JointBeamHeight > 0
                    ? c.JointBeamHeight.ToString(CultureInfo.InvariantCulture)
                    : "";
                c.GroupLongBars = GroupLongCheck.IsChecked == true;
                c.GroupTies = true;
                if (LoopRadio.IsChecked != true && HookRadio.IsChecked != true)
                    throw new InvalidOperationException("Chọn hình dạng đai theo chi tiết thiết kế đã duyệt.");
                c.TieStyle = LoopRadio.IsChecked == true ? V3TieStyle.ClosedLoop : V3TieStyle.Hook135;
                _configs[_activeId.Value] = c;
                var row = ActiveRow;
                if (row != null) RefreshFloorPreview(row, c);
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
            bool enabled = false; // Maximum Spacing is the only user-facing tie layout
            EndStepBox.IsEnabled = !enabled;
            MidStepBox.IsEnabled = !enabled;
            EndCountBox.IsEnabled = enabled;
            MidCountBox.IsEnabled = enabled;
            GroupTiesCheck.IsEnabled = false;
            TieHook135Box.IsEnabled = HookRadio.IsChecked == true || CrossCheck.IsChecked == true;
            TieHook90Box.IsEnabled = CrossCheck.IsChecked == true && CrossAlternateCheck.IsChecked == true;
            CrossAlternateCheck.IsEnabled = CrossCheck.IsChecked == true;
            JointTiesCheck.IsEnabled = false;
            JointHeightBox.IsEnabled = false;
            JointStepBox.IsEnabled = false;
        }


        private void SyncCompactFromLegacy()
        {
            if (_compactSync || QuickMainBarBox == null || ActiveConfig == null) return;
            _compactSync = true;
            try
            {
                int count = ActiveConfig.BarCount;
                var current = MainBarBox.SelectedItem as BarSizeChoice;
                QuickMainBarBox.Items.Clear();
                QuickBarChoice selected = null;
                foreach (var size in _sizes)
                {
                    var option = new QuickBarChoice { Size = size, Count = count };
                    QuickMainBarBox.Items.Add(option);
                    if (current != null && option.Size.UniqueId == current.UniqueId &&
                        Math.Abs(option.Size.Mm - current.Mm) < .01) selected = option;
                }
                QuickMainBarBox.SelectedItem = selected;
                QuickCxBox.SelectedItem = CxBox.SelectedItem;
                QuickCyBox.SelectedItem = CyBox.SelectedItem;
                QuickWidthBox.Text = Math.Round(ActiveRow.Segment.WidthMm).ToString(CultureInfo.InvariantCulture);
                QuickHeightBox.Text = Math.Round(ActiveRow.Segment.DepthMm).ToString(CultureInfo.InvariantCulture);
                QuickBeamHeightBox.Text = BeamHeightBox.Text;
                SetDiameter(QuickTieBarBox, ActiveConfig.TieDiameter, ActiveConfig.TieBarTypeUniqueId);
                string diameter = ActiveConfig.TieDiameter.ToString("0.##", CultureInfo.InvariantCulture);
                QuickCoverBox.Text = CoverBox.Text;
                QuickZoneBox.Text = ZoneBox.Text;
                QuickEndBox.Text = EndStepBox.Text;
                QuickMidBox.Text = MidStepBox.Text;
                QuickStarterCheck.IsChecked = MainStarterCheck.IsChecked == true;
                QuickJointTiesCheck.IsChecked = ActiveConfig.ShowJointTies;
                QuickTieStyleBox.SelectedIndex = LoopRadio.IsChecked == true ? 1 : 0;
                QuickDrawingCheck.IsChecked = _deployment != null && _deployment.IsDeploymentEnabled;
            }
            finally { _compactSync = false; }
        }

        // The mockup fields are shortcuts to the original V4 controls. No
        // parallel reinforcement configuration is stored.
        private void QuickInput_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || _compactSync || !_activeId.HasValue) return;
            _compactSync = true;
            try
            {
                if (ReferenceEquals(sender, QuickMainBarBox))
                {
                    var selected = QuickMainBarBox.SelectedItem as QuickBarChoice;
                    if (selected != null) MainBarBox.SelectedItem = selected.Size;
                }
                else if (ReferenceEquals(sender, QuickCxBox)) { CxBox.SelectedItem = QuickCxBox.SelectedItem; }
                else if (ReferenceEquals(sender, QuickCyBox)) { CyBox.SelectedItem = QuickCyBox.SelectedItem; }
                else if (ReferenceEquals(sender, QuickTieBarBox)) { TieBarBox.SelectedItem = QuickTieBarBox.SelectedItem; }
                else if (ReferenceEquals(sender, QuickTieStyleBox))
                {
                    if (QuickTieStyleBox.SelectedIndex == 1) LoopRadio.IsChecked = true;
                    else HookRadio.IsChecked = true;
                }
                else if (ReferenceEquals(sender, QuickStarterCheck))
                {
                    MainStarterCheck.IsChecked = QuickStarterCheck.IsChecked;
                    StarterCheck.IsChecked = QuickStarterCheck.IsChecked;
                }
                else if (ReferenceEquals(sender, QuickJointTiesCheck))
                {
                    ActiveConfig.ShowJointTies = QuickJointTiesCheck.IsChecked == true;
                }
            }
            finally { _compactSync = false; }
            // A checked starter requires a genuine engineer-specified length,
            // which is intentionally edited in the advanced tab.
            if (ReferenceEquals(sender, QuickStarterCheck) && QuickStarterCheck.IsChecked == true &&
                string.IsNullOrWhiteSpace(MainStarterLengthBox.Text))
            {
                MessageBox.Show(this, "Hãy nhập chiều dài thép chờ ở Thiết lập nâng cao → Thép chờ / Neo móng trước khi tạo.",
                    "KStruct - Thép chờ", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            SaveActive(false);
            SyncCompactFromLegacy();
            RefreshLivePreviews();
        }

        private static string QuickNumber(string value, string caption)
        {
            string clean = (value ?? "").Replace("mm", "").Trim();
            if (!double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                || double.IsNaN(parsed) || double.IsInfinity(parsed) || parsed <= 0)
                throw new InvalidOperationException(caption + " phải là số mm dương.");
            return parsed.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private string QuickTieSpacing(string notation, string caption)
        {
            string text = (notation ?? "").Replace(" ", "").ToLowerInvariant();
            int divider = text.IndexOf('a');
            if (!text.StartsWith("ø") || divider < 2)
                throw new InvalidOperationException(caption + ": nhập dạng Ø8a100.");
            string inputDiameter = QuickNumber(text.Substring(1, divider - 1), caption + " - đường kính");
            string actualDiameter = ActiveConfig.TieDiameter.ToString("0.##", CultureInfo.InvariantCulture);
            if (inputDiameter != actualDiameter)
                throw new InvalidOperationException(caption + ": đường kính đai hiện chọn là Ø" + actualDiameter +
                    ". Để đổi đường kính đai, sử dụng Thiết lập nâng cao.");
            return QuickNumber(text.Substring(divider + 1), caption + " - bước đai");
        }

        private void QuickText_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loading || _compactSync || !_activeId.HasValue) return;
            try
            {
                // Validate *all* visible fields before changing any legacy values.
                string cover = QuickNumber(QuickCoverBox.Text, "Lớp bảo vệ");
                string zone = QuickNumber(QuickZoneBox.Text, "Chiều dài vùng đầu");
                string end = QuickNumber(QuickEndBox.Text, "Bước đai dày");
                string mid = QuickNumber(QuickMidBox.Text, "Bước đai thưa");
                string beam = QuickNumber(QuickBeamHeightBox.Text, "Cao dầm sàn");
                _compactSync = true;
                try
                {
                    CoverBox.Text = cover;
                    ZoneBox.Text = zone;
                    EndStepBox.Text = end;
                    MidStepBox.Text = mid;
                    BeamHeightBox.Text = beam;
                    JointHeightBox.Text = beam;
                }
                finally { _compactSync = false; }
                if (!SaveActive(true))
                {
                    LoadActive();
                    return;
                }
                SyncCompactFromLegacy();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "KStruct - Dữ liệu thép", MessageBoxButton.OK, MessageBoxImage.Warning);
                SyncCompactFromLegacy();
            }
        }

        private void ShowAdvanced(bool show)
        {
            // Both editors now live inside Tab 1; switch its nested view, not the entire window.
            MainTabs.SelectedIndex = 0;
            ColumnModeTabs.SelectedIndex = 0;
            if (QuickAdvancedCheck.IsChecked != false)
            {
                _compactSync = true;
                QuickAdvancedCheck.IsChecked = false;
                _compactSync = false;
            }
        }
        private void QuickAdvanced_Click(object sender, RoutedEventArgs e)
        {
            // Always allow opening Advanced to fix an incomplete design profile.
            SaveActive(false);
            string caption = (sender as Button)?.Content?.ToString() ?? "";
            ShowAdvanced(false);
            if (caption.Contains("Hook")) MainTabs.SelectedIndex = 1;
            else if (caption.Contains("Neo")) MainTabs.SelectedIndex = 2;
            else MainTabs.SelectedIndex = 3;
        }
        private void QuickAdvanced_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || _compactSync) return;
            if (QuickAdvancedCheck.IsChecked == true)
                QuickAdvanced_Click(sender, e);
            else ShowAdvanced(false);
        }
        private void QuickBack_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveActive(true)) return;
            SyncCompactFromLegacy();
            ShowAdvanced(false);
        }
        private void QuickDrawing_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading || _compactSync || _deployment == null) return;
            _deployment.SetDeploymentEnabled(QuickDrawingCheck.IsChecked == true);
        }
        private void QuickPreview_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveActive(true)) return;
            DrawSection();
            DrawLongSection();
            var config = ActiveConfig;
            MessageBox.Show(this, config == null ? "Chưa chọn tầng." :
                "Tầng: " + ActiveRow.Segment.LevelName +
                "\nThép chủ: " + config.BarCount + "Ø" + config.MainDiameter +
                "\nĐai hai đầu/giữa: Ø" + config.TieDiameter + "a" +
                config.EndSpacing + " / a" + config.MidSpacing +
                "\nĐây là bản xem trước hình học; không thay thế kiểm tra kết cấu và bản vẽ thi công.",
                "KStruct - Xem trước dữ liệu", MessageBoxButton.OK, MessageBoxImage.Information);
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
            SyncCompactFromLegacy();
            RefreshLivePreviews();
        }
        private void TieStyle_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (SaveActive(false) && ActiveConfig != null)
                V4StackTieSettings.Apply(ActiveConfig, _configs.Values);
            RefreshTypeChoices(ActiveConfig);
            SyncCompactFromLegacy();
        }
        private void Text_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (ReferenceEquals(sender, BeamHeightBox)) JointHeightBox.Text = BeamHeightBox.Text;
            else if (ReferenceEquals(sender, JointHeightBox)) BeamHeightBox.Text = JointHeightBox.Text;
            else if (ReferenceEquals(sender, MainStarterLengthBox)) StarterLengthBox.Text = MainStarterLengthBox.Text;
            else if (ReferenceEquals(sender, StarterLengthBox)) MainStarterLengthBox.Text = StarterLengthBox.Text;
            SaveActive(false);
            SyncCompactFromLegacy();
            RefreshLivePreviews();
        }
        private void RefreshLivePreviews()
        {
            DrawSection();
            DrawLongSection();
        }

        private void SectionCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RefreshLivePreviews();
        }
        private void ApplyAll_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveActive(true)) return;
            var c = ActiveConfig;
            if (c == null) return;
            foreach (ColumnSegment s in _segments)
                _configs[s.Id.Value] = c.Copy();
            foreach (FloorRow row in _rows)
                RefreshFloorPreview(row, _configs[row.Segment.Id.Value]);
            LoadActive();
            MessageBox.Show(this, "Đã áp dụng cấu hình cho " + _segments.Count + " tầng trong cửa sổ. Chưa thay đổi mô hình Revit.",
                "KStruct", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        // V5 is a read-only advisory preflight. V4 remains the sole production writer.
        // Do not interpret V5InputReview.CanCreateRebar=false as permission to run its pilot writer.
        private void V5Review_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveActive(true)) return;
            var messages = new List<string>();
            foreach (var segment in _segments)
            {
                V3RebarConfig cfg;
                if (!_configs.TryGetValue(segment.Id.Value, out cfg))
                {
                    messages.Add(segment.LevelName + ": thiếu cấu hình V4.");
                    continue;
                }
                var host = _document == null ? null : _document.GetElement(segment.Id);
                var input = new SDK_HD.KStruct.Core.V5.V5RebarInput
                {
                    Module = SDK_HD.KStruct.Core.V5.V5StructuralModule.Column,
                    HostModule = SDK_HD.KStruct.Core.V5.V5StructuralModule.Column,
                    HostUniqueId = host == null ? "" : host.UniqueId,
                    WidthMm = segment.WidthMm,
                    HeightMm = segment.DepthMm,
                    CoverMm = cfg.Cover,
                    MainBarDiameterMm = cfg.MainDiameter,
                    TieDiameterMm = cfg.TieDiameter,
                    TieSpacingMm = cfg.MidSpacing,
                    MainBarCount = 2 * (cfg.Cx + cfg.Cy) - 4
                };
                var review = SDK_HD.KStruct.Core.V5.V5RebarInputPolicy.Review(input,
                    _sizes.Select(x => x.Mm));
                var prefix = segment.LevelName + ": ";
                messages.Add(prefix + (review.IsValid
                    ? "Dữ liệu V5 hợp lệ ở mức kiểm tra đầu vào."
                    : string.Join("; ", review.Errors)));
                foreach (var warning in review.Warnings)
                    messages.Add("  • " + warning);
                // V5 host geometry inventory is diagnostic evidence, never permission to write.
                if (host != null)
                {
                    var inventory = SDK_HD.KStruct.Core.V5.V5HostSolidInspector.Inspect(host);
                    messages.Add("  Solid: " + inventory.SolidCount
                        + " | ứng viên bê tông trực tiếp: " + inventory.DirectConcreteCandidateSolids
                        + " | ứng viên bê tông nested: " + inventory.NestedConcreteCandidateSolids
                        + " | hình học bị cắt ngắn: " + (inventory.Truncated ? "có" : "không"));
                    foreach (var warning in inventory.Warnings)
                        messages.Add("  • " + warning);
                    var gate = SDK_HD.KStruct.Core.V5.V5ExecutionGatePolicy.Review(
                        new SDK_HD.KStruct.Core.V5.V5ExecutionEvidence
                        {
                            HostSolidIsConcrete = inventory.ConcreteVerified,
                            PhysicalCoverFacesVerified = inventory.CoverVerified
                        });
                    foreach (var blocker in gate.Blockers)
                        messages.Add("  CHẶN V5: " + blocker);
                }
            }
            messages.Add("");
            messages.Add("V5 chưa được cấp phép tạo thép: Solid, cover, nút giao, neo/nối và transaction chưa được xác minh. Nút này KHÔNG sửa mô hình.");
            MessageBox.Show(this, string.Join(Environment.NewLine, messages),
                "KStruct · V5 kiểm tra đầu vào (chỉ đọc)", MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveActive(true)) return;
            // One explicit Hook selection on a floor should apply to other floors
            // using the same tie diameter, unless they already have their own IDs.
            V4StackTieSettings.Apply(ActiveConfig, _configs.Values);
            V4FloorHookInheritance.Apply(_configs.Values);
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
                    ApplyDetectedBeamInfo(works);
                    foreach (var candidate in works)
                        _configs[candidate.Segment.Id.Value] = candidate.Config.Copy();
                    V3StarterConnection.Resolve(works);
                    V3RebarValidator.Preflight(works);
                    // Report the exact floor if its project Hook remains ambiguous.
                    foreach (var candidate in works)
                    {
                        try
                        {
                            V3RebarBuilder.ValidateProjectTypes(_document, new List<V3SegmentWork> { candidate });
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException(
                                "Tầng " + candidate.Column.Id.Value + ": " + ex.Message, ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MainTabs.SelectedIndex = ex.Message.IndexOf("móc",StringComparison.OrdinalIgnoreCase)>=0 ||
                    ex.Message.Contains("Hook") || ex.Message.Contains("Shape") ? 1 : 2;
                // Keep the quick editor active; route to the relevant main tab.
                ColumnModeTabs.SelectedIndex = 0;
                MessageBox.Show(this, ex.Message, "KStruct - Kiểm tra tuyến cột", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Accepted = true;
            DialogResult = true;
        }

        private string ApplyDetectedBeamInfo(V3SegmentWork lower, V3SegmentWork upper, bool updateControls)
        {
            if (_document == null || lower == null || upper == null || !lower.Config.StarterBars)
                return null;
            int lowerCount = lower.Config.BarCount, upperCount = upper.Config.BarCount;
            bool needsJoint = lowerCount != upperCount ||
                Math.Min(lowerCount, upperCount) > Math.Max(lowerCount, upperCount) / 2;
            if (!needsJoint) return null;

            Tuple<bool, V3BeamJointInfo, string> detection;
            long cacheKey = lower.Segment.Id.Value;
            bool shouldRecheck = !_beamDetectionCache.TryGetValue(cacheKey, out detection) ||
                                 (!detection.Item1 && lower.Column != null && upper.Column != null);
            if (shouldRecheck)
            {
                V3BeamJointInfo detected;
                string detectionReason;
                bool found = V3BeamJointDetector.TryDetect(_document, lower, upper,
                    out detected, out detectionReason);
                detection = Tuple.Create(found, detected, detectionReason);
                _beamDetectionCache[cacheKey] = detection;
            }
            if (detection.Item1)
            {
                V3BeamJointInfo info = detection.Item2;
                lower.Config.JointBeamHeight = info.HeightMm;
                lower.Config.BeamTopCoverMm = info.TopCoverMm;
                if (updateControls && _activeId.HasValue && _activeId.Value == lower.Segment.Id.Value)
                {
                    bool wasLoading = _loading;
                    _loading = true;
                    string value = info.HeightMm.ToString("0.##", CultureInfo.InvariantCulture);
                    QuickBeamHeightBox.Text = value;
                    BeamHeightBox.Text = value;
                    JointHeightBox.Text = value;
                    BeamTopCoverBox.Text = info.TopCoverMm.ToString("0.##", CultureInfo.InvariantCulture);
                    _loading = wasLoading;
                }
                return "Tự nhận diện " + info.BeamName + (info.MatchingBeamCount > 1
                    ? " (" + info.MatchingBeamCount + " dầm cùng cao độ)" : "") +
                    ": cao " + Math.Round(info.HeightMm, 1) + " mm, cover mặt trên " +
                    Math.Round(info.TopCoverMm, 1) + " mm.";
            }

            string reason = detection.Item3;
            if (lower.Config.JointBeamHeight <= 0 || lower.Config.BeamTopCoverMm <= 0)
                return reason;
            return "Không tự nhận diện được dầm (" + reason + "); đang dùng chiều cao/cover nhập tay.";
        }

        private void ApplyDetectedBeamInfo(IList<V3SegmentWork> works)
        {
            for (int i = 0; i + 1 < works.Count; i++)
            {
                var lower = works[i];
                var upper = works[i + 1];
                string reason = ApplyDetectedBeamInfo(lower, upper, false);
                bool mismatch = lower.Config.BarCount != upper.Config.BarCount;
                bool moreThanHalf = Math.Min(lower.Config.BarCount, upper.Config.BarCount) >
                    Math.Max(lower.Config.BarCount, upper.Config.BarCount) / 2;
                if (lower.Config.StarterBars && (mismatch || moreThanHalf) &&
                    (lower.Config.JointBeamHeight <= 0 || lower.Config.BeamTopCoverMm <= 0))
                    throw new InvalidOperationException("Không thể tự nhận diện đủ chiều cao dầm và lớp bảo vệ mặt trên cho nút " +
                        lower.Segment.LevelName + " → " + upper.Segment.LevelName + ". " + reason);
            }
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

        // Lightweight, dimensioned cross-section for every floor in the stack.
        // This preview depicts the configured Cx/Cy and is not a native Revit View.
        private static void RefreshFloorPreview(FloorRow row, V3RebarConfig config)
        {
            row.BarText = FormatBars(config);
            row.TieTag = config.TieDiameter > 0
                ? "Đai Ø" + config.TieDiameter.ToString("0.##", CultureInfo.InvariantCulture) +
                    " a" + config.EndSpacing.ToString("0.##", CultureInfo.InvariantCulture) +
                    "/" + config.MidSpacing.ToString("0.##", CultureInfo.InvariantCulture)
                : "Đai: chưa cấu hình";
            var drawing = new DrawingGroup();
            using (var dc = drawing.Open())
            {
                const double width = 120, height = 86;
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
                var concrete = new Pen(Brushes.SeaGreen, 1.5);
                var tie = new Pen(Brushes.DarkCyan, 1.7);
                double w = Math.Max(35, Math.Min(96, row.Segment.WidthMm /
                    Math.Max(row.Segment.WidthMm, row.Segment.DepthMm) * 88));
                double h = Math.Max(35, Math.Min(63, row.Segment.DepthMm /
                    Math.Max(row.Segment.WidthMm, row.Segment.DepthMm) * 63));
                double x = (width-w)/2, y = (height-h)/2;
                dc.DrawRectangle(null, concrete, new Rect(x,y,w,h));
                dc.DrawRectangle(null, tie, new Rect(x+5,y+5,Math.Max(8,w-10),Math.Max(8,h-10)));
                if (config.Cx >= 2 && config.Cy >= 2)
                {
                    var fill = Brushes.Firebrick;
                    double x1=x+9, x2=x+w-9, y1=y+9, y2=y+h-9;
                    for (int i=0;i<config.Cx;i++)
                    {
                        double px=x1+(x2-x1)*i/(config.Cx-1);
                        dc.DrawEllipse(fill, null, new Point(px,y1), 2.2, 2.2);
                        dc.DrawEllipse(fill, null, new Point(px,y2), 2.2, 2.2);
                    }
                    for (int i=1;i<config.Cy-1;i++)
                    {
                        double py=y1+(y2-y1)*i/(config.Cy-1);
                        dc.DrawEllipse(fill, null, new Point(x1,py), 2.2, 2.2);
                        dc.DrawEllipse(fill, null, new Point(x2,py), 2.2, 2.2);
                    }
                }
            }
            drawing.Freeze();
            var image = new DrawingImage(drawing);
            image.Freeze();
            row.SectionPreview = image;
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
                var lowerWork = new V3SegmentWork
                {
                    Column = _document?.GetElement(row.Segment.Id) as Autodesk.Revit.DB.FamilyInstance,
                    Segment = row.Segment,
                    Config = c,
                    Geometry = _geometry[row.Segment.Id.Value]
                };
                var upperWork = new V3SegmentWork
                {
                    Column = _document?.GetElement(upper.Id) as Autodesk.Revit.DB.FamilyInstance,
                    Segment = upper,
                    Config = u,
                    Geometry = _geometry[upper.Id.Value]
                };
                string beamInfo = ApplyDetectedBeamInfo(lowerWork, upperWork, true);
                var lowerProfile = V3ConnectionAdapter.Profile(lowerWork);
                var upperProfile = V3ConnectionAdapter.Profile(upperWork);
                lowerProfile.HasFoot = index == 0 && FoundationAnchorCheck.IsChecked == true;
                if (lowerProfile.HasFoot && FoundationDirectionBox.SelectedIndex >= 0)
                    lowerProfile.FootDirection = (V3AnchorDirection)FoundationDirectionBox.SelectedIndex;
                ConnectionComparisonGrid.ItemsSource = V3ColumnPairing.Compare(lowerProfile, upperProfile);
                if (!string.IsNullOrWhiteSpace(beamInfo) &&
                    (c.JointBeamHeight <= 0 || c.BeamTopCoverMm <= 0))
                {
                    ConnectionPairingStatus.Text = beamInfo;
                }
                else try
                {
                    var plan = V3ColumnPairing.Create(lowerProfile, upperProfile, c.BottleneckClearanceMm, c.BottleneckSlope);
                    _previewConnectionPlan = plan;
                    int terminated = plan.Bars.Count(bar => bar.Terminated);
                    int matched = plan.Bars.Count - terminated;
                    string mismatch = terminated > 0
                        ? "; dư dưới " + terminated + " thanh, kết thúc ở đỉnh dầm trừ cover"
                        : plan.UnmatchedUpperPositions.Count > 0
                            ? "; thiếu dưới " + plan.UnmatchedUpperPositions.Count + " thanh, cần thép chờ neo trong dầm"
                            : "";
                    ConnectionPairingStatus.Text = "Ghép " + matched + " cặp" + mismatch + "; dịch ngang lớn nhất " + Math.Round(plan.MaximumShift, 1) +
                        " mm; chiều cao nhấn lớn nhất " + Math.Round(plan.MaximumHeight, 1) + " mm. Nhóm chỉ khi cùng hình dạng và mặt phẳng.";
                    if (!string.IsNullOrWhiteSpace(beamInfo))
                        ConnectionPairingStatus.Text += " " + beamInfo;
                    int largerCount = Math.Max(lowerProfile.Cx*2+lowerProfile.Cy*2-4,
                        upperProfile.Cx*2+upperProfile.Cy*2-4);
                    if (matched > largerCount / 2)
                    {
                        double gapMm = upper.BottomMm-row.Segment.TopMm;
                        double availableLap = c.StarterLength-gapMm-u.Cover-u.MainDiameter/2;
                        double stagger=(availableLap+Math.Max(c.MainDiameter,u.MainDiameter))/3;
                        ConnectionPairingStatus.Text += " Nối so le 50%: dịch ±" +
                            Math.Round(stagger,1) + " mm, tính từ chiều chờ và đường kính đã nhập; cần kiểm tra chiều dài nối còn lại.";
                    }
                    if (plan.UnmatchedUpperPositions.Count > 0 && u.SupplementalAnchorLengthMm <= 0)
                        ConnectionPairingStatus.Text += " Chưa nhập chiều dài neo bổ sung theo chi tiết được duyệt.";
                }
                catch (InvalidOperationException ex) { ConnectionPairingStatus.Text = ex.Message; }
            }
            double gap = upper.BottomMm - row.Segment.TopMm;
            double lap = c.StarterLength - gap - u.Cover - u.MainDiameter / 2;
            double minimum = gap + u.Cover + u.MainDiameter / 2 + V3BottleneckRules.MinimumLap(Math.Max(c.MainDiameter, u.MainDiameter));
            StarterConnectionSummary.Text = "Khoảng hở lên " + upper.LevelName + ": " + Math.Round(gap, 1) +
                " mm. Phần nối thẳng còn lại: " + Math.Round(lap, 1) + " mm. Chiều dài chờ tối thiểu theo giới hạn 20d/250 mm: " +
                Math.Round(minimum, 1) + " mm; chiều dài tính toán có thể lớn hơn.";
            if (_previewConnectionPlan != null && _previewConnectionPlan.Bars.Any(bar => bar.Terminated))
                StarterConnectionSummary.Text += " Có " + _previewConnectionPlan.Bars.Count(bar => bar.Terminated) +
                    " thanh dư dưới; đầu thanh đặt tại cao độ đỉnh dầm trừ lớp bảo vệ trên đã nhập.";
            if (_previewConnectionPlan != null && _previewConnectionPlan.UnmatchedUpperPositions.Count > 0)
                StarterConnectionSummary.Text += " Có " + _previewConnectionPlan.UnmatchedUpperPositions.Count +
                    " thanh tầng trên cần neo thẳng xuống dầm dài " +
                    (u.SupplementalAnchorLengthMm > 0 ? u.SupplementalAnchorLengthMm.ToString("0.##", CultureInfo.InvariantCulture) : "(chưa nhập)") +
                    " mm theo chi tiết đã duyệt.";
        }
        private Point LongSectionPointer(MouseEventArgs e)
        {
            var parent = LongSectionCanvas.Parent as IInputElement;
            Point point = e.GetPosition(parent);
            return new Point(point.X - LongSectionCanvas.Margin.Left,
                point.Y - LongSectionCanvas.Margin.Top);
        }

        private void ApplyLongSectionTransform()
        {
            if (LongSectionCanvas == null) return;
            var matrix = Matrix.Identity;
            matrix.ScaleAt(_longSectionZoom, _longSectionZoom,
                LongSectionCanvas.ActualWidth / 2, LongSectionCanvas.ActualHeight / 2);
            matrix.Translate(_longSectionPanX, _longSectionPanY);
            LongSectionCanvas.RenderTransform = new MatrixTransform(matrix);
            if (LongSectionZoomLabel != null)
                LongSectionZoomLabel.Text = Math.Round(_longSectionZoom * 100).ToString(CultureInfo.InvariantCulture) + "%";
        }

        private void ZoomLongSection(Point anchor, double factor)
        {
            if (LongSectionCanvas == null) return;
            double nextZoom = Math.Max(0.5, Math.Min(5.0, _longSectionZoom * factor));
            if (Math.Abs(nextZoom - _longSectionZoom) < 0.0001) return;
            double ratio = nextZoom / _longSectionZoom;
            double relativeX = anchor.X - LongSectionCanvas.ActualWidth / 2;
            double relativeY = anchor.Y - LongSectionCanvas.ActualHeight / 2;
            _longSectionPanX = relativeX - (relativeX - _longSectionPanX) * ratio;
            _longSectionPanY = relativeY - (relativeY - _longSectionPanY) * ratio;
            _longSectionZoom = nextZoom;
            ApplyLongSectionTransform();
        }

        private void LongSectionCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            ZoomLongSection(LongSectionPointer(e), e.Delta > 0 ? 1.15 : 1 / 1.15);
            e.Handled = true;
        }

        private void LongSectionCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _longSectionPanning = true;
            _longSectionLastPanPoint = LongSectionPointer(e);
            LongSectionCanvas.Cursor = Cursors.SizeAll;
            LongSectionCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void LongSectionCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_longSectionPanning) return;
            Point point = LongSectionPointer(e);
            _longSectionPanX += point.X - _longSectionLastPanPoint.X;
            _longSectionPanY += point.Y - _longSectionLastPanPoint.Y;
            _longSectionLastPanPoint = point;
            ApplyLongSectionTransform();
        }

        private void LongSectionCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _longSectionPanning = false;
            LongSectionCanvas.Cursor = Cursors.Arrow;
            LongSectionCanvas.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void LongSectionCanvas_LostMouseCapture(object sender, MouseEventArgs e)
        {
            _longSectionPanning = false;
            if (LongSectionCanvas != null) LongSectionCanvas.Cursor = Cursors.Arrow;
        }

        private void LongSectionZoomIn_Click(object sender, RoutedEventArgs e)
        {
            ZoomLongSection(new Point(LongSectionCanvas.ActualWidth / 2, LongSectionCanvas.ActualHeight / 2), 1.2);
        }

        private void LongSectionZoomOut_Click(object sender, RoutedEventArgs e)
        {
            ZoomLongSection(new Point(LongSectionCanvas.ActualWidth / 2, LongSectionCanvas.ActualHeight / 2), 1 / 1.2);
        }

        private void LongSectionZoomFit_Click(object sender, RoutedEventArgs e)
        {
            _longSectionZoom = 1.0;
            _longSectionPanX = 0;
            _longSectionPanY = 0;
            ApplyLongSectionTransform();
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
            double barRadius = Math.Max(3.5, Math.Min(12, c.MainDiameter * factor / 2.0));
            for (int i = 0; i < c.Cx; i++)
            {
                double xx = bx1 + (bx2 - bx1) * i / (c.Cx - 1);
                AddBar(xx, by1, barRadius); AddBar(xx, by2, barRadius);
            }
            for (int j = 1; j < c.Cy - 1; j++)
            {
                double yy = by1 + (by2 - by1) * j / (c.Cy - 1);
                AddBar(bx1, yy, barRadius); AddBar(bx2, yy, barRadius);
            }
            var selectedMainType = MainBarBox.SelectedItem as BarSizeChoice;
            string mainTypeLabel = selectedMainType == null
                ? "D" + c.MainDiameter.ToString("0.##", CultureInfo.InvariantCulture)
                : selectedMainType.Label;
            var sizeLabel = new TextBlock {
                Text = Math.Round(widthMm) + " × " + Math.Round(depthMm) + " mm  ·  " +
                    c.BarCount + " thanh  ·  " + mainTypeLabel,
                FontSize = 11, Foreground = Brushes.DimGray
            };
            Canvas.SetLeft(sizeLabel, Math.Max(4, left));
            Canvas.SetTop(sizeLabel, Math.Max(4, top - 23));
            SectionCanvas.Children.Add(sizeLabel);
            RefreshAllFloorSections();
        }

        private void RefreshAllFloorSections()
        {
            if (FloorSectionPreviewStack == null || _segments == null) return;
            // Retain the interactive section canvas and its parent Grid.
            var interactive = SectionCanvas.Parent as Grid;
            FloorSectionPreviewStack.Children.Clear();
            foreach (var segment in _segments)
            {
                V3RebarConfig cfg;
                if (!_configs.TryGetValue(segment.Id.Value, out cfg)) continue;
                var title = new TextBlock {
                    Text = "LEVEL " + segment.LevelName + "  ·  " +
                        Math.Round(segment.WidthMm) + " × " + Math.Round(segment.DepthMm) +
                        " mm  ·  " + cfg.BarCount + " thanh",
                    FontWeight = FontWeights.SemiBold, Margin = new Thickness(8,12,8,4),
                    Foreground = Brushes.DarkSlateBlue };
                FloorSectionPreviewStack.Children.Add(title);
                var diagram = new Canvas { Height=155, Margin=new Thickness(8),
                    Background=Brushes.White };
                double size = 125, cx = 75, cy = 75;
                double factor = size / Math.Max(segment.WidthMm,segment.DepthMm);
                double ww = segment.WidthMm*factor, hh=segment.DepthMm*factor;
                var concrete = new Rectangle { Width=ww,Height=hh,
                    Stroke=Brushes.SeaGreen,StrokeThickness=2,
                    Fill=new SolidColorBrush(Color.FromArgb(18,47,143,86)) };
                Canvas.SetLeft(concrete,cx-ww/2); Canvas.SetTop(concrete,cy-hh/2);
                diagram.Children.Add(concrete);
                double inset = Math.Min(ww,hh)*.14;
                var tie = new Rectangle { Width=Math.Max(2,ww-2*inset),
                    Height=Math.Max(2,hh-2*inset), Stroke=Brushes.Teal,StrokeThickness=2 };
                Canvas.SetLeft(tie,cx-ww/2+inset);Canvas.SetTop(tie,cy-hh/2+inset);
                diagram.Children.Add(tie);
                int nx=Math.Max(2,cfg.Cx),ny=Math.Max(2,cfg.Cy);
                Action<double,double> dot=(px,py) =>
                {
                    var circle=new Ellipse { Width=7,Height=7,Fill=Brushes.OrangeRed };
                    Canvas.SetLeft(circle,px-3.5);Canvas.SetTop(circle,py-3.5);
                    diagram.Children.Add(circle);
                };
                double x0=cx-ww/2+inset,x1=cx+ww/2-inset;
                double y0=cy-hh/2+inset,y1=cy+hh/2-inset;
                for(int i=0;i<nx;i++) {double xx=x0+(x1-x0)*i/(nx-1);dot(xx,y0);dot(xx,y1);}
                for(int j=1;j<ny-1;j++) {double yy=y0+(y1-y0)*j/(ny-1);dot(x0,yy);dot(x1,yy);}
                FloorSectionPreviewStack.Children.Add(diagram);
            }
            // The live interactive section remains accessible after the floor cards.
            if (interactive != null)
            {
                var oldParent = interactive.Parent as Panel;
                if (oldParent != null) oldParent.Children.Remove(interactive);
                FloorSectionPreviewStack.Children.Add(new TextBlock {
                    Text="Mặt cắt tương tác · tầng đang chọn",Margin=new Thickness(8,8,8,2),
                    Foreground=Brushes.DimGray });
                FloorSectionPreviewStack.Children.Add(interactive);
            }
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
        private void SectionBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_loading || ActiveConfig == null) return;
            if (ActiveConfig.Cx <= 2 && ActiveConfig.Cy <= 2)
            {
                MessageBox.Show(this, "Đai phụ cần có ít nhất một thanh giữa cạnh (Cx hoặc Cy lớn hơn 2).",
                    "KStruct - Đai phụ", MessageBoxButton.OK, MessageBoxImage.Information);
                e.Handled = true;
                return;
            }

            // The existing Revit builder creates the compatible cross-tie sets for
            // every intermediate bar once this shared configuration option is enabled.
            CrossCheck.IsChecked = CrossCheck.IsChecked != true;
            e.Handled = true;
        }
        private void AddBar(double x, double y, double radius)
        {
            var circle = new Ellipse { Width = radius * 2, Height = radius * 2, Stroke = Brushes.DarkRed,
                StrokeThickness = 1.2, Fill = Brushes.Red, Cursor = Cursors.Hand, ToolTip = "Click để bật / tắt đai phụ cho các thanh giữa cạnh." };
            circle.MouseLeftButtonDown += SectionBar_MouseLeftButtonDown;
            Canvas.SetLeft(circle, x - radius); Canvas.SetTop(circle, y - radius);
            SectionCanvas.Children.Add(circle);
        }
    
        // ========== MẶT CẮT DỌC MINH HỌA ==========
        // Whole-stack preview uses actual segment elevations. Junction markers are
        // shown only when the adjacent columns physically meet or leave a joint gap.
        private void DrawWholeRouteOverview()
        {
            var canvas = LongSectionCanvas;
            if (canvas == null || _segments.Count == 0) return;
            double w = canvas.ActualWidth, h = canvas.ActualHeight;
            if (w < 100 || h < 100) return;
            canvas.Children.Clear();
            double z0 = _segments.Min(v => v.BottomMm);
            double z1 = _segments.Max(v => v.TopMm);
            double scale = (h - 75) / Math.Max(1, z1 - z0);
            double x = w * .42;
            Func<double,double> y = z => h - 35 - (z-z0)*scale;
            Action<double,double,string,Brush> label = (px,py,value,color) =>
            {
                var text = new TextBlock { Text=value, FontSize=10, Foreground=color };
                Canvas.SetLeft(text,px); Canvas.SetTop(text,py);
                canvas.Children.Add(text);
            };
            label(8,4,"TOÀN TUYẾN CỘT · " + _segments.Count + " tầng",Brushes.DarkSlateBlue);
            foreach (var seg in _segments)
            {
                double half = Math.Max(5,Math.Min(55,seg.WidthMm*.09));
                var rect = new Rectangle { Width=2*half,
                    Height=Math.Max(1,(seg.TopMm-seg.BottomMm)*scale),
                    Stroke=Brushes.SeaGreen, StrokeThickness=1.6,
                    Fill=new SolidColorBrush(Color.FromArgb(22,47,143,86)) };
                Canvas.SetLeft(rect,x-half); Canvas.SetTop(rect,y(seg.TopMm));
                canvas.Children.Add(rect);
                double baseY=y(seg.BottomMm);
                canvas.Children.Add(new Line { X1=12,X2=w-12,Y1=baseY,Y2=baseY,
                    Stroke=Brushes.SlateGray,StrokeThickness=.7,StrokeDashArray=new DoubleCollection { 4,3 } });
                label(14,baseY-15,"LEVEL " + seg.LevelName + "  (" + Math.Round(seg.BottomMm) + " mm)",Brushes.DarkSlateGray);
                label(x+half+6,(y(seg.TopMm)+baseY)/2,
                    Math.Round(seg.WidthMm) + "×" + Math.Round(seg.DepthMm),Brushes.DimGray);
            }
            for (int i=0;i<_segments.Count-1;i++)
            {
                var lower=_segments[i]; var upper=_segments[i+1];
                double gap=upper.BottomMm-lower.TopMm;
                if (gap < -2) continue;
                double jointY=y(lower.TopMm);
                // A beam joint is not asserted solely from a user-entered beam height.
                label(x+58,jointY-12,"Nút nối " + lower.LevelName + " / " + upper.LevelName +
                    " · dầm: cần xác minh",Brushes.DarkOrange);
                canvas.Children.Add(new Line { X1=x-70,X2=x+70,Y1=jointY,Y2=jointY,
                    Stroke=Brushes.DarkOrange,StrokeThickness=2,StrokeDashArray=new DoubleCollection { 5,3 } });
            }
            label(8,h-18,"Minh họa theo cao độ mô hình; dầm chưa xác minh không vẽ thành cấu kiện thật.",Brushes.Gray);
            ApplyLongSectionTransform();
        }

        private void DrawLongSection()
        {
            if (_segments.Count > 1) { DrawWholeRouteOverview(); return; }
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
            var selectedMainType = MainBarBox.SelectedItem as BarSizeChoice;
            string mainTypeLabel = selectedMainType == null
                ? "D" + c.MainDiameter.ToString("0.##", CultureInfo.InvariantCulture)
                : selectedMainType.Label;
            addTxt(8,4,"Thép chủ: " + mainTypeLabel,Brushes.DimGray,9);

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
            double barThick = Math.Max(1.5, Math.Min(6.0, c.MainDiameter * scale));

            double barBot = c.Cover + c.MainDiameter / 2;
            double barTop = colH - c.Cover - c.MainDiameter / 2;
            if (preview != null)
            {
                Func<V3Point2,double> project=p=>(p-preview.Lower.Center).Dot(preview.Lower.AxisX);
                foreach(var pair in preview.Bars)
                {
                    double x=project(pair.Lower), endX=project(pair.End), upperX=project(pair.Upper);
                    addLn(mmToX(x),mmToY(barBot),mmToX(x),mmToY(colH-pair.Height),barBrush,barThick);
                    if(pair.Height>0)
                        addLn(mmToX(x),mmToY(colH-pair.Height),mmToX(endX),mmToY(colH),barBrush,barThick);
                    addLn(mmToX(endX),mmToY(colH),mmToX(endX),mmToY(colH+starterMm),Brushes.OrangeRed,barThick);
                    addLn(mmToX(upperX),mmToY(colH+upperGapMm+preview.Upper.Cover+preview.Upper.Diameter/2),
                        mmToX(upperX),mmToY(colH+upperGapMm+upperDrawH),Brushes.Firebrick,1.2);
                }
                addTxt(8,18,"Chiếu theo trục X cột dưới; các thanh có thể chồng hình.",Brushes.DimGray,9);
            }
            else
            {
                foreach(double x in new[]{barXL,barXR})
                    addLn(mmToX(x),mmToY(barBot),mmToX(x),mmToY(barTop),barBrush,barThick);
                if(hasUpper && c.StarterBars)
                    addTxt(8,18,"Chưa có phương án ghép hợp lệ — xem bảng so sánh.",Brushes.Firebrick,9);
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
                    c.EndZone, c.EndSpacing, c.MidSpacing, false,
                    c.EndTieCount, c.MidTieCount, false, 0, 0);
                // Same joint exclusion as used when constructing actual Rebar Sets.
                if (_document != null && _geometry.TryGetValue(row.Segment.Id.Value, out var geometry))
                {
                    var column = _document.GetElement(row.Segment.Id) as Autodesk.Revit.DB.FamilyInstance;
                    if (column != null)
                    {
                        var work = new V3SegmentWork { Column=column, Segment=row.Segment,
                            Geometry=geometry, Config=c };
                        var excluded = V4TieJointExclusion.Find(_document, work);
                        bands = V4TieBandExclusion.KeepOutside(bands, excluded);
                        if (excluded.Count > 0)
                            addTxt(4, cH-16, "Đã loại đai tại " + excluded.Count +
                                " vùng giao dầm/sàn", Brushes.Firebrick, 9);
                    }
                }
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
            ApplyLongSectionTransform();
        }
    }
}
