using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using WpfControl = System.Windows.Controls.Control;
using MediaColor = System.Windows.Media.Color;
using Autodesk.Revit.DB;

namespace SDK_HD.KStruct.NativeDocumentation
{
    internal sealed class FootingDrawingConfiguration
    {
        internal FootingDrawingProfile Profile { get; set; }
        internal ViewFamilyType PlanType { get; set; }
        internal ViewFamilyType DetailType { get; set; }
        internal FamilySymbol TitleBlock { get; set; }
        internal View PlanTemplate { get; set; }
        internal View SectionTemplate { get; set; }
        internal MultiReferenceAnnotationType RebarMultiTagType { get; set; }
        internal ElementType FoundationTagType { get; set; }
        internal DimensionType DimensionType { get; set; }
        internal ViewSheet ExistingSheet { get; set; }
        internal ViewPlan SourcePlan { get; set; }
        internal IList<FamilyInstance> SelectedFootings { get; set; }
    }

    internal sealed partial class FootingDrawingSettingsWindow : Window
    {
        private sealed class Item
        {
            internal string Text { get; set; }
            internal Element Element { get; set; }
            public override string ToString() => Text;
        }

        private readonly Document _doc;
        private readonly ViewPlan _parent;
        private readonly ComboBox _profiles = new ComboBox { MinWidth = 260 };
        private readonly ComboBox _planType = new ComboBox { MinWidth = 260 };
        private readonly ComboBox _detailType = new ComboBox { MinWidth = 260 };
        private readonly ComboBox _titleBlock = new ComboBox { MinWidth = 260 };
        private readonly ComboBox _planTemplate = new ComboBox { MinWidth = 150 };
        private readonly ComboBox _sectionTemplate = new ComboBox { MinWidth = 260 };
        private readonly CheckBox _usePlanDetail = new CheckBox { Content = "Mặt bằng sử dụng Detail Component 2D (ẩn Rebar thật)", IsChecked = false };
        private readonly ComboBox _planTopDetail = new ComboBox { MinWidth = 260 };
        private readonly ComboBox _planBotDetail = new ComboBox { MinWidth = 260 };
        private readonly CheckBox _pileSection2D = new CheckBox
            { Content = "Đặt Detail Component Cọc 2D tại vị trí cọc thật trên Detail-X / Detail-Y" };
        private readonly ComboBox _pileSectionFamily = new ComboBox { MinWidth = 260 };
        private readonly TextBox _pileDiameterParameter = new TextBox { Text = "" };
        private readonly TextBox _pileFamilyFilter = new TextBox { Text = "cọc,coc,pile" };
        private readonly CheckBox _pileOriginConfirmed = new CheckBox
            { Content = "Đã kiểm tra tim nét vẽ tại gốc Family 2D trong Family Editor", IsChecked = false };
        private readonly CheckBox _planSizeConfirmed = new CheckBox { Content = "Tôi đã kiểm tra ánh xạ với Family Types (bắt buộc)", IsChecked = false };
        private readonly TextBox _planCoverMm = new TextBox { Text = "0" };
        private readonly CheckBox _alignRebarCover = new CheckBox
            { Content = "Căn thép 2D theo lớp bảo vệ móng và tim thanh (L/W = B/H trừ 2 × (C + Ø/2))", IsChecked = true };
        private readonly CheckBox _readHostCover = new CheckBox
            { Content = "Ưu tiên Rebar Cover - Other Faces của móng thật", IsChecked = true };
        private readonly TextBox[] _planExpressions = new [] {
            new TextBox { Text = "B" }, new TextBox { Text = "H" },
            new TextBox { Text = "KEEP" }, new TextBox { Text = "KEEP" },
            new TextBox { Text = "KEEP" }, new TextBox { Text = "KEEP" },
            new TextBox { Text = "KEEP" }, new TextBox { Text = "KEEP" }
        };
        private readonly ComboBox _rebarTag = new ComboBox { MinWidth = 260 };
        private readonly ComboBox _foundationTag = new ComboBox { MinWidth = 260 };
        private readonly ComboBox _dimension = new ComboBox { MinWidth = 150 };
        private Canvas _planCanvas;
        private Canvas _sectionCanvas;
        private readonly ComboBox _existingSheet = new ComboBox { MinWidth = 260 };
        private readonly CheckBox _topLayer = new CheckBox { Content = "Tạo chế độ xem chi tiết trên cùng", IsChecked = true, Margin = new Thickness(4) };
        private readonly CheckBox _bottomLayer = new CheckBox { Content = "Tạo chế độ xem chi tiết dưới cùng", IsChecked = true, Margin = new Thickness(4) };
        private readonly CheckBox _callouts = new CheckBox { Content = "Tạo callout mặt bằng", IsChecked = true };
        private readonly CheckBox _detailA = new CheckBox { Content = "Detail A–A", IsChecked = true };
        private readonly CheckBox _detailB = new CheckBox { Content = "Detail B–B", IsChecked = true };
        private bool AnyDetailSelected => _detailA.IsChecked == true || _detailB.IsChecked == true;
        private readonly CheckBox _dimensions = new CheckBox { Content = "Tạo kích thước host", IsChecked = true };
        private readonly CheckBox _tags = new CheckBox { Content = "Gắn tag móng và Rebar", IsChecked = true };
        private readonly ComboBox _footingKind = new ComboBox { MinWidth = 260 };
        private readonly CheckBox _noteBot = new CheckBox { Content = "Ghi chú nhóm thép BOT", IsChecked = true };
        private readonly CheckBox _noteTop = new CheckBox { Content = "Ghi chú nhóm thép TOP (móng cọc)", IsChecked = true };
        private readonly CheckBox _newSheet = new CheckBox { Content = "Tạo sheet mới cho từng móng", IsChecked = true };
        private readonly TextBox _planScale = new TextBox { Text = "20", MinWidth = 90 };
        private readonly TextBox _sectionScale = new TextBox { Text = "20", MinWidth = 90 };
        private readonly TextBox _cropMargin = new TextBox { Text = "6", MinWidth = 90 };
        private readonly TextBox _cropBottomMargin = new TextBox { Text = "500", MinWidth = 90 };
        private readonly TextBox _sheetPrefix = new TextBox { Text = "KSMD-", MinWidth = 240 };
        private readonly TextBox _sheetName = new TextBox { Text = "Móng đơn - {Mark}", MinWidth = 240 };
        private readonly TextBox _viewName = new TextBox { Text = "KStruct - MĐ {Id} - {Role}", MinWidth = 240 };
        private readonly TextBox _profileName = new TextBox { MinWidth = 180 };

