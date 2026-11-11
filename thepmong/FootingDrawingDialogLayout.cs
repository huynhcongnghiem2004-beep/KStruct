using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Transform = Autodesk.Revit.DB.Transform;
using Grid = System.Windows.Controls.Grid;

namespace SDK_HD.KStruct.NativeDocumentation
{
    internal sealed partial class FootingDrawingSettingsWindow
    {
        private readonly List<Tuple<CheckBox, FamilyInstance>> _hostChoices = new List<Tuple<CheckBox, FamilyInstance>>();
        private readonly ComboBox _sourcePlan = new ComboBox();
        private readonly CheckBox _remember = new CheckBox { Content = "Nhớ lựa chọn cho lần tạo hồ sơ kế tiếp", IsChecked = true };
        private readonly CheckBox _templateOnce = new CheckBox { Content = "Áp template một lần, chỉnh view riêng", IsChecked = true };
        private readonly CheckBox _beamBreaks = new CheckBox { Content = "Đặt Breakline tại dầm cắt biên crop", IsChecked = true };
        private readonly CheckBox _reverseBreak = new CheckBox { Content = "Đảo phía che của Breakline" };
        private readonly ComboBox _breakLine = new ComboBox();
        private readonly TextBox _breakPadding = new TextBox { Text = "1" };
        private readonly CheckBox _fourSides = new CheckBox { Content = "Dim hai lớp trên cả bốn cạnh mặt bằng", IsChecked = true };
        private readonly CheckBox _reverseX = new CheckBox { Content = "Đảo chiều A–A" };
        private readonly CheckBox _reverseY = new CheckBox { Content = "Đảo chiều B–B" };
        private readonly TextBox _sectionDepth = new TextBox { Text = "1200" };
        private readonly CheckBox _autoSectionX = new CheckBox
            { Content = "Tự căn A–A qua hàng tim cọc thực (bỏ qua Offset)", IsChecked = true };
        private readonly CheckBox _autoSectionY = new CheckBox
            { Content = "Tự căn B–B qua hàng tim cọc thực (bỏ qua Offset)", IsChecked = true };
        private readonly TextBox _sectionXOffset = new TextBox { Text = "0" };
        private readonly TextBox _sectionYOffset = new TextBox { Text = "0" };
        private readonly TextBox _dimOffset = new TextBox { Text = "8" };
        private readonly TextBox _dimGap = new TextBox { Text = "6" };
        private readonly TextBox _reserveRight = new TextBox { Text = "70" };
        private readonly TextBox _reserveBottom = new TextBox { Text = "35" };
        private readonly TextBox _sheetMargin = new TextBox { Text = "10" };
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _hostSummary = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private FamilyInstance _previewHost;
        private readonly Dictionary<long, List<XYZ>> _previewVertices = new Dictionary<long, List<XYZ>>();
        private readonly Dictionary<long, List<List<XYZ>>> _previewBars = new Dictionary<long, List<List<XYZ>>>();
        internal bool RequestSelection { get; private set; }
        private IList<FamilyInstance> CheckedFootings => _hostChoices.Where(x => x.Item1.IsChecked == true).Select(x => x.Item2).ToList();
        private string SessionKey => _doc.ProjectInformation.UniqueId + "|" +
            (string.IsNullOrWhiteSpace(_doc.PathName) ? _doc.Title : _doc.PathName).ToUpperInvariant();

