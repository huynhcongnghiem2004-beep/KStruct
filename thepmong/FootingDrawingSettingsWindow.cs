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
        internal ViewFamilyType SectionType { get; set; }
        internal FamilySymbol TitleBlock { get; set; }
        internal View PlanTemplate { get; set; }
        internal View SectionTemplate { get; set; }
        internal ElementType RebarTagType { get; set; }
        internal ElementType FoundationTagType { get; set; }
        internal DimensionType DimensionType { get; set; }
        internal ViewSheet ExistingSheet { get; set; }
    }

    internal sealed class FootingDrawingSettingsWindow : Window
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
        private readonly ComboBox _sectionType = new ComboBox { MinWidth = 260 };
        private readonly ComboBox _titleBlock = new ComboBox { MinWidth = 260 };
        private readonly ComboBox _planTemplate = new ComboBox { MinWidth = 150 };
        private readonly ComboBox _planBottomTemplate = new ComboBox { MinWidth = 150 };
        private readonly ComboBox _sectionTemplate = new ComboBox { MinWidth = 260 };
        private readonly ComboBox _rebarTag = new ComboBox { MinWidth = 260 };
        private readonly ComboBox _foundationTag = new ComboBox { MinWidth = 260 };
        private readonly ComboBox _dimension = new ComboBox { MinWidth = 150 };
        private readonly ComboBox _sectionDimension = new ComboBox { MinWidth = 150 };
        private readonly ComboBox _sectionRebarTag = new ComboBox { MinWidth = 150 };
        private readonly ComboBox _sectionFoundationTag = new ComboBox { MinWidth = 150 };
        private readonly ComboBox _sectionLineStyle = new ComboBox { MinWidth = 120 };
        private readonly RadioButton _sectionVertical = new RadioButton { Content = "Mặt cắt dọc", IsChecked = true, GroupName = "SectionAxis" };
        private readonly RadioButton _sectionHorizontal = new RadioButton { Content = "Mặt cắt ngang", GroupName = "SectionAxis" };
        private Canvas _planCanvas;
        private Canvas _sectionCanvas;
        private readonly ComboBox _existingSheet = new ComboBox { MinWidth = 260 };
        private readonly ComboBox _layers = new ComboBox { MinWidth = 260, ItemsSource = new[] { "Top", "Bot", "Both" }, SelectedItem = "Both" };
        private readonly CheckBox _topLayer = new CheckBox { Content = "Tạo chế độ xem chi tiết trên cùng", IsChecked = true, Margin = new Thickness(4) };
        private readonly CheckBox _bottomLayer = new CheckBox { Content = "Tạo chế độ xem chi tiết dưới cùng", IsChecked = true, Margin = new Thickness(4) };
        private readonly CheckBox _twoLayer = new CheckBox { Content = "Tạo chế độ xem chi tiết 2 lớp", Margin = new Thickness(4) };
        private readonly CheckBox _callouts = new CheckBox { Content = "Tạo callout mặt bằng", IsChecked = true };
        private readonly CheckBox _sections = new CheckBox { Content = "Tạo hai mặt cắt vuông góc", IsChecked = true };
        private readonly CheckBox _dimensions = new CheckBox { Content = "Tạo kích thước host", IsChecked = true };
        private readonly CheckBox _tags = new CheckBox { Content = "Gắn tag móng và Rebar", IsChecked = true };
        private readonly CheckBox _newSheet = new CheckBox { Content = "Tạo sheet mới cho từng móng", IsChecked = true };
        private readonly TextBox _planScale = new TextBox { Text = "20", MinWidth = 90 };
        private readonly TextBox _sectionScale = new TextBox { Text = "20", MinWidth = 90 };
        private readonly TextBox _cropMargin = new TextBox { Text = "300", MinWidth = 90 };
        private readonly TextBox _sheetPrefix = new TextBox { Text = "KSMD-", MinWidth = 240 };
        private readonly TextBox _sheetName = new TextBox { Text = "Móng đơn - {Mark}", MinWidth = 240 };
        private readonly TextBox _viewName = new TextBox { Text = "KStruct - MĐ {Id} - {Role}", MinWidth = 240 };
        private readonly TextBox _profileName = new TextBox { MinWidth = 180 };

        internal FootingDrawingConfiguration Configuration { get; private set; }

                private readonly ComboBox _footingTypeSelector = new ComboBox { MinWidth = 180 };
        private static bool IsPileCap(FamilyInstance fi)
        {
            if (fi == null) return false;
            string fam = fi.Symbol?.FamilyName ?? "";
            string typ = fi.Name ?? "";
            return fam.IndexOf("cọc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fam.IndexOf("coc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fam.IndexOf("pile", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fam.IndexOf("cap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fam.IndexOf("đài", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fam.IndexOf("dai", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typ.IndexOf("cọc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typ.IndexOf("pile", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal FootingDrawingSettingsWindow(Document doc, IntPtr owner, ViewPlan parent, IList<FamilyInstance> footings = null)
        {
            _doc = doc;
            _parent = parent;
            _callouts.IsChecked = parent != null;
            _callouts.IsEnabled = parent != null;
            Title = "KStruct - Tạo hồ sơ móng cọc & móng đơn";
            Width = 1140;
            Height = 600;
            MinWidth = 1080;
            MinHeight = 560;
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
                .Where(x => (parentType == null || x.ViewFamily == parentType.ViewFamily) &&
                    (x.ViewFamily == ViewFamily.StructuralPlan || x.ViewFamily == ViewFamily.FloorPlan))
                .Select(x => new Item { Text = x.Name + " | " + x.ViewFamily, Element = x }), "StructuralPlan");
            Populate(_sectionType, new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .Where(x => x.ViewFamily == ViewFamily.Section).Select(x => new Item { Text = x.Name, Element = x }), null);
            Populate(_titleBlock, new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Select(x => new Item { Text = x.FamilyName + " : " + x.Name, Element = x }), null);
            Populate(_planTemplate, Templates(doc).Select(x => new Item { Text = x.Name + " | " + x.ViewType, Element = x }), null, true);
            Populate(_sectionTemplate, Templates(doc).Select(x => new Item { Text = x.Name + " | " + x.ViewType, Element = x }), null, true);
            Populate(_rebarTag, new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RebarTags).WhereElementIsElementType().Cast<ElementType>()
                .Select(x => new Item { Text = x.FamilyName + " : " + x.Name, Element = x }), null, true);
            Populate(_foundationTag, new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFoundationTags).WhereElementIsElementType().Cast<ElementType>()
                .Select(x => new Item { Text = x.FamilyName + " : " + x.Name, Element = x }), null, true);
            Populate(_dimension, new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>()
                .Select(x => new Item { Text = x.Name, Element = x }), null, true);
            Populate(_existingSheet, new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(x => !x.IsPlaceholder).Select(x => new Item { Text = x.SheetNumber + " | " + x.Name, Element = x }), null, true);
            Populate(_planBottomTemplate, Templates(doc).Select(x => new Item { Text = x.Name + " | " + x.ViewType, Element = x }), null, true);
            Populate(_sectionDimension, new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().Select(x => new Item { Text = x.Name, Element = x }), null, true);
            Populate(_sectionRebarTag, new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RebarTags).WhereElementIsElementType().Cast<ElementType>().Select(x => new Item { Text = x.FamilyName + " : " + x.Name, Element = x }), null, true);
            Populate(_sectionFoundationTag, new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFoundationTags).WhereElementIsElementType().Cast<ElementType>().Select(x => new Item { Text = x.FamilyName + " : " + x.Name, Element = x }), null, true);
            _sectionLineStyle.Items.Add("Solid"); _sectionLineStyle.Items.Add("Dash"); _sectionLineStyle.SelectedIndex = 1;
            MirrorSelection(_planTemplate, _planBottomTemplate);
            MirrorSelection(_dimension, _sectionDimension);
            MirrorSelection(_rebarTag, _sectionRebarTag);
            MirrorSelection(_foundationTag, _sectionFoundationTag);
            _existingSheet.SelectionChanged += (s, e) => { if (_existingSheet.SelectedItem is Item i && i.Element is ViewSheet) _newSheet.IsChecked = false; };
            _newSheet.Checked += (s, e) => _existingSheet.IsEnabled = false;
            _newSheet.Unchecked += (s, e) => _existingSheet.IsEnabled = true;
            foreach (CheckBox check in new[] { _topLayer, _bottomLayer, _twoLayer, _callouts, _sections, _dimensions, _tags })
            { check.Checked += (s, e) => RefreshPreviews(); check.Unchecked += (s, e) => RefreshPreviews(); }
            foreach (ComboBox combo in new[] { _planType, _sectionType, _planTemplate, _planBottomTemplate, _sectionTemplate, _dimension, _sectionDimension, _rebarTag, _sectionRebarTag, _foundationTag, _sectionFoundationTag, _sectionLineStyle })
                combo.SelectionChanged += (s, e) => RefreshPreviews();
            _sectionVertical.Checked += (s, e) => RefreshPreviews();
            _sectionHorizontal.Checked += (s, e) => RefreshPreviews();
            _planScale.TextChanged += (s, e) => RefreshPreviews();
            _sectionScale.TextChanged += (s, e) => RefreshPreviews();
            _cropMargin.TextChanged += (s, e) => RefreshPreviews();

            var root = new System.Windows.Controls.Grid { Margin = new Thickness(3) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var workArea = new System.Windows.Controls.Grid();
            workArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            workArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var planGroup = new GroupBox { Header = "☑  Mặt bằng kết cấu", Margin = new Thickness(0, 0, 3, 4), Padding = new Thickness(4) };
            var sectionGroup = new GroupBox { Header = "☑  Mặt cắt", Margin = new Thickness(3, 0, 0, 4), Padding = new Thickness(4) };
            planGroup.Content = BuildPlanPanel(); sectionGroup.Content = BuildSectionPanel();
            System.Windows.Controls.Grid.SetColumn(sectionGroup, 1);
            workArea.Children.Add(planGroup); workArea.Children.Add(sectionGroup);
            root.Children.Add(workArea);

            var sheetGroup = new GroupBox { Header = "☑  Bản vẽ", Foreground = Brushes.DarkRed, Margin = new Thickness(0, 2, 0, 4), Padding = new Thickness(4) };
            sheetGroup.Content = BuildSheetPanel();
            System.Windows.Controls.Grid.SetRow(sheetGroup, 1); root.Children.Add(sheetGroup);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "TẠO HỒ SƠ", IsDefault = true, Padding = new Thickness(16, 5, 16, 5), Background = Brush("#1267A5"), Foreground = Brushes.White, BorderBrush = Brush("#1267A5"), FontWeight = FontWeights.Bold };
            var cancel = new Button { Content = "Đóng", IsCancel = true, Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(5, 0, 0, 0) };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            System.Windows.Controls.Grid.SetRow(buttons, 2); root.Children.Add(buttons);
            Content = root; ok.Click += (s, e) => Accept();
            RefreshProfiles();
            if (_planType.Items.Count > 0) _planType.SelectedIndex = 0;
            if (_sectionType.Items.Count > 0) _sectionType.SelectedIndex = 0;
            if (_titleBlock.Items.Count > 0) _titleBlock.SelectedIndex = 0;
            _dimensions.IsChecked = _dimension.Items.Count > 0; _tags.IsChecked = _rebarTag.Items.Count > 0 || _foundationTag.Items.Count > 0;
            _existingSheet.IsEnabled = false;
            RefreshPreviews();
        }

        private UIElement BuildPlanPanel()
        {
            var root = new System.Windows.Controls.Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var form = new StackPanel();
            var layerRow = new WrapPanel();
            layerRow.Children.Add(_topLayer); layerRow.Children.Add(_bottomLayer);
            form.Children.Add(layerRow);
            form.Children.Add(_twoLayer);
            form.Children.Add(CreatePairRow("① Loại Dim:", _dimension, "Loại callout:", _planType));
            form.Children.Add(CreatePairRow("② Thư viện tag:", _rebarTag, "Tag móng:", _foundationTag));
            form.Children.Add(CreatePairRow("Mẫu khung nhìn trên:", _planTemplate, "Dưới:", _planBottomTemplate));
            form.Children.Add(CreateWideRow("Tỷ lệ:", _planScale));
            var flags = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
            flags.Children.Add(_callouts); flags.Children.Add(_dimensions); flags.Children.Add(_tags);
            form.Children.Add(flags);
            root.Children.Add(form);
            var previewHost = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            previewHost.Children.Add(new CheckBox { Content = "Trên", HorizontalAlignment = HorizontalAlignment.Center, IsEnabled = false });
            _planCanvas = new Canvas { Width = 500, Height = 300, Background = Brushes.White };
            previewHost.Children.Add(new Border { BorderBrush = Brush("#C5C5C5"), BorderThickness = new Thickness(1), Background = Brushes.White, Padding = new Thickness(4), Child = _planCanvas });
            System.Windows.Controls.Grid.SetRow(previewHost, 3); root.Children.Add(previewHost);
            return root;
        }

        private UIElement BuildSectionPanel()
        {
            var root = new System.Windows.Controls.Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var form = new StackPanel();
            var axis = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            axis.Children.Add(_sectionVertical); axis.Children.Add(_sectionHorizontal);
            form.Children.Add(axis);
            form.Children.Add(CreatePairRow("Loại mặt cắt:", _sectionType, "Mở rộng vùng cắt:", _cropMargin));
            form.Children.Add(CreatePairRow("Mẫu khung nhìn:", _sectionTemplate, "Tỷ lệ:", _sectionScale));
            form.Children.Add(CreatePairRow("① Loại Dim:", _sectionDimension, "② Nét cắt:", _sectionLineStyle));
            form.Children.Add(CreatePairRow("③ Thư viện tag:", _sectionRebarTag, "④ Thư viện tag:", _sectionFoundationTag));
            form.Children.Add(_sections);
            root.Children.Add(form);
            var previewHost = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _sectionCanvas = new Canvas { Width = 500, Height = 300, Background = Brushes.White };
            previewHost.Children.Add(new Border { BorderBrush = Brush("#C5C5C5"), BorderThickness = new Thickness(1), Background = Brushes.White, Padding = new Thickness(4), Child = _sectionCanvas });
            var sides = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            sides.Children.Add(new CheckBox { Content = "Trái", IsChecked = true, IsEnabled = false, Margin = new Thickness(30, 2, 70, 0) });
            sides.Children.Add(new CheckBox { Content = "Phải", IsEnabled = false, Margin = new Thickness(30, 2, 10, 0) });
            previewHost.Children.Add(sides);
            previewHost.Children.Add(new CheckBox { Content = "Dưới", IsChecked = true, IsEnabled = false, HorizontalAlignment = HorizontalAlignment.Center });
            System.Windows.Controls.Grid.SetRow(previewHost, 2); root.Children.Add(previewHost);
            return root;
        }

        private UIElement BuildSheetPanel()
        {
            var stack = new StackPanel();
            var grid = new System.Windows.Controls.Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            AddField(grid, "Số Sheet", _sheetPrefix, 0);
            AddField(grid, "Tên Sheet:", _sheetName, 1);
            AddField(grid, "Title block:", _titleBlock, 2);
            AddField(grid, "View Name:", _viewName, 3);
            var lookup = new Button { Content = "⌕", FontSize = 25, Foreground = Brushes.DarkRed, Padding = new Thickness(5, 0, 5, 0), ToolTip = "Chọn sheet có sẵn" };
            lookup.Click += (s, e) => { _existingSheet.IsEnabled = true; _existingSheet.Focus(); };
            System.Windows.Controls.Grid.SetColumn(lookup, 4); grid.Children.Add(lookup);
            stack.Children.Add(grid);
            var options = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            options.Children.Add(_newSheet); options.Children.Add(_existingSheet);
            stack.Children.Add(options);
            var presets = new Expander { Header = "Preset cấu hình", Margin = new Thickness(0, 2, 0, 0) };
            var presetRow = new StackPanel { Orientation = Orientation.Horizontal };
            _profiles.MinWidth = 150; _profileName.MinWidth = 120;
            presetRow.Children.Add(_profiles); presetRow.Children.Add(_profileName);
            foreach (var entry in new[] { new { Label = "Nạp", Action = new Action(() => { if (_profiles.SelectedItem is FootingDrawingProfile p) ApplyProfile(p); }) },
                new { Label = "Lưu", Action = new Action(() => SaveProfile()) }, new { Label = "Xóa", Action = new Action(() => DeleteProfile()) },
                new { Label = "Nhập XML", Action = new Action(() => ImportProfiles()) }, new { Label = "Xuất XML", Action = new Action(() => ExportProfiles()) } })
            { var button = new Button { Content = entry.Label, Margin = new Thickness(3, 0, 0, 0) }; button.Click += (s, e) => entry.Action(); presetRow.Children.Add(button); }
            presets.Content = presetRow; stack.Children.Add(presets);
            return stack;
        }

        private static UIElement CreatePairRow(string label1, UIElement control1, string label2, UIElement control2)
        {
            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(2, 2, 2, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var first = new TextBlock { Text = label1, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 4, 0) };
            var second = new TextBlock { Text = label2, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 0, 4, 0) };
            System.Windows.Controls.Grid.SetColumn(first, 0); System.Windows.Controls.Grid.SetColumn(control1, 1);
            System.Windows.Controls.Grid.SetColumn(second, 2); System.Windows.Controls.Grid.SetColumn(control2, 3);
            grid.Children.Add(first); grid.Children.Add(control1); grid.Children.Add(second); grid.Children.Add(control2);
            return grid;
        }

        private static UIElement CreateWideRow(string label, System.Windows.Controls.Control control)
        {
            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(2, 2, 2, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(105) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var caption = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            System.Windows.Controls.Grid.SetColumn(control, 1); grid.Children.Add(caption); grid.Children.Add(control);
            return grid;
        }

        private static void AddField(System.Windows.Controls.Grid parent, string label, System.Windows.Controls.Control control, int column)
        {
            var panel = new StackPanel { Margin = new Thickness(2, 0, 5, 0) };
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2) });
            panel.Children.Add(control);
            System.Windows.Controls.Grid.SetColumn(panel, column); parent.Children.Add(panel);
        }

        private static string SelectedText(ComboBox box)
        {
            return (box.SelectedItem as Item)?.Text ?? "(Không dùng)";
        }

        private static void MirrorSelection(ComboBox first, ComboBox second)
        {
            bool syncing = false;
            Action<ComboBox, ComboBox> mirror = (source, target) =>
            {
                if (syncing) return;
                syncing = true;
                string text = SelectedText(source);
                target.SelectedItem = target.Items.Cast<object>().FirstOrDefault(x => ((Item)x).Text == text);
                syncing = false;
            };
            first.SelectionChanged += (s, e) => mirror(first, second);
            second.SelectionChanged += (s, e) => mirror(second, first);
            if (first.SelectedIndex >= 0) mirror(first, second);
        }

        private void RefreshPreviews()
        {
            if (_planCanvas == null || _sectionCanvas == null) return;
            DrawPlanPreview(_planCanvas);
            DrawSectionPreview(_sectionCanvas);
        }

        private void DrawPlanPreview(Canvas canvas)
        {
            canvas.Children.Clear();
            AddPreviewNote(canvas, "Mặt bằng kết cấu  |  Tỷ lệ 1:" + _planScale.Text + "  |  Crop " + _cropMargin.Text + " mm");
            AddPreviewNote(canvas, "DIM: " + ShortLabel(_dimension) + "  |  Tag thép: " + ShortLabel(_rebarTag) + "  |  Tag móng: " + ShortLabel(_foundationTag));
            AddPreviewNote(canvas, "Template: " + ShortLabel(_planTemplate) + "  |  Lớp: " + (_topLayer.IsChecked == true ? "Trên " : "") + (_bottomLayer.IsChecked == true ? "Dưới" : "") + (_twoLayer.IsChecked == true ? "Hai lớp" : ""));
            bool top = _topLayer.IsChecked == true, bottom = _bottomLayer.IsChecked == true;
            var outer = new System.Windows.Shapes.Rectangle { Width = 290, Height = 210, Fill = Brush("#858585"), Stroke = Brushes.Black, StrokeThickness = 1.3 };
            Canvas.SetLeft(outer, 105); Canvas.SetTop(outer, 55); canvas.Children.Add(outer);
            var inner = new System.Windows.Shapes.Rectangle { Width = 230, Height = 150, Fill = Brush("#D5D5D5"), Stroke = Brushes.Black, StrokeThickness = 1.2 };
            Canvas.SetLeft(inner, 135); Canvas.SetTop(inner, 85); canvas.Children.Add(inner);
            var column = new System.Windows.Shapes.Rectangle { Width = 42, Height = 42, Fill = Brush("#555555"), Stroke = Brushes.Black, StrokeThickness = 1.2 };
            Canvas.SetLeft(column, 229); Canvas.SetTop(column, 139); canvas.Children.Add(column);
            if (top || _twoLayer.IsChecked == true) DrawPlanBars(canvas, 95, Brushes.Red);
            if (bottom || _twoLayer.IsChecked == true) DrawPlanBars(canvas, 211, Brushes.Black);

            if (_dimensions.IsChecked == true)
            {
                DrawDimension(canvas, 105, 45, 395, 45, "200", null);
                DrawDimension(canvas, 407, 55, 407, 265, "200", null);
                DrawDimension(canvas, 135, 277, 365, 277, "150", null);
            }
            if (_callouts.IsChecked == true)
            {
                DrawBubble(canvas, 43, 148, "1");
                DrawBubble(canvas, 427, 56, "2");
                DrawLeader(canvas, 249, 140, 310, 130, "Móng");
            }
            if (_tags.IsChecked == true)
            {
                DrawLeader(canvas, 308, 95, 372, 78, MakeTagLabel("Ø12a100", _rebarTag));
                DrawLeader(canvas, 248, 160, 365, 178, MakeTagLabel("Móng M1", _foundationTag));
            }
        }

        private void DrawSectionPreview(Canvas canvas)
        {
            canvas.Children.Clear();
            AddPreviewNote(canvas, "Mặt cắt  |  Tỷ lệ 1:" + _sectionScale.Text + "  |  Crop " + _cropMargin.Text + " mm");
            AddPreviewNote(canvas, "Template: " + ShortLabel(_sectionTemplate) + "  |  DIM: " + ShortLabel(_sectionDimension) + "  |  " + ShortLabel(_sectionType));
            if (_sections.IsChecked != true)
            {
                canvas.Children.Add(new TextBlock { Text = "Tạo mặt cắt đang tắt", FontSize = 14, Foreground = Brush("#777777") });
                return;
            }
            bool horizontal = _sectionHorizontal.IsChecked == true;
            var concrete = new System.Windows.Shapes.Rectangle { Width = 250, Height = 150, Fill = Brush("#D5D5D5"), Stroke = Brushes.Black, StrokeThickness = 1.2 };
            Canvas.SetLeft(concrete, 135); Canvas.SetTop(concrete, 53); canvas.Children.Add(concrete);
            var footing = new System.Windows.Shapes.Rectangle { Width = 310, Height = 40, Fill = Brush("#858585"), Stroke = Brushes.Black, StrokeThickness = 1.2 };
            Canvas.SetLeft(footing, 105); Canvas.SetTop(footing, 203); canvas.Children.Add(footing);
            for (int i = 0; i < 6; i++)
            {
                var dot = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = i < 3 ? Brushes.Red : Brushes.Black, Stroke = Brushes.Black, StrokeThickness = 0.5 };
                Canvas.SetLeft(dot, 165 + i * 38); Canvas.SetTop(dot, i < 3 ? 64 : 207); canvas.Children.Add(dot);
            }
            DrawLine(canvas, 155, 128, 365, 128, Brush("#2368C4"), _sectionLineStyle.SelectedItem?.ToString() == "Dash");
            DrawBubble(canvas, 439, 58, horizontal ? "2B" : "2A");
            if (_dimensions.IsChecked == true)
            {
                DrawDimension(canvas, 88, 53, 88, 243, "500", null);
                DrawDimension(canvas, 135, 256, 385, 256, "600", null);
                DrawDimension(canvas, 105, 280, 415, 280, "700", null);
            }
            if (_tags.IsChecked == true)
            {
                DrawLeader(canvas, 325, 65, 399, 83, MakeTagLabel("Ø12a100", _sectionRebarTag));
                DrawLeader(canvas, 330, 208, 400, 222, MakeTagLabel("2Ø10", _sectionFoundationTag));
            }
            if (_callouts.IsChecked == true) DrawLeader(canvas, 166, 111, 92, 100, "Móng");
            var caption = new TextBlock { Text = horizontal ? "Mặt cắt ngang móng" : "Mặt cắt dọc móng", FontSize = 12, Foreground = Brushes.Black };
            Canvas.SetLeft(caption, 195); Canvas.SetTop(caption, 284); canvas.Children.Add(caption);
        }

        private static void AddPreviewNote(Canvas canvas, string text)
        {
            var note = new TextBlock { Text = text, FontSize = 10.5, Foreground = Brushes.Black, Background = Brushes.White };
            Canvas.SetLeft(note, 5); Canvas.SetTop(note, 2 + canvas.Children.Count * 14); canvas.Children.Add(note);
        }

        private static string ShortLabel(ComboBox combo)
        {
            string text = SelectedText(combo);
            if (text == "(Không dùng)") return "chưa chọn";
            return text.Length > 22 ? text.Substring(0, 21) + "…" : text;
        }

        private static string MakeTagLabel(string symbol, ComboBox combo)
        {
            string selected = SelectedText(combo);
            return selected == "(Không dùng)" ? symbol : symbol + "  [" + ShortLabel(combo) + "]";
        }

        private static void DrawPlanBars(Canvas canvas, double y, Brush color)
        {
            for (int i = 0; i < 6; i++)
            {
                var bar = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = color, Stroke = Brushes.Black, StrokeThickness = 0.7 };
                Canvas.SetLeft(bar, 158 + i * 31); Canvas.SetTop(bar, y); canvas.Children.Add(bar);
            }
        }

        private static void DrawBubble(Canvas canvas, double x, double y, string text)
        {
            var circle = new System.Windows.Shapes.Ellipse { Width = 27, Height = 27, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1.4 };
            Canvas.SetLeft(circle, x); Canvas.SetTop(circle, y); canvas.Children.Add(circle);
            var label = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Brushes.Black };
            Canvas.SetLeft(label, x + 5); Canvas.SetTop(label, y + 5); canvas.Children.Add(label);
        }

        private static void DrawLeader(Canvas canvas, double x1, double y1, double x2, double y2, string text)
        {
            DrawLine(canvas, x1, y1, x2, y2, Brushes.Black, false);
            var label = new TextBlock { Text = text, FontSize = 11.5, Foreground = Brushes.Black, Background = Brushes.White };
            Canvas.SetLeft(label, x2); Canvas.SetTop(label, y2 - 16); canvas.Children.Add(label);
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
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brush("#135B96") });
            panel.Children.Add(new TextBlock { Text = description, FontSize = 11, Foreground = Brush("#6A7F93"), Margin = new Thickness(0, 2, 0, 8) });
            panel.Children.Add(new Border { Height = 1, Background = Brush("#DCE8F2"), Margin = new Thickness(0, 0, 0, 7) });
            panel.Children.Add(content);
            return new Border { Background = Brushes.White, BorderBrush = Brush("#D5E3EF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(15), Margin = new Thickness(0, 0, 0, 11), Child = panel };
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

        private FootingDrawingProfile ReadProfile(string name = null) => new FootingDrawingProfile
        {
            Name = name ?? string.Empty,
            PlanTypeName = TypeName(Selected(_planType)), SectionTypeName = TypeName(Selected(_sectionType)),
            TitleBlockFamily = FamilyName(Selected(_titleBlock)), TitleBlockType = TypeName(Selected(_titleBlock)),
            PlanTemplateName = TypeName(Selected(_planTemplate)), SectionTemplateName = TypeName(Selected(_sectionTemplate)),
            RebarTagFamily = FamilyName(Selected(_rebarTag)), RebarTagType = TypeName(Selected(_rebarTag)),
            FoundationTagFamily = FamilyName(Selected(_foundationTag)), FoundationTagType = TypeName(Selected(_foundationTag)),
            DimensionTypeName = TypeName(Selected(_dimension)), OutputLayers = Convert.ToString(_layers.SelectedItem) ?? "Both",
            SheetPrefix = _sheetPrefix.Text.Trim(), SheetNamePattern = _sheetName.Text.Trim(), ViewNamePattern = _viewName.Text.Trim(),
            PlanScale = ParseInt(_planScale.Text, 20), SectionScale = ParseInt(_sectionScale.Text, 20), CropMarginMm = ParseDouble(_cropMargin.Text, 300),
            CreateCallouts = _callouts.IsChecked == true, CreateSections = _sections.IsChecked == true,
            CreateDimensions = _dimensions.IsChecked == true, CreateTags = _tags.IsChecked == true,
            PlaceOnNewSheet = _newSheet.IsChecked == true, ExistingSheetUniqueId = SheetUniqueId(_existingSheet)
        };
        private void ApplyProfile(FootingDrawingProfile p)
        {
            SelectBy(_planType, p.PlanTypeName); SelectBy(_sectionType, p.SectionTypeName);
            SelectByPair(_titleBlock, p.TitleBlockFamily, p.TitleBlockType);
            SelectBy(_planTemplate, p.PlanTemplateName); SelectBy(_sectionTemplate, p.SectionTemplateName);
            SelectByPair(_rebarTag, p.RebarTagFamily, p.RebarTagType); SelectByPair(_foundationTag, p.FoundationTagFamily, p.FoundationTagType);
            SelectBy(_dimension, p.DimensionTypeName); _layers.SelectedItem = p.OutputLayers ?? "Both";
            _topLayer.IsChecked = p.OutputLayers == "Top" || p.OutputLayers == "Both";
            _bottomLayer.IsChecked = p.OutputLayers == "Bot" || p.OutputLayers == "Both";
            _twoLayer.IsChecked = p.OutputLayers == "Both";
            _sheetPrefix.Text = p.SheetPrefix ?? "KSMD-"; _sheetName.Text = p.SheetNamePattern ?? "Móng đơn - {Mark}"; _viewName.Text = p.ViewNamePattern ?? "KStruct - MĐ {Id} - {Role}";
            _planScale.Text = p.PlanScale.ToString(); _sectionScale.Text = p.SectionScale.ToString(); _cropMargin.Text = p.CropMarginMm.ToString("0.##");
            _callouts.IsChecked = _parent != null && p.CreateCallouts; _sections.IsChecked = p.CreateSections; _dimensions.IsChecked = p.CreateDimensions; _tags.IsChecked = p.CreateTags;
            _newSheet.IsChecked = p.PlaceOnNewSheet; _existingSheet.IsEnabled = !p.PlaceOnNewSheet;
            var sheet = new FilteredElementCollector(_doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().FirstOrDefault(x => x.UniqueId == p.ExistingSheetUniqueId);
            if (sheet != null) _existingSheet.SelectedItem = _existingSheet.Items.Cast<Item>().FirstOrDefault(x => ReferenceEquals(x.Element, sheet));
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
            if (!(Selected(_planType) is ViewFamilyType plan) || !(Selected(_sectionType) is ViewFamilyType section) || (_newSheet.IsChecked == true && !(Selected(_titleBlock) is FamilySymbol)))
            { MessageBox.Show(this, "Cần chọn View Type mặt bằng, View Type mặt cắt và Title Block khi tạo sheet mới.", "Thiếu cấu hình", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (_callouts.IsChecked == true && _parent != null && plan.ViewFamily != ( _doc.GetElement(_parent.GetTypeId()) as ViewFamilyType)?.ViewFamily)
            { MessageBox.Show(this, "Callout phải dùng View Type cùng họ với mặt bằng nguồn.", "View Type không tương thích", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            int planScale = ParseInt(_planScale.Text, -1), sectionScale = ParseInt(_sectionScale.Text, -1);
            double margin = ParseDouble(_cropMargin.Text, -1);
            if (planScale <= 0 || sectionScale <= 0 || margin < 0)
            { MessageBox.Show(this, "Tỷ lệ phải là số nguyên dương; lề crop không được âm.", "Thông số chưa hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (_newSheet.IsChecked != true && !(Selected(_existingSheet) is ViewSheet))
            { MessageBox.Show(this, "Hãy chọn sheet hiện có hoặc bật tạo sheet mới.", "Thiếu sheet", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            FootingDrawingProfile profile = ReadProfile();
            Configuration = new FootingDrawingConfiguration
            {
                Profile = profile, PlanType = plan, SectionType = section, TitleBlock = Selected(_titleBlock) as FamilySymbol,
                PlanTemplate = Selected(_planTemplate) as View, SectionTemplate = Selected(_sectionTemplate) as View,
                RebarTagType = Selected(_rebarTag) as ElementType, FoundationTagType = Selected(_foundationTag) as ElementType,
                DimensionType = Selected(_dimension) as DimensionType, ExistingSheet = Selected(_existingSheet) as ViewSheet
            };
            DialogResult = true;
        }
        private static int ParseInt(string text, int fallback) => int.TryParse(text, out int value) ? value : fallback;
        private static double ParseDouble(string text, double fallback) => double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out double value) ? value : fallback;
    }
}