        internal FootingDrawingConfiguration Configuration { get; private set; }

        private static bool IsPileCap(FamilyInstance fi) =>
            FootingDrawingBuilder.IsPileCapForPolicy(fi);

        internal FootingDrawingSettingsWindow(Document doc, IntPtr owner, ViewPlan parent, IList<FamilyInstance> footings = null)
        {
            _doc = doc;
            _parent = parent;
            _callouts.IsChecked = parent != null;
            _callouts.IsEnabled = parent != null;
            Title = "KStruct - Tạo hồ sơ móng cọc & móng đơn";
            Width = Math.Min(1560, SystemParameters.WorkArea.Width - 30);
            Height = Math.Min(980, SystemParameters.WorkArea.Height - 30);
            MinWidth = 1080;
            MinHeight = 680;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            Background = Brush("#F4F4F4");
            Foreground = Brushes.Black;
            ApplyTheme();
            if (owner != IntPtr.Zero) new WindowInteropHelper(this).Owner = owner;

            ViewFamilyType parentType = _parent == null ? null : doc.GetElement(_parent.GetTypeId()) as ViewFamilyType;
            Populate(_planType, new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .Where(x => (x.ViewFamily == ViewFamily.StructuralPlan || x.ViewFamily == ViewFamily.FloorPlan))
                .Select(x => new Item { Text = x.Name + " | " + x.ViewFamily, Element = x }), "StructuralPlan");
            Populate(_detailType, new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .Where(x => x.ViewFamily == ViewFamily.Detail).Select(x => new Item { Text = x.Name, Element = x }), null);
            Populate(_titleBlock, new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Select(x => new Item { Text = x.FamilyName + " : " + x.Name, Element = x }), null);
            Populate(_planTemplate, Templates(doc).Select(x => new Item { Text = x.Name + " | " + x.ViewType, Element = x }), null, true);
            Populate(_sectionTemplate, Templates(doc).Select(x => new Item { Text = x.Name + " | " + x.ViewType, Element = x }), null, true);
            var detailItems = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_DetailComponents)
                .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().OrderBy(x => x.FamilyName).ThenBy(x => x.Name).ToList();
            Populate(_planTopDetail, detailItems.Select(x => new Item { Text = x.FamilyName + " : " + x.Name, Element = x }), null, true);
            Populate(_planBotDetail, detailItems.Select(x => new Item { Text = x.FamilyName + " : " + x.Name, Element = x }), null, true);
            Populate(_pileSectionFamily, detailItems
                .Where(x => x.Family.FamilyPlacementType == FamilyPlacementType.ViewBased)
                .Select(x => new Item { Text = x.FamilyName + " : " + x.Name, Element = x }), null, true);
            _pileSectionFamily.SelectedItem = _pileSectionFamily.Items.Cast<Item>()
                .FirstOrDefault(x => x.Element is FamilySymbol fs &&
                    fs.FamilyName.Equals("BS_FM_Detail Cọc 2D", StringComparison.OrdinalIgnoreCase));
            var preferred = detailItems.Where(x => x.FamilyName == "BS_FM_Detail Rebar 2D Top & Bot").ToList();
            _planTopDetail.SelectedItem = _planTopDetail.Items.Cast<Item>().FirstOrDefault(x => x.Element?.Id == preferred.FirstOrDefault(y => y.Name == "2D-Top")?.Id);
            _planBotDetail.SelectedItem = _planBotDetail.Items.Cast<Item>().FirstOrDefault(x => x.Element?.Id == preferred.FirstOrDefault(y => y.Name == "2D-Bot")?.Id);
            _usePlanDetail.Checked += (s, e) => { _planTopDetail.IsEnabled = _planBotDetail.IsEnabled = true; };
            _usePlanDetail.Unchecked += (s, e) => { _planTopDetail.IsEnabled = _planBotDetail.IsEnabled = false; };
            _planTopDetail.IsEnabled = _planBotDetail.IsEnabled = false;
            // Only explicit user action or a saved opt-in profile may enable this feature.
            _pileSection2D.IsChecked = false;
            _pileSection2D.Checked += (s, e) => _pileSectionFamily.IsEnabled = true;
            _pileSection2D.Unchecked += (s, e) => _pileSectionFamily.IsEnabled = false;
            _pileSectionFamily.IsEnabled = _pileSection2D.IsChecked == true;
            Populate(_rebarTag, FootingDrawingRebarMultiTags.Types(doc)
                .Select(x => new Item { Text = x.Name, Element = x }), null, true);
            Populate(_foundationTag, new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFoundationTags).WhereElementIsElementType().Cast<ElementType>()
                .Select(x => new Item { Text = x.FamilyName + " : " + x.Name, Element = x }), null, true);
            Populate(_dimension, new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().Where(x => x.StyleType == DimensionStyleType.Linear)
                .Select(x => new Item { Text = x.Name, Element = x }), null, true);
            Populate(_existingSheet, new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(x => !x.IsPlaceholder).Select(x => new Item { Text = x.SheetNumber + " | " + x.Name, Element = x }), null, true);




            foreach (string mode in new[] { "Auto", "Isolated", "PileCap" }) _footingKind.Items.Add(mode);
            _footingKind.SelectedIndex = 0;
            _footingKind.SelectionChanged += (s, e) => RefreshPreviews();
            _existingSheet.SelectionChanged += (s, e) => { if (_existingSheet.SelectedItem is Item i && i.Element is ViewSheet) _newSheet.IsChecked = false; };
            _newSheet.Checked += (s, e) => _existingSheet.IsEnabled = false;
            _newSheet.Unchecked += (s, e) => _existingSheet.IsEnabled = true;
            foreach (CheckBox check in new[] { _topLayer, _bottomLayer, _callouts, _detailA, _detailB, _dimensions, _tags })
            { check.Checked += (s, e) => RefreshPreviews(); check.Unchecked += (s, e) => RefreshPreviews(); }
            foreach (ComboBox combo in new[] { _planType, _detailType, _planTemplate, _sectionTemplate, _dimension, _rebarTag, _foundationTag })
                combo.SelectionChanged += (s, e) => RefreshPreviews();
            _planScale.TextChanged += (s, e) => RefreshPreviews();
            _sectionScale.TextChanged += (s, e) => RefreshPreviews();
            _cropMargin.TextChanged += (s, e) => RefreshPreviews();
            _cropBottomMargin.TextChanged += (s, e) => RefreshPreviews();