        private UIElement BuildMockupLayout(IList<FamilyInstance> footings)
        {
            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new TextBlock { Text = "Revit 2024  |  Thiết lập view và trình bày hồ sơ", FontSize = 16,
                FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12), Foreground = Brush("#174A75") };
            root.Children.Add(heading);
            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star), MinWidth = 220 });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.45, GridUnitType.Star), MinWidth = 380 });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star), MinWidth = 330 });
            Grid.SetRow(columns, 1); root.Children.Add(columns);

            var hosts = new DockPanel();
            var repick = new Button { Content = "Chọn lại từ mô hình", Margin = new Thickness(0, 0, 0, 8),
                ToolTip = "Đóng cửa sổ và chọn lại móng trong Revit; không thay đổi mô hình." };
            repick.Click += (s, e) => { RequestSelection = true; DialogResult = false; };
            DockPanel.SetDock(repick, Dock.Top); hosts.Children.Add(repick);
            var details = new Border { Background = Brush("#EEF5FC"), Padding = new Thickness(10), Margin = new Thickness(0, 10, 0, 0), Child = _hostSummary };
            DockPanel.SetDock(details, Dock.Bottom); hosts.Children.Add(details);
            var hostList = new StackPanel();
            var existingHosts = new HashSet<string>(new FilteredElementCollector(_doc).OfClass(typeof(View)).Cast<View>()
                .Select(v => FootingDrawingStorage.TryRead(v, out string h, out string r, out double m) ? h : null).Where(h => h != null));
            foreach (FamilyInstance host in footings)
            {
                string mark = host.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
                bool existing = existingHosts.Contains(host.UniqueId);
                var check = new CheckBox { Content = (string.IsNullOrWhiteSpace(mark) ? host.Id.Value.ToString() : mark) + "  |  " +
                    (IsPileCap(host) ? "Đài cọc" : "Móng đơn") + "\n" + (existing ? "Đã có hồ sơ" : "Chưa tạo hồ sơ"),
                    IsChecked = true, Margin = new Thickness(2, 6, 2, 6), Padding = new Thickness(4) };
                _hostChoices.Add(Tuple.Create(check, host));
                check.Checked += (s, e) => SelectPreviewHost(host);
                check.Unchecked += (s, e) => { _previewHost = CheckedFootings.FirstOrDefault(); RefreshPreviews(); };
                check.PreviewMouseDown += (s, e) => SelectPreviewHost(host);
                hostList.Children.Add(check);
            }
            hosts.Children.Add(new ScrollViewer { Content = hostList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            AddColumn(columns, CreateCard("DANH SÁCH MÓNG", "Tích chọn móng cần tạo hồ sơ", hosts), 0);

            _topLayer.Content = "Thép trên"; _bottomLayer.Content = "Thép dưới";
            _callouts.Content = "Callout từ view nguồn (tắt: plan độc lập)";
            _callouts.IsEnabled = true;
            foreach(CheckBox detail in new[]{_detailA,_detailB})
                detail.Checked += (s,e) => { _callouts.IsChecked = true; if (_topLayer.IsChecked != true && _bottomLayer.IsChecked != true) _bottomLayer.IsChecked = true; };
            _dimensions.Content = "Tạo kích thước liên kết mô hình";
            var tabs = new TabControl();
            var view = new StackPanel();
            Field(view, "Loại móng", _footingKind);
            view.Children.Add(Note("Auto: nhận diện theo Family/Type; có thể chọn Móng đơn hoặc Móng cọc để ghi đè khi tên Family không rõ. Móng đơn chỉ có BOT; móng cọc có hai View BOT và TOP tách biệt."));
            var layers = new WrapPanel(); layers.Children.Add(_bottomLayer); layers.Children.Add(_topLayer); view.Children.Add(layers);
            _detailA.Margin = new Thickness(0, 0, 14, 0);
            var detailChoices = new WrapPanel(); detailChoices.Children.Add(_detailA); detailChoices.Children.Add(_detailB);
            view.Children.Add(detailChoices); view.Children.Add(Note("Hiển thị ký hiệu gốc của Detail trên callout móng; không tạo Reference Other View.")); view.Children.Add(_callouts);
            Populate(_sourcePlan, new FilteredElementCollector(_doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .Where(v => !v.IsTemplate && (v.ViewType == ViewType.EngineeringPlan || v.ViewType == ViewType.FloorPlan))
                .Select(v => new Item { Text = v.Name, Element = v }), null, true);
            if (_parent != null) _sourcePlan.SelectedItem = _sourcePlan.Items.Cast<Item>().FirstOrDefault(i => i.Element?.Id == _parent.Id);
            Field(view, "View nguồn", _sourcePlan); Field(view, "View Type mặt bằng", _planType);
            Field(view, "Template mặt bằng", _planTemplate); view.Children.Add(_templateOnce);
            view.Children.Add(Note("Sao chép định dạng vào view rồi chỉnh view range/hiển thị riêng; template gốc không bị sửa."));
            Field(view, "Tỷ lệ mặt bằng 1:", _planScale);
            var crop = new StackPanel();
            Field(crop, "Mép móng → crop (mm)", _cropMargin);
            Field(crop, "Mở rộng đáy thấy cọc (mm)", _cropBottomMargin);
            crop.Children.Add(Note("Kích thước thực tế. Mở rộng đáy áp dụng cho mặt cắt Detail A–A/B–B để thấy cọc ngàm và thân cọc dưới đáy đài."));
            view.Children.Add(new Border { Background = Brush("#E7F2FF"), Padding = new Thickness(8), Margin = new Thickness(0, 8, 0, 8), Child = crop });
            Field(view, "Detail Type (A–A / B–B)", _detailType); Field(view, "Template mặt cắt", _sectionTemplate);
            Field(view, "Tỷ lệ mặt cắt 1:", _sectionScale);
            Field(view, "Chiều sâu nhìn (mm)", _sectionDepth);
            view.Children.Add(_autoSectionX);
            Field(view, "A–A: lệch theo Y (mm)", _sectionXOffset); view.Children.Add(_reverseX);
            view.Children.Add(_autoSectionY);
            Field(view, "B–B: lệch theo X (mm)", _sectionYOffset); view.Children.Add(_reverseY);
            view.Children.Add(Note("A–A chứa trục X/Z; B–B chứa Y/Z của móng. Mốc offset là tâm hình học móng. Preview là sơ đồ, cần kiểm tra đồ họa trong Revit."));
            AddTab(tabs, "View", view);

            var dims = new StackPanel(); dims.Children.Add(_dimensions); dims.Children.Add(_fourSides);
            Field(dims, "Dimension Type", _dimension); Field(dims, "Lớp dim 1 (mm giấy)", _dimOffset); Field(dims, "Cách hai lớp (mm giấy)", _dimGap);
            dims.Children.Add(Note("Lớp trong: mép–tim cột/cọc–mép. Lớp ngoài: kích thước tổng thể. Thiếu reference chi tiết sẽ báo cạnh thiếu; không dùng kích thước giả."));
            dims.Children.Add(_usePlanDetail);
            Field(dims, "Detail Item thép TOP", _planTopDetail);
            Field(dims, "Detail Item thép BOT", _planBotDetail);
            dims.Children.Add(_pileSection2D);
            Field(dims, "Detail Item Cọc 2D (mặt cắt)", _pileSectionFamily);
            Field(dims, "Tên tham số đường kính cọc mô hình (để trống: tự tìm duy nhất)", _pileDiameterParameter);
            Field(dims, "Từ khóa nhận diện Family cọc (phân cách bằng dấu phẩy)", _pileFamilyFilter);
            var pileAudit = new WrapPanel();
            AddButton(pileAudit, "Kiểm tra tham số Family Cọc 2D", AuditPileFamily);
            dims.Children.Add(pileAudit);
            dims.Children.Add(_pileOriginConfirmed);
            dims.Children.Add(Note("Tâm Family 2D trùng tuyệt đối với tim cọc trên mặt cắt (không có offset thủ công). D Coc nhận đường kính thật từ mô hình; KStruct tạo Type riêng theo đường kính, không đổi Type gốc. Các Type Parameter khác giữ nguyên."));
            var auditActions = new WrapPanel();
            AddButton(auditActions, "Kiểm tra công thức Family trong Revit", AuditPlanDetailFamily);
            dims.Children.Add(auditActions);
            dims.Children.Add(Note("Ánh xạ Family theo mm: B = kích thước trục X móng, H = trục Y, C = lớp bảo vệ. KEEP giữ nguyên. Chỉ xác nhận sau khi kiểm tra Family Types."));
            dims.Children.Add(_alignRebarCover);
            dims.Children.Add(_readHostCover);
            Field(dims, "Lớp bảo vệ dự phòng C (mm)", _planCoverMm);
            dims.Children.Add(Note("Khi bật căn theo lớp bảo vệ, L/W tự động tính theo B/H và C + Ø/2 (tim thanh). Công thức L/W lưu cũ được ghi đè để ngăn nét thép nằm trên biên bê tông. Các tham số phụ X1/X2/Y1/Y2 chỉ sửa theo ánh xạ Family đã xác minh."));
            for (int i = 0; i < FootingPlanDetailComponentSizing.Names.Length; i++)
                Field(dims, "Parameter " + FootingPlanDetailComponentSizing.Names[i], _planExpressions[i]);
            dims.Children.Add(_planSizeConfirmed);
            dims.Children.Add(new TextBlock { Text = "Chế độ 2D không tạo Multi-Rebar Tag ở mặt bằng; giữ Rebar thật trong mô hình.", TextWrapping = TextWrapping.Wrap });
            dims.Children.Add(_tags);
            dims.Children.Add(_noteBot); dims.Children.Add(_noteTop);
            dims.Children.Add(Note("Hai lựa chọn ghi chú độc lập theo lớp thép. Móng đơn chỉ xuất ghi chú BOT; móng cọc có thể bật/tắt riêng TOP hoặc BOT. Không gom hai Rebar Set trùng thông số."));
            Field(dims, "Multi-Rebar Annotation Type (thép móng)", _rebarTag); Field(dims, "Tag móng / cọc", _foundationTag);
            Populate(_breakLine, new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_DetailComponents)
                .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                .Where(s => s.Family.FamilyPlacementType == FamilyPlacementType.CurveBasedDetail || s.Family.FamilyPlacementType == FamilyPlacementType.ViewBased)
                .Select(s => new Item {Text = s.FamilyName + " : " + s.Name, Element = s}), "break", true);
            dims.Children.Add(_beamBreaks); Field(dims, "Family / Type Breakline", _breakLine);
            Field(dims, "Dư hai đầu (mm giấy)", _breakPadding); dims.Children.Add(_reverseBreak);
            dims.Children.Add(Note("Ưu tiên Detail Item theo đường. Phía che tùy family; kiểm tra và đảo hướng nếu cần. Chưa chọn family thì báo và bỏ qua Breakline."));
            dims.Children.Add(Note("MultiTag cần loại Multi-Rebar Annotation có Rebar Tag và Dimension Type trong project; dùng trên mặt bằng và Detail."));
            AddTab(tabs, "Dim / Tag", dims);

            var sheet = new StackPanel(); sheet.Children.Add(_newSheet);
            Field(sheet, "Sheet có sẵn", _existingSheet); Field(sheet, "Title Block", _titleBlock);
            Field(sheet, "Tiền tố số sheet", _sheetPrefix); Field(sheet, "Tên sheet", _sheetName); Field(sheet, "Tên view", _viewName);
            Field(sheet, "Lề sheet (mm giấy)", _sheetMargin); Field(sheet, "Chừa bên phải (mm)", _reserveRight); Field(sheet, "Chừa phía dưới (mm)", _reserveBottom);
            sheet.Children.Add(Note("Giữ tỷ lệ đã chọn. Vùng phải/dưới dành cho khung tên và ghi chú; chỉnh theo title block thực tế. Không vừa hoặc chồng nội dung cũ: bỏ qua móng và báo lỗi."));
            AddTab(tabs, "Sheet", sheet);

            var preset = new StackPanel(); Field(preset, "Preset", _profiles); Field(preset, "Tên cấu hình", _profileName);
            var presetButtons = new WrapPanel();
            AddButton(presetButtons, "Nạp cấu hình", () => { if (_profiles.SelectedItem is FootingDrawingProfile p) { ApplyProfile(p); RefreshPreviews(); } });
            AddButton(presetButtons, "Lưu cấu hình", SaveProfile); AddButton(presetButtons, "Xóa preset", DeleteProfile);
            AddButton(presetButtons, "Nhập XML", ImportProfiles); AddButton(presetButtons, "Xuất XML", ExportProfiles);
            preset.Children.Add(presetButtons); preset.Children.Add(_remember);
            preset.Children.Add(Note("Tự nhớ cấu hình hợp lệ khi bấm Tạo hồ sơ, riêng cho project này. Không nhớ danh sách móng. Hủy không lưu thay đổi. Preset có tên vẫn được giữ riêng."));
            AddTab(tabs, "Cấu hình", preset);
            // Relax legacy minimum widths so controls fit the middle panel at laptop resolutions.
            foreach (ComboBox box in new[] { _profiles, _planType, _detailType, _titleBlock, _planTemplate, _sectionTemplate,
                _rebarTag, _planTopDetail, _planBotDetail, _pileSectionFamily, _foundationTag, _dimension, _existingSheet }) box.MinWidth = 0;
            foreach (TextBox box in new[] { _sheetPrefix, _sheetName, _viewName, _profileName }) box.MinWidth = 0;
            AddColumn(columns, CreateCard("THIẾT LẬP HỒ SƠ", "View • Dim / Tag • Sheet • Cấu hình", tabs), 1);

            var previews = new Grid();
            previews.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            previews.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _planCanvas = new Canvas { Width = 500, Height = 330, Background = Brushes.White };
            _sectionCanvas = new Canvas { Width = 500, Height = 330, Background = Brushes.White };
            var first = PreviewBox("Mặt bằng và tuyến cắt", _planCanvas); previews.Children.Add(first);
            var second = PreviewBox("Bố cục sheet — sơ đồ, chưa đo extents Revit", _sectionCanvas); Grid.SetRow(second, 1); previews.Children.Add(second);
            AddColumn(columns, CreateCard("XEM TRƯỚC", "Hiển thị theo móng được chọn và cấu hình", previews), 2);
            var status = new Border { Background = Brush("#FFF3D4"), Padding = new Thickness(8), Margin = new Thickness(0, 6, 0, 8), Child = _status };
            Grid.SetRow(status, 2); root.Children.Add(status);
            var footer = new DockPanel();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            AddButton(buttons, "Xem trước", RefreshPreviews);
            var create = new Button { Content = "Tạo hồ sơ", Background = Brush("#006CD8"), Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold, Padding = new Thickness(24, 7, 24, 7) };
            create.Click += (s, e) => Accept(); buttons.Children.Add(create);
            buttons.Children.Add(new Button { Content = "Hủy", IsCancel = true });
            DockPanel.SetDock(buttons, Dock.Right); footer.Children.Add(buttons);
            footer.Children.Add(Note("Cấu hình được lưu khi tạo • Cancel không đổi mô hình")); Grid.SetRow(footer, 3); root.Children.Add(footer);
            foreach (TextBox box in new[] { _breakPadding, _sectionDepth, _sectionXOffset, _sectionYOffset, _dimOffset, _dimGap, _reserveRight, _reserveBottom, _sheetMargin })
                box.TextChanged += (s, e) => RefreshPreviews();
            foreach (CheckBox box in new[] { _fourSides, _reverseX, _reverseY, _newSheet, _autoSectionX, _autoSectionY })
            { box.Checked += (s, e) => RefreshPreviews(); box.Unchecked += (s, e) => RefreshPreviews(); }
            _previewHost = footings.FirstOrDefault();
            return root;
        }

        private static void Field(StackPanel panel, string label, System.Windows.Controls.Control control)
        {
            control.MinWidth = 0;
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 7, 0, 3), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(control);
        }
        private static TextBlock Note(string text) => new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("#53677D"), Margin = new Thickness(0, 6, 0, 6), FontSize = 12 };
        private static void AddColumn(Grid grid, UIElement element, int column)
        { Grid.SetColumn(element, column); if (element is FrameworkElement f) f.Margin = new Thickness(0, 0, column == 2 ? 0 : 10, 0); grid.Children.Add(element); }
        private static void AddTab(TabControl tabs, string name, UIElement content) => tabs.Items.Add(new TabItem { Header = name,
            Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10) } });
        private static UIElement PreviewBox(string title, Canvas canvas)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 6) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition());
            grid.Children.Add(Note(title));
            var box = new Viewbox { Child = canvas, Stretch = Stretch.Uniform }; Grid.SetRow(box, 1); grid.Children.Add(box); return grid;
        }
        private void AddButton(System.Windows.Controls.Panel panel, string title, Action action)
        {
            var button = new Button { Content = title, Margin = new Thickness(0, 4, 6, 4) };
            button.Click += (s, e) => { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "KStruct"); } };
            panel.Children.Add(button);
        }
        private void SelectPreviewHost(FamilyInstance host) { _previewHost = host; RefreshPreviews(); }

        private void RestoreSession()
        {
            try
            {
                FootingDrawingSession saved = FootingDrawingSessionStore.Load(SessionKey);
                if (saved == null) return;
                _remember.IsChecked = saved.Remember;
                if (saved.Remember && saved.Profile != null) ApplyProfile(saved.Profile);
            }
            catch (Exception ex) { MessageBox.Show(this, "Không đọc được lựa chọn lần trước; dùng cấu hình mặc định.\n" + ex.Message, "KStruct"); }
        }
        private void SaveSession()
        {
            try { FootingDrawingSessionStore.Save(SessionKey, new FootingDrawingSession { Remember = _remember.IsChecked == true,
                Profile = _remember.IsChecked == true ? ReadProfile() : null }); }
            catch (Exception ex) { MessageBox.Show(this, "Không lưu được lựa chọn; vẫn tiếp tục tạo hồ sơ.\n" + ex.Message, "KStruct"); }
        }
        private bool ValidateExtraFields()
        {
            foreach (TextBox box in new[] { _breakPadding, _sectionDepth, _sectionXOffset, _sectionYOffset, _dimOffset, _dimGap, _reserveRight, _reserveBottom, _sheetMargin })
            {
                if ((!AnyDetailSelected && box == _sectionDepth) ||
                    (box == _sectionXOffset && (_detailA.IsChecked != true || _autoSectionX.IsChecked == true)) ||
                    (box == _sectionYOffset && (_detailB.IsChecked != true || _autoSectionY.IsChecked == true))) continue;
                double value = ParseDouble(box.Text, double.NaN);
                if (double.IsNaN(value) || double.IsInfinity(value) ||
                    (box != _sectionXOffset && box != _sectionYOffset && value < 0))
                { MessageBox.Show(this, "Offset phải hữu hạn; khoảng cách dim và lề sheet không được âm.", "Thông số chưa hợp lệ"); box.Focus(); return false; }
            }
            if (AnyDetailSelected && ParseDouble(_sectionDepth.Text, 0) <= 0)
            { MessageBox.Show(this, "Chiều sâu nhìn phải lớn hơn 0.", "Chiều sâu mặt cắt"); return false; }
            if (_dimensions.IsChecked == true && _fourSides.IsChecked == true &&
                (ParseDouble(_dimOffset.Text, 0) <= 0 || ParseDouble(_dimGap.Text, 0) <= 0))
            { MessageBox.Show(this, "Hai lớp dim cần khoảng đặt và khoảng cách lớn hơn 0.", "Khoảng cách dim"); return false; }
            if (_dimensions.IsChecked == true && !(Selected(_dimension) is DimensionType))
            { MessageBox.Show(this, "Hãy chọn Dimension Type hoặc tắt tạo kích thước.", "Thiếu loại dim"); return false; }
            return true;
        }

        private void DrawModelPlanPreview(Canvas canvas)
        {
            canvas.Children.Clear();
            if (_previewHost == null) { AddPreviewNote(canvas, "Hãy chọn móng để xem trước."); return; }
            BoundingBoxXYZ box = _previewHost.get_BoundingBox(null);
            if (box == null) { AddPreviewNote(canvas, "Không đọc được hình học móng."); return; }
            Transform local = _previewHost.GetTransform();
            if (!_previewVertices.TryGetValue(_previewHost.Id.Value, out List<XYZ> vertices))
            { vertices = FootingDrawingGeometry.WorldVertices(_previewHost); _previewVertices[_previewHost.Id.Value] = vertices; }
            List<XYZ> points = vertices.Select(local.Inverse.OfPoint).ToList();
            double w = points.Max(p => p.X) - points.Min(p => p.X), h = points.Max(p => p.Y) - points.Min(p => p.Y);
            double factor = 260 / Math.Max(w, h); double pw = w * factor, ph = h * factor;
            double left = 250 - pw / 2, top = 165 - ph / 2;
            string mark = _previewHost.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? _previewHost.Id.Value.ToString();
            _hostSummary.Text = mark + "\n" + _previewHost.Name + "\n" + Mm(w) + " × " + Mm(h) + " mm\nCốt thép: lấy từ mô hình\nĐang chọn: " + CheckedFootings.Count + " móng";
            AddPreviewNote(canvas, mark + " — sơ đồ hướng local móng, không phải render Revit");
            var rect = new System.Windows.Shapes.Rectangle { Width = pw, Height = ph, Stroke = Brushes.Black, Fill = Brush("#F8FAFC") };
            Canvas.SetLeft(rect, left); Canvas.SetTop(rect, top); canvas.Children.Add(rect);
            if (!_previewBars.TryGetValue(_previewHost.Id.Value, out List<List<XYZ>> barLines))
            {
                barLines = new List<List<XYZ>>();
                foreach (Rebar rebar in new FilteredElementCollector(_doc).OfClass(typeof(Rebar)).Cast<Rebar>().Where(r => r.GetHostId() == _previewHost.Id))
                {
                    for (int index = 0; index < rebar.NumberOfBarPositions && barLines.Count < 500; index++)
                    {
                        if (!rebar.DoesBarExistAtPosition(index)) continue;
                        try
                        {
                            foreach (Curve curve in rebar.GetTransformedCenterlineCurves(false, false, false, MultiplanarOption.IncludeAllMultiplanarCurves, index))
                                barLines.Add(curve.Tessellate().ToList());
                        }
                        catch { /* Unavailable geometry is omitted from the schematic, not invented. */ }
                    }
                }
                _previewBars[_previewHost.Id.Value] = barLines;
            }
            double cx = (points.Max(p => p.X) + points.Min(p => p.X)) / 2, cy = (points.Max(p => p.Y) + points.Min(p => p.Y)) / 2;
            double midZ = (box.Min.Z + box.Max.Z) / 2;
            foreach (List<XYZ> line in barLines)
            {
                bool upper = line.Average(p => p.Z) >= midZ;
                if (upper && _topLayer.IsChecked != true || !upper && _bottomLayer.IsChecked != true) continue;
                List<XYZ> projected = line.Select(local.Inverse.OfPoint).ToList();
                for (int i = 1; i < projected.Count; i++)
                    DrawLine(canvas, 250 + (projected[i-1].X-cx)*factor, 165 - (projected[i-1].Y-cy)*factor,
                        250 + (projected[i].X-cx)*factor, 165 - (projected[i].Y-cy)*factor, Brush("#334455"), false);
            }
            double crop = UnitUtils.ConvertToInternalUnits(Math.Max(0, ParseDouble(_cropMargin.Text, 6)), UnitTypeId.Millimeters) * factor;
            var cropRect = new System.Windows.Shapes.Rectangle { Width = pw + 2 * crop, Height = ph + 2 * crop,
                Stroke = Brush("#006CD8"), StrokeDashArray = new DoubleCollection { 4, 2 } };
            Canvas.SetLeft(cropRect, left - crop); Canvas.SetTop(cropRect, top - crop); canvas.Children.Add(cropRect);
            double dx = _detailA.IsChecked == true ? UnitUtils.ConvertToInternalUnits(ParseDouble(_sectionXOffset.Text, 0), UnitTypeId.Millimeters) * factor : 0;
            double dy = _detailB.IsChecked == true ? UnitUtils.ConvertToInternalUnits(ParseDouble(_sectionYOffset.Text, 0), UnitTypeId.Millimeters) * factor : 0;
            if (_detailA.IsChecked == true)
            {
                DrawLine(canvas, left - 12, 165 - dx, left + pw + 12, 165 - dx, Brushes.Red, true);
                Label(canvas, left - 22, 145 - dx, _reverseX.IsChecked == true ? "A ↓" : "A ↑", Brushes.Red);
            }
            if (_detailB.IsChecked == true)
            {
                DrawLine(canvas, 250 + dy, top - 12, 250 + dy, top + ph + 12, Brushes.Red, true);
                Label(canvas, 255 + dy, top - 25, _reverseY.IsChecked == true ? "B ←" : "B →", Brushes.Red);
            }
            if (_dimensions.IsChecked == true)
            {
                foreach (int side in new[] { -1, 1 })
                {
                    double yy = side < 0 ? top - 25 : top + ph + 25;
                    double xx = side < 0 ? left - 30 : left + pw + 30;
                    DrawDimension(canvas, left, yy, left + pw, yy, Mm(w), null);
                    DrawDimension(canvas, xx, top, xx, top + ph, Mm(h), null);
                    if (_fourSides.IsChecked == true)
                    {
                        DrawLine(canvas, left, yy - side * 12, left + pw, yy - side * 12, Brushes.Gray, false);
                        DrawLine(canvas, xx - side * 12, top, xx - side * 12, top + ph, Brushes.Gray, false);
                    }
                }
            }
            Label(canvas, 20, 305, "Crop +" + _cropMargin.Text + " mm thực tế • Dim chi tiết cần reference thật", Brush("#006CD8"));
            _status.Text = "Đã chọn " + CheckedFootings.Count + " móng • Crop " + _cropMargin.Text + " mm mô hình • " +
                (_fourSides.IsChecked == true ? "Dim hai lớp × bốn cạnh" : "Dim tổng thể") + " • Preview sơ đồ; kiểm tra kết quả trong Revit.";
        }
        private static string Mm(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters).ToString("0.##");
        private static void Label(Canvas canvas, double x, double y, string text, Brush brush = null)
        { var label = new TextBlock { Text = text, FontSize = 12, Foreground = brush ?? Brushes.Black }; Canvas.SetLeft(label, x); Canvas.SetTop(label, y); canvas.Children.Add(label); }
        private void DrawSheetPreview(Canvas canvas)
        {
            canvas.Children.Clear();
            DrawLine(canvas, 12, 18, 485, 18, Brushes.Black, false); DrawLine(canvas, 485, 18, 485, 295, Brushes.Black, false);
            DrawLine(canvas, 485, 295, 12, 295, Brushes.Black, false); DrawLine(canvas, 12, 295, 12, 18, Brushes.Black, false);
            var titles = new List<string>();
            if (_bottomLayer.IsChecked == true) titles.Add("THÉP DƯỚI  1:" + _planScale.Text);
            if (_topLayer.IsChecked == true) titles.Add("THÉP TRÊN  1:" + _planScale.Text);
            if (_detailA.IsChecked == true) titles.Add("A–A  1:" + _sectionScale.Text);
            if (_detailB.IsChecked == true) titles.Add("B–B  1:" + _sectionScale.Text);
            for (int i = 0; i < titles.Count; i++)
            {
                double x = 30 + i % 2 * 165, y = 35 + i / 2 * 120;
                var shape = new System.Windows.Shapes.Rectangle { Width = 135, Height = 75, Stroke = Brush("#6C8092"), Fill = Brush("#F5F8FC") };
                Canvas.SetLeft(shape, x); Canvas.SetTop(shape, y); canvas.Children.Add(shape); Label(canvas, x + 3, y + 80, titles[i]);
            }
            DrawLine(canvas, 370, 240, 485, 240, Brushes.Black, false); DrawLine(canvas, 370, 240, 370, 295, Brushes.Black, false);
            Label(canvas, 380, 250, "KSTRUCT"); Label(canvas, 380, 270, "KHUNG TÊN");
            Label(canvas, 20, 310, "Giữ tỷ lệ • Vùng chừa khung tên theo tab Sheet", Brush("#006CD8"));
        }
    }
}