            Content = BuildMockupLayout(footings ?? new List<FamilyInstance>());
            RefreshProfiles();
            if (_planType.Items.Count > 0) _planType.SelectedIndex = 0;
            if (_detailType.Items.Count > 0) _detailType.SelectedIndex = 0;
            if (_titleBlock.Items.Count > 0) _titleBlock.SelectedIndex = 0;
            if (_dimension.Items.Count > 1) _dimension.SelectedIndex = 1;
            _dimensions.IsChecked = Selected(_dimension) != null;
            _tags.IsChecked = Selected(_rebarTag) != null || Selected(_foundationTag) != null;
            _existingSheet.IsEnabled = false;
            RestoreSession();
            RefreshPreviews();
        }

        private void RefreshPreviews()
        {
            _sectionXOffset.IsEnabled = _reverseX.IsEnabled = _detailA.IsChecked == true;
            _sectionYOffset.IsEnabled = _reverseY.IsEnabled = _detailB.IsChecked == true;
            _detailType.IsEnabled = _sectionTemplate.IsEnabled = _sectionScale.IsEnabled = _sectionDepth.IsEnabled = AnyDetailSelected;
            bool detectedPile = _previewHost != null && IsPileCap(_previewHost);
            bool pile = FootingDrawingLayerPolicy.IsPileCap(_footingKind.SelectedItem as string, detectedPile);
            _bottomLayer.IsChecked = true;
            _topLayer.IsChecked = pile;
            _bottomLayer.IsEnabled = _topLayer.IsEnabled = false;
            _noteTop.IsEnabled = pile;
            // Mixed selection may contain pile caps even when the preview host is isolated.
            _pileSection2D.IsEnabled = AnyDetailSelected && CheckedFootings.Any(host =>
                FootingDrawingLayerPolicy.IsPileCap(_footingKind.SelectedItem as string, IsPileCap(host)));
            if (_planCanvas == null || _sectionCanvas == null) return;
            try { DrawModelPlanPreview(_planCanvas); DrawSheetPreview(_sectionCanvas); }
            catch (Exception ex) { _status.Text = "Không xem trước được móng: " + ex.Message; }
        }

        private static void AddPreviewNote(Canvas canvas, string text)
        {
            var note = new TextBlock { Text = text, FontSize = 10.5, Foreground = Brushes.Black, Background = Brushes.White };
            Canvas.SetLeft(note, 5); Canvas.SetTop(note, 2 + canvas.Children.Count * 14); canvas.Children.Add(note);
        }

        private static void DrawDimension(Canvas canvas, double x1, double y1, double x2, double y2, string value, string styleName)
        {
            DrawLine(canvas, x1, y1, x2, y2, Brushes.Black, false);
            DrawLine(canvas, x1 - 4, y1 - 4, x1 + 4, y1 + 4, Brushes.Black, false);
            DrawLine(canvas, x2 - 4, y2 - 4, x2 + 4, y2 + 4, Brushes.Black, false);
            bool horizontal = Math.Abs(y2 - y1) < Math.Abs(x2 - x1);
            var label = new TextBlock { Text = value, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black, Background = Brushes.White };
            if (horizontal)
            {
                Canvas.SetLeft(label, (x1 + x2) / 2 - 13);
                Canvas.SetTop(label, y1 - 19);
            }
            else
            {
                Canvas.SetLeft(label, x1 + 6);
                Canvas.SetTop(label, (y1 + y2) / 2 - 9);
            }
            canvas.Children.Add(label);
        }

        private static void DrawLine(Canvas canvas, double x1, double y1, double x2, double y2, Brush brush, bool dashed)
        {
            var line = new System.Windows.Shapes.Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = 1.4 };
            if (dashed) line.StrokeDashArray = new DoubleCollection { 3, 2 };
            canvas.Children.Add(line);
        }

        private void ApplyTheme()
        {
            var buttonStyle = new Style(typeof(Button));
            buttonStyle.Setters.Add(new Setter(WpfControl.BackgroundProperty, Brush("#FFFFFF")));
            buttonStyle.Setters.Add(new Setter(WpfControl.ForegroundProperty, Brush("#174A75")));
            buttonStyle.Setters.Add(new Setter(WpfControl.BorderBrushProperty, Brush("#B7D0E5")));
            buttonStyle.Setters.Add(new Setter(WpfControl.BorderThicknessProperty, new Thickness(1)));
            buttonStyle.Setters.Add(new Setter(WpfControl.PaddingProperty, new Thickness(10, 5, 10, 5)));
            buttonStyle.Setters.Add(new Setter(WpfControl.MarginProperty, new Thickness(0, 0, 7, 0)));
            buttonStyle.Setters.Add(new Setter(WpfControl.CursorProperty, System.Windows.Input.Cursors.Hand));
            buttonStyle.Triggers.Add(new Trigger { Property = UIElement.IsMouseOverProperty, Value = true, Setters = { new Setter(WpfControl.BackgroundProperty, Brush("#E7F2FC")), new Setter(WpfControl.BorderBrushProperty, Brush("#6FA7D4")) } });
            buttonStyle.Triggers.Add(new Trigger { Property = Button.IsPressedProperty, Value = true, Setters = { new Setter(WpfControl.BackgroundProperty, Brush("#CFE4F6")) } });
            Resources.Add(typeof(Button), buttonStyle);

            var comboStyle = new Style(typeof(ComboBox));
            comboStyle.Setters.Add(new Setter(WpfControl.MinHeightProperty, 30.0));
            comboStyle.Setters.Add(new Setter(WpfControl.PaddingProperty, new Thickness(7, 3, 7, 3)));
            comboStyle.Setters.Add(new Setter(WpfControl.BorderBrushProperty, Brush("#B7D0E5")));
            comboStyle.Setters.Add(new Setter(WpfControl.BorderThicknessProperty, new Thickness(1)));
            comboStyle.Setters.Add(new Setter(WpfControl.BackgroundProperty, Brushes.White));
            Resources.Add(typeof(ComboBox), comboStyle);

            var textStyle = new Style(typeof(TextBox));
            textStyle.Setters.Add(new Setter(WpfControl.MinHeightProperty, 30.0));
            textStyle.Setters.Add(new Setter(WpfControl.PaddingProperty, new Thickness(7, 4, 7, 4)));
            textStyle.Setters.Add(new Setter(WpfControl.BorderBrushProperty, Brush("#B7D0E5")));
            textStyle.Setters.Add(new Setter(WpfControl.BorderThicknessProperty, new Thickness(1)));
            textStyle.Setters.Add(new Setter(WpfControl.BackgroundProperty, Brushes.White));
            Resources.Add(typeof(TextBox), textStyle);

            var checkStyle = new Style(typeof(CheckBox));
            checkStyle.Setters.Add(new Setter(WpfControl.MarginProperty, new Thickness(2, 5, 0, 5)));
            checkStyle.Setters.Add(new Setter(WpfControl.ForegroundProperty, Brushes.Black));
            Resources.Add(typeof(CheckBox), checkStyle);
        }

        private static SolidColorBrush Brush(string color) => new SolidColorBrush((MediaColor)ColorConverter.ConvertFromString(color));

        private static Border CreateCard(string title, string description, UIElement content)
        {
            var panel = new System.Windows.Controls.Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brush("#135B96") });
            var note = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, FontSize = 11,
                Foreground = Brush("#6A7F93"), Margin = new Thickness(0, 4, 0, 10) };
            System.Windows.Controls.Grid.SetRow(note, 1); panel.Children.Add(note);
            System.Windows.Controls.Grid.SetRow(content, 2); panel.Children.Add(content);
            return new Border { Background = Brushes.White, BorderBrush = Brush("#D5E3EF"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5), Padding = new Thickness(12), Child = panel };
        }

        private static IEnumerable<View> Templates(Document doc) => new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(x => x.IsTemplate).OrderBy(x => x.Name);
        private static void AddRow(System.Windows.Controls.Panel panel, string label, System.Windows.Controls.Control control)
        {
            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(0, 3, 0, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0) };
            System.Windows.Controls.Grid.SetColumn(text, 0); System.Windows.Controls.Grid.SetColumn(control, 1); grid.Children.Add(text); grid.Children.Add(control); panel.Children.Add(grid);
        }
        private static void Populate(ComboBox box, IEnumerable<Item> items, string prefer, bool optional = false)
        {
            if (optional) box.Items.Add(new Item { Text = "(Không dùng)", Element = null });
            foreach (Item item in items) box.Items.Add(item);
            if (prefer != null)
            {
                Item match = box.Items.Cast<Item>().FirstOrDefault(x => x.Text.IndexOf(prefer, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null) box.SelectedItem = match;
            }
            if (box.SelectedIndex < 0 && box.Items.Count > 0) box.SelectedIndex = 0;
        }
        private static Element Selected(ComboBox box) => (box.SelectedItem as Item)?.Element;
        private static string TypeName(Element element) => element == null ? null : element.Name;
        private static string FamilyName(Element element) => element is FamilySymbol s ? s.FamilyName : element is ElementType t ? t.FamilyName : null;
        private static string SheetUniqueId(ComboBox box) => (Selected(box) as ViewSheet)?.UniqueId;

        private void RefreshProfiles(string selectName = null)
        {
            List<FootingDrawingProfile> items = FootingDrawingProfileStore.Load();
            _profiles.ItemsSource = items;
            _profiles.DisplayMemberPath = "Name";
            if (!string.IsNullOrWhiteSpace(selectName)) _profiles.SelectedItem = items.FirstOrDefault(x => x.Name == selectName);
            if (_profiles.SelectedIndex < 0 && items.Count > 0) _profiles.SelectedIndex = 0;
        }

        private void AuditPlanDetailFamily()
        {
            FamilySymbol symbol = Selected(_planTopDetail) as FamilySymbol ??
                                  Selected(_planBotDetail) as FamilySymbol;
            if (symbol == null)
            {
                MessageBox.Show(this, "Chọn một Detail Item TOP hoặc BOT trong project trước khi kiểm tra.",
                    "Thiếu Family"); return;
            }
            try
            {
                string report = FootingPlanFamilyAudit.Inspect(_doc, symbol);
                var output = new TextBox { Text = report, IsReadOnly = true, AcceptsReturn = true,
                    AcceptsTab = true, TextWrapping = TextWrapping.NoWrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
                var dialog = new Window
                {
                    Title = "KStruct - Kiểm tra công thức Family (chỉ đọc)",
                    Owner = this, Width = Math.Min(980, SystemParameters.WorkArea.Width - 60),
                    Height = Math.Min(760, SystemParameters.WorkArea.Height - 60),
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = output
                };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Không kiểm tra được Family: " + ex.Message +
                    "\\nHãy mở Family trong Revit 2024 để kiểm tra trực tiếp.",
                    "Kiểm tra Family", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void AuditPileFamily()
        {
            FamilySymbol symbol = Selected(_pileSectionFamily) as FamilySymbol;
            if (symbol == null)
            { MessageBox.Show(this, "Chưa chọn Family Cọc 2D.", "Kiểm tra Family"); return; }
            try
            {
                string report = FootingPlanFamilyAudit.Inspect(_doc, symbol);
                var output = new TextBox { Text = report + "\\nGHI CHÚ: Đây là báo cáo thông số, không phải chứng nhận nét tim đồ họa nằm đúng tại gốc Family. Hãy kiểm tra Reference Planes và hình vẽ trong Family Editor.", IsReadOnly = true,
                    AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
                new Window { Title = "KStruct - Kiểm tra Family Cọc 2D", Owner = this,
                    Width = Math.Min(980, SystemParameters.WorkArea.Width - 60),
                    Height = Math.Min(760, SystemParameters.WorkArea.Height - 60),
                    WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = output }.ShowDialog();
            }
            catch (Exception ex)
            { MessageBox.Show(this, "Không thể đọc Family Cọc 2D: " + ex.Message,
                "Kiểm tra Family", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        private FootingDrawingProfile ReadProfile(string name = null) => new FootingDrawingProfile
        {
            Name = name ?? string.Empty,
            PlanTypeName = TypeName(Selected(_planType)), DetailTypeName = TypeName(Selected(_detailType)),
            TitleBlockFamily = FamilyName(Selected(_titleBlock)), TitleBlockType = TypeName(Selected(_titleBlock)),
            PlanTemplateName = TypeName(Selected(_planTemplate)), SectionTemplateName = TypeName(Selected(_sectionTemplate)),
            UsePlanDetailComponents = _usePlanDetail.IsChecked == true,
            PlanDetailSizingConfirmed = _planSizeConfirmed.IsChecked == true,
            PlanDetailCoverMm = ParseDouble(_planCoverMm.Text, -1),
            PlanDetailAlignToCover = _alignRebarCover.IsChecked == true,
            PlanDetailReadHostCover = _readHostCover.IsChecked == true,
            PlanDetailLExpression = _planExpressions[0].Text, PlanDetailWExpression = _planExpressions[1].Text,
            PlanDetailL1Expression = _planExpressions[2].Text, PlanDetailL2Expression = _planExpressions[3].Text,
            PlanDetailX1Expression = _planExpressions[4].Text, PlanDetailX2Expression = _planExpressions[5].Text,
            PlanDetailY1Expression = _planExpressions[6].Text, PlanDetailY2Expression = _planExpressions[7].Text,
            PlanTopDetailTypeUniqueId = Selected(_planTopDetail)?.UniqueId,
            PlanBotDetailTypeUniqueId = Selected(_planBotDetail)?.UniqueId,
            CreatePileSectionSymbols = _pileSection2D.IsChecked == true,
            PileSymbolOriginConfirmed = _pileOriginConfirmed.IsChecked == true,
            PileDiameterParameterName = _pileDiameterParameter.Text?.Trim() ?? "",
            PileFamilyFilter = _pileFamilyFilter.Text?.Trim() ?? "",
            PileSectionDetailTypeUniqueId = Selected(_pileSectionFamily)?.UniqueId,
            PileSymbolOffsetXmm = 0, PileSymbolOffsetYmm = 0,
            RebarMultiTagTypeName = TypeName(Selected(_rebarTag)), RebarMultiTagTypeUniqueId = Selected(_rebarTag)?.UniqueId,
            FoundationTagFamily = FamilyName(Selected(_foundationTag)), FoundationTagType = TypeName(Selected(_foundationTag)),
            DimensionTypeName = TypeName(Selected(_dimension)), OutputLayers = _topLayer.IsChecked == true ? (_bottomLayer.IsChecked == true ? "Both" : "Top") : (_bottomLayer.IsChecked == true ? "Bot" : "None"),
            SheetPrefix = _sheetPrefix.Text.Trim(), SheetNamePattern = _sheetName.Text.Trim(), ViewNamePattern = _viewName.Text.Trim(),
            PlanScale = ParseInt(_planScale.Text, 20), SectionScale = ParseInt(_sectionScale.Text, 20), CropMarginMm = ParseDouble(_cropMargin.Text, 6),
            CropBottomMarginMm = ParseDouble(_cropBottomMargin.Text, 500),
            ApplyViewTemplatesOnce = _templateOnce.IsChecked == true,
            CreateBeamBreakLines = _beamBreaks.IsChecked == true, BreakLineFamily = FamilyName(Selected(_breakLine)), BreakLineType = TypeName(Selected(_breakLine)),
            BreakLinePaddingPaperMm = ParseDouble(_breakPadding.Text, 1), ReverseBreakLineMask = _reverseBreak.IsChecked == true,
            UseDetailedLayout = true, DimensionFourSides = _fourSides.IsChecked == true,
            DimensionOffsetPaperMm = ParseDouble(_dimOffset.Text, 8), DimensionGapPaperMm = ParseDouble(_dimGap.Text, 6),
            SectionDepthMm = ParseDouble(_sectionDepth.Text, 1200),
            SectionXOffsetMm = ParseDouble(_sectionXOffset.Text, 0), SectionYOffsetMm = ParseDouble(_sectionYOffset.Text, 0),
            ReverseSectionX = _reverseX.IsChecked == true, ReverseSectionY = _reverseY.IsChecked == true,
            SheetRightReserveMm = ParseDouble(_reserveRight.Text, 70), SheetBottomReserveMm = ParseDouble(_reserveBottom.Text, 35),
            SheetMarginMm = ParseDouble(_sheetMargin.Text, 10), SourceViewUniqueId = Selected(_sourcePlan)?.UniqueId,
            CreateCallouts = _callouts.IsChecked == true, CreateSections = AnyDetailSelected, CreateDetailA = _detailA.IsChecked == true, CreateDetailB = _detailB.IsChecked == true,
            CreateDimensions = _dimensions.IsChecked == true, CreateTags = _tags.IsChecked == true,
            FootingKindMode = _footingKind.SelectedItem as string ?? "Auto",
            AnnotateBot = _noteBot.IsChecked == true, AnnotateTop = _noteTop.IsChecked == true,
            PlaceOnNewSheet = _newSheet.IsChecked == true, ExistingSheetUniqueId = SheetUniqueId(_existingSheet)
        };
        private void ApplyProfile(FootingDrawingProfile p)
        {
            _templateOnce.IsChecked = p.ApplyViewTemplatesOnce;
            _footingKind.SelectedItem = new[] { "Auto", "Isolated", "PileCap" }.Contains(p.FootingKindMode) ? p.FootingKindMode : "Auto";
            _noteBot.IsChecked = p.AnnotateBot;
            _noteTop.IsChecked = p.AnnotateTop;
            _usePlanDetail.IsChecked = p.UsePlanDetailComponents;
            _planSizeConfirmed.IsChecked = p.PlanDetailSizingConfirmed;
            _planCoverMm.Text = p.PlanDetailCoverMm.ToString(System.Globalization.CultureInfo.CurrentCulture);
            _alignRebarCover.IsChecked = p.PlanDetailAlignToCover;
            _readHostCover.IsChecked = p.PlanDetailReadHostCover;
            string[] expressions = FootingPlanDetailComponentSizing.Expressions(p);
            for (int i = 0; i < _planExpressions.Length; i++)
                _planExpressions[i].Text = expressions[i] ?? "KEEP";
            _planTopDetail.SelectedItem = _planTopDetail.Items.Cast<Item>().FirstOrDefault(x => x.Element?.UniqueId == p.PlanTopDetailTypeUniqueId);
            _planBotDetail.SelectedItem = _planBotDetail.Items.Cast<Item>().FirstOrDefault(x => x.Element?.UniqueId == p.PlanBotDetailTypeUniqueId);
            _pileSection2D.IsChecked = p.CreatePileSectionSymbols;
            _pileOriginConfirmed.IsChecked = p.PileSymbolOriginConfirmed;
            _pileDiameterParameter.Text = p.PileDiameterParameterName ?? "";
            _pileFamilyFilter.Text = p.PileFamilyFilter ?? "cọc,coc,pile";
            _pileSectionFamily.SelectedItem = _pileSectionFamily.Items.Cast<Item>()
                .FirstOrDefault(x => x.Element?.UniqueId == p.PileSectionDetailTypeUniqueId);
            _beamBreaks.IsChecked = p.CreateBeamBreakLines; _reverseBreak.IsChecked = p.ReverseBreakLineMask;
            _breakPadding.Text = p.BreakLinePaddingPaperMm.ToString("0.##"); SelectByPair(_breakLine, p.BreakLineFamily, p.BreakLineType);
            _fourSides.IsChecked = p.DimensionFourSides;
            _dimOffset.Text = p.DimensionOffsetPaperMm.ToString("0.##"); _dimGap.Text = p.DimensionGapPaperMm.ToString("0.##");
            _sectionDepth.Text = p.SectionDepthMm.ToString("0.##");
            _sectionXOffset.Text = p.SectionXOffsetMm.ToString("0.##"); _sectionYOffset.Text = p.SectionYOffsetMm.ToString("0.##");
            _reverseX.IsChecked = p.ReverseSectionX; _reverseY.IsChecked = p.ReverseSectionY;
            _reserveRight.Text = p.SheetRightReserveMm.ToString("0.##"); _reserveBottom.Text = p.SheetBottomReserveMm.ToString("0.##");
            _sheetMargin.Text = p.SheetMarginMm.ToString("0.##");
            if (!string.IsNullOrWhiteSpace(p.SourceViewUniqueId))
            {
                var recovered = _sourcePlan.Items.Cast<Item>()
                    .FirstOrDefault(x => x.Element?.UniqueId == p.SourceViewUniqueId);
                // Preserve the current valid source plan when an old saved UniqueId was removed.
                if (recovered != null) _sourcePlan.SelectedItem = recovered;
            }
            if (Selected(_sourcePlan) == null)
            {
                var current = _sourcePlan.Items.Cast<Item>()
                    .FirstOrDefault(x => x.Element?.Id.Value == _parent?.Id.Value);
                if (current != null) _sourcePlan.SelectedItem = current;
            }
            SelectBy(_planType, p.PlanTypeName); SelectBy(_detailType, p.DetailTypeName);
            if (string.IsNullOrWhiteSpace(p.DetailTypeName) && !string.IsNullOrWhiteSpace(p.SectionTypeName)) _detailType.SelectedIndex = -1;
            SelectByPair(_titleBlock, p.TitleBlockFamily, p.TitleBlockType);
            SelectBy(_planTemplate, p.PlanTemplateName); SelectBy(_sectionTemplate, p.SectionTemplateName);
            SelectBy(_rebarTag, p.RebarMultiTagTypeName); SelectByPair(_foundationTag, p.FoundationTagFamily, p.FoundationTagType);
            SelectBy(_dimension, p.DimensionTypeName);
            _topLayer.IsChecked = p.OutputLayers == "Top" || p.OutputLayers == "Both";
            _bottomLayer.IsChecked = p.OutputLayers == "Bot" || p.OutputLayers == "Both";
            _sheetPrefix.Text = p.SheetPrefix ?? "KSMD-"; _sheetName.Text = p.SheetNamePattern ?? "Móng đơn - {Mark}"; _viewName.Text = p.ViewNamePattern ?? "KStruct - MĐ {Id} - {Role}";
            _planScale.Text = p.PlanScale.ToString(); _sectionScale.Text = p.SectionScale.ToString(); _cropMargin.Text = p.CropMarginMm.ToString("0.##");
            _cropBottomMargin.Text = (p.CropBottomMarginMm > 0 ? p.CropBottomMarginMm : 500).ToString("0.##");
            _callouts.IsChecked = p.CreateCallouts; _detailA.IsChecked = p.WantsDetailA; _detailB.IsChecked = p.WantsDetailB; _dimensions.IsChecked = p.CreateDimensions; _tags.IsChecked = p.CreateTags;
            _newSheet.IsChecked = p.PlaceOnNewSheet; _existingSheet.IsEnabled = !p.PlaceOnNewSheet;
            var sheet = new FilteredElementCollector(_doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().FirstOrDefault(x => x.UniqueId == p.ExistingSheetUniqueId);
            _existingSheet.SelectedIndex = 0;
            if (sheet != null) _existingSheet.SelectedItem = _existingSheet.Items.Cast<Item>().FirstOrDefault(x => x.Element?.Id == sheet.Id);
            _newSheet.IsChecked = p.PlaceOnNewSheet;
            _existingSheet.IsEnabled = !p.PlaceOnNewSheet;
            var missing = new List<string>();
            if (!string.IsNullOrWhiteSpace(p.PlanTypeName) && Selected(_planType) == null) missing.Add("View Type mặt bằng");
            if (!string.IsNullOrWhiteSpace(p.DetailTypeName ?? p.SectionTypeName) && Selected(_detailType) == null) missing.Add("Detail Type");
            if (!string.IsNullOrWhiteSpace(p.PlanTemplateName) && Selected(_planTemplate) == null) missing.Add("Template mặt bằng");
            if (!string.IsNullOrWhiteSpace(p.SectionTemplateName) && Selected(_sectionTemplate) == null) missing.Add("Template mặt cắt");
            if (!string.IsNullOrWhiteSpace(p.RebarMultiTagTypeName ?? p.RebarTagType) && Selected(_rebarTag) == null) missing.Add("Multi-Rebar Annotation Type (tag đơn cũ cần chọn lại)");
            if (!string.IsNullOrWhiteSpace(p.FoundationTagType) && Selected(_foundationTag) == null) missing.Add("Tag móng");
            if (!string.IsNullOrWhiteSpace(p.BreakLineType) && Selected(_breakLine) == null) missing.Add("Family Breakline");
            if (!string.IsNullOrWhiteSpace(p.DimensionTypeName) && Selected(_dimension) == null) missing.Add("Dimension Type");
            if (p.PlaceOnNewSheet && Selected(_titleBlock) == null) missing.Add("Title Block");
            if (p.CreateCallouts && Selected(_sourcePlan) == null) missing.Add("View nguồn");
            if (!p.PlaceOnNewSheet && Selected(_existingSheet) == null) missing.Add("Sheet đích");
            if (missing.Count > 0) MessageBox.Show(this, "Một số lựa chọn đã lưu không có trong project hiện tại; hãy chọn lại: " + string.Join(", ", missing), "KStruct — Khôi phục lựa chọn");
            RefreshPreviews();
        }
        private static void SelectBy(ComboBox box, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) { if (box.Items.Count > 0 && box.Items[0] is Item first && first.Element == null) box.SelectedIndex = 0; return; }
            box.SelectedItem = box.Items.Cast<Item>().FirstOrDefault(x => x.Element != null && x.Element.Name == name);
        }
        private static void SelectByPair(ComboBox box, string family, string type)
        {
            if (string.IsNullOrWhiteSpace(type)) { if (box.Items.Count > 0 && box.Items[0] is Item first && first.Element == null) box.SelectedIndex = 0; return; }
            box.SelectedItem = box.Items.Cast<Item>().FirstOrDefault(x => x.Element != null && x.Element.Name == type && (family == null || FamilyName(x.Element) == family));
        }
        private void SaveProfile()
        {
            string name = _profileName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;
            List<FootingDrawingProfile> items = FootingDrawingProfileStore.Load();
            items.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            items.Add(ReadProfile(name)); FootingDrawingProfileStore.Save(items); RefreshProfiles(name);
        }
        private void ImportProfiles()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "KStruct footing profiles (*.xml)|*.xml|All files (*.*)|*.*" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                List<FootingDrawingProfile> saved = FootingDrawingProfileStore.Load();
                foreach (FootingDrawingProfile imported in FootingDrawingProfileStore.Import(dialog.FileName))
                {
                    saved.RemoveAll(x => string.Equals(x.Name, imported.Name, StringComparison.OrdinalIgnoreCase));
                    saved.Add(imported);
                }
                FootingDrawingProfileStore.Save(saved); RefreshProfiles();
            }
            catch (Exception ex) { MessageBox.Show(this, "Không nhập được preset: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
        private void ExportProfiles()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "KStruct footing profiles (*.xml)|*.xml", FileName = "FootingDrawingProfiles.xml" };
            if (dialog.ShowDialog(this) != true) return;
            try { FootingDrawingProfileStore.Export(dialog.FileName, FootingDrawingProfileStore.Load()); }
            catch (Exception ex) { MessageBox.Show(this, "Không xuất được preset: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
        private void DeleteProfile()
        {
            FootingDrawingProfile selected = _profiles.SelectedItem as FootingDrawingProfile;
            if (selected == null) return;
            var items = FootingDrawingProfileStore.Load(); items.RemoveAll(x => x.Name == selected.Name); FootingDrawingProfileStore.Save(items); RefreshProfiles();
        }
        private void Accept()
        {
            if (CheckedFootings.Count == 0)
            { MessageBox.Show(this, "Hãy tích chọn ít nhất một móng.", "Thiếu móng"); return; }
            if (_topLayer.IsChecked != true && _bottomLayer.IsChecked != true && !AnyDetailSelected)
            { MessageBox.Show(this, "Hãy chọn ít nhất một mặt bằng hoặc mặt cắt.", "Thiếu view"); return; }
            if (AnyDetailSelected && (_callouts.IsChecked != true || (_topLayer.IsChecked != true && _bottomLayer.IsChecked != true)))
            { MessageBox.Show(this, "Mặt cắt cần callout móng. Hãy bật Callout và chọn thép trên hoặc dưới.", "Thiếu callout"); return; }
            if (_tags.IsChecked == true && _usePlanDetail.IsChecked != true && !(Selected(_rebarTag) is MultiReferenceAnnotationType))
            { MessageBox.Show(this, "Hãy chọn Multi-Rebar Annotation Type có Rebar Tag và Dimension Type, hoặc tắt tạo tag. Tag thép đơn không dùng cho MultiTag.", "Thiếu MultiTag"); return; }
            if (_usePlanDetail.IsChecked == true && _planSizeConfirmed.IsChecked != true)
            { MessageBox.Show(this, "Hãy đối chiếu L/W/L1/L2/X/Y với Family Types rồi xác nhận ánh xạ kích thước.", "Chưa xác nhận kích thước"); return; }
            bool anyPileCap = CheckedFootings.Any(f =>
                FootingDrawingLayerPolicy.IsPileCap(_footingKind.SelectedItem as string, IsPileCap(f)));
            if (_usePlanDetail.IsChecked == true &&
                ((anyPileCap && !(Selected(_planTopDetail) is FamilySymbol)) ||
                 !(Selected(_planBotDetail) is FamilySymbol)))
            { MessageBox.Show(this, "Mọi móng cần Family BOT; nếu có móng cọc phải chọn thêm Family TOP.", "Thiếu Detail Item"); return; }
            if (_usePlanDetail.IsChecked == true)
            {
                double cover = ParseDouble(_planCoverMm.Text, -1);
                if (cover < 0 || double.IsNaN(cover) || double.IsInfinity(cover))
                { MessageBox.Show(this, "Lớp bảo vệ phải là số mm không âm.", "Sai lớp bảo vệ"); return; }
                try {
                    foreach (string expression in _planExpressions.Select(t => t.Text))
                        if (!string.IsNullOrWhiteSpace(expression) && !expression.Trim().Equals("KEEP", StringComparison.OrdinalIgnoreCase))
                            FootingPlanDetailComponentSizing.Evaluate(expression, 2000, 1800, cover);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Công thức không hợp lệ"); return; }
            }
            if (_pileSection2D.IsChecked == true &&
                CheckedFootings.Any(f => FootingDrawingLayerPolicy.IsPileCap(
                    _footingKind.SelectedItem as string, IsPileCap(f))))
            {
                var pileSymbol = Selected(_pileSectionFamily) as FamilySymbol;
                if (pileSymbol == null || pileSymbol.Family.FamilyPlacementType != FamilyPlacementType.ViewBased)
                { MessageBox.Show(this, "Hãy chọn Family BS_FM_Detail Cọc 2D dạng ViewBased (đặt bằng điểm).", "Thiếu Family Cọc 2D"); return; }
                if (!AnyDetailSelected)
                { MessageBox.Show(this, "Phải bật Detail A–A hoặc B–B để đặt ký hiệu Cọc 2D.", "Thiếu View Detail"); return; }
                if (_pileOriginConfirmed.IsChecked != true)
                { MessageBox.Show(this, "Phải mở Family Editor và xác nhận nét tim BS_FM_Detail Cọc 2D nằm tại gốc đặt Family. Không thể suy ra từ tên tham số.", "Chưa xác nhận tim Family"); return; }
                if (string.IsNullOrWhiteSpace(_pileFamilyFilter.Text) ||
                    _pileFamilyFilter.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Length == 0)
                { MessageBox.Show(this, "Nhập từ khóa Family cọc để KStruct nhận diện cọc thật.", "Thiếu bộ lọc cọc"); return; }
            }
            ViewPlan source = Selected(_sourcePlan) as ViewPlan;
            if (_callouts.IsChecked == true && source == null)
            { MessageBox.Show(this, "Hãy chọn mặt bằng nguồn hoặc tắt callout để tạo plan độc lập.", "Thiếu view nguồn"); return; }
            ViewFamilyType detail = Selected(_detailType) as ViewFamilyType;
            if (!(Selected(_planType) is ViewFamilyType plan) || (AnyDetailSelected && detail == null) || (_newSheet.IsChecked == true && !(Selected(_titleBlock) is FamilySymbol)))
            { MessageBox.Show(this, "Cần chọn View Type mặt bằng, Detail Type và Title Block khi tạo sheet mới.", "Thiếu cấu hình", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (AnyDetailSelected && detail.ViewFamily != ViewFamily.Detail)
            { MessageBox.Show(this, "Cần chọn Detail Type, không dùng Section Type.", "Sai loại view"); return; }
            if (_callouts.IsChecked == true && source != null && plan.ViewFamily != (_doc.GetElement(source.GetTypeId()) as ViewFamilyType)?.ViewFamily)
            { MessageBox.Show(this, "Callout phải dùng View Type cùng họ với mặt bằng nguồn.", "View Type không tương thích", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            int planScale = ParseInt(_planScale.Text, -1), sectionScale = ParseInt(_sectionScale.Text, -1);
            double margin = ParseDouble(_cropMargin.Text, -1);
            double cropBottom = ParseDouble(_cropBottomMargin.Text, -1);
            if (planScale <= 0 || (AnyDetailSelected && sectionScale <= 0) || double.IsNaN(margin) || double.IsInfinity(margin) || margin < 0 ||
                double.IsNaN(cropBottom) || double.IsInfinity(cropBottom) || cropBottom < 0)
            { MessageBox.Show(this, "Tỷ lệ phải là số nguyên dương; lề crop và mở rộng đáy không được âm.", "Thông số chưa hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (_newSheet.IsChecked != true && !(Selected(_existingSheet) is ViewSheet))
            { MessageBox.Show(this, "Hãy chọn sheet hiện có hoặc bật tạo sheet mới.", "Thiếu sheet", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (!ValidateExtraFields()) return;
            if (_newSheet.IsChecked != true && CheckedFootings.Count > 1)
            { MessageBox.Show(this, "Sheet có sẵn chỉ hỗ trợ một móng mỗi lần.", "Chọn một móng"); return; }
            var selectedIds = new HashSet<string>(CheckedFootings.Select(f => f.UniqueId));
            bool alreadyHasDrawings = new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>()
                .Any(v => FootingDrawingStorage.TryRead(v, out string host, out string role, out double crop) && selectedIds.Contains(host));
            if (alreadyHasDrawings && MessageBox.Show(this,
                "Một số móng đã có hồ sơ. Tiếp tục sẽ tạo bộ view mới và giữ nguyên hồ sơ cũ. Để cập nhật, dùng lệnh Cập nhật hồ sơ.",
                "Tạo thêm hồ sơ?", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            FootingDrawingProfile profile = ReadProfile();
            Configuration = new FootingDrawingConfiguration
            {
                Profile = profile, PlanType = plan, DetailType = detail, TitleBlock = Selected(_titleBlock) as FamilySymbol,
                PlanTemplate = Selected(_planTemplate) as View, SectionTemplate = Selected(_sectionTemplate) as View,
                RebarMultiTagType = Selected(_rebarTag) as MultiReferenceAnnotationType, FoundationTagType = Selected(_foundationTag) as ElementType,
                DimensionType = Selected(_dimension) as DimensionType, ExistingSheet = Selected(_existingSheet) as ViewSheet,
                SourcePlan = source, SelectedFootings = CheckedFootings
            };
            SaveSession();
            DialogResult = true;
        }
        private static int ParseInt(string text, int fallback) => int.TryParse(text, out int value) ? value : fallback;
        private static double ParseDouble(string text, double fallback) => double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out double value) ? value : fallback;
    }
}









