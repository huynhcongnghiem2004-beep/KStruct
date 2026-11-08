using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Grid = System.Windows.Controls.Grid;
using Color = System.Windows.Media.Color;
using Rectangle = System.Windows.Shapes.Rectangle;
using Ellipse = System.Windows.Shapes.Ellipse;
using Line = System.Windows.Shapes.Line;
using WpfControl = System.Windows.Controls.Control;
using Point = System.Windows.Point;

namespace SDK_HD.KStruct.NativeRebar
{
    internal static class BeamRebarEditorWindow
    {
        private sealed class BarGeometry
        {
            public RebarBarType Type;
            public double StartMm;
            public double EndMm;
            public double Position = 1;
            public double LeftHookMm;
            public double RightHookMm;
            public double HookDownMm;
            public RebarHookType StartHook;
            public RebarHookType EndHook;
            public int StartPointIndex = -1;
            public int EndPointIndex = -1;
            public double LeftRatio = 0.25;
            public double RightRatio = 0.25;
        }

        private sealed class BarRow
        {
            public string Key;
            public string Caption;
            public int Count;
            public RebarBarType Type;
            public bool Optional;
            public double StartMm;
            public double EndMm = 4000;
            public double Position = 1;
            public double LeftHookMm;
            public double RightHookMm;
            public double HookDownMm;
            public int StartPointIndex = -1;
            public int EndPointIndex = -1;
            public double LeftRatio = 0.25;
            public double RightRatio = 0.25;
            public List<BarGeometry> Bars = new List<BarGeometry>();
            public int SelectedBarIndex;
            public bool HasMultipleIntervals;
            public bool QuickTypeChanged;
        }

        private static readonly Brush Blue = new SolidColorBrush(Color.FromRgb(39, 99, 245));
        private static readonly Brush Teal = new SolidColorBrush(Color.FromRgb(0, 137, 126));
        private static readonly Brush MutedBlue = new SolidColorBrush(Color.FromRgb(145, 177, 240));
        private static readonly Brush Page = new SolidColorBrush(Color.FromRgb(244, 246, 249));
        private static readonly Brush BorderBrush = new SolidColorBrush(Color.FromRgb(215, 223, 235));

        internal static ReinforceStraightBeamsCommand.Options Show(IntPtr owner, IList<RebarBarType> types,
            IList<RebarHookType> hookTypes, ReinforceStraightBeamsCommand.Options defaults,
            bool settingsOnly, string titleOverride)
        {
            var window = new Window
            {
                Title = titleOverride ?? "Thép dầm",
                Width = 1530,
                Height = 890,
                MinWidth = 1200,
                MinHeight = 720,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.SingleBorderWindow,
                Background = Page,
                ShowInTaskbar = false
            };

            List<BarRow> rows = BuildRows(types, defaults);
            var nav = new ListBox
            {
                Width = 150,
                Margin = new Thickness(8, 0, 8, 0),
                Background = Brushes.White,
                BorderBrush = BorderBrush,
                ItemsSource = new[] { "Hình học", "Thép chủ (trên)", "Thép chủ (dưới)", "Gia cường (trên)", "Gia cường (dưới)", "Thép đai", "Thép chống phình", "Cắt thép · TCVN 5574:2018" },
                SelectedIndex = 1
            };
            var navItemStyle = new Style(typeof(ListBoxItem));
            navItemStyle.Setters.Add(new Setter(WpfControl.PaddingProperty, new Thickness(12, 11, 8, 11)));
            navItemStyle.Setters.Add(new Setter(WpfControl.MarginProperty, new Thickness(3, 2, 3, 2)));
            navItemStyle.Setters.Add(new Setter(WpfControl.BorderThicknessProperty, new Thickness(1)));
            navItemStyle.Setters.Add(new Setter(WpfControl.BorderBrushProperty, Brushes.Transparent));
            var navSelected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            navSelected.Setters.Add(new Setter(WpfControl.BackgroundProperty, new SolidColorBrush(Color.FromRgb(233, 240, 255))));
            navSelected.Setters.Add(new Setter(WpfControl.ForegroundProperty, Blue));
            navSelected.Setters.Add(new Setter(WpfControl.BorderBrushProperty, Blue));
            navItemStyle.Triggers.Add(navSelected);
            nav.ItemContainerStyle = navItemStyle;
            var pageTitle = new TextBlock { Text = "Thép chủ (trên)", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(37, 61, 102)), Margin = new Thickness(8, 0, 0, 8) };
            var editorHost = new ContentControl();
            // Keep one visual tree per tab. Rebuilding the selected page on every click
            // used to re-parent shared WPF controls (notably the hook ComboBoxes), which
            // throws "already the logical child of another element" during fast tab changes.
            var editorPages = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);
            var editorPageRows = new Dictionary<string, BarRow>(StringComparer.Ordinal);
            var sectionCanvas = new Canvas { Height = 270, Background = Brushes.White };
            var beamCanvas = new Canvas { MinHeight = 220, Background = Brushes.White, ClipToBounds = false, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            var beamViewport = new Grid { ClipToBounds = true, Background = Brushes.White, Cursor = Cursors.Hand };
            beamViewport.Children.Add(beamCanvas);
            double beamLengthMm = defaults?.PreviewBeamLengthMm > 0 ? defaults.PreviewBeamLengthMm : 4000.0;
            double beamWidthMm = defaults?.PreviewBeamWidthMm > 0 ? defaults.PreviewBeamWidthMm : 200.0;
            double beamHeightMm = defaults?.PreviewBeamHeightMm > 0 ? defaults.PreviewBeamHeightMm : 400.0;
            NormalizeRanges(rows, beamLengthMm);
            double beamZoom = 1.0, beamOffsetX = 0.0, beamOffsetY = 0.0;
            bool panning = false, tiesVisible = true;
            Point panStart = new Point();
            double panStartX = 0, panStartY = 0;
            void ApplyBeamTransform()
            {
                beamCanvas.RenderTransform = new MatrixTransform(new Matrix(beamZoom, 0, 0, beamZoom, beamOffsetX, beamOffsetY));
            }
            void ZoomAt(Point anchor, double factor)
            {
                double next = Math.Max(0.45, Math.Min(8.0, beamZoom * factor));
                double ratio = next / beamZoom;
                beamOffsetX = anchor.X - (anchor.X - beamOffsetX) * ratio;
                beamOffsetY = anchor.Y - (anchor.Y - beamOffsetY) * ratio;
                beamZoom = next;
                ApplyBeamTransform();
            }
            beamViewport.PreviewMouseWheel += (s, e) =>
            {
                ZoomAt(e.GetPosition(beamViewport), e.Delta > 0 ? 1.15 : 1.0 / 1.15);
                e.Handled = true;
            };
            beamViewport.MouseLeftButtonDown += (s, e) =>
            {
                panning = true; panStart = e.GetPosition(beamViewport); panStartX = beamOffsetX; panStartY = beamOffsetY;
                beamViewport.Cursor = Cursors.SizeAll; beamViewport.CaptureMouse(); e.Handled = true;
            };
            beamViewport.MouseMove += (s, e) =>
            {
                if (!panning || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
                Point current = e.GetPosition(beamViewport);
                beamOffsetX = panStartX + current.X - panStart.X; beamOffsetY = panStartY + current.Y - panStart.Y;
                ApplyBeamTransform();
            };
            beamViewport.MouseLeftButtonUp += (s, e) =>
            {
                panning = false; beamViewport.Cursor = Cursors.Hand; beamViewport.ReleaseMouseCapture();
            };
            var status = new TextBlock { Text = beamWidthMm.ToString("0") + "×" + beamHeightMm.ToString("0") + " mm   ·   L=" + beamLengthMm.ToString("0") + " mm", Margin = new Thickness(10, 10, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(67, 91, 130)) };
            var spanCombo = new ComboBox { Width = 90, ItemsSource = new[] { "0" }, SelectedIndex = 0, Margin = new Thickness(8, 0, 8, 0) };
            var showImage = new CheckBox { Content = "Toggle hình ảnh", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
            var zoneRadio = new RadioButton { Content = "Chia vùng", GroupName = "TieMode", IsChecked = defaults == null || defaults.UseZonedTies, Margin = new Thickness(6, 0, 0, 0) };
            var uniformRadio = new RadioButton { Content = "Phân bố đều", GroupName = "TieMode", IsChecked = defaults != null && !defaults.UseZonedTies, Margin = new Thickness(6, 0, 0, 0) };
            var coverBox = Input(defaults?.CoverMm ?? 30, 74);
            var layerGapBox = Input(defaults?.LongitudinalLayerSpacingMm > 0 ? defaults.LongitudinalLayerSpacingMm : 30, 74);
            var tieSpacingBox = Input(defaults?.TieSpacingMm ?? 150, 74);
            var stirrupOffsetBox = Input(defaults?.StirrupSupportOffsetMm > 0 ? defaults.StirrupSupportOffsetMm : 50, 74);
            var sideMinimumHeightBox = Input(defaults?.SideBarMinimumHeightMm > 0 ? defaults.SideBarMinimumHeightMm : 700, 74);
            var sideSpacingBox = Input(defaults?.SideBarSpacingMm > 0 ? defaults.SideBarSpacingMm : 400, 74);
            var sideAnchorageBox = Input(defaults?.SideBarAnchorageMm > 0 ? defaults.SideBarAnchorageMm : 100, 74);
            var tcvnCutoffCheck = new CheckBox { Content = "Tự kéo dài thanh qua điểm cắt theo chiều dài neo TCVN", IsChecked = defaults == null || defaults.TcvnCutoffEnabled, Margin = new Thickness(4) };
            var tcvnRbtBox = Input(defaults?.TcvnConcreteRbtMpa ?? 0, 90);
            var tcvnRsBox = Input(defaults?.TcvnSteelRsMpa ?? 0, 90);
            var tcvnEta1Box = Input(defaults?.TcvnBondEta1 > 0 ? defaults.TcvnBondEta1 : 2.5, 90);
            var tcvnAreaRatioBox = Input(defaults?.TcvnAreaRatio > 0 ? defaults.TcvnAreaRatio : 1.0, 90);
            var tcvnAlphaBox = Input(defaults?.TcvnAlpha > 0 ? defaults.TcvnAlpha : 1.0, 90);
            var zoneLengthBox = Input(defaults?.TieEndZoneLengthMm > 0 ? defaults.TieEndZoneLengthMm : 1000, 74);
            var endSpacingBox = Input(defaults?.TieEndSpacingMm > 0 ? defaults.TieEndSpacingMm : 100, 74);
            var middleSpacingBox = Input(defaults?.TieMiddleSpacingMm > 0 ? defaults.TieMiddleSpacingMm : 200, 74);
            zoneLengthBox.IsEnabled = endSpacingBox.IsEnabled = middleSpacingBox.IsEnabled = defaults == null || defaults.UseZonedTies;
            var hookChoices = new[] { new HookChoice(null) }.Concat((hookTypes ?? new List<RebarHookType>()).Select(h => new HookChoice(h))).ToList();
            var topStartHook = HookCombo(hookChoices, defaults?.TopStartHook);
            var topEndHook = HookCombo(hookChoices, defaults?.TopEndHook);
            var bottomStartHook = HookCombo(hookChoices, defaults?.BottomStartHook);
            var bottomEndHook = HookCombo(hookChoices, defaults?.BottomEndHook);

            int sectionIndex = 0;
            Action refreshDrawing = () => { };
            var sectionTitle = new TextBlock { Text = beamWidthMm.ToString("0") + " × " + beamHeightMm.ToString("0") + " mm", HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 4, 0, 4) };
            var sectionTabs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 4) };
            var sectionTabButtons = new List<Button>();
            for (int i = 0; i < 3; i++)
            {
                int tabIndex = i;
                var tab = Button((i + 1) + "-" + (i + 1), 48);
                tab.Click += (s, e) => { sectionIndex = tabIndex; UpdateSectionTabs(sectionTabButtons, sectionIndex); sectionTitle.Text = beamWidthMm.ToString("0") + " × " + beamHeightMm.ToString("0") + " mm   ·   Mặt cắt " + (sectionIndex + 1) + "-" + (sectionIndex + 1); refreshDrawing(); };
                sectionTabButtons.Add(tab); sectionTabs.Children.Add(tab);
            }
            UpdateSectionTabs(sectionTabButtons, sectionIndex);
            var sectionDock = new DockPanel { LastChildFill = true };
            var sectionHeader = new StackPanel { Children =
                {
                    new TextBlock { Text = "Mặt cắt", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,7) },
                    sectionTabs, sectionTitle
                }
            };
            var sectionLegend = new TextBlock { Text = "● Thép trên   ● Thép dưới   ─ Đai chính\n○ Thép tăng cường\nChọn vị trí mặt cắt bằng các thẻ phía trên.", Foreground = new SolidColorBrush(Color.FromRgb(72, 108, 169)), Margin = new Thickness(2,8,0,0), TextWrapping = TextWrapping.Wrap };
            DockPanel.SetDock(sectionHeader, Dock.Top); DockPanel.SetDock(sectionLegend, Dock.Bottom);
            sectionDock.Children.Add(sectionHeader); sectionDock.Children.Add(sectionLegend); sectionDock.Children.Add(sectionCanvas);
            Border sideCard = Card(sectionDock, new Thickness(10));

            var mainGrid = new Grid();
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(158) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            Grid.SetColumn(nav, 0); mainGrid.Children.Add(nav);
            var center = new Grid();
            center.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                center.RowDefinitions.Add(new RowDefinition { Height = new GridLength(330) });
            center.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(pageTitle, 0); center.Children.Add(pageTitle);
            Grid.SetRow(editorHost, 1); center.Children.Add(editorHost);
            var drawing = new Border { Background = Brushes.White, BorderBrush = BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 8, 8, 0) };
            var drawingGrid = new Grid();
            drawingGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            drawingGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            drawingGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
            var drawingHeader = new DockPanel { Margin = new Thickness(10, 6, 10, 5) };
            DockPanel.SetDock(status, Dock.Left); drawingHeader.Children.Add(status);
            var diagramTools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var fitBeamButton = Button("Toàn dầm", 68);
            var fitSpanButton = Button("Vùng thép", 72);
            var tiesButton = Button("Đai", 46);
            var zoomOutButton = Button("−", 36);
            var zoomInButton = Button("+", 36);
            var fitHeightButton = Button("↕", 36);
            foreach (Button tool in new[] { fitBeamButton, fitSpanButton, tiesButton, zoomOutButton, zoomInButton, fitHeightButton }) diagramTools.Children.Add(tool);
            DockPanel.SetDock(diagramTools, Dock.Right); drawingHeader.Children.Add(diagramTools);
            drawingGrid.Children.Add(drawingHeader);
            Grid.SetRow(beamViewport, 1); drawingGrid.Children.Add(beamViewport);
            var timeline = new Slider { Minimum = 0, Maximum = 100, Value = 50, Margin = new Thickness(16, 2, 16, 4), VerticalAlignment = VerticalAlignment.Center, IsSnapToTickEnabled = false };
            Grid.SetRow(timeline, 2); drawingGrid.Children.Add(timeline);
            drawing.Child = drawingGrid;
            Grid.SetRow(drawing, 2); center.Children.Add(drawing);
            Grid.SetColumn(center, 1); mainGrid.Children.Add(center);
            Grid.SetColumn(sideCard, 2); mainGrid.Children.Add(sideCard);

            var footer = new DockPanel { Margin = new Thickness(8, 6, 8, 6), LastChildFill = false };
            var brand = new TextBlock { Text = "KSTRUCT / Beam Rebar", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
            DockPanel.SetDock(brand, Dock.Left); footer.Children.Add(brand);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            actions.Children.Add(new TextBlock { Text = "Nhịp dầm:", VerticalAlignment = VerticalAlignment.Center });
            actions.Children.Add(spanCombo);
            actions.Children.Add(new TextBlock { Text = "Đơn vị: mm", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,8,0) });
            actions.Children.Add(showImage);
            var cancel = Button(settingsOnly ? "Quay lại" : "Quay lại", 86);
            var close = Button("Đóng", 70);
            var ok = Button(settingsOnly ? "Lưu cài đặt" : "Tạo cốt thép", 128);
            ok.Background = Blue; ok.Foreground = Brushes.White; ok.FontWeight = FontWeights.SemiBold;
            actions.Children.Add(cancel); actions.Children.Add(close); actions.Children.Add(ok);
            DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions);

            var root = new DockPanel { Margin = new Thickness(0) };
            var heading = new StackPanel { Margin = new Thickness(18, 8, 18, 10) };
            heading.Children.Add(new TextBlock { Text = "Cấu hình chi tiết cốt thép dầm", FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(37, 61, 102)) });
            heading.Children.Add(new TextBlock { Text = "Chỉnh thép theo nhịp, xem mặt cắt và sơ đồ dọc trước khi tạo cốt thép.", Foreground = new SolidColorBrush(Color.FromRgb(89, 116, 164)), Margin = new Thickness(0, 2, 0, 0) });
            DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
            DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            root.Children.Add(mainGrid);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;

            BarRow activeRow = rows.First(r => r.Key == "top");
            Action drawPreview = () => DrawSection(sectionCanvas, rows, types, activeRow, sectionIndex, beamLengthMm, beamWidthMm, beamHeightMm,
                Number(coverBox.Text, 30), Number(layerGapBox.Text, 30));
            IList<ReinforceStraightBeamsCommand.BeamPreviewSupport> previewSupports = defaults?.PreviewSupports ?? new List<ReinforceStraightBeamsCommand.BeamPreviewSupport>();
            Action drawBeam = () =>
            {
                var oldRanges = rows.Select(r => Tuple.Create(r, r.StartMm, r.EndMm)).ToList();
                if (tcvnCutoffCheck.IsChecked == true && Number(tcvnRbtBox.Text, 0) > 0 && Number(tcvnRsBox.Text, 0) > 0 &&
                    Number(tcvnEta1Box.Text,0) > 0 && Number(tcvnAreaRatioBox.Text,0) > 0 && Number(tcvnAlphaBox.Text,0) > 0)
                {
                    foreach (BarRow row in rows.Where(r => r.Key != "tie" && r.Key != "side" && r.Count > 0 && r.Type != null))
                    {
                        double anchor = ReinforceStraightBeamsCommand.CalculateTcvnRequiredAnchorageMm(row.Type,
                            Number(tcvnRbtBox.Text, 0), Number(tcvnRsBox.Text, 0), Number(tcvnEta1Box.Text, 2.5),
                            Number(tcvnAreaRatioBox.Text, 1.0), Number(tcvnAlphaBox.Text, 1.0));
                        if (row.StartMm > 0) row.StartMm -= anchor;
                        if (row.EndMm < beamLengthMm) row.EndMm += anchor;
                    }
                }
                DrawBeam(beamCanvas, rows, types, previewSupports, showImage.IsChecked == true, tiesVisible, activeRow, sectionIndex, beamLengthMm,
                    zoneRadio.IsChecked == true, Number(tieSpacingBox.Text, 150), Number(endSpacingBox.Text, 100),
                    Number(middleSpacingBox.Text, 200), Number(zoneLengthBox.Text, 1000), -1, -1, beamWidthMm, beamHeightMm,
                    Number(stirrupOffsetBox.Text, 50), Number(coverBox.Text, 30), defaults?.PreviewBeamSections);
                foreach (var range in oldRanges) { range.Item1.StartMm = range.Item2; range.Item1.EndMm = range.Item3; }
                int columns = previewSupports.Count(s => s.Kind == "Cột giao"), framing = previewSupports.Count(s => s.Kind == "Dầm giao");
                string junctionSummary = columns + framing == 0 ? "Chưa nhận diện giao cột/dầm" : "Giao: " + columns + " cột, " + framing + " dầm";
                status.Text = beamWidthMm.ToString("0") + "×" + beamHeightMm.ToString("0") + " mm   ·   L=" + beamLengthMm.ToString("0") + " mm   ·   " + junctionSummary;
            };
            refreshDrawing = () => { drawPreview(); drawBeam(); };
            void FitBeam()
            {
                beamZoom = 1.0; beamOffsetX = 0.0; beamOffsetY = 0.0; ApplyBeamTransform();
            }
            void FitActiveSpan()
            {
                double start = Math.Max(0, Math.Min(beamLengthMm, activeRow?.StartMm ?? 0));
                double end = Math.Max(start + 1, Math.Min(beamLengthMm, activeRow?.EndMm ?? beamLengthMm));
                double viewWidth = Math.Max(200, beamCanvas.ActualWidth - 108);
                double viewHeight = Math.Max(100, beamCanvas.ActualHeight);
                double baseSpanWidth = viewWidth * (end - start) / Math.Max(1, beamLengthMm);
                beamZoom = Math.Max(0.45, Math.Min(8.0, viewWidth * 0.78 / Math.Max(1, baseSpanWidth)));
                double centerX = 54 + viewWidth * ((start + end) * 0.5 / Math.Max(1, beamLengthMm));
                double centerY = viewHeight * 0.485;
                beamOffsetX = beamCanvas.ActualWidth * 0.5 - centerX * beamZoom;
                beamOffsetY = beamCanvas.ActualHeight * 0.5 - centerY * beamZoom;
                ApplyBeamTransform();
            }
            fitBeamButton.Click += (s,e) => FitBeam();
            fitSpanButton.Click += (s,e) => FitActiveSpan();
            tiesButton.Click += (s,e) => { tiesVisible = !tiesVisible; tiesButton.Background = tiesVisible ? Brushes.White : new SolidColorBrush(Color.FromRgb(232,240,255)); drawBeam(); };
            zoomOutButton.Click += (s,e) => ZoomAt(new Point(beamCanvas.ActualWidth/2, beamCanvas.ActualHeight/2), 1.0/1.2);
            zoomInButton.Click += (s,e) => ZoomAt(new Point(beamCanvas.ActualWidth/2, beamCanvas.ActualHeight/2), 1.2);
            fitHeightButton.Click += (s,e) => { beamOffsetY = 0; ApplyBeamTransform(); };
            void RefreshEditor(int selectedIndex)
            {
                string selected = nav.Items[selectedIndex]?.ToString() ?? "Thép chủ (trên)";
                pageTitle.Text = selected;
                if (editorPages.TryGetValue(selected, out FrameworkElement cachedPage))
                {
                    if (editorPageRows.TryGetValue(selected, out BarRow cachedRow)) activeRow = cachedRow;
                    editorHost.Content = null;
                    editorHost.Content = cachedPage;
                    refreshDrawing();
                    return;
                }

                editorHost.Content = null;
                FrameworkElement page = BuildPage(selected, rows, types, beamLengthMm, beamWidthMm, beamHeightMm, uniformRadio, zoneRadio, coverBox,
                    layerGapBox, tieSpacingBox, zoneLengthBox, endSpacingBox, middleSpacingBox, stirrupOffsetBox,
                    sideMinimumHeightBox, sideSpacingBox, sideAnchorageBox, previewSupports,
                    tcvnCutoffCheck, tcvnRbtBox, tcvnRsBox, tcvnEta1Box, tcvnAreaRatioBox, tcvnAlphaBox,
                    topStartHook, topEndHook, bottomStartHook, bottomEndHook,
                    () => { status.Text = "Đã áp dụng thông số cho nhịp đang chọn."; drawPreview(); drawBeam(); },
                    row => { activeRow = row; editorPageRows[selected] = row; refreshDrawing(); });
                editorPages.Add(selected, page);
                editorHost.Content = page;
                refreshDrawing();
            }
            nav.SelectionChanged += (s, e) => { if (nav.SelectedIndex >= 0) RefreshEditor(nav.SelectedIndex); };
            uniformRadio.Checked += (s, e) => { zoneLengthBox.IsEnabled = endSpacingBox.IsEnabled = middleSpacingBox.IsEnabled = false; drawBeam(); };
            zoneRadio.Checked += (s, e) => { zoneLengthBox.IsEnabled = endSpacingBox.IsEnabled = middleSpacingBox.IsEnabled = true; drawBeam(); };
            foreach (TextBox box in new[] { tieSpacingBox, zoneLengthBox, endSpacingBox, middleSpacingBox, stirrupOffsetBox }) box.TextChanged += (s,e) => drawBeam();
            foreach (TextBox box in new[] { tcvnRbtBox, tcvnRsBox, tcvnEta1Box }) box.TextChanged += (s,e) => drawBeam();
            foreach (TextBox box in new[] { tcvnAreaRatioBox, tcvnAlphaBox }) box.TextChanged += (s,e) => drawBeam();
            tcvnCutoffCheck.Checked += (s,e) => drawBeam(); tcvnCutoffCheck.Unchecked += (s,e) => drawBeam();
            foreach (TextBox box in new[] { coverBox, layerGapBox }) box.TextChanged += (s,e) => { drawPreview(); drawBeam(); };
            showImage.Checked += (s, e) => drawBeam(); showImage.Unchecked += (s, e) => drawBeam();
            foreach (BarRow row in rows)
            {
                row.Type = row.Type ?? (types.Count > 0 ? types[0] : null);
            }
            nav.SelectedIndex = 1;
            RefreshEditor(1);
            window.Loaded += (s, e) => { drawPreview(); drawBeam(); };
            window.SizeChanged += (s, e) => { drawPreview(); drawBeam(); };
            cancel.Click += (s, e) => window.DialogResult = false;
            close.Click += (s, e) => window.DialogResult = false;
            ok.Click += (s, e) =>
            {
                double cover, gap, tieSpacing, zoneLength, endSpacing, middleSpacing, supportOffset;
                if (!TryNumber(coverBox.Text, out cover) || cover <= 0 || !TryNumber(layerGapBox.Text, out gap) || gap <= 0 ||
                    !TryNumber(tieSpacingBox.Text, out tieSpacing) || tieSpacing <= 0 ||
                    !TryNumber(zoneLengthBox.Text, out zoneLength) || zoneLength <= 0 ||
                    !TryNumber(endSpacingBox.Text, out endSpacing) || endSpacing <= 0 ||
                    !TryNumber(middleSpacingBox.Text, out middleSpacing) || middleSpacing <= 0 ||
                    !TryNumber(stirrupOffsetBox.Text, out supportOffset) || supportOffset < 0)
                {
                    MessageBox.Show(window, "Kiểm tra lớp bảo vệ, khoảng cách lớp và bước đai phải lớn hơn 0.", "KStruct - Thép dầm", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (rows.Any(r => r.Type == null) || rows.First(r => r.Key == "top").Count < 1 || rows.First(r => r.Key == "bottom").Count < 1)
                {
                    MessageBox.Show(window, "Chọn đầy đủ đường kính và số thanh cho thép chủ trên/dưới.", "KStruct - Thép dầm", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                bool hasInternalCut = rows.Any(r => r.Key != "tie" && r.Key != "side" && r.Count > 0 && (r.StartMm > 0 || r.EndMm < beamLengthMm));
                if (tcvnCutoffCheck.IsChecked == true && hasInternalCut &&
                    (!TryNumber(tcvnRbtBox.Text, out double rbt) || rbt <= 0 ||
                     !TryNumber(tcvnRsBox.Text, out double rs) || rs <= 0 ||
                     !TryNumber(tcvnEta1Box.Text, out double eta1) || eta1 <= 0 ||
                     !TryNumber(tcvnAreaRatioBox.Text, out double areaRatio) || areaRatio <= 0 ||
                     !TryNumber(tcvnAlphaBox.Text, out double alpha) || alpha <= 0))
                {
                    MessageBox.Show(window, "Chế độ cắt thép TCVN đang bật. Nhập Rbt, Rs, η1, As,cal/As,ef và α dương.", "KStruct - TCVN 5574:2018", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                window.DialogResult = true;
            };

            if (window.ShowDialog() != true) return null;
            BarRow top = rows.First(r => r.Key == "top"), bottom = rows.First(r => r.Key == "bottom");
            BarRow top1 = rows.First(r => r.Key == "top1"), top2 = rows.First(r => r.Key == "top2");
            BarRow bottom1 = rows.First(r => r.Key == "bottom1"), bottom2 = rows.First(r => r.Key == "bottom2");
            int ParseCount(BarRow row) => row.Optional && row.Count < 0 ? 0 : Math.Max(0, row.Count);
            return new ReinforceStraightBeamsCommand.Options
            {
                MainType = top.Type ?? types.FirstOrDefault(), TopType = top.Type ?? types.FirstOrDefault(), BottomType = bottom.Type ?? top.Type ?? types.FirstOrDefault(),
                TopCount = Math.Max(1, top.Count), BottomCount = Math.Max(1, bottom.Count),
                TopLayer2Count = ParseCount(top1), TopLayer2Type = top1.Type ?? top.Type,
                TopLayer3Count = ParseCount(top2), TopLayer3Type = top2.Type ?? top1.Type ?? top.Type,
                BottomLayer2Count = ParseCount(bottom1), BottomLayer2Type = bottom1.Type ?? bottom.Type,
                BottomLayer3Count = ParseCount(bottom2), BottomLayer3Type = bottom2.Type ?? bottom1.Type ?? bottom.Type,
                SideBarCount = Math.Max(0, rows.First(r=>r.Key=="side").Count), SideBarType = rows.First(r=>r.Key=="side").Type ?? top.Type,
                TieType = rows.First(r => r.Key == "tie").Type ?? types.FirstOrDefault(),
                LongitudinalLayerSpacingMm = Number(layerGapBox.Text, 30), CoverMm = Number(coverBox.Text, 30), TieSpacingMm = Number(tieSpacingBox.Text, 150),
                UseZonedTies = zoneRadio.IsChecked == true, TieEndZoneLengthMm = Number(zoneLengthBox.Text, 1000),
                TieEndSpacingMm = Number(endSpacingBox.Text, 100), TieMiddleSpacingMm = Number(middleSpacingBox.Text, 200),
                StirrupSupportOffsetMm = Number(stirrupOffsetBox.Text, 50),
                SideBarMinimumHeightMm = Number(sideMinimumHeightBox.Text, 700),
                SideBarSpacingMm = Number(sideSpacingBox.Text, 400),
                SideBarAnchorageMm = Number(sideAnchorageBox.Text, 100),
                TcvnCutoffEnabled = tcvnCutoffCheck.IsChecked == true,
                TcvnConcreteRbtMpa = Number(tcvnRbtBox.Text, 0), TcvnSteelRsMpa = Number(tcvnRsBox.Text, 0),
                TcvnBondEta1 = Number(tcvnEta1Box.Text, 2.5),
                TcvnAreaRatio = Number(tcvnAreaRatioBox.Text, 1.0), TcvnAlpha = Number(tcvnAlphaBox.Text, 1.0),
                TopStartHook = ((HookChoice)topStartHook.SelectedItem)?.Type, TopEndHook = ((HookChoice)topEndHook.SelectedItem)?.Type,
                BottomStartHook = ((HookChoice)bottomStartHook.SelectedItem)?.Type, BottomEndHook = ((HookChoice)bottomEndHook.SelectedItem)?.Type,
                PreviewBeamLengthMm = defaults?.PreviewBeamLengthMm ?? 0, PreviewBeamWidthMm = defaults?.PreviewBeamWidthMm ?? 0,
                PreviewBeamHeightMm = defaults?.PreviewBeamHeightMm ?? 0, PreviewSupports = defaults?.PreviewSupports ?? new List<ReinforceStraightBeamsCommand.BeamPreviewSupport>(),
                PreviewBeamSections = defaults?.PreviewBeamSections ?? new List<ReinforceStraightBeamsCommand.BeamPreviewSection>(),
                DetailedBars = ToDetailedSpecs(rows, ((HookChoice)topStartHook.SelectedItem)?.Type, ((HookChoice)topEndHook.SelectedItem)?.Type,
                    ((HookChoice)bottomStartHook.SelectedItem)?.Type, ((HookChoice)bottomEndHook.SelectedItem)?.Type, false),
            };
        }

        internal static ReinforceStraightBeamsCommand.Options ShowQuick(IntPtr owner, IList<RebarBarType> types,
            IList<RebarHookType> hookTypes, ReinforceStraightBeamsCommand.Options defaults,
            bool settingsOnly, string titleOverride, Action<ReinforceStraightBeamsCommand.Options> saveCurrent = null)
        {
            List<BarRow> rows = BuildRows(types, defaults);
            var window = new Window
            {
                Title = titleOverride ?? "Thép dầm",
                Width = 1600, Height = 960, MinWidth = 1280, MinHeight = 760,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.SingleBorderWindow,
                Background = Page, ShowInTaskbar = false
            };

            double beamLengthMm = defaults?.PreviewBeamLengthMm > 0 ? defaults.PreviewBeamLengthMm : 4000.0;
            double beamWidthMm = defaults?.PreviewBeamWidthMm > 0 ? defaults.PreviewBeamWidthMm : 200.0;
            double beamHeightMm = defaults?.PreviewBeamHeightMm > 0 ? defaults.PreviewBeamHeightMm : 400.0;
            NormalizeRanges(rows, beamLengthMm);
            IList<ReinforceStraightBeamsCommand.BeamPreviewSupport> previewSupports =
                defaults?.PreviewSupports ?? new List<ReinforceStraightBeamsCommand.BeamPreviewSupport>();
            var spanNodes = new List<double> { 0, beamLengthMm };
            foreach (var support in previewSupports.Where(s => s != null).OrderBy(s => s.StartMm))
            {
                double center = (support.StartMm + support.EndMm) * 0.5;
                if (center <= 150 || center >= beamLengthMm - 150) continue;
                if (!spanNodes.Any(x => Math.Abs(x - center) < 80)) spanNodes.Add(center);
            }
            spanNodes = spanNodes.OrderBy(x => x).ToList();
            var previewSpans = new List<Tuple<double, double>>();
            for (int i = 0; i < spanNodes.Count - 1; i++)
                if (spanNodes[i + 1] - spanNodes[i] > 150)
                    previewSpans.Add(Tuple.Create(spanNodes[i], spanNodes[i + 1]));

            var sectionCanvas = new Canvas { MinHeight = 360, Background = Brushes.White };
            var beamCanvas = new Canvas { MinHeight = 360, Background = Brushes.White, ClipToBounds = true };
            var uniformCanvas = new Canvas { Height = 58, Background = Brushes.White };
            var zonedCanvas = new Canvas { Height = 58, Background = Brushes.White };
            var coverBox = Input(defaults?.CoverMm ?? 30, 90);
            var layerGapBox = Input(defaults?.LongitudinalLayerSpacingMm > 0 ? defaults.LongitudinalLayerSpacingMm : 30, 90);

            var tieSpacingBox = Input(defaults?.TieSpacingMm > 0 ? defaults.TieSpacingMm : 150, 90);
            var zoneLengthBox = Input(defaults?.TieEndZoneLengthMm > 0 ? defaults.TieEndZoneLengthMm : 1000, 90);
            var endSpacingBox = Input(defaults?.TieEndSpacingMm > 0 ? defaults.TieEndSpacingMm : 100, 90);
            var middleSpacingBox = Input(defaults?.TieMiddleSpacingMm > 0 ? defaults.TieMiddleSpacingMm : 200, 90);
            var uniformRadio = new RadioButton { Content = "Phân bố đều", GroupName = "QuickTieMode", IsChecked = defaults != null && !defaults.UseZonedTies, VerticalAlignment = VerticalAlignment.Center };
            var zonedRadio = new RadioButton { Content = "Chia vùng", GroupName = "QuickTieMode", IsChecked = defaults == null || defaults.UseZonedTies, VerticalAlignment = VerticalAlignment.Center };
            var tieRow = rows.First(r => r.Key == "tie");
            var tieCombo = MakeTypeCombo(types, tieRow);
            var title = new TextBlock { Text = "Bố trí cốt thép dầm", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(37, 61, 102)) };
            var subtitle = new TextBlock
            {
                Text = "Dầm · " + previewSpans.Count + " nhịp · " + beamWidthMm.ToString("0") + " × " +
                    beamHeightMm.ToString("0") + " mm · L = " + beamLengthMm.ToString("0") + " mm · Chọn thanh trên sơ đồ để sửa trực tiếp",
                Foreground = new SolidColorBrush(Color.FromRgb(89, 116, 164)), Margin = new Thickness(0, 3, 0, 0),
                ToolTip = defaults?.PreviewSectionDescription
            };

            Action redraw = null;
            if (!string.IsNullOrWhiteSpace(defaults?.PreviewSectionDescription))
                subtitle.Text += "\n" + defaults.PreviewSectionDescription;
            Action refreshLayerChoices = () => { };
            BarRow activePreviewRow = rows.First(r => r.Key == "top");
            bool syncingBarEditor = false;
            double highlightStartMm = -1, highlightEndMm = -1;
            var spanSelector = new ComboBox
            {
                Width = 150, Height = 30,
                ItemsSource = new[] { "Toàn dầm" }.Concat(previewSpans.Select((span, index) =>
                    "Nhịp " + (index + 1) + " · " + (span.Item2 - span.Item1).ToString("0") + " mm")).ToList(),
                SelectedIndex = 0, Margin = new Thickness(4)
            };
            var spanStatus = new TextBlock
            {
                Text = "Đang xem toàn dầm · thông số thanh theo chiều dài dầm",
                Foreground = new SolidColorBrush(Color.FromRgb(72, 96, 134)),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0)
            };
            var barLayerSelector = new ComboBox
            {
                Width = 230, Height = 30,
                ItemsSource = new[] { "top", "bottom", "top1", "top2", "bottom1", "bottom2" }.Select(k => rows.First(r => r.Key == k)).Where(r => !r.Optional || r.Count > 0).ToList(),
                DisplayMemberPath = "Caption", SelectedIndex = 0, Margin = new Thickness(4),
                ToolTip = "Chọn lớp thép để kéo chỉnh chiều dài trên sơ đồ"
            };
            var selectedBarTitle = new TextBlock { Text = "Thông tin thanh đang chọn", FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 2, 5) };
            var selectedTypeCombo = MakeTypeCombo(types, activePreviewRow);
            var selectedCountCombo = CountCombo(activePreviewRow.Count);
            var selectedStartBox = Input(activePreviewRow.StartMm, 100);
            var selectedEndBox = Input(activePreviewRow.EndMm, 100);
            var selectedHookChoices = new[] { new HookChoice(null) }.Concat((hookTypes ?? new List<RebarHookType>()).Select(h => new HookChoice(h))).ToList();
            var selectedLeftHookCombo = HookCombo(selectedHookChoices, defaults?.TopStartHook);
            var selectedRightHookCombo = HookCombo(selectedHookChoices, defaults?.TopEndHook);
            var selectedLeftXBox = Input(activePreviewRow.LeftHookMm, 100);
            var selectedRightXBox = Input(activePreviewRow.RightHookMm, 100);
            var selectedPositionBox = Input(activePreviewRow.Position, 100);
            var selectedHookDownBox = Input(activePreviewRow.HookDownMm, 100);
            var inlineDeleteAdditionalButton = Button("Xóa", 58);
            inlineDeleteAdditionalButton.Foreground = Brushes.Firebrick;
            inlineDeleteAdditionalButton.ToolTip = "Xóa nhóm thép gia cường đang chọn trên sơ đồ";
            var selectedHookDownLabel = new TextBlock { Text = "Chiều dài móc (mm)", Foreground = new SolidColorBrush(Color.FromRgb(77, 107, 158)), Margin = new Thickness(4, 5, 5, 5) };
            Action syncSelectedBarEditor = () =>
            {
                BarGeometry bar = CurrentGeometry(activePreviewRow);
                syncingBarEditor = true;
                inlineDeleteAdditionalButton.IsEnabled = activePreviewRow.Optional &&
                    activePreviewRow.Key != "tie" && activePreviewRow.Key != "side";
                selectedBarTitle.Text = activePreviewRow.Caption + " · nhóm " + activePreviewRow.Count + " thanh" +
                    (activePreviewRow.HasMultipleIntervals ? " · đoạn " + (activePreviewRow.SelectedBarIndex + 1) : "");
                int typeIndex = types.ToList().FindIndex(t => t.Id == bar.Type?.Id);
                selectedTypeCombo.SelectedIndex = typeIndex < 0 ? 0 : typeIndex;
                selectedCountCombo.SelectedIndex = Math.Max(0, Math.Min(11, activePreviewRow.Count - 1));
                selectedStartBox.Text = bar.StartMm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                selectedEndBox.Text = bar.EndMm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                var leftChoices = selectedLeftHookCombo.ItemsSource as IList<HookChoice>;
                var rightChoices = selectedRightHookCombo.ItemsSource as IList<HookChoice>;
                selectedLeftHookCombo.SelectedIndex = Math.Max(0, leftChoices.ToList().FindIndex(h => h.Type?.Id == bar.StartHook?.Id));
                selectedRightHookCombo.SelectedIndex = Math.Max(0, rightChoices.ToList().FindIndex(h => h.Type?.Id == bar.EndHook?.Id));
                selectedLeftXBox.Text = bar.LeftHookMm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                selectedRightXBox.Text = bar.RightHookMm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                selectedPositionBox.Text = bar.Position.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                selectedHookDownBox.Text = bar.HookDownMm
                    .ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                selectedHookDownLabel.Text = "Móc xuống (mm)";
                bool isTopMain = activePreviewRow.Key == "top";
                selectedStartBox.IsReadOnly = selectedEndBox.IsReadOnly = isTopMain;
                selectedStartBox.ToolTip = selectedEndBox.ToolTip = isTopMain
                    ? "Thép chủ trên chạy liên tục hết chiều dài tuyến dầm, trừ lớp bảo vệ ở hai đầu." : null;
                selectedLeftHookCombo.IsEnabled = selectedRightHookCombo.IsEnabled = !isTopMain;
                selectedLeftXBox.IsEnabled = selectedRightXBox.IsEnabled = isTopMain;
                selectedHookDownBox.IsEnabled = isTopMain || (activePreviewRow.Key.StartsWith("top") &&
                    (bar.StartMm <= 1 || bar.EndMm >= beamLengthMm - 1));
                syncingBarEditor = false;
            };
            var fitAllButton = Button("Toàn dầm", 78);
            var zoomOutButton = Button("−", 34);
            var zoomInButton = Button("+", 34);
            var fitSpanButton = Button("Fit nhịp", 72);
            var barCardGrid = new Grid();
            barCardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            barCardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            barCardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
            barCardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            barCardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddQuickCell(barCardGrid, new TextBlock { Text = "Lớp thép", FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 8) }, 0, 0, 1, 2);
            AddQuickCell(barCardGrid, new TextBlock { Text = "Số thanh", Foreground = new SolidColorBrush(Color.FromRgb(72, 108, 169)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 5) }, 0, 2);
            AddQuickCell(barCardGrid, new TextBlock { Text = "Đường kính", Foreground = new SolidColorBrush(Color.FromRgb(72, 108, 169)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 5) }, 0, 3);
            // Keep the first screen focused on the four reinforcement groups shown in
            // the new beam layout. Second layers remain editable in the detailed editor.
            var quickRows = new[] { "top", "bottom", "top1", "bottom1" }.Select(k => rows.First(r => r.Key == k)).ToList();
            var rowToggles = new Dictionary<string, CheckBox>();
            var quickCountCombos = new Dictionary<string, ComboBox>();
            var quickTypeCombos = new Dictionary<string, ComboBox>();
            bool syncingQuickControls = false;
            void SyncQuickControls()
            {
                syncingQuickControls = true;
                foreach (var entry in quickCountCombos)
                    entry.Value.SelectedIndex = Math.Max(0, Math.Min(11, rows.First(r => r.Key == entry.Key).Count - 1));
                foreach (var entry in quickTypeCombos)
                {
                    var row = rows.First(r => r.Key == entry.Key);
                    entry.Value.SelectedIndex = types.ToList().FindIndex(t => t.Id == row.Type?.Id);
                }
                syncingQuickControls = false;
            }
            var supportInputs = new Dictionary<string, Tuple<TextBox, TextBox>>();
            var supportPanel = new StackPanel();
            var deletedRanges = new HashSet<string>(defaults?.DeletedReinforcementRanges ?? new List<string>());
            string RangeKey(BarRow row, BarGeometry bar) => row.Key + "/" +
                (bar.StartMm / beamLengthMm).ToString("F6", System.Globalization.CultureInfo.InvariantCulture) + "/" +
                (bar.EndMm / beamLengthMm).ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
            bool useSupportSpanPreset = false;
            var supportSpanPresetButton = Button("Bố trí gối + nhịp · 1/4–1/5", 0);
            supportSpanPresetButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            supportSpanPresetButton.Width = double.NaN;
            supportSpanPresetButton.Height = 44;
            supportSpanPresetButton.Margin = new Thickness(0, 10, 0, 8);
            supportSpanPresetButton.Background = Blue;
            supportSpanPresetButton.Foreground = Brushes.White;
            supportSpanPresetButton.FontWeight = FontWeights.SemiBold;
            supportPanel.Children.Add(supportSpanPresetButton);
            var thirdQuarterPresetButton = Button("Bố trí gối + nhịp · 1/3–1/4", 0);
            thirdQuarterPresetButton.HorizontalAlignment = HorizontalAlignment.Stretch;
            thirdQuarterPresetButton.Width = double.NaN;
            thirdQuarterPresetButton.Margin = new Thickness(0, 2, 0, 8);
            supportPanel.Children.Add(thirdQuarterPresetButton);
            for (int i = 0; i < quickRows.Count; i++)
            {
                BarRow row = quickRows[i]; int gridRow = i + 1;
                barCardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
                AddQuickCell(barCardGrid, new TextBlock { Text = row.Caption, FontSize = 11, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) }, gridRow, 0);
                var toggle = new CheckBox { IsChecked = !row.Optional || row.Count > 0, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsEnabled = row.Optional };
                rowToggles[row.Key] = toggle;
                var count = CountCombo(Math.Max(1, row.Count)); count.Width = 62; count.HorizontalAlignment = HorizontalAlignment.Center;
                quickCountCombos[row.Key] = count;
                var type = MakeTypeCombo(types, row); type.Width = 140; type.HorizontalAlignment = HorizontalAlignment.Center;
                quickTypeCombos[row.Key] = type;
                AddQuickCell(barCardGrid, toggle, gridRow, 1); AddQuickCell(barCardGrid, count, gridRow, 2); AddQuickCell(barCardGrid, type, gridRow, 3);
                count.IsEnabled = toggle.IsChecked == true;
                count.SelectionChanged += (s, e) =>
                {
                    if (syncingQuickControls) return;
                    if (toggle.IsChecked == true)
                    {
                        row.Count = CountValue(count);
                        if (!row.HasMultipleIntervals) ResizeBarGeometries(row, row.Count);
                    }
                    if (activePreviewRow == row) syncSelectedBarEditor();
                    redraw?.Invoke();
                };
                type.SelectionChanged += (s, e) =>
                {
                    if (syncingQuickControls) return;
                    row.Type = (type.SelectedItem as BarChoice)?.Type ?? row.Type;
                    row.QuickTypeChanged = true;
                    foreach (var bar in row.Bars) bar.Type = row.Type;
                    if (activePreviewRow == row) syncSelectedBarEditor();
                    redraw?.Invoke();
                };
                toggle.Checked += (s, e) => { count.IsEnabled = true; if (row.Count <= 0) row.Count = CountValue(count); refreshLayerChoices(); redraw?.Invoke(); };
                toggle.Unchecked += (s, e) => { count.IsEnabled = false; row.Count = 0; refreshLayerChoices(); redraw?.Invoke(); };
            }
            var barCard = Card(new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Thép chủ & thép tăng cường", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 2, 8) },
                    barCardGrid,
                    supportPanel,
                    new TextBlock { Text = "Tăng cường gối tự có ở hai đầu dầm và các gối giữa. Thép tăng cường được chia theo từng nhịp.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(94, 112, 140)), Margin = new Thickness(2, 4, 2, 0) },
                    new TextBlock { Text = "Chọn thanh trên sơ đồ để sửa đường kính, số lượng, phạm vi và chiều dài móc trực tiếp.", Foreground = new SolidColorBrush(Color.FromRgb(94, 112, 140)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 2, 0) }
                }
            }, new Thickness(12));

            var tieGrid = new Grid();
            tieGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tieGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 4; i++) tieGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var tieHeader = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock { Text = "Đường kính đai", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                    tieCombo
                }
            };
            AddQuickCell(tieGrid, tieHeader, 0, 0, 1, 2);
            var uniformPanel = new StackPanel { Margin = new Thickness(4, 5, 16, 0), Children = { uniformRadio, uniformCanvas } };
            var zonedPanel = new StackPanel { Margin = new Thickness(4, 5, 4, 0), Children = { zonedRadio, zonedCanvas } };
            AddQuickCell(tieGrid, uniformPanel, 1, 0); AddQuickCell(tieGrid, zonedPanel, 1, 1);
            endSpacingBox.Width = middleSpacingBox.Width = zoneLengthBox.Width = 78;
            var zoneFields = new Grid { Margin = new Thickness(4, 5, 0, 0) };
            for (int i = 0; i < 3; i++) zoneFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var endSpacingPanel = new StackPanel { Children = { new TextBlock { Text = "Bước đầu (mm)" }, endSpacingBox } };
            var middleSpacingPanel = new StackPanel { Children = { new TextBlock { Text = "Bước giữa (mm)" }, middleSpacingBox } };
            var zoneLengthPanel = new StackPanel { Children = { new TextBlock { Text = "Vùng đầu (mm)" }, zoneLengthBox } };
            Grid.SetColumn(endSpacingPanel, 0); zoneFields.Children.Add(endSpacingPanel);
            Grid.SetColumn(middleSpacingPanel, 1); zoneFields.Children.Add(middleSpacingPanel);
            Grid.SetColumn(zoneLengthPanel, 2); zoneFields.Children.Add(zoneLengthPanel);
            AddQuickCell(tieGrid, zoneFields, 2, 0, 1, 2);
            var uniformFields = new StackPanel
            {
                Orientation = Orientation.Horizontal, Margin = new Thickness(4, 5, 0, 0),
                Children = { new TextBlock { Text = "Bước đai (mm)", VerticalAlignment = VerticalAlignment.Center }, tieSpacingBox }
            };
            AddQuickCell(tieGrid, uniformFields, 3, 0, 1, 2);
            var tieCard = Card(new StackPanel
            {
                Children = { new TextBlock { Text = "Thép đai", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 2, 8) }, tieGrid }
            }, new Thickness(12));
            var commonCard = Card(new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Thông số chung", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 2, 8) },
                    FormRows(new object[] { "Lớp bảo vệ (mm)", coverBox, "Khoảng cách giữa lớp thép (mm)", layerGapBox })
                }
            }, new Thickness(12));
            var leftColumn = new StackPanel { Children = { barCard, tieCard, commonCard } };
            var leftScroll = new ScrollViewer { Content = leftColumn, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

            var selectedDiagramHint = new Button { Content = "Chọn thanh trên sơ đồ để sửa", Height = 34, Margin = new Thickness(5), IsHitTestVisible = false,
                Background = new SolidColorBrush(Color.FromRgb(235, 242, 255)), Foreground = Blue, BorderBrush = new SolidColorBrush(Color.FromRgb(203, 219, 255)), FontWeight = FontWeights.SemiBold };
            var selectedBarInfoCard = Card(new StackPanel
            {
                Children =
                {
                    selectedBarTitle,
                    QuickSelectedBarGrid(new object[]
                    {
                        "Đường kính", selectedTypeCombo,
                        "Số thanh", selectedCountCombo,
                        "Điểm bắt đầu (mm)", selectedStartBox,
                        "Điểm kết thúc (mm)", selectedEndBox,
                        selectedHookDownLabel, selectedHookDownBox,
                        "", new StackPanel { Orientation = Orientation.Horizontal, Children = {
                            inlineDeleteAdditionalButton, selectedDiagramHint } }
                    })
                }
            }, new Thickness(8));
            var beamTabGrid = new Grid { Margin = new Thickness(4) };
            beamTabGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            beamTabGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var beamCanvasBorder = new Border { Background = Brushes.White, Padding = new Thickness(8), Child = beamCanvas };
            Grid.SetRow(beamCanvasBorder, 0); beamTabGrid.Children.Add(beamCanvasBorder);
            Grid.SetRow(selectedBarInfoCard, 1); beamTabGrid.Children.Add(selectedBarInfoCard);
            var beamTab = new TabItem { Header = "Mặt đứng", Content = beamTabGrid };
            var sectionTab = new TabItem { Header = "Mặt cắt", Content = new ScrollViewer {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border { Background = Brushes.White, Padding = new Thickness(8), Child = sectionCanvas } } };
            var previewTabs = new TabControl { Items = { beamTab, sectionTab }, Margin = new Thickness(4) };
            var previewHeader = new DockPanel { Margin = new Thickness(8, 4, 8, 2) };
            var previewTitle = new TextBlock { Text = "Sơ đồ cốt thép", FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(previewTitle, Dock.Left); previewHeader.Children.Add(previewTitle);
            var spanTools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            spanTools.Children.Add(new TextBlock { Text = "Phạm vi xem", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            spanTools.Children.Add(spanSelector); spanTools.Children.Add(barLayerSelector);
            spanTools.Children.Add(fitAllButton); spanTools.Children.Add(zoomOutButton); spanTools.Children.Add(zoomInButton); spanTools.Children.Add(fitSpanButton);
            DockPanel.SetDock(spanTools, Dock.Right); previewHeader.Children.Add(spanTools);
            var previewGrid = new Grid();
            previewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            previewGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(previewHeader, 0); previewGrid.Children.Add(previewHeader);
            Grid.SetRow(previewTabs, 1); previewGrid.Children.Add(previewTabs);

            var main = new Grid();
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(464) });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(leftScroll, 0); main.Children.Add(leftScroll);
            Grid.SetColumn(previewGrid, 1); main.Children.Add(previewGrid);

            var footer = new DockPanel { Margin = new Thickness(8, 5, 8, 6), LastChildFill = false };
            var footerStatus = new TextBlock { Text = "Chọn nhịp để phóng to · kéo tay nắm ở đầu thanh để chỉnh chiều dài neo.", Foreground = new SolidColorBrush(Color.FromRgb(72, 96, 134)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(footerStatus, Dock.Left); footer.Children.Add(footerStatus);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var closeButton = Button("Hủy", 82);
            var createButton = Button(settingsOnly ? "Lưu cài đặt" : "Tạo cốt thép", 150);
            var detailsButton = Button("Chỉnh chi tiết", 140);
            createButton.Background = Blue; createButton.Foreground = Brushes.White; createButton.FontWeight = FontWeights.SemiBold;
            actions.Children.Add(detailsButton); actions.Children.Add(closeButton); actions.Children.Add(createButton);
            DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions);
            var root = new DockPanel();
            var heading = new StackPanel { Margin = new Thickness(18, 10, 18, 10), Children = { title, subtitle } };
            DockPanel.SetDock(heading, Dock.Top); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(heading); root.Children.Add(footer); root.Children.Add(main);
            window.Width = 1600; window.Height = 960; window.MinWidth = 1280; window.MinHeight = 760;
            window.Content = root; new WindowInteropHelper(window).Owner = owner;
            bool refreshingLayerChoices = false;
            refreshLayerChoices = () =>
            {
                var availableRows = quickRows.Where(r => !r.Optional || r.Count > 0).ToList();
                string activeKey = activePreviewRow.Key;
                refreshingLayerChoices = true;
                barLayerSelector.ItemsSource = availableRows;
                barLayerSelector.DisplayMemberPath = "Caption";
                int selected = availableRows.FindIndex(r => r.Key == activeKey);
                if (selected < 0) selected = availableRows.FindIndex(r => r.Key == "top");
                barLayerSelector.SelectedIndex = Math.Max(0, selected);
                activePreviewRow = barLayerSelector.SelectedItem as BarRow ?? availableRows[0];
                refreshingLayerChoices = false;
                syncSelectedBarEditor();
                CurrentGeometry(activePreviewRow);
            };
            double diagramZoom = 1, diagramOffsetX = 0, diagramOffsetY = 0;
            bool draggingStart = false, draggingEnd = false, panningDiagram = false;
            Point panStart = new Point();
            double panStartX = 0, panStartY = 0;
            void ApplyDiagramTransform()
            {
                beamCanvas.RenderTransform = new MatrixTransform(new Matrix(diagramZoom, 0, 0, diagramZoom, diagramOffsetX, diagramOffsetY));
            }
            void FitDiagram(bool selectedSpan)
            {
                double width = beamCanvas.ActualWidth, height = beamCanvas.ActualHeight;
                if (width < 100 || height < 100) return;
                if (!selectedSpan || highlightStartMm < 0 || highlightEndMm <= highlightStartMm)
                {
                    diagramZoom = 1; diagramOffsetX = 0; diagramOffsetY = 0; ApplyDiagramTransform(); return;
                }
                double left = 68, right = width - 68;
                double rangeWidth = (right - left) * (highlightEndMm - highlightStartMm) / Math.Max(1, beamLengthMm);
                diagramZoom = Math.Max(1, Math.Min(6, (width - 100) / Math.Max(1, rangeWidth)));
                double centerX = left + (highlightStartMm + highlightEndMm) * 0.5 / Math.Max(1, beamLengthMm) * (right - left);
                double centerY = Math.Max(100, height * 0.30) + Math.Max(64, Math.Min(104, height * 0.23)) * 0.5;
                diagramOffsetX = width * 0.5 - centerX * diagramZoom;
                diagramOffsetY = height * 0.48 - centerY * diagramZoom;
                ApplyDiagramTransform();
            }
            void ZoomDiagram(Point anchor, double factor)
            {
                double next = Math.Max(0.75, Math.Min(8, diagramZoom * factor));
                double ratio = next / diagramZoom;
                diagramOffsetX = anchor.X - (anchor.X - diagramOffsetX) * ratio;
                diagramOffsetY = anchor.Y - (anchor.Y - diagramOffsetY) * ratio;
                diagramZoom = next; ApplyDiagramTransform();
            }
            void ActiveBarHandles(out double startX, out double endX, out double y, out BarGeometry bar)
            {
                bar = CurrentGeometry(activePreviewRow);
                double width = Math.Max(500, beamCanvas.ActualWidth), height = Math.Max(360, beamCanvas.ActualHeight);
                double left = 68, right = width - 68, top = Math.Max(100, height * 0.30);
                double bodyHeight = Math.Max(64, Math.Min(104, height * 0.23));
                double bottom = top + bodyHeight;
                double scale = (right - left) / Math.Max(1, beamLengthMm);
                startX = left + Math.Max(0, Math.Min(beamLengthMm, bar.StartMm)) * scale;
                endX = left + Math.Max(0, Math.Min(beamLengthMm, bar.EndMm)) * scale;
                y = activePreviewRow.Key == "top" ? top + 21 :
                    activePreviewRow.Key == "top1" ? top + 36 :
                    activePreviewRow.Key == "top2" ? top + 49 :
                    activePreviewRow.Key == "bottom1" ? bottom - 35 :
                    activePreviewRow.Key == "bottom2" ? bottom - 48 : bottom - 20;
                if (activePreviewRow.Bars.Count > 1)
                    y += (activePreviewRow.SelectedBarIndex - (activePreviewRow.Bars.Count - 1) / 2.0) * 1.8;
            }
            bool TrySelectBarAt(Point point)
            {
                double width = Math.Max(500, beamCanvas.ActualWidth), height = Math.Max(360, beamCanvas.ActualHeight);
                double left = 68, right = width - 68, top = Math.Max(100, height * 0.30);
                double bodyHeight = Math.Max(64, Math.Min(104, height * 0.23)), bottom = top + bodyHeight;
                double scale = (right - left) / Math.Max(1, beamLengthMm);
                BarRow bestRow = null;
                int bestIndex = -1;
                double bestDistance = double.PositiveInfinity;
                foreach (BarRow row in quickRows.Where(r => r.Count > 0))
                {
                    double y = row.Key == "top" ? top + 21 :
                        row.Key == "top1" ? top + 36 :
                        row.Key == "top2" ? top + 49 :
                        row.Key == "bottom1" ? bottom - 35 :
                        row.Key == "bottom2" ? bottom - 48 : bottom - 20;
                    var bars = row.Bars.Take(12).ToList();
                    for (int i = 0; i < bars.Count; i++)
                    {
                        BarGeometry bar = bars[i];
                        double laneY = y + (i - (bars.Count - 1) / 2.0) * 1.8;
                        IEnumerable<Tuple<double, double>> ranges = row.Key == "top1" || row.Key == "top2" || row.Key == "bottom1" || row.Key == "bottom2"
                            ? previewSpans
                            : new[] { Tuple.Create(0.0, beamLengthMm) };
                        foreach (Tuple<double, double> range in ranges)
                        {
                            double start = Math.Max(bar.StartMm, range.Item1), end = Math.Min(bar.EndMm, range.Item2);
                            if (end <= start) continue;
                            double x1 = left + start * scale, x2 = left + end * scale;
                            double distance = Math.Abs(point.Y - laneY);
                            if (point.X >= x1 - 7 && point.X <= x2 + 7 && distance <= 11 && distance < bestDistance)
                            {
                                bestRow = row; bestIndex = i; bestDistance = distance;
                            }
                        }
                    }
                }
                if (bestRow == null) return false;
                activePreviewRow = bestRow;
                activePreviewRow.SelectedBarIndex = bestIndex;
                var availableRows = barLayerSelector.ItemsSource as IList<BarRow>;
                int layerIndex = availableRows?.ToList().FindIndex(r => r.Key == bestRow.Key) ?? -1;
                if (layerIndex >= 0 && barLayerSelector.SelectedIndex != layerIndex)
                    barLayerSelector.SelectedIndex = layerIndex;
                syncSelectedBarEditor();
                redraw?.Invoke();
                footerStatus.Text = activePreviewRow.Caption + " · thanh " + (bestIndex + 1) + " đang chọn.";
                return true;
            }
            redraw = () =>
            {
                bool zoned = zonedRadio.IsChecked == true;
                DrawQuickTies(uniformCanvas, zonedCanvas, zoned, Number(endSpacingBox.Text, 100), Number(middleSpacingBox.Text, 200), Number(zoneLengthBox.Text, 1000), Number(tieSpacingBox.Text, 150));
                zoneFields.IsEnabled = zoned; uniformFields.IsEnabled = !zoned;
                DrawBeam(beamCanvas, rows, types, previewSupports, true, true, activePreviewRow, 1, beamLengthMm,
                    zoned, Number(tieSpacingBox.Text, 150), Number(endSpacingBox.Text, 100), Number(middleSpacingBox.Text, 200),
                    Number(zoneLengthBox.Text, 1000), highlightStartMm, highlightEndMm, beamWidthMm, beamHeightMm,
                    defaults?.StirrupSupportOffsetMm > 0 ? defaults.StirrupSupportOffsetMm : 50,
                    defaults?.CoverMm > 0 ? defaults.CoverMm : 30, defaults?.PreviewBeamSections);
                DrawSectionGallery(sectionCanvas, rows, beamCanvas.Tag as List<double>, beamWidthMm, beamHeightMm,
                    defaults?.PreviewBeamSections);
            };
            var deleteAdditionalButton = Button("Xóa nhóm gia cường đang chọn", 230);
            supportPanel.Children.Add(deleteAdditionalButton);
            Action deleteSelectedAdditional = () =>
            {
                if (!activePreviewRow.Optional || activePreviewRow.Key == "tie" || activePreviewRow.Key == "side") return;
                deletedRanges.Add(RangeKey(activePreviewRow, CurrentGeometry(activePreviewRow)));
                if (activePreviewRow.HasMultipleIntervals && activePreviewRow.Bars.Count > 1)
                {
                    activePreviewRow.Bars.RemoveAt(Math.Max(0, Math.Min(activePreviewRow.SelectedBarIndex, activePreviewRow.Bars.Count - 1)));
                    activePreviewRow.SelectedBarIndex = 0;
                    CopyGeometry(activePreviewRow, activePreviewRow.Bars[0]);
                }
                else
                {
                    activePreviewRow.Count = 0;
                    activePreviewRow.Bars.Clear();
                    rowToggles[activePreviewRow.Key].IsChecked = false;
                    activePreviewRow = rows.First(row => row.Key == "top");
                }
                refreshLayerChoices();
                syncSelectedBarEditor();
                redraw();
            };
            deleteAdditionalButton.Click += (s, e) => deleteSelectedAdditional();
            inlineDeleteAdditionalButton.Click += (s, e) => deleteSelectedAdditional();
            selectedTypeCombo.SelectionChanged += (s, e) =>
            {
                if (syncingBarEditor) return;
                RebarBarType type = (selectedTypeCombo.SelectedItem as BarChoice)?.Type;
                if (type == null) return;
                BarGeometry bar = CurrentGeometry(activePreviewRow);
                bar.Type = activePreviewRow.Type = type; activePreviewRow.QuickTypeChanged = true;
                foreach (var member in activePreviewRow.Bars) member.Type = type;
                SyncQuickControls();
                CopyGeometry(activePreviewRow, bar); redraw?.Invoke();
            };
            selectedCountCombo.SelectionChanged += (s, e) =>
            {
                if (syncingBarEditor) return;
                activePreviewRow.Count = CountValue(selectedCountCombo);
                if (!activePreviewRow.HasMultipleIntervals) ResizeBarGeometries(activePreviewRow, activePreviewRow.Count);
                SyncQuickControls();
                CurrentGeometry(activePreviewRow); syncSelectedBarEditor(); redraw?.Invoke();
            };
            selectedStartBox.TextChanged += (s, e) =>
            {
                if (syncingBarEditor) return;
                BarGeometry bar = CurrentGeometry(activePreviewRow);
                bar.StartMm = Math.Max(0, Math.Min(beamLengthMm - 25, Number(selectedStartBox.Text, bar.StartMm)));
                if (bar.StartMm >= bar.EndMm) bar.StartMm = Math.Max(0, bar.EndMm - 25);
                CopyGeometry(activePreviewRow, bar); redraw?.Invoke();
            };
            selectedEndBox.TextChanged += (s, e) =>
            {
                if (syncingBarEditor) return;
                BarGeometry bar = CurrentGeometry(activePreviewRow);
                bar.EndMm = Math.Max(bar.StartMm + 25, Math.Min(beamLengthMm, Number(selectedEndBox.Text, bar.EndMm)));
                CopyGeometry(activePreviewRow, bar); redraw?.Invoke();
            };
            selectedLeftHookCombo.SelectionChanged += (s, e) =>
            {
                if (syncingBarEditor) return;
                BarGeometry bar = CurrentGeometry(activePreviewRow); bar.StartHook = (selectedLeftHookCombo.SelectedItem as HookChoice)?.Type;
                CopyGeometry(activePreviewRow, bar); redraw?.Invoke();
            };
            selectedRightHookCombo.SelectionChanged += (s, e) =>
            {
                if (syncingBarEditor) return;
                BarGeometry bar = CurrentGeometry(activePreviewRow); bar.EndHook = (selectedRightHookCombo.SelectedItem as HookChoice)?.Type;
                CopyGeometry(activePreviewRow, bar); redraw?.Invoke();
            };
            selectedLeftXBox.TextChanged += (s, e) =>
            {
                if (syncingBarEditor) return;
                BarGeometry bar = CurrentGeometry(activePreviewRow); bar.LeftHookMm = Math.Max(0, Number(selectedLeftXBox.Text, bar.LeftHookMm));
                CopyGeometry(activePreviewRow, bar); redraw?.Invoke();
            };
            selectedRightXBox.TextChanged += (s, e) =>
            {
                if (syncingBarEditor) return;
                BarGeometry bar = CurrentGeometry(activePreviewRow); bar.RightHookMm = Math.Max(0, Number(selectedRightXBox.Text, bar.RightHookMm));
                CopyGeometry(activePreviewRow, bar); redraw?.Invoke();
            };
            selectedPositionBox.TextChanged += (s, e) =>
            {
                if (syncingBarEditor) return;
                BarGeometry bar = CurrentGeometry(activePreviewRow); bar.Position = Math.Max(0, Math.Min(4, Number(selectedPositionBox.Text, bar.Position)));
                CopyGeometry(activePreviewRow, bar); redraw?.Invoke();
            };
            selectedHookDownBox.TextChanged += (s, e) =>
            {
                if (syncingBarEditor) return;
                BarGeometry bar = CurrentGeometry(activePreviewRow);
                double length = Math.Max(0, Number(selectedHookDownBox.Text, bar.HookDownMm));
                bar.HookDownMm = length;
                CopyGeometry(activePreviewRow, bar); redraw?.Invoke();
            };
            syncSelectedBarEditor();
            uniformRadio.Checked += (s, e) => redraw(); zonedRadio.Checked += (s, e) => redraw();
            tieCombo.SelectionChanged += (s, e) => { tieRow.Type = (tieCombo.SelectedItem as BarChoice)?.Type ?? tieRow.Type; redraw(); };
            spanSelector.SelectionChanged += (s, e) =>
            {
                int selected = spanSelector.SelectedIndex;
                if (selected <= 0)
                {
                    highlightStartMm = highlightEndMm = -1;
                    spanStatus.Text = "Đang xem toàn dầm · thông số thanh theo chiều dài dầm";
                }
                else
                {
                    Tuple<double, double> range = previewSpans[Math.Min(selected - 1, previewSpans.Count - 1)];
                    highlightStartMm = range.Item1; highlightEndMm = range.Item2;
                    spanStatus.Text = "Đang xem Nhịp " + selected + " · " + (range.Item2 - range.Item1).ToString("0") + " mm";
                }
                redraw?.Invoke(); FitDiagram(selected > 0);
                footerStatus.Text = spanStatus.Text + " · Kéo tay nắm thanh để chỉnh neo.";
            };
            barLayerSelector.SelectionChanged += (s, e) =>
            {
                if (refreshingLayerChoices) return;
                activePreviewRow = barLayerSelector.SelectedItem as BarRow ?? rows.First(r => r.Key == "top");
                CurrentGeometry(activePreviewRow);
                syncSelectedBarEditor();
                footerStatus.Text = activePreviewRow.Caption + " · kéo tay nắm hai đầu để chỉnh chiều dài.";
                redraw?.Invoke();
            };
            fitAllButton.Click += (s, e) => { spanSelector.SelectedIndex = 0; FitDiagram(false); };
            fitSpanButton.Click += (s, e) => FitDiagram(spanSelector.SelectedIndex > 0);
            zoomOutButton.Click += (s, e) => ZoomDiagram(new Point(beamCanvas.ActualWidth / 2, beamCanvas.ActualHeight / 2), 1.0 / 1.25);
            zoomInButton.Click += (s, e) => ZoomDiagram(new Point(beamCanvas.ActualWidth / 2, beamCanvas.ActualHeight / 2), 1.25);
            beamCanvas.PreviewMouseWheel += (s, e) =>
            {
                ZoomDiagram(e.GetPosition(beamCanvas), e.Delta > 0 ? 1.15 : 1.0 / 1.15);
                e.Handled = true;
            };
            beamCanvas.MouseLeftButtonDown += (s, e) =>
            {
                Point point = e.GetPosition(beamCanvas);
                ActiveBarHandles(out double startX, out double endX, out double handleY, out BarGeometry selectedBar);
                if (Math.Abs(point.X - startX) <= 15 && Math.Abs(point.Y - handleY) <= 8)
                {
                    draggingStart = true; beamCanvas.CaptureMouse(); e.Handled = true; return;
                }
                if (Math.Abs(point.X - endX) <= 15 && Math.Abs(point.Y - handleY) <= 8)
                {
                    draggingEnd = true; beamCanvas.CaptureMouse(); e.Handled = true; return;
                }
                if (TrySelectBarAt(point)) { e.Handled = true; return; }
                if (diagramZoom > 1.05)
                {
                    panningDiagram = true; panStart = point; panStartX = diagramOffsetX; panStartY = diagramOffsetY;
                    beamCanvas.Cursor = Cursors.SizeAll; beamCanvas.CaptureMouse(); e.Handled = true;
                }
            };
            beamCanvas.MouseMove += (s, e) =>
            {
                Point point = e.GetPosition(beamCanvas);
                if (draggingStart || draggingEnd)
                {
                    ActiveBarHandles(out double oldStartX, out double oldEndX, out double handleY, out BarGeometry selectedBar);
                    double width = Math.Max(500, beamCanvas.ActualWidth), left = 68, right = width - 68;
                    double mm = Math.Max(0, Math.Min(beamLengthMm, (point.X - left) / Math.Max(1, right - left) * beamLengthMm));
                    if (draggingStart) selectedBar.StartMm = Math.Min(mm, selectedBar.EndMm - 25);
                    else selectedBar.EndMm = Math.Max(mm, selectedBar.StartMm + 25);
                    selectedBar.StartMm = Math.Max(0, selectedBar.StartMm);
                    selectedBar.EndMm = Math.Min(beamLengthMm, selectedBar.EndMm);
                    CopyGeometry(activePreviewRow, selectedBar);
                    syncSelectedBarEditor();
                    footerStatus.Text = activePreviewRow.Caption + " · đầu trái: " + selectedBar.StartMm.ToString("0") +
                        " mm · đầu phải: " + selectedBar.EndMm.ToString("0") + " mm · chiều dài: " +
                        (selectedBar.EndMm - selectedBar.StartMm).ToString("0") + " mm";
                    redraw?.Invoke(); e.Handled = true; return;
                }
                if (panningDiagram)
                {
                    diagramOffsetX = panStartX + point.X - panStart.X;
                    diagramOffsetY = panStartY + point.Y - panStart.Y;
                    ApplyDiagramTransform(); e.Handled = true;
                }
            };
            beamCanvas.MouseLeftButtonUp += (s, e) =>
            {
                draggingStart = draggingEnd = panningDiagram = false;
                beamCanvas.Cursor = Cursors.Hand;
                beamCanvas.ReleaseMouseCapture();
            };
            foreach (TextBox field in new[] { tieSpacingBox, zoneLengthBox, endSpacingBox, middleSpacingBox, coverBox, layerGapBox })
            { field.TextChanged += (s, e) => redraw(); }
window.Loaded += (s, e) => { redraw(); FitDiagram(spanSelector.SelectedIndex > 0); };
            window.SizeChanged += (s, e) => { redraw(); FitDiagram(spanSelector.SelectedIndex > 0); };
            previewTabs.SelectionChanged += (s, e) => { if (window.IsLoaded) redraw(); };            closeButton.Click += (s, e) => window.DialogResult = false;
            ReinforceStraightBeamsCommand.Options quickResult = null;
            var saveSelectionButton = Button("Lưu lựa chọn hiện tại", 200);
            saveSelectionButton.IsEnabled = saveCurrent != null;
            supportPanel.Children.Add(saveSelectionButton);
            saveSelectionButton.Click += (s, e) =>
            {
                var current = QuickOptions(rows, defaults, coverBox, layerGapBox, tieSpacingBox, zoneLengthBox,
                    endSpacingBox, middleSpacingBox, zonedRadio.IsChecked == true);
                current.DeletedReinforcementRanges = deletedRanges.ToList();
                try { saveCurrent(current); footerStatus.Text = "Đã lưu lựa chọn và các nhóm còn lại cho lần vẽ kế tiếp."; }
                catch (Exception error) { MessageBox.Show(window, error.Message, "KStruct - Lưu lựa chọn"); }
            };
            createButton.Click += (s, e) =>
            {
if (!ValidateQuick(rows, coverBox, layerGapBox, tieSpacingBox, zoneLengthBox, endSpacingBox, middleSpacingBox, zonedRadio.IsChecked == true, window)) return;
                quickResult = QuickOptions(rows, defaults, coverBox, layerGapBox, tieSpacingBox, zoneLengthBox, endSpacingBox, middleSpacingBox, zonedRadio.IsChecked == true);
                quickResult.DeletedReinforcementRanges = deletedRanges.ToList();
                foreach (var entry in supportInputs)
                {
                    var row = rows.First(r => r.Key == entry.Key);
                    if (useSupportSpanPreset) continue;
                    if (row.Count <= 0) continue;
                    string leftText = entry.Value.Item1.Text.Trim(), rightText = entry.Value.Item2.Text.Trim();
                    if (leftText.Length == 0 && rightText.Length == 0) continue;
                    if (!TryNumber(leftText, out double leftLength) || !TryNumber(rightText, out double rightLength) ||
                        leftLength < 0 || rightLength < 0 || leftLength + rightLength > beamLengthMm)
                    {
                        MessageBox.Show(window, "Nhập chiều dài cho cả hai gối. Tổng chiều dài không được vượt chiều dài dầm.", "KStruct - Thép dầm");
                        quickResult = null;
                        return;
                    }
                    quickResult.DetailedBars.RemoveAll(bar => bar.Key == row.Key);
                    foreach (var range in new[] { Tuple.Create(0.0, leftLength), Tuple.Create(beamLengthMm - rightLength, beamLengthMm) })
                        if (range.Item2 > range.Item1)
                            quickResult.DetailedBars.Add(new ReinforceStraightBeamsCommand.DetailedBarSpec
                            {
                                Key = row.Key, Count = row.Count, Type = row.Type, IsTop = true,
                                StartMm = range.Item1, EndMm = range.Item2, SectionPosition = row.Position
                            });
                    if (leftLength == 0 && rightLength == 0)
                    {
                        if (row.Key == "top1") quickResult.TopLayer2Count = 0;
                        else quickResult.TopLayer3Count = 0;
                    }
                }
                if (useSupportSpanPreset)
                {
                    quickResult.DetailedBars.RemoveAll(bar => bar.Key == "top1" || bar.Key == "bottom1");
                    AddSupportSpanPresetSpecs(quickResult, rows, previewSpans);
                }
                bool hasReinforcement = rows.Any(r => (r.Key == "top1" || r.Key == "top2" || r.Key == "bottom1" || r.Key == "bottom2") && r.Count > 0);
                if (hasReinforcement && quickResult.TcvnCutoffEnabled &&
                    (quickResult.TcvnConcreteRbtMpa <= 0 || quickResult.TcvnSteelRsMpa <= 0 || quickResult.TcvnBondEta1 <= 0 || quickResult.TcvnAreaRatio <= 0 || quickResult.TcvnAlpha <= 0))
                {
                    MessageBox.Show(window, "Thép gia cường sẽ được chia tại ranh giới nhịp. Để kéo dài neo TCVN, hãy vào Chỉnh chi tiết > TCVN và nhập đủ thông số; hoặc tắt chế độ kéo dài neo TCVN.", "KStruct - Chia thép gia cường", MessageBoxButton.OK, MessageBoxImage.Warning);
                    quickResult = null;
                    return;
                }
                window.DialogResult = true;
            };
            Action<int, int> applySupportPreset = (supportDivisor, spanDivisor) =>
            {
                foreach (string key in new[] { "top1", "bottom1" })
                {
                    BarRow row = rows.First(r => r.Key == key);
                    row.HasMultipleIntervals = true;
                    var toggle = rowToggles[key];
                    toggle.IsChecked = true;
                    row.Count = CountValue(quickCountCombos[key]);
                }
                useSupportSpanPreset = true;
                ApplySupportSpanPreset(rows, previewSpans, previewSupports, supportDivisor, spanDivisor);
                foreach (BarRow row in rows.Where(row => row.Key == "top1" || row.Key == "bottom1"))
                {
                    row.Bars.RemoveAll(bar => deletedRanges.Contains(RangeKey(row, bar)));
                    if (row.Bars.Count == 0) row.Count = 0;
                    else { row.SelectedBarIndex = 0; CopyGeometry(row, row.Bars[0]); }
                }
                refreshLayerChoices();
                syncSelectedBarEditor();
                redraw?.Invoke();
                footerStatus.Text = "Đã bố trí gối 1/" + supportDivisor + " và nhịp 1/" + spanDivisor + ", giữ số thanh và đường kính đã chọn.";
            };
            supportSpanPresetButton.Click += (s, e) => applySupportPreset(4, 5);
            thirdQuarterPresetButton.Click += (s, e) => applySupportPreset(3, 4);
            detailsButton.Click += (s, e) =>
            {
                // Let users enter the detailed editor even while a quick-screen field is
                // temporarily invalid. Validate when they apply/create instead of blocking
                // navigation to the screen where those values can be corrected.
                var current = QuickOptions(rows, defaults, coverBox, layerGapBox, tieSpacingBox, zoneLengthBox, endSpacingBox, middleSpacingBox, zonedRadio.IsChecked == true);
                var detailed = Show(owner, types, hookTypes, current, settingsOnly, titleOverride);
                if (detailed != null) { quickResult = detailed; window.DialogResult = true; }
            };
            window.ShowDialog();
            return quickResult;
        }

        private static bool ValidateQuick(List<BarRow> rows, TextBox cover, TextBox gap, TextBox tieSpacing,
            TextBox zoneLength, TextBox endSpacing, TextBox middleSpacing, bool zoned, Window owner)
        {
            double value;
            if (rows.Any(r => r.Type == null) || rows.First(r => r.Key == "top").Count < 1 || rows.First(r => r.Key == "bottom").Count < 1 ||
                !TryNumber(cover.Text, out value) || value <= 0 || !TryNumber(gap.Text, out value) || value <= 0 ||
                !TryNumber(tieSpacing.Text, out value) || value <= 0 ||
                (zoned && (!TryNumber(zoneLength.Text, out value) || value <= 0 || !TryNumber(endSpacing.Text, out value) || value <= 0 || !TryNumber(middleSpacing.Text, out value) || value <= 0)))
            {
                MessageBox.Show(owner, "Kiểm tra số thanh, đường kính, lớp bảo vệ và bước đai trước khi tiếp tục.", "KStruct - Thép dầm", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            return true;
        }

        private static ReinforceStraightBeamsCommand.Options QuickOptions(List<BarRow> rows,
            ReinforceStraightBeamsCommand.Options defaults, TextBox cover, TextBox gap, TextBox tieSpacing,
            TextBox zoneLength, TextBox endSpacing, TextBox middleSpacing, bool zoned)
        {
            BarRow top = rows.First(r => r.Key == "top"), bottom = rows.First(r => r.Key == "bottom");
            BarRow top1 = rows.First(r => r.Key == "top1"), top2 = rows.First(r => r.Key == "top2");
            BarRow bottom1 = rows.First(r => r.Key == "bottom1"), bottom2 = rows.First(r => r.Key == "bottom2");
            return new ReinforceStraightBeamsCommand.Options
            {
                MainType = top.Type, TopType = top.Type, BottomType = bottom.Type,
                TopCount = top.Count, BottomCount = bottom.Count,
                TopLayer2Count = top1.Count, TopLayer2Type = top1.Type, TopLayer3Count = top2.Count, TopLayer3Type = top2.Type,
                BottomLayer2Count = bottom1.Count, BottomLayer2Type = bottom1.Type, BottomLayer3Count = bottom2.Count, BottomLayer3Type = bottom2.Type,
                SideBarCount = rows.First(r => r.Key == "side").Count, SideBarType = rows.First(r => r.Key == "side").Type,
                TieType = rows.First(r => r.Key == "tie").Type,
                CoverMm = Number(cover.Text, 30), LongitudinalLayerSpacingMm = Number(gap.Text, 30), TieSpacingMm = Number(tieSpacing.Text, 150),
                UseZonedTies = zoned, TieEndZoneLengthMm = Number(zoneLength.Text, 1000), TieEndSpacingMm = Number(endSpacing.Text, 100), TieMiddleSpacingMm = Number(middleSpacing.Text, 200),
                TopStartHook = defaults?.TopStartHook, TopEndHook = defaults?.TopEndHook, BottomStartHook = defaults?.BottomStartHook, BottomEndHook = defaults?.BottomEndHook,
                PreviewBeamLengthMm = defaults?.PreviewBeamLengthMm ?? 0, PreviewBeamWidthMm = defaults?.PreviewBeamWidthMm ?? 0,
                PreviewBeamHeightMm = defaults?.PreviewBeamHeightMm ?? 0, PreviewSupports = defaults?.PreviewSupports ?? new List<ReinforceStraightBeamsCommand.BeamPreviewSupport>(),
                PreviewBeamSections = defaults?.PreviewBeamSections ?? new List<ReinforceStraightBeamsCommand.BeamPreviewSection>(),
                DetailedBars = ToDetailedSpecs(rows, defaults?.TopStartHook, defaults?.TopEndHook, defaults?.BottomStartHook, defaults?.BottomEndHook, true),
                StirrupSupportOffsetMm = defaults?.StirrupSupportOffsetMm > 0 ? defaults.StirrupSupportOffsetMm : 50,
                    TcvnCutoffEnabled = defaults?.TcvnCutoffEnabled ?? true,
                    TcvnConcreteRbtMpa = defaults?.TcvnConcreteRbtMpa ?? 0,
                    TcvnSteelRsMpa = defaults?.TcvnSteelRsMpa ?? 0,
                    TcvnBondEta1 = defaults?.TcvnBondEta1 > 0 ? defaults.TcvnBondEta1 : 2.5,
                    TcvnAreaRatio = defaults?.TcvnAreaRatio > 0 ? defaults.TcvnAreaRatio : 1.0,
                    TcvnAlpha = defaults?.TcvnAlpha > 0 ? defaults.TcvnAlpha : 1.0,
                SideBarMinimumHeightMm = defaults?.SideBarMinimumHeightMm > 0 ? defaults.SideBarMinimumHeightMm : 700,
                SideBarSpacingMm = defaults?.SideBarSpacingMm > 0 ? defaults.SideBarSpacingMm : 400,
                SideBarAnchorageMm = defaults?.SideBarAnchorageMm > 0 ? defaults.SideBarAnchorageMm : 100
            };
        }

        private static void AddQuickCell(Grid grid, UIElement element, int row, int column, int rowSpan = 1, int columnSpan = 1)
        {
            Grid.SetRow(element, row); Grid.SetColumn(element, column);
            if (rowSpan > 1) Grid.SetRowSpan(element, rowSpan);
            if (columnSpan > 1) Grid.SetColumnSpan(element, columnSpan);
            grid.Children.Add(element);
        }

        private static void DrawSectionGallery(Canvas canvas, IList<BarRow> rows, IList<double> stations,
            double widthMm, double heightMm, IList<ReinforceStraightBeamsCommand.BeamPreviewSection> beamSections = null)
        {
            canvas.Children.Clear();
            if (stations == null || stations.Count == 0) return;
            int columns = Math.Max(1, (int)(Math.Max(360, canvas.ActualWidth) / 360));
            canvas.Height = Math.Ceiling(stations.Count / (double)columns) * 390;
            for (int index = 0; index < stations.Count; index++)
            {
                double station = stations[index];
                var actualSection = (beamSections ?? new List<ReinforceStraightBeamsCommand.BeamPreviewSection>())
                    .FirstOrDefault(section => station >= section.StartMm && station <= section.EndMm)
                    ?? beamSections?.OrderBy(section => Math.Abs((section.StartMm + section.EndMm) / 2 - station)).FirstOrDefault();
                double sectionWidthMm = actualSection?.WidthMm > 0 ? actualSection.WidthMm : widthMm;
                double sectionHeightMm = actualSection?.HeightMm > 0 ? actualSection.HeightMm : heightMm;
                var profile = new List<BarRow>();
                foreach (BarRow row in rows)
                {
                    var crossing = row.Bars.Where(bar => bar.StartMm <= station && bar.EndMm >= station).ToList();
                    int count = row.Key == "tie" || row.Key == "side" ? row.Count :
                        row.HasMultipleIntervals ? (crossing.Count > 0 ? row.Count : 0) : crossing.Count;
                    profile.Add(new BarRow { Key = row.Key, Caption = row.Caption, Count = count,
                        Type = crossing.FirstOrDefault()?.Type ?? row.Type });
                }
                var diagram = new Canvas { Width = 170, Height = 280, Background = Brushes.White };
                DrawQuickSection(diagram, profile, sectionWidthMm, sectionHeightMm);
                var card = new Canvas { Width = 350, Height = 380, Background = Brushes.White };
                Canvas.SetTop(diagram, 40); Canvas.SetLeft(diagram, 15); card.Children.Add(diagram);
                AddCanvasText(card, actualSection?.Label ?? "MẶT CẮT " + (index + 1) + "-" + (index + 1), 28, 8, Blue, 12);
                AddCanvasText(card, "Vị trí " + station.ToString("0") + " mm", 28, 26, Brushes.SlateGray, 10);
                DrawDimension(card, 54, 146, 342, sectionWidthMm.ToString("0") + " mm", 0);
                DrawVerticalDimension(card, 35, 49, 311, sectionHeightMm.ToString("0") + " mm");
                int tagIndex = 0;
                foreach (BarRow row in profile.Where(row => row.Count > 0))
                {
                    string label = row.Key == "tie" ? "Đai " + ShortType(row.Type) :
                        row.Caption + " · " + row.Count + ShortType(row.Type);
                    AddCanvasText(card, label, 180, 55 + tagIndex * 35, row.Key.StartsWith("top") ? Blue : Teal, 10);
                    card.Children.Add(new Line { X1 = 146, X2 = 177, Y1 = 65 + tagIndex * 35,
                        Y2 = 65 + tagIndex * 35, Stroke = Brushes.SlateGray, StrokeThickness = 0.7 });
                    tagIndex++;
                }
                Canvas.SetLeft(card, (index % columns) * 360);
                Canvas.SetTop(card, (index / columns) * 390);
                canvas.Children.Add(card);
            }
        }

        private static void DrawQuickSection(Canvas canvas, IList<BarRow> rows, double widthMm = 200, double heightMm = 400)
        {
            canvas.Children.Clear();
            double h = Math.Max(180, double.IsNaN(canvas.Height) ? canvas.ActualHeight : canvas.Height);
            double w = Math.Max(120, double.IsNaN(canvas.Width) ? canvas.ActualWidth : canvas.Width);
            double ratio = Math.Max(0.2, Math.Min(3.0, widthMm / Math.Max(1, heightMm)));
            double bh = h - 18, bw = Math.Min(w - 36, bh * ratio);
            double x = (w - bw) / 2.0, y = 9;
            var body = new Rectangle { Width = bw, Height = bh, Fill = new SolidColorBrush(Color.FromRgb(235, 241, 247)), Stroke = new SolidColorBrush(Color.FromRgb(211, 139, 36)), StrokeThickness = 2, RadiusX = 8, RadiusY = 8 };
            Canvas.SetLeft(body, x); Canvas.SetTop(body, y); canvas.Children.Add(body);
            var tie = new Rectangle { Width = bw - 18, Height = bh - 18, Stroke = new SolidColorBrush(Color.FromRgb(187, 128, 35)), StrokeThickness = 1.5 };
            Canvas.SetLeft(tie, x + 9); Canvas.SetTop(tie, y + 9); canvas.Children.Add(tie);
            DrawQuickLayer(canvas, rows.First(r => r.Key == "top"), x + 13, y + 13, bw - 26, Blue);
            DrawQuickLayer(canvas, rows.First(r => r.Key == "top1"), x + 13, y + bh * 0.24, bw - 26, MutedBlue);
            DrawQuickLayer(canvas, rows.First(r => r.Key == "top2"), x + 13, y + bh * 0.35, bw - 26, MutedBlue);
            DrawQuickLayer(canvas, rows.First(r => r.Key == "bottom2"), x + 13, y + bh * 0.65, bw - 26, MutedBlue);
            DrawQuickLayer(canvas, rows.First(r => r.Key == "bottom1"), x + 13, y + bh * 0.76, bw - 26, Teal);
            DrawQuickLayer(canvas, rows.First(r => r.Key == "bottom"), x + 13, y + bh - 13, bw - 26, Teal);
        }

        private static void DrawQuickLayer(Canvas canvas, BarRow row, double x, double y, double width, Brush color)
        {
            int count = Math.Max(0, Math.Min(12, row.Count)); if (count == 0) return;
            double diameterMm = row.Type == null ? 16 : UnitUtils.ConvertFromInternalUnits(row.Type.BarNominalDiameter, UnitTypeId.Millimeters);
            double diameter = Math.Max(6, Math.Min(12, 5 + diameterMm * 0.25));
            int cols = Math.Min(count, 6); int lineCount = (int)Math.Ceiling(count / (double)cols);
            for (int i = 0; i < count; i++)
            {
                int rr = i / cols, cc = i % cols;
                double px = x + (cols == 1 ? width / 2 : width * cc / (cols - 1.0));
                var dot = new Ellipse { Width = diameter, Height = diameter, Fill = color, Stroke = Brushes.White, StrokeThickness = 1 };
                Canvas.SetLeft(dot, px - diameter / 2); Canvas.SetTop(dot, y + (rr - (lineCount - 1) / 2.0) * (diameter + 2) - diameter / 2); canvas.Children.Add(dot);
            }
        }

        private static void DrawQuickTies(Canvas uniform, Canvas zoned, bool useZones, double a1, double a2, double length, double spacing)
        {
            DrawTiePreview(uniform, false, spacing, spacing, length, spacing);
            DrawTiePreview(zoned, useZones, a1, a2, length, spacing);
        }

        private static void DrawTiePreview(Canvas canvas, bool zoned, double endSpacing, double midSpacing, double length, double uniformSpacing)
        {
            canvas.Children.Clear(); double w = Math.Max(140, canvas.ActualWidth), h = Math.Max(46, canvas.ActualHeight);
            double left = 16, right = w - 16, top = 10, bottom = h - 10;
            var beam = new Rectangle { Width = right - left, Height = bottom - top, Fill = new SolidColorBrush(Color.FromRgb(235, 242, 251)), Stroke = new SolidColorBrush(Color.FromRgb(188, 201, 220)), StrokeThickness = 1 };
            Canvas.SetLeft(beam, left); Canvas.SetTop(beam, top); canvas.Children.Add(beam);
            double span = 4000;
            double zoneLength = Math.Max(0, Math.Min(span / 2, length));
            var positions = new List<double>();
            if (!zoned)
            {
                AddRepresentativeStations(positions, 0, span, 9);
            }
            else
            {
                AddRepresentativeStations(positions, 0, zoneLength, 3);
                AddRepresentativeStations(positions, zoneLength, span - zoneLength, 3);
                AddRepresentativeStations(positions, span - zoneLength, span, 3);
            }
            double denseEnd = zoneLength;
            foreach (double station in positions.Distinct().OrderBy(x => x))
            {
                double x = left + 8 + (right - left - 16) * station / span;
                bool dense = !zoned || station <= denseEnd || station >= span - denseEnd;
                var line = new Line { X1 = x, X2 = x, Y1 = top + 4, Y2 = bottom - 4, Stroke = dense ? new SolidColorBrush(Color.FromRgb(207, 128, 46)) : new SolidColorBrush(Color.FromRgb(224, 169, 99)), StrokeThickness = dense ? 1.15 : 0.75 };
                canvas.Children.Add(line);
            }
        }

        private static void AddRepresentativeStations(List<double> stations, double start, double end, int count)
        {
            if (stations == null || end < start || count <= 0) return;
            if (count == 1)
            {
                stations.Add((start + end) * 0.5);
                return;
            }
            for (int i = 0; i < count; i++)
                stations.Add(start + (end - start) * i / (count - 1.0));
        }

        private static void AddPreviewStations(List<double> stations, double start, double end, double maxSpacing)
        {
            if (end < start) return;
            int intervals = Math.Max(1, Math.Min(48, (int)Math.Ceiling((end - start) / Math.Max(1, maxSpacing))));
            for (int i = 0; i <= intervals; i++) stations.Add(start + (end - start) * i / intervals);
        }

        private static List<BarRow> BuildRows(IList<RebarBarType> types, ReinforceStraightBeamsCommand.Options defaults)
        {
            RebarBarType first = types.FirstOrDefault();
            double previewLength = defaults?.PreviewBeamLengthMm > 0 ? defaults.PreviewBeamLengthMm : 4000.0;
            double defaultHookDownMm = defaults?.PreviewBeamHeightMm > 0 ? Math.Max(50, Math.Min(150, defaults.PreviewBeamHeightMm * 0.35)) : 150;
            ReinforceStraightBeamsCommand.DetailedBarSpec Spec(string key) => defaults?.DetailedBars?.FirstOrDefault(x => x.Key == key);
            var rows = new List<BarRow>
            {
                new BarRow { Key="top1", Caption="Thép tăng cường trên · lớp 1", Count=defaults?.TopLayer2Count ?? 3, Type=defaults?.TopLayer2Type ?? first, StartMm=Spec("top1")?.StartMm ?? 0, EndMm=Spec("top1")?.EndMm ?? previewLength, Position=Spec("top1")?.SectionPosition ?? 1, Optional=true },
                new BarRow { Key="top", Caption="Thép chủ trên", Count=defaults?.TopCount ?? 3, Type=defaults?.TopType ?? first, StartMm=Spec("top")?.StartMm ?? 0, EndMm=Spec("top")?.EndMm ?? previewLength, Position=Spec("top")?.SectionPosition ?? 1, HookDownMm=Spec("top")?.HookDownMm > 0 ? Spec("top").HookDownMm : defaultHookDownMm },
                new BarRow { Key="top2", Caption="Thép tăng cường trên · lớp 2", Count=defaults?.TopLayer3Count ?? 0, Type=defaults?.TopLayer3Type ?? first, StartMm=Spec("top2")?.StartMm ?? 0, EndMm=Spec("top2")?.EndMm ?? previewLength, Position=Spec("top2")?.SectionPosition ?? 1, Optional=true },
                new BarRow { Key="bottom2", Caption="Thép tăng cường dưới · lớp 2", Count=defaults?.BottomLayer3Count ?? 0, Type=defaults?.BottomLayer3Type ?? first, StartMm=Spec("bottom2")?.StartMm ?? 0, EndMm=Spec("bottom2")?.EndMm ?? previewLength, Position=Spec("bottom2")?.SectionPosition ?? 1, Optional=true },
                new BarRow { Key="bottom", Caption="Thép chủ dưới", Count=defaults?.BottomCount ?? 3, Type=defaults?.BottomType ?? first, StartMm=Spec("bottom")?.StartMm ?? 0, EndMm=Spec("bottom")?.EndMm ?? previewLength, Position=Spec("bottom")?.SectionPosition ?? 1 },
                new BarRow { Key="bottom1", Caption="Thép tăng cường dưới · lớp 1", Count=defaults?.BottomLayer2Count ?? 3, Type=defaults?.BottomLayer2Type ?? first, StartMm=Spec("bottom1")?.StartMm ?? 0, EndMm=Spec("bottom1")?.EndMm ?? previewLength, Position=Spec("bottom1")?.SectionPosition ?? 1, Optional=true },
                new BarRow { Key="tie", Caption="Thép đai", Count=0, Type=defaults?.TieType ?? first, EndMm=previewLength },
                new BarRow { Key="side", Caption="Thép chống phình", Count=defaults?.SideBarCount ?? 0, Type=defaults?.SideBarType ?? first, EndMm=previewLength, Optional=true }
            };
            foreach (BarRow row in rows.Where(r => r.Key != "tie" && r.Key != "side"))
            {
                var specs = defaults?.DetailedBars?.Where(x => x.Key == row.Key).ToList() ?? new List<ReinforceStraightBeamsCommand.DetailedBarSpec>();
                if (row.Optional && specs.Any(spec => spec.BarIndex < 0 && spec.Count > 0))
                {
                    row.HasMultipleIntervals = true;
                    row.Count = specs.Max(spec => spec.Count);
                    foreach (var spec in specs)
                        row.Bars.Add(new BarGeometry { Type = spec.Type ?? row.Type, StartMm = spec.StartMm, EndMm = spec.EndMm,
                            Position = spec.SectionPosition, LeftHookMm = spec.LeftHookMm, RightHookMm = spec.RightHookMm,
                            HookDownMm = spec.HookDownMm, StartHook = spec.StartHook, EndHook = spec.EndHook,
                            StartPointIndex = spec.StartPointIndex, EndPointIndex = spec.EndPointIndex,
                            LeftRatio = spec.LeftRatio, RightRatio = spec.RightRatio });
                    CopyGeometry(row, row.Bars[0]);
                    continue;
                }
                if (specs.Count == 0)
                {
                    if (!row.Optional || row.Count > 0)
                        for (int i = 0; i < Math.Max(1, row.Count); i++) row.Bars.Add(new BarGeometry { Type = row.Type, StartMm = row.StartMm, EndMm = row.EndMm, Position = row.Position, HookDownMm = row.HookDownMm, StartHook = row.Key.StartsWith("top") ? defaults?.TopStartHook : defaults?.BottomStartHook, EndHook = row.Key.StartsWith("top") ? defaults?.TopEndHook : defaults?.BottomEndHook });
                }
                else
                    foreach (var spec in specs)
                        for (int i = 0; i < Math.Max(1, spec.Count); i++) row.Bars.Add(new BarGeometry { Type = spec.Type ?? row.Type, StartMm = spec.StartMm, EndMm = spec.EndMm, Position = spec.SectionPosition,
                            LeftHookMm=spec.LeftHookMm, RightHookMm=spec.RightHookMm, HookDownMm=spec.HookDownMm > 0 ? spec.HookDownMm : (row.Key == "top" ? row.HookDownMm : 0),
                            StartPointIndex=spec.StartPointIndex, EndPointIndex=spec.EndPointIndex, LeftRatio=spec.LeftRatio, RightRatio=spec.RightRatio,
                            StartHook = spec.StartHook, EndHook = spec.EndHook });
                if (row.Bars.Count > 0 || !row.Optional) row.Count = row.Bars.Count;
                if (row.Bars.Count > 0) CopyGeometry(row, row.Bars[0]);
            }
            return rows;
        }

        private static void NormalizeRanges(IEnumerable<BarRow> rows, double beamLengthMm)
        {
            foreach (BarRow row in rows.Where(r => r.Key != "tie" && r.Key != "side"))
            {
                bool main = row.Key == "top" || row.Key == "bottom";
                foreach (BarGeometry bar in row.Bars)
                {
                    if (main)
                    {
                        bar.StartMm = 0;
                        bar.EndMm = beamLengthMm;
                    }
                    else
                    {
                        bar.StartMm = Math.Max(0, Math.Min(beamLengthMm, bar.StartMm));
                        bar.EndMm = Math.Max(0, Math.Min(beamLengthMm, bar.EndMm));
                        if (bar.EndMm - bar.StartMm < Math.Min(25, beamLengthMm))
                        {
                            bar.StartMm = 0;
                            bar.EndMm = beamLengthMm;
                        }
                    }
                }
                if (row.Bars.Count > 0) CopyGeometry(row, row.Bars[0]);
            }
        }

        private static void CopyGeometry(BarRow row, BarGeometry bar)
        {
            if (!row.HasMultipleIntervals)
            {
                foreach (BarGeometry member in row.Bars.Where(member => member != bar))
                {
                    member.Type = bar.Type;
                    member.StartMm = bar.StartMm; member.EndMm = bar.EndMm;
                    member.Position = bar.Position;
                    member.LeftHookMm = bar.LeftHookMm; member.RightHookMm = bar.RightHookMm;
                    member.HookDownMm = bar.HookDownMm;
                    member.StartHook = bar.StartHook; member.EndHook = bar.EndHook;
                    member.StartPointIndex = bar.StartPointIndex; member.EndPointIndex = bar.EndPointIndex;
                    member.LeftRatio = bar.LeftRatio; member.RightRatio = bar.RightRatio;
                }
                row.SelectedBarIndex = 0;
            }
            row.Type = bar.Type ?? row.Type; row.StartMm = bar.StartMm; row.EndMm = bar.EndMm;
            row.Position = bar.Position; row.LeftHookMm = bar.LeftHookMm; row.RightHookMm = bar.RightHookMm; row.HookDownMm = bar.HookDownMm;
            row.StartPointIndex = bar.StartPointIndex; row.EndPointIndex = bar.EndPointIndex; row.LeftRatio = bar.LeftRatio; row.RightRatio = bar.RightRatio;
        }

        private static BarGeometry CurrentGeometry(BarRow row)
        {
            if (!row.HasMultipleIntervals) ResizeBarGeometries(row, Math.Max(1,row.Count));
            int index = Math.Max(0, Math.Min(row.Bars.Count - 1, row.SelectedBarIndex));
            row.SelectedBarIndex = index;
            return row.Bars[index];
        }

        private static void ApplySupportSpanPreset(IList<BarRow> rows, IList<Tuple<double, double>> spans,
            IList<ReinforceStraightBeamsCommand.BeamPreviewSupport> supports, int supportDivisor = 4, int spanDivisor = 5)
        {
            BarRow topSupport = rows.First(r => r.Key == "top1");
            BarRow bottomSpan = rows.First(r => r.Key == "bottom1");
            List<Tuple<double, double>> supportRanges = ReinforceStraightBeamsCommand.BuildSupportReinforcementRanges(spans);
            if (supportDivisor != 4 && spans.Count > 0)
            {
                supportRanges.Clear();
                for (int index = 0; index <= spans.Count; index++)
                {
                    double start = index == 0 ? spans[0].Item1 :
                        spans[index - 1].Item2 - (spans[index - 1].Item2 - spans[index - 1].Item1) / supportDivisor;
                    double end = index == spans.Count ? spans[index - 1].Item2 :
                        spans[index].Item1 + (spans[index].Item2 - spans[index].Item1) / supportDivisor;
                    supportRanges.Add(Tuple.Create(start, end));
                }
            }
            var midspanRanges = new List<Tuple<double, double>>();
            foreach (Tuple<double, double> span in spans ?? new List<Tuple<double, double>>())
            {
                double length = span.Item2 - span.Item1;
                if (length < 250) continue;
                double fifth = length / spanDivisor;
                if (length - 2 * fifth > 50)
                    midspanRanges.Add(Tuple.Create(span.Item1 + fifth, span.Item2 - fifth));
            }
            if (spans.Count > 1 && supports != null && supports.Any(s => s.Kind == "Cột giao"))
            {
                bool Supported(double station) => supports.Any(s => s.Kind == "Cột giao" &&
                    station >= s.StartMm - 1 && station <= s.EndMm + 1);
                if (!Supported(spans[0].Item1) && Supported(spans[0].Item2) && supportRanges.Count > 1)
                {
                    supportRanges.RemoveAt(0);
                    supportRanges[0] = Tuple.Create(spans[0].Item1, supportRanges[0].Item2);
                    if (midspanRanges.Count > 0) midspanRanges[0] = spans[0];
                }
                int last = spans.Count - 1;
                if (!Supported(spans[last].Item2) && Supported(spans[last].Item1) && supportRanges.Count > 1)
                {
                    supportRanges.RemoveAt(supportRanges.Count - 1);
                    int endIndex = supportRanges.Count - 1;
                    supportRanges[endIndex] = Tuple.Create(supportRanges[endIndex].Item1, spans[last].Item2);
                    if (midspanRanges.Count > 0) midspanRanges[midspanRanges.Count - 1] = spans[last];
                }
            }
            SetPreviewRanges(topSupport, supportRanges);
            SetPreviewRanges(bottomSpan, midspanRanges);
        }

        private static void SetPreviewRanges(BarRow row, IList<Tuple<double, double>> ranges)
        {
            row.Bars.Clear();
            foreach (Tuple<double, double> range in ranges)
                row.Bars.Add(new BarGeometry { Type = row.Type, StartMm = range.Item1, EndMm = range.Item2, Position = row.Position });
            if (row.Bars.Count > 0) CopyGeometry(row, row.Bars[0]);
            row.HasMultipleIntervals = row.Bars.Count > 1;
        }

        private static void AddSupportSpanPresetSpecs(ReinforceStraightBeamsCommand.Options options, IList<BarRow> rows,
            IList<Tuple<double, double>> spans)
        {
            BarRow topSupport = rows.First(r => r.Key == "top1");
            BarRow bottomSpan = rows.First(r => r.Key == "bottom1");
            void Add(BarRow row, Tuple<double, double> range)
            {
                if (range.Item2 - range.Item1 < 50) return;
                options.DetailedBars.Add(new ReinforceStraightBeamsCommand.DetailedBarSpec
                {
                    Key = row.Key, Count = row.Count, BarIndex = -1, BarGroupCount = row.Count,
                    Type = row.Type, IsTop = row.Key == "top1", StartMm = range.Item1, EndMm = range.Item2,
                    SectionPosition = row.Position,
                    HookDownMm = row.Bars.FirstOrDefault(bar => Math.Abs(bar.StartMm - range.Item1) < 1 &&
                        Math.Abs(bar.EndMm - range.Item2) < 1)?.HookDownMm ?? row.HookDownMm
                });
            }
            foreach (var bar in topSupport.Bars) Add(topSupport, Tuple.Create(bar.StartMm, bar.EndMm));
            foreach (var bar in bottomSpan.Bars) Add(bottomSpan, Tuple.Create(bar.StartMm, bar.EndMm));
        }

        private static void ResizeBarGeometries(BarRow row, int count)
        {
            count = Math.Max(1,Math.Min(12,count));
            if (row.Bars.Count == 0) row.Bars.Add(new BarGeometry { Type=row.Type, StartMm=row.StartMm, EndMm=row.EndMm, Position=row.Position });
            BarGeometry seed = row.Bars[Math.Max(0,Math.Min(row.Bars.Count-1,row.SelectedBarIndex))];
            while (row.Bars.Count < count) row.Bars.Add(new BarGeometry { Type=seed.Type, StartMm=seed.StartMm, EndMm=seed.EndMm, Position=seed.Position,
                LeftHookMm=seed.LeftHookMm, RightHookMm=seed.RightHookMm, HookDownMm=seed.HookDownMm, StartPointIndex=seed.StartPointIndex, EndPointIndex=seed.EndPointIndex,
                LeftRatio=seed.LeftRatio, RightRatio=seed.RightRatio, StartHook=seed.StartHook, EndHook=seed.EndHook });
            if (row.Bars.Count > count) row.Bars.RemoveRange(count,row.Bars.Count-count);
            row.SelectedBarIndex = Math.Min(row.SelectedBarIndex,row.Bars.Count-1);
        }

        private static List<ReinforceStraightBeamsCommand.DetailedBarSpec> ToDetailedSpecs(IEnumerable<BarRow> rows,
            RebarHookType topStart, RebarHookType topEnd, RebarHookType bottomStart, RebarHookType bottomEnd, bool applyQuickValues)
        {
            var result = new List<ReinforceStraightBeamsCommand.DetailedBarSpec>();
            foreach (BarRow row in rows.Where(r => r.Key != "tie" && r.Key != "side" && r.Count > 0))
            {
                int count = Math.Min(12, Math.Max(1, row.Count));
                if (row.HasMultipleIntervals)
                {
                    foreach (BarGeometry bar in row.Bars)
                        result.Add(new ReinforceStraightBeamsCommand.DetailedBarSpec {
                            Key = row.Key, Count = count, BarIndex = -1, BarGroupCount = count,
                            Type = bar.Type ?? row.Type, IsTop = row.Key.StartsWith("top", StringComparison.Ordinal),
                            StartMm = bar.StartMm, EndMm = bar.EndMm, SectionPosition = bar.Position,
                            LeftHookMm = bar.LeftHookMm, RightHookMm = bar.RightHookMm, HookDownMm = bar.HookDownMm,
                            StartHook = bar.StartHook, EndHook = bar.EndHook,
                            StartPointIndex = bar.StartPointIndex, EndPointIndex = bar.EndPointIndex,
                            LeftRatio = bar.LeftRatio, RightRatio = bar.RightRatio
                        });
                    continue;
                }
                while (row.Bars.Count < count) row.Bars.Add(new BarGeometry { Type = row.Type, StartMm = row.StartMm, EndMm = row.EndMm, Position = row.Position, HookDownMm = row.HookDownMm,
                    StartPointIndex=row.StartPointIndex, EndPointIndex=row.EndPointIndex, LeftRatio=row.LeftRatio, RightRatio=row.RightRatio,
                    StartHook = row.Key.StartsWith("top") ? topStart : bottomStart, EndHook = row.Key.StartsWith("top") ? topEnd : bottomEnd });
                if (row.Bars.Count > count) row.Bars.RemoveRange(count, row.Bars.Count - count);
                for (int i = 0; i < count; i++)
                {
                    BarGeometry bar = row.Bars[i];
                if (applyQuickValues && row.QuickTypeChanged) bar.Type = row.Type;
                    result.Add(new ReinforceStraightBeamsCommand.DetailedBarSpec { Key = row.Key, Count = 1, BarIndex = i, BarGroupCount = count,
                        Type = bar.Type ?? row.Type, IsTop = row.Key.StartsWith("top", StringComparison.Ordinal), StartMm = bar.StartMm, EndMm = bar.EndMm,
                        SectionPosition = bar.Position, LeftHookMm=bar.LeftHookMm, RightHookMm=bar.RightHookMm, HookDownMm=bar.HookDownMm, StartHook = bar.StartHook ?? (row.Key.StartsWith("top") ? topStart : bottomStart),
                        StartPointIndex=bar.StartPointIndex, EndPointIndex=bar.EndPointIndex, LeftRatio=bar.LeftRatio, RightRatio=bar.RightRatio,
                        EndHook = bar.EndHook ?? (row.Key.StartsWith("top") ? topEnd : bottomEnd) });
                }
            }
            return result;
        }

        private static FrameworkElement BuildPage(string title, List<BarRow> rows, IList<RebarBarType> types, double beamLengthMm, double beamWidthMm, double beamHeightMm,
            RadioButton uniform, RadioButton zoned, TextBox cover, TextBox gap, TextBox tieSpacing,
            TextBox zoneLength, TextBox endSpacing, TextBox middleSpacing, TextBox stirrupOffset,
            TextBox sideMinimumHeight, TextBox sideSpacing, TextBox sideAnchorage,
            IList<ReinforceStraightBeamsCommand.BeamPreviewSupport> previewSupports,
            CheckBox tcvnCutoff, TextBox tcvnRbt, TextBox tcvnRs, TextBox tcvnEta1, TextBox tcvnAreaRatio, TextBox tcvnAlpha,
            ComboBox topStartHook, ComboBox topEndHook, ComboBox bottomStartHook, ComboBox bottomEndHook, Action redraw,
            Action<BarRow> selectPreviewRow)
        {
            var stack = new StackPanel();
            List<double> pointStations = SupportNodeStations(previewSupports, beamLengthMm);
            string supportSummary = string.Join("; ", (previewSupports ?? new List<ReinforceStraightBeamsCommand.BeamPreviewSupport>())
                .Select(s => s.Kind + " " + s.Label + " (" + s.WidthMm.ToString("0") + " mm)"));
            if (title == "Hình học")
            {
                stack.Children.Add(Card(new StackPanel { Children = {
                    new TextBlock { Text = "Thông tin nhịp", FontWeight = FontWeights.SemiBold, Margin = new Thickness(2,0,0,8) },
                    FormRows(new object[] { "Thứ tự nhịp", new TextBlock { Text = "Nhịp đang chọn", VerticalAlignment = VerticalAlignment.Center },
                        "Bề rộng (mm)", ReadOnlyValue(beamWidthMm), "Chiều cao (mm)", ReadOnlyValue(beamHeightMm),
                        "Chiều dài nhịp (mm)", ReadOnlyValue(beamLengthMm), "Gối/giao cấu kiện", ReadOnlyValue(supportSummary) })
                }}, new Thickness(10)));
                stack.Children.Add(Card(FormRows(new object[] { "Lớp bảo vệ (mm)", cover, "Khoảng cách giữa lớp thép (mm)", gap }), new Thickness(10)));
                stack.Children.Add(new TextBlock { Text = "Kích thước và giao cột/dầm được đọc từ cấu kiện đang chọn trong Revit.", Margin = new Thickness(12, 8, 0, 0), Foreground = Brushes.SlateGray });
            }
            else if (title == "Thép đai")
            {
                var tie = rows.First(r => r.Key == "tie");
                var tieType = MakeTypeCombo(types, tie);
                tieType.SelectionChanged += (s,e) => { tie.Type = (tieType.SelectedItem as BarChoice)?.Type ?? tie.Type; redraw(); };
                var panel = FormRows(new object[] { "Nhịp dầm", ReadOnlyValue("Tự nhận diện theo giao cột"), "Đường kính đai chính", tieType,
                    "Khoảng cách đến mép gối (mm)", stirrupOffset, "", uniform, "Bước đều (mm)", tieSpacing,
                    "", zoned, "A1 · bước vùng đầu (mm)", endSpacing, "A2 · bước vùng giữa (mm)", middleSpacing,
                    "Chiều dài vùng đầu 1/2 (mm)", zoneLength });
                stack.Children.Add(Card(panel, new Thickness(10)));
            }
            else if (title == "Thép chống phình")
            {
                var row = rows.First(r => r.Key == "side");
                var count = CountCombo(Math.Max(1,row.Count)); var diameter = MakeTypeCombo(types,row);
                var toggle = new CheckBox { Content="Bố trí thép chống phình hai bên bụng dầm", IsChecked=row.Count>0, Margin=new Thickness(3) };
                var apply = Button("Áp dụng",90); apply.Click+=(s,e)=>{row.Count=toggle.IsChecked==true?CountValue(count):0;row.Type=(diameter.SelectedItem as BarChoice)?.Type??row.Type;redraw();};
                count.SelectionChanged+=(s,e)=>{if(toggle.IsChecked==true)row.Count=CountValue(count);redraw();};
                diameter.SelectionChanged+=(s,e)=>{row.Type=(diameter.SelectedItem as BarChoice)?.Type??row.Type;redraw();};
                stack.Children.Add(Card(FormRows(new object[] { "",toggle,"Đường kính thép bụng",diameter,
                    "Chiều cao bắt đầu bố trí (mm)",sideMinimumHeight,"Khoảng cách hàng đứng (mm)",sideSpacing,
                    "Số hàng tối thiểu mỗi bên",count,"Chiều dài neo vào gối (mm)",sideAnchorage,"",apply }), new Thickness(10)));
            }
            else if (title == "Cắt thép · TCVN 5574:2018")
            {
                stack.Children.Add(Card(new StackPanel { Children = {
                    tcvnCutoff,
                    new TextBlock { Text = "Nhập Rbt (MPa), Rs (MPa) và các hệ số theo hồ sơ thiết kế. η1 = 2,5 ứng với thép cán nóng có gân; hãy đổi theo loại thanh thực tế.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4,6,4,10), Foreground = Brushes.SlateGray },
                    FormRows(new object[] { "Rbt bê tông (MPa)", tcvnRbt, "Rs thép thiết kế (MPa)", tcvnRs,
                        "Hệ số bám dính η1", tcvnEta1, "As,cal / As,ef", tcvnAreaRatio, "Hệ số neo α", tcvnAlpha })
                }}, new Thickness(10)));
                var result = new TextBlock { Margin = new Thickness(12,8,12,4), TextWrapping = TextWrapping.Wrap, Foreground = Blue };
                Action update = () =>
                {
                    BarRow selected = rows.FirstOrDefault(r => r == rows.FirstOrDefault(x => x.Key == "top")) ?? rows.FirstOrDefault(r => r.Count > 0 && r.Type != null);
                    if (selected?.Type == null || Number(tcvnRbt.Text,0) <= 0 || Number(tcvnRs.Text,0) <= 0 || Number(tcvnEta1.Text,0) <= 0 || Number(tcvnAreaRatio.Text,0) <= 0 || Number(tcvnAlpha.Text,0) <= 0)
                    { result.Text = "Nhập đủ Rbt, Rs, η1, As,cal/As,ef và α để xem chiều dài neo tối thiểu."; return; }
                    double length = ReinforceStraightBeamsCommand.CalculateTcvnRequiredAnchorageMm(selected.Type, Number(tcvnRbt.Text,0), Number(tcvnRs.Text,0),
                        Number(tcvnEta1.Text,2.5), Number(tcvnAreaRatio.Text,1.0), Number(tcvnAlpha.Text,1.0));
                    result.Text = "Với " + ShortType(selected.Type) + ": kéo dài tối thiểu khoảng " + length.ToString("0") + " mm tại mỗi điểm cắt nội bộ.";
                };
                tcvnRbt.TextChanged += (s,e) => { update(); redraw(); };
                tcvnRs.TextChanged += (s,e) => { update(); redraw(); };
                tcvnEta1.TextChanged += (s,e) => { update(); redraw(); };
                tcvnAreaRatio.TextChanged += (s,e) => { update(); redraw(); };
                tcvnAlpha.TextChanged += (s,e) => { update(); redraw(); };
                stack.Children.Add(Card(result, new Thickness(10)));
                stack.Children.Add(new TextBlock { Text = "KStruct chỉ kéo dài qua điểm cắt do người dùng nhập. Vị trí cắt vẫn cần kiểm tra theo nội lực và tiết diện nghiêng tại vị trí cắt (TCVN 5574:2018, 8.1.3.4); lệnh không tự phân tích tải trọng.", Margin = new Thickness(12,6,12,8), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkSlateGray });
                update();
            }
            else
            {
                bool upper = title.Contains("trên");
                bool additional = title.Contains("Gia cường");
                BarRow row = rows.First(r => r.Key == (upper ? (additional ? "top1" : "top") : (additional ? "bottom1" : "bottom")));
                selectPreviewRow(row);
                var countCombo = CountCombo(row.Count);
                var typeCombo = MakeTypeCombo(types, row);
                var barSelect = new ComboBox { Width = 112, Height = 30, Margin = new Thickness(3), ItemsSource = Enumerable.Range(1, Math.Max(1, row.Count)).Select(i => "Thanh " + i).ToList(), SelectedIndex = 0 };
                var rowCaption = new TextBlock { Text = row.Caption + " theo nhịp", FontWeight = FontWeights.SemiBold, Margin = new Thickness(4,2,0,12) };
                var summaryLabel = new TextBlock { Text = "Count:" + row.Count + "-" + ShortType(row.Type) + "-S:0-E:1", VerticalAlignment = VerticalAlignment.Center };
                var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 8) };
                toolbar.Children.Add(new TextBlock { Text = "Nhịp 1      Số thanh", VerticalAlignment = VerticalAlignment.Center });
                toolbar.Children.Add(countCombo); toolbar.Children.Add(new TextBlock { Text = "Thanh đang sửa", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8,0,2,0) }); toolbar.Children.Add(barSelect);
                toolbar.Children.Add(new TextBlock { Text = "Đường kính", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8,0,4,0) }); toolbar.Children.Add(typeCombo);
                ComboBox layerCombo = null;
                bool syncingControls = false, syncingDetails = false, syncingHooks = false;
                ComboBox infoType = MakeTypeCombo(types, row);
                ComboBox infoCount = CountCombo(Math.Max(1, row.Count));
                var startPointBox = new ComboBox { Width=150, Height=28, ItemsSource=Enumerable.Range(1, pointStations.Count).Select(i=>i.ToString()).ToList(), Margin=new Thickness(3) };
                var endPointBox = new ComboBox { Width=150, Height=28, ItemsSource=Enumerable.Range(1, pointStations.Count).Select(i=>i.ToString()).ToList(), Margin=new Thickness(3) };
                var leftRatioBox = Input(row.LeftRatio, 150);
                var rightRatioBox = Input(row.RightRatio, 150);
                var leftLengthBox = Input(0, 150); leftLengthBox.IsReadOnly=true; leftLengthBox.Background=new SolidColorBrush(Color.FromRgb(244,246,249));
                var rightLengthBox = Input(0, 150); rightLengthBox.IsReadOnly=true; rightLengthBox.Background=new SolidColorBrush(Color.FromRgb(244,246,249));
                var hookLeftX = Input(row.LeftHookMm, 150);
                var hookRightX = Input(row.RightHookMm, 150);
                var hookDownBox = Input(row.HookDownMm, 150);
                var positionBox = Input(row.Position, 150);
                // Keep hook selectors local to this page while preserving the window defaults.
                ComboBox leftHookState = upper ? topStartHook : bottomStartHook;
                ComboBox rightHookState = upper ? topEndHook : bottomEndHook;
                ComboBox leftHook = CloneHookCombo(leftHookState);
                ComboBox rightHook = CloneHookCombo(rightHookState);
                Action drawParameterSketch = () => { };
                string LayerName(BarRow current) => upper ? (current.Key == "top2" ? "T3" : additional ? "T2" : "T1") : (current.Key == "bottom2" ? "D3" : additional ? "D2" : "D1");
                var layerValue = new TextBlock { Text=LayerName(row), Height=28, Padding=new Thickness(8,5,6,4), Background=new SolidColorBrush(Color.FromRgb(244,246,249)), VerticalAlignment=VerticalAlignment.Center };
                int NearestPoint(double mm) => Enumerable.Range(0,pointStations.Count).OrderBy(i=>Math.Abs(pointStations[i]-mm)).FirstOrDefault();
                void SyncParameterControls(BarGeometry bar)
                {
                    int first = bar.StartPointIndex >= 0 && bar.StartPointIndex < pointStations.Count ? bar.StartPointIndex : NearestPoint(bar.StartMm);
                    int last = bar.EndPointIndex >= first && bar.EndPointIndex < pointStations.Count ? bar.EndPointIndex : NearestPoint(bar.EndMm);
                    last = Math.Max(first, last);
                    bar.StartPointIndex=first; bar.EndPointIndex=last;
                    syncingControls = syncingDetails = true;
                    startPointBox.SelectedIndex=first; endPointBox.SelectedIndex=last;
                    leftRatioBox.Text=bar.LeftRatio.ToString("G",System.Globalization.CultureInfo.InvariantCulture);
                    rightRatioBox.Text=bar.RightRatio.ToString("G",System.Globalization.CultureInfo.InvariantCulture);
                    hookLeftX.Text=bar.LeftHookMm.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);
                    hookRightX.Text=bar.RightHookMm.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);
                    hookDownBox.Text=bar.HookDownMm.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);
                    positionBox.Text=bar.Position.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);
                    syncingControls = syncingDetails = false;
                    syncingHooks=true;
                    int leftHookIndex=leftHook.Items.Cast<object>().OfType<HookChoice>().ToList().FindIndex(h=>h.Type?.Id==bar.StartHook?.Id);
                    int rightHookIndex=rightHook.Items.Cast<object>().OfType<HookChoice>().ToList().FindIndex(h=>h.Type?.Id==bar.EndHook?.Id);
                    if(leftHookIndex>=0)leftHook.SelectedIndex=leftHookIndex;
                    if(rightHookIndex>=0)rightHook.SelectedIndex=rightHookIndex;
                    syncingHooks=false;
                    double leftSpan=first>0 ? pointStations[first]-pointStations[first-1] : 0;
                    double rightSpan=last<pointStations.Count-1 ? pointStations[last+1]-pointStations[last] : 0;
                    leftLengthBox.Text=(leftSpan*bar.LeftRatio).ToString("0.#",System.Globalization.CultureInfo.InvariantCulture);
                    rightLengthBox.Text=(rightSpan*bar.RightRatio).ToString("0.#",System.Globalization.CultureInfo.InvariantCulture);
                }
                void UpdateAnchorageAndStations()
                {
                    if (syncingDetails || pointStations.Count == 0) return;
                    BarGeometry bar=CurrentGeometry(row);
                    int first=Math.Max(0,Math.Min(pointStations.Count-1,startPointBox.SelectedIndex));
                    int last=Math.Max(first,Math.Min(pointStations.Count-1,endPointBox.SelectedIndex));
                    bar.StartPointIndex=first; bar.EndPointIndex=last;
                    bar.LeftRatio=Math.Max(0,Math.Min(1,Number(leftRatioBox.Text,bar.LeftRatio)));
                    bar.RightRatio=Math.Max(0,Math.Min(1,Number(rightRatioBox.Text,bar.RightRatio)));
                    double leftSpan=first>0 ? pointStations[first]-pointStations[first-1] : 0;
                    double rightSpan=last<pointStations.Count-1 ? pointStations[last+1]-pointStations[last] : 0;
                    double leftLength=leftSpan*bar.LeftRatio, rightLength=rightSpan*bar.RightRatio;
                    bar.StartMm=Math.Max(0,pointStations[first]-leftLength);
                    bar.EndMm=Math.Min(beamLengthMm,pointStations[last]+rightLength);
                    if (bar.EndMm <= bar.StartMm) bar.EndMm=Math.Min(beamLengthMm,bar.StartMm+25);
                    CopyGeometry(row,bar);
                    syncingDetails=true;
                    leftLengthBox.Text=leftLength.ToString("0.#",System.Globalization.CultureInfo.InvariantCulture);
                    rightLengthBox.Text=rightLength.ToString("0.#",System.Globalization.CultureInfo.InvariantCulture);
                    syncingDetails=false;
                    drawParameterSketch(); redraw();
                }
                if (additional)
                {
                    layerCombo = new ComboBox { Width=88, ItemsSource=new[] { "Lớp 1", "Lớp 2" }, SelectedIndex=0, Margin=new Thickness(3) };
                    toolbar.Children.Add(new TextBlock { Text = "Lớp", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8,0,2,0) });
                    toolbar.Children.Add(layerCombo);
                    layerCombo.SelectionChanged += (s,e) =>
                    {
                        row = rows.First(r => r.Key == (upper ? (layerCombo.SelectedIndex == 0 ? "top1" : "top2") : (layerCombo.SelectedIndex == 0 ? "bottom1" : "bottom2")));
                        ResizeBarGeometries(row,Math.Max(1,row.Count)); row.SelectedBarIndex=0; CopyGeometry(row,row.Bars[0]);
                        barSelect.ItemsSource=Enumerable.Range(1,Math.Max(1,row.Count)).Select(i=>"Thanh "+i).ToList(); barSelect.SelectedIndex=0;
                        selectPreviewRow(row);
                        syncingControls = syncingDetails = true;
                        countCombo.SelectedIndex = row.Count > 0 ? Math.Min(11,row.Count-1) : 0;
                        typeCombo.SelectedIndex = Math.Max(0,types.ToList().FindIndex(t=>t.Id==row.Type?.Id));
                        infoCount.SelectedIndex = row.Count > 0 ? Math.Min(11,row.Count-1) : 0;
                        infoType.SelectedIndex = Math.Max(0,types.ToList().FindIndex(t=>t.Id==row.Type?.Id));
                        syncingControls = syncingDetails = false;
                        SyncParameterControls(CurrentGeometry(row));
                        layerValue.Text=LayerName(row);
                        rowCaption.Text = row.Caption + " theo nhịp";
                        summaryLabel.Text = "Count:" + row.Count + "-" + ShortType(row.Type) + "-S:0-E:1";
                        redraw();
                    };
                }
                void ApplyToolbarValues()
                {
                    if (syncingControls) return;
                    row.Count = CountValue(countCombo);
                    row.Type = (typeCombo.SelectedItem as BarChoice)?.Type ?? row.Type;
                    ResizeBarGeometries(row, row.Count);
                    barSelect.ItemsSource = Enumerable.Range(1, Math.Max(1, row.Count)).Select(i => "Thanh " + i).ToList();
                    row.SelectedBarIndex = Math.Min(row.SelectedBarIndex, Math.Max(0,row.Count-1));
                    barSelect.SelectedIndex = row.SelectedBarIndex;
                    BarGeometry activeBar = row.Bars[Math.Max(0,row.SelectedBarIndex)];
                    activeBar.Type = row.Type; CopyGeometry(row, activeBar);
                    summaryLabel.Text = "Count:" + row.Count + "-" + ShortType(row.Type) + "-S:0-E:1";
                    syncingControls = true;
                    infoCount.SelectedIndex = Math.Max(0,Math.Min(11,row.Count-1));
                    infoType.SelectedIndex = Math.Max(0,types.ToList().FindIndex(t=>t.Id==row.Type?.Id));
                    syncingControls = false;
                    redraw();
                }
                countCombo.SelectionChanged += (s,e) => ApplyToolbarValues();
                typeCombo.SelectionChanged += (s,e) => { if(!syncingControls) row.QuickTypeChanged=true; ApplyToolbarValues(); };
                var apply = Button("Áp dụng cho nhịp", 130); apply.Margin = new Thickness(8,0,0,0); apply.Click += (s,e) => { row.Count = CountValue(countCombo); row.Type = (typeCombo.SelectedItem as BarChoice)?.Type ?? row.Type; summaryLabel.Text = "Count:" + row.Count + "-" + ShortType(row.Type) + "-S:0-E:1"; redraw(); };
                toolbar.Children.Add(apply);
                stack.Children.Add(Card(new StackPanel { Children = { rowCaption, toolbar } }, new Thickness(10)));
                var summary = new DockPanel { Margin = new Thickness(4,8,4,4), LastChildFill = false };
                summary.Children.Add(summaryLabel);
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                var add = Button("Thêm", 54); add.Background = Blue; add.Foreground = Brushes.White;
                var next = Button("Kế tiếp", 62); var del = Button("Xóa", 54); var clear = Button("Xóa hết", 64);
                add.Click += (s,e) => { row.Count = CountValue(countCombo); row.Type = (typeCombo.SelectedItem as BarChoice)?.Type ?? row.Type; summaryLabel.Text = "Count:" + row.Count + "-" + ShortType(row.Type) + "-S:0-E:1"; redraw(); };
                next.Click += (s,e) => { int i = Math.Max(0, types.ToList().FindIndex(t => t.Id == row.Type?.Id)); typeCombo.SelectedIndex = (i + 1) % Math.Max(1, types.Count); };
                del.Click += (s,e) => { if (row.Optional) row.Count = 0; else row.Count = 1; countCombo.SelectedIndex = Math.Max(0, row.Count - 1); summaryLabel.Text = "Count:" + row.Count + "-" + ShortType(row.Type) + "-S:0-E:1"; redraw(); };
                clear.Click += (s,e) => { if (row.Optional) row.Count = 0; summaryLabel.Text = "Count:" + row.Count + "-" + ShortType(row.Type) + "-S:0-E:1"; redraw(); };
                buttons.Children.Add(add); buttons.Children.Add(next); buttons.Children.Add(del); buttons.Children.Add(clear);
                DockPanel.SetDock(buttons, Dock.Right); summary.Children.Add(buttons);
                stack.Children.Add(Card(summary, new Thickness(8,6,8,6)));
                var parameterSketch = new Canvas { Height = 132, Background = Brushes.White, ClipToBounds = true };
                drawParameterSketch = () => { BarGeometry bar=CurrentGeometry(row); CopyGeometry(row, bar); DrawBarParameterSketch(parameterSketch, row, bar, beamLengthMm); };
                parameterSketch.SizeChanged += (s,e) => drawParameterSketch();
                var expander = new Expander { Header = "Sơ đồ tham số", IsExpanded = true, Margin = new Thickness(4,0,4,6), Foreground = new SolidColorBrush(Color.FromRgb(63, 91, 131)) };
                expander.Content = Card(new StackPanel { Margin = new Thickness(4), Children = {
                    new TextBlock { Text = ParameterSketchCaption(row), FontWeight = FontWeights.SemiBold },
                    parameterSketch
                }}, new Thickness(8));
                stack.Children.Add(expander);
                barSelect.SelectionChanged += (s,e) =>
                {
                    if (syncingControls || row.Bars.Count == 0) return;
                    row.SelectedBarIndex = Math.Max(0, barSelect.SelectedIndex);
                    BarGeometry selectedBar = row.Bars[Math.Min(row.SelectedBarIndex,row.Bars.Count-1)]; CopyGeometry(row, selectedBar);
                    syncingControls = true;
                    int selectedTypeIndex = types.ToList().FindIndex(t=>t.Id==selectedBar.Type?.Id);
                    typeCombo.SelectedIndex = selectedTypeIndex < 0 ? 0 : selectedTypeIndex;
                    infoType.SelectedIndex = selectedTypeIndex < 0 ? 0 : selectedTypeIndex;
                    syncingControls = false;
                    syncingHooks = true;
                    var leftChoices=leftHook.Items.Cast<object>().OfType<HookChoice>().ToList();
                    var rightChoices=rightHook.Items.Cast<object>().OfType<HookChoice>().ToList();
                    int leftIndex=leftChoices.FindIndex(h=>h.Type?.Id==selectedBar.StartHook?.Id); int rightIndex=rightChoices.FindIndex(h=>h.Type?.Id==selectedBar.EndHook?.Id);
                    if(leftIndex>=0)leftHook.SelectedIndex=leftIndex; if(rightIndex>=0)rightHook.SelectedIndex=rightIndex;
                    syncingHooks = false;
                    SyncParameterControls(selectedBar); drawParameterSketch(); redraw();
                };
                infoType.SelectionChanged += (s,e) => { if(syncingControls)return; row.Type = (infoType.SelectedItem as BarChoice)?.Type ?? row.Type; BarGeometry selectedBar = CurrentGeometry(row); selectedBar.Type = row.Type; summaryLabel.Text = "Count:" + row.Count + "-" + ShortType(row.Type) + "-S:0-E:1"; syncingControls=true; typeCombo.SelectedIndex=Math.Max(0,types.ToList().FindIndex(t=>t.Id==row.Type?.Id)); syncingControls=false; redraw(); };
                infoCount.SelectionChanged += (s,e) => { if (syncingControls || (row.Optional && row.Count == 0)) return; row.Count = CountValue(infoCount); ResizeBarGeometries(row,row.Count); barSelect.ItemsSource=Enumerable.Range(1,Math.Max(1,row.Count)).Select(i=>"Thanh "+i).ToList(); barSelect.SelectedIndex=row.SelectedBarIndex; summaryLabel.Text = "Count:" + row.Count + "-" + ShortType(row.Type) + "-S:0-E:1"; syncingControls=true; countCombo.SelectedIndex=Math.Max(0,Math.Min(11,row.Count-1)); syncingControls=false; redraw(); };
                startPointBox.SelectionChanged += (s,e) => UpdateAnchorageAndStations();
                endPointBox.SelectionChanged += (s,e) => UpdateAnchorageAndStations();
                leftRatioBox.TextChanged += (s,e) => UpdateAnchorageAndStations();
                rightRatioBox.TextChanged += (s,e) => UpdateAnchorageAndStations();
                hookLeftX.TextChanged += (s,e) => { if (syncingDetails) return; BarGeometry b=CurrentGeometry(row); row.LeftHookMm=b.LeftHookMm=Math.Max(0,Number(hookLeftX.Text,b.LeftHookMm)); drawParameterSketch(); redraw(); };
                hookRightX.TextChanged += (s,e) => { if (syncingDetails) return; BarGeometry b=CurrentGeometry(row); row.RightHookMm=b.RightHookMm=Math.Max(0,Number(hookRightX.Text,b.RightHookMm)); drawParameterSketch(); redraw(); };
                hookDownBox.TextChanged += (s,e) => { if (syncingDetails) return; BarGeometry b=CurrentGeometry(row); row.HookDownMm=b.HookDownMm=Math.Max(0,Number(hookDownBox.Text,b.HookDownMm)); drawParameterSketch(); redraw(); };
                positionBox.TextChanged += (s,e) => { if (syncingDetails) return; BarGeometry b=CurrentGeometry(row); row.Position=b.Position=Math.Max(0,Math.Min(4,Number(positionBox.Text,b.Position))); redraw(); };
                // The hook selections are stored in the window-level controls, but each
                // page owns its own ComboBoxes. Never insert a control already attached to
                // a page into another page's Grid.
                leftHook.SelectionChanged += (s,e) => { if(syncingHooks)return; leftHookState.SelectedItem = leftHook.SelectedItem; CurrentGeometry(row).StartHook=(leftHook.SelectedItem as HookChoice)?.Type; drawParameterSketch(); redraw(); };
                rightHook.SelectionChanged += (s,e) => { if(syncingHooks)return; rightHookState.SelectedItem = rightHook.SelectedItem; CurrentGeometry(row).EndHook=(rightHook.SelectedItem as HookChoice)?.Type; drawParameterSketch(); redraw(); };
                stack.Children.Add(Card(new StackPanel { Children = {
                    new TextBlock { Text = "Thông tin thép", FontWeight = FontWeights.SemiBold, Margin = new Thickness(2,0,0,7) },
                    SteelInfoGrid(new object[] {
                        "Lớp", layerValue, "Đường kính", infoType,
                        "Điểm bắt đầu", startPointBox, "Điểm kết thúc", endPointBox,
                        "Kiểu bắt đầu", leftHook, "Kiểu kết thúc", rightHook,
                        "Tỷ lệ trái", leftRatioBox, "Tỷ lệ phải", rightRatioBox,
                        "Chiều dài trái (mm)", leftLengthBox, "Chiều dài phải (mm)", rightLengthBox,
                        "Số lượng", infoCount, "Vị trí trong mặt cắt", positionBox
                    }),
                    new Expander { Header="Thông số neo bổ sung", Margin=new Thickness(2,6,2,0), Content=SteelInfoGrid(new object[] {
                        "Neo X trái (mm)", hookLeftX, "Neo X phải (mm)", hookRightX,
                        "Móc xuống Ly (mm)", hookDownBox
                    }) }
                }}, new Thickness(8)));
                SyncParameterControls(CurrentGeometry(row));
            }
            return new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        }

        private static UIElement FormRows(object[] items)
        {
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i=0;i<items.Length;i+=2)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                UIElement left = items[i] as UIElement ?? new TextBlock { Text = items[i]?.ToString(), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3,5,5,5) };
                UIElement right = i+1<items.Length ? items[i+1] as UIElement ?? new TextBlock { Text = items[i+1]?.ToString(), VerticalAlignment = VerticalAlignment.Center } : new TextBlock();
                Grid.SetRow(left,i/2); Grid.SetColumn(left,0); grid.Children.Add(left); Grid.SetRow(right,i/2); Grid.SetColumn(right,1); grid.Children.Add(right);
            }
            return grid;
        }

        private static List<double> SupportNodeStations(IList<ReinforceStraightBeamsCommand.BeamPreviewSupport> supports, double beamLengthMm)
        {
            double length=Math.Max(1,beamLengthMm);
            var stations=new List<double>{0,length};
            foreach(var support in (supports ?? new List<ReinforceStraightBeamsCommand.BeamPreviewSupport>()).Where(s=>s!=null).OrderBy(s=>s.StartMm))
            {
                double center=(support.StartMm+support.EndMm)*0.5;
                if(center<=150 || center>=length-150 || stations.Any(value=>Math.Abs(value-center)<80)) continue;
                stations.Add(center);
            }
            return stations.OrderBy(value=>value).ToList();
        }

        private static UIElement SteelInfoGrid(object[] items)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.8, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.5, GridUnitType.Star) });
            int pairs = (items.Length + 3) / 4;
            for (int r = 0; r < pairs; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int i = 0; i < items.Length; i += 2)
            {
                int pair = i / 2;
                int row = pair / 2;
                int col = (pair % 2) * 2;
                UIElement label = items[i] as UIElement ?? new TextBlock { Text = items[i]?.ToString(), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 5, 5, 5), Foreground = new SolidColorBrush(Color.FromRgb(77, 107, 158)) };
                UIElement field = i + 1 < items.Length ? items[i + 1] as UIElement ?? new TextBlock { Text = items[i + 1]?.ToString(), VerticalAlignment = VerticalAlignment.Center } : new TextBlock();
                if (label is TextBlock text) text.Margin = new Thickness(4, 5, 5, 5);
                if (field is WpfControl control) { control.Margin = new Thickness(3, 2, 6, 2); control.VerticalAlignment = VerticalAlignment.Center; }
                Grid.SetRow(label, row); Grid.SetColumn(label, col); grid.Children.Add(label);
                Grid.SetRow(field, row); Grid.SetColumn(field, col + 1); grid.Children.Add(field);
            }
            return grid;
        }

        private static UIElement QuickSelectedBarGrid(object[] items)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            int fieldCount = (items.Length + 1) / 2;
            for (int i = 0; i < fieldCount; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < fieldCount; i++)
            {
                object rawLabel = items[i * 2];
                object rawField = i * 2 + 1 < items.Length ? items[i * 2 + 1] : null;
                var column = new StackPanel { Margin = new Thickness(3, 0, 3, 0) };
                UIElement label = rawLabel as UIElement ?? new TextBlock
                {
                    Text = rawLabel?.ToString() ?? string.Empty,
                    Foreground = new SolidColorBrush(Color.FromRgb(77, 107, 158)),
                    Margin = new Thickness(2, 4, 2, 2),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                UIElement field = rawField as UIElement ?? new TextBlock { Text = rawField?.ToString() ?? string.Empty };
                if (field is WpfControl control)
                {
                    control.Width = double.NaN;
                    control.HorizontalAlignment = HorizontalAlignment.Stretch;
                    control.Margin = new Thickness(2, 2, 2, 4);
                }
                column.Children.Add(label);
                column.Children.Add(field);
                Grid.SetColumn(column, i);
                grid.Children.Add(column);
            }
            return grid;
        }

        private static UIElement ReadOnlyValue(double value) => new TextBlock
        {
            Text = value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
            Height = 28,
            Padding = new Thickness(8, 5, 6, 4),
            Background = new SolidColorBrush(Color.FromRgb(244, 246, 249)),
            VerticalAlignment = VerticalAlignment.Center
        };

        private static UIElement ReadOnlyValue(string value) => new TextBlock
        {
            Text = value ?? string.Empty,
            Height = 28,
            Padding = new Thickness(8, 5, 6, 4),
            Background = new SolidColorBrush(Color.FromRgb(244, 246, 249)),
            VerticalAlignment = VerticalAlignment.Center
        };

        private static void DrawBarParameterSketch(Canvas canvas, BarRow row, BarGeometry selectedBar, double beamLengthMm)
        {
            canvas.Children.Clear();
            double width = Math.Max(300, canvas.ActualWidth), height = Math.Max(120, canvas.ActualHeight);
            double left = 44, right = width - 44, span = Math.Max(1, beamLengthMm), scale = (right - left) / span;
            double x1 = left + Math.Max(0, Math.Min(span, row.StartMm)) * scale;
            double x2 = left + Math.Max(0, Math.Min(span, row.EndMm)) * scale;
            if (x2 < x1) { double swap=x1; x1=x2; x2=swap; }
            double beamTop = Math.Max(30, height * 0.28), beamBottom = Math.Min(height - 42, height * 0.64);
            Brush barColor = row.Key == "bottom" || row.Key == "bottom1" ? Teal : Blue;
            bool mainBar = row.Key == "top" || row.Key == "bottom";
            if (mainBar) DrawMainBarParameter(canvas,row,selectedBar,width,left,right,beamTop,beamBottom,x1,x2,scale,span,barColor);
            else if (row.Key == "top1" || row.Key == "top2") DrawTopAdditionalParameter(canvas,row,width,left,right,beamTop,beamBottom,x1,x2,scale,span,barColor);
            else DrawBottomAdditionalParameter(canvas,row,width,left,right,beamTop,beamBottom,x1,x2,scale,span,barColor);
            AddCanvasText(canvas, "Thanh " + (row.SelectedBarIndex + 1) + " · " + ShortType(selectedBar?.Type ?? row.Type) + " · L=" + Math.Max(0,row.EndMm-row.StartMm).ToString("0.#") + " mm",
                Math.Max(4,(left+right)/2-95),height-17,new SolidColorBrush(Color.FromRgb(72,96,134)),9);
        }

        private static string ParameterSketchCaption(BarRow row)
        {
            if (row.Key == "top") return "Thép chủ trên · Lx trái/phải và Ly neo tại gối";
            if (row.Key == "bottom") return "Thép chủ dưới · Lx trái/phải và Ly neo tại gối";
            if (row.Key == "top1" || row.Key == "top2") return "Thép gia cường trên · chiều dài hai phía nút/gối";
            return "Thép gia cường dưới · chiều dài thanh và phần kéo qua gối";
        }

        private static void DrawMainBarParameter(Canvas canvas, BarRow row, BarGeometry selected, double width, double left, double right,
            double beamTop, double beamBottom, double x1, double x2, double scale, double span, Brush color)
        {
            DrawSketchBeam(canvas,left,right,beamTop,beamBottom);
            double y = row.Key == "top" ? beamTop + 10 : beamBottom - 10;
            double hook = 34;
            double hxLeft = Math.Min(Math.Max(0,selected?.LeftHookMm ?? row.LeftHookMm)*scale,Math.Max(0,x2-x1-5));
            double hxRight = Math.Min(Math.Max(0,selected?.RightHookMm ?? row.RightHookMm)*scale,Math.Max(0,x2-x1-5));
            if (selected?.StartHook != null && hxLeft < 5) hxLeft = hook;
            if (selected?.EndHook != null && hxRight < 5) hxRight = hook;
            bool top = row.Key == "top";
            double downMm = Math.Max(0, selected?.HookDownMm ?? row.HookDownMm);
            double downPx = top ? Math.Min(downMm * scale, Math.Max(0, beamBottom - y - 4)) : hook;
            double leftLeg = downPx, rightLeg = downPx;
            var path = new Polyline { Stroke=color, StrokeThickness=3, StrokeLineJoin=PenLineJoin.Round, Points = top
                ? new PointCollection { new Point(x1+hxLeft,y+leftLeg),new Point(x1,y+leftLeg),new Point(x1,y),new Point(x2,y),new Point(x2,y+rightLeg),new Point(x2-hxRight,y+rightLeg) }
                : new PointCollection { new Point(x1,y-hook),new Point(x1,y),new Point(x2,y),new Point(x2,y-hook) } };
            canvas.Children.Add(path);
            DrawDimension(canvas,x1,x1+hxLeft,beamTop-12,"Lx1",-6);
            DrawDimension(canvas,x2-hxRight,x2,beamTop-12,"Lx2",-6);
            string lyLabel = top ? "Ly " + downMm.ToString("0") : "Ly";
            DrawVerticalDimension(canvas,x1-22,top?y:y-hook,top?y+downPx:y,lyLabel);
            DrawVerticalDimension(canvas,x2+22,top?y:y-hook,top?y+downPx:y,lyLabel);
            DrawDimension(canvas,x1,x1+hxLeft,beamTop-12,"Lx1",-6);
            DrawDimension(canvas,x2-hxRight,x2,beamTop-12,"Lx2",-6);
            DrawVerticalDimension(canvas,top?x1-22:x1-22,top?y:y-hook,top?y+hook:y,"Ly");
            DrawVerticalDimension(canvas,top?x2+22:x2+22,top?y:y-hook,top?y+hook:y,"Ly");
            AddCanvasText(canvas,"S="+row.StartMm.ToString("0.#"),Math.Max(0,x1-28),beamBottom+4,Blue,8);
            AddCanvasText(canvas,"E="+row.EndMm.ToString("0.#"),Math.Min(width-64,x2-22),beamBottom+4,Blue,8);
        }

        private static void DrawTopAdditionalParameter(Canvas canvas, BarRow row, double width, double left, double right,
            double beamTop, double beamBottom, double x1, double x2, double scale, double span, Brush color)
        {
            double center=(left+right)/2, columnWidth=Math.Max(26,(right-left)*0.055), columnLeft=center-columnWidth/2, columnRight=center+columnWidth/2;
            DrawSketchBeam(canvas,left,right,beamTop,beamBottom);
            DrawSketchSupport(canvas,center,beamTop-14,beamBottom+14,columnWidth);
            double yUpper=beamTop+12, yLower=beamBottom-10;
            // Scheme 1: bar starts/ends at the selected central joint and extends into one side.
            double activeStart=Math.Max(left,Math.Min(right,x1)), activeEnd=Math.Max(activeStart,Math.Min(right,x2));
            if (activeEnd-activeStart < 12) { activeStart=columnLeft; activeEnd=right-20; }
            var jointBar=new Polyline { Stroke=color,StrokeThickness=3,StrokeLineJoin=PenLineJoin.Round,
                Points=new PointCollection{new Point(Math.Max(columnLeft,activeStart),yUpper+20),new Point(Math.Max(columnLeft,activeStart),yUpper),new Point(activeEnd,yUpper)} };
            canvas.Children.Add(jointBar);
            DrawDimension(canvas,columnLeft,columnRight,beamTop-10,"Lx",-2);
            DrawDimension(canvas,Math.Max(columnRight,activeStart),activeEnd,beamTop-10,"L2",-2);
            // Scheme 2: full-length straight reinforcement, split dimensioned at the joint.
            var throughBar=new Line{X1=x1,X2=x2,Y1=yLower,Y2=yLower,Stroke=color,StrokeThickness=3,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round}; canvas.Children.Add(throughBar);
            DrawDimension(canvas,x1,center,beamBottom+12,"L1",4);
            DrawDimension(canvas,center,x2,beamBottom+12,"L2",4);
            AddSketchAxis(canvas,center,beamTop-16,beamBottom+16);
            AddCanvasText(canvas,"S="+row.StartMm.ToString("0.#")+" mm",Math.Max(2,x1-20),beamTop+3,Blue,8);
            AddCanvasText(canvas,"E="+row.EndMm.ToString("0.#")+" mm",Math.Min(width-70,x2-24),beamTop+3,Blue,8);
        }

        private static void DrawBottomAdditionalParameter(Canvas canvas, BarRow row, double width, double left, double right,
            double beamTop, double beamBottom, double x1, double x2, double scale, double span, Brush color)
        {
            DrawSketchBeam(canvas,left,right,beamTop,beamBottom);
            DrawSketchSupport(canvas,left+8,beamTop-12,beamBottom+12,28);
            DrawSketchSupport(canvas,right-8,beamTop-12,beamBottom+12,28);
            double y=(beamTop+beamBottom)/2;
            var bar=new Line{X1=x1,X2=x2,Y1=y,Y2=y,Stroke=color,StrokeThickness=3,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};canvas.Children.Add(bar);
            double middle=(x1+x2)/2;
            DrawDimension(canvas,left,x1,beamTop-19,"L3",-3);
            DrawDimension(canvas,x1,middle,beamTop-19,"L1",-3);
            DrawDimension(canvas,middle,x2,beamTop-19,"L2",-3);
            DrawDimension(canvas,x2,right,beamTop-19,"L3",-3);
            DrawDimension(canvas,left,right,beamBottom+20,"L = "+Math.Max(0,row.EndMm-row.StartMm).ToString("0.#")+" mm",5);
            AddCanvasText(canvas,"S="+row.StartMm.ToString("0.#")+" mm",Math.Max(2,x1-20),y+8,Teal,8);
            AddCanvasText(canvas,"E="+row.EndMm.ToString("0.#")+" mm",Math.Min(width-70,x2-24),y+8,Teal,8);
        }

        private static void DrawSketchBeam(Canvas canvas,double left,double right,double top,double bottom)
        {
            var beam=new Rectangle{Width=Math.Max(1,right-left),Height=Math.Max(1,bottom-top),Fill=new SolidColorBrush(Color.FromRgb(235,241,247)),Stroke=BorderBrush,StrokeThickness=1};
            Canvas.SetLeft(beam,left);Canvas.SetTop(beam,top);canvas.Children.Add(beam);
        }

        private static void DrawSketchSupport(Canvas canvas,double center,double top,double bottom,double supportWidth)
        {
            var support=new Rectangle{Width=supportWidth,Height=Math.Max(1,bottom-top),Fill=new SolidColorBrush(Color.FromRgb(220,228,238)),Stroke=BorderBrush,StrokeThickness=1};
            Canvas.SetLeft(support,center-supportWidth/2);Canvas.SetTop(support,top);canvas.Children.Add(support);
        }

        private static void AddSketchAxis(Canvas canvas,double x,double top,double bottom)
        {
            canvas.Children.Add(new Line{X1=x,X2=x,Y1=top,Y2=bottom,Stroke=Brushes.SlateGray,StrokeThickness=0.8,StrokeDashArray=new DoubleCollection{2,2}});
        }

        private static void DrawDimension(Canvas canvas,double x1,double x2,double y,string label,double textOffset)
        {
            double a=Math.Min(x1,x2),b=Math.Max(x1,x2);
            canvas.Children.Add(new Line{X1=a,X2=b,Y1=y,Y2=y,Stroke=new SolidColorBrush(Color.FromRgb(112,132,160)),StrokeThickness=0.8});
            foreach(double x in new[]{a,b}) canvas.Children.Add(new Line{X1=x-2,X2=x+2,Y1=y+3,Y2=y-3,Stroke=new SolidColorBrush(Color.FromRgb(112,132,160)),StrokeThickness=0.8});
            AddCanvasText(canvas,label,(a+b)/2-12,y+textOffset,new SolidColorBrush(Color.FromRgb(72,96,134)),8);
        }

        private static void DrawVerticalDimension(Canvas canvas,double x,double y1,double y2,string label)
        {
            canvas.Children.Add(new Line{X1=x,X2=x,Y1=y1,Y2=y2,Stroke=new SolidColorBrush(Color.FromRgb(112,132,160)),StrokeThickness=0.8});
            foreach(double y in new[]{y1,y2}) canvas.Children.Add(new Line{X1=x-3,X2=x+3,Y1=y-2,Y2=y+2,Stroke=new SolidColorBrush(Color.FromRgb(112,132,160)),StrokeThickness=0.8});
            AddCanvasText(canvas,label,x-20,(y1+y2)/2-5,new SolidColorBrush(Color.FromRgb(72,96,134)),8);
        }

        private static void UpdateSectionTabs(IList<Button> tabs, int selected)
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                tabs[i].Background = i == selected ? new SolidColorBrush(Color.FromRgb(232, 240, 255)) : Brushes.White;
                tabs[i].Foreground = i == selected ? Blue : Brushes.Black;
                tabs[i].BorderBrush = i == selected ? new SolidColorBrush(Color.FromRgb(216, 228, 250)) : BorderBrush;
            }
        }

        private static Border Card(UIElement content, Thickness padding) => new Border { Child=content, Background=Brushes.White, BorderBrush=new SolidColorBrush(Color.FromRgb(229,234,243)), BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(10), Padding=padding, Margin=new Thickness(7), Effect=new System.Windows.Media.Effects.DropShadowEffect { Color=Color.FromRgb(28,48,80), Opacity=0.08, BlurRadius=16, ShadowDepth=3 } };
        private static TextBox Input(double value, double width) => new TextBox { Text=value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), Width=width, Height=28, Margin=new Thickness(3), VerticalContentAlignment=VerticalAlignment.Center };
        private static Button Button(string text, double width) => new Button { Content=text, Width=width, Height=30, Padding=new Thickness(5,2,5,2), Margin=new Thickness(3), Background=Brushes.White, BorderBrush=BorderBrush };
        private static ComboBox CountCombo(int count) => new ComboBox { Width=64, Height=30, ItemsSource=Enumerable.Range(1,12).Select(x=>x.ToString()).ToList(), SelectedIndex=Math.Max(0,Math.Min(11,count-1)), Margin=new Thickness(3) };
        private static int CountValue(ComboBox combo)
        {
            string selected = combo.SelectedItem?.ToString();
            if (int.TryParse(selected, out int count)) return Math.Max(1, Math.Min(12, count));
            return int.TryParse(combo.Text, out count) ? Math.Max(1, Math.Min(12, count)) : 1;
        }
        private static ComboBox MakeTypeCombo(IList<RebarBarType> types, BarRow row)
        {
            var combo = new ComboBox { Width=110, Height=30, ItemsSource=types.Select(t=>new BarChoice(t)).ToList(), DisplayMemberPath="Display", Margin=new Thickness(3) };
            int index=types.ToList().FindIndex(t=>t.Id==row.Type?.Id); combo.SelectedIndex=index<0?0:index; return combo;
        }
        private sealed class BarChoice { public RebarBarType Type { get; private set; } public string Display { get; private set; } public BarChoice(RebarBarType type) { Type=type; Display=type.Name; } }
        private sealed class HookChoice { public RebarHookType Type { get; private set; } public string Display { get; private set; } public HookChoice(RebarHookType type) { Type=type; Display=type==null?"Không dùng neo":type.Name; } }
        private static ComboBox HookCombo(IList<HookChoice> choices, RebarHookType selected) { var c=new ComboBox { Width=180, ItemsSource=choices, DisplayMemberPath="Display", Margin=new Thickness(3) }; c.SelectedIndex=Math.Max(0,choices.ToList().FindIndex(x=>x.Type?.Id==selected?.Id)); return c; }
        private static ComboBox CloneHookCombo(ComboBox source)
        {
            var choices = source.ItemsSource as IList<HookChoice> ?? source.Items.Cast<HookChoice>().ToList();
            RebarHookType selected = (source.SelectedItem as HookChoice)?.Type;
            return HookCombo(choices, selected);
        }
        private static string ShortType(RebarBarType type) => type == null ? "Ø?" : "Ø" + UnitUtils.ConvertFromInternalUnits(type.BarNominalDiameter, UnitTypeId.Millimeters).ToString("0");
        private static double Number(string text, double fallback) => TryNumber(text, out double value) ? value : fallback;
        private static bool TryNumber(string text, out double value) => double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) || double.TryParse(text, out value);

        private static void DrawSection(Canvas canvas, IList<BarRow> rows, IList<RebarBarType> types, BarRow activeRow, int sectionIndex,
            double beamLengthMm, double beamWidthMm, double beamHeightMm, double coverMm, double layerGapMm)
        {
            canvas.Children.Clear();
            double width = Math.Max(180, canvas.ActualWidth), height = Math.Max(180, canvas.ActualHeight);
            double ratio = Math.Max(0.15, Math.Min(5.0, beamWidthMm / Math.Max(1.0, beamHeightMm)));
            double sectionHeight = Math.Min(height - 46, (width - 36) / ratio);
            double sectionWidth = sectionHeight * ratio;
            double x = (width - sectionWidth) / 2.0, y = (height - sectionHeight) / 2.0;
            var rect = new Rectangle { Width = sectionWidth, Height = sectionHeight, Stroke = new SolidColorBrush(Color.FromRgb(211,139,36)), StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromRgb(235,241,247)) };
            Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y); canvas.Children.Add(rect);
            double station = GetSectionStation(activeRow, sectionIndex, beamLengthMm);
            int ActiveCount(BarRow row) => row.Bars.Count > 0 ? row.Bars.Count(b => station >= b.StartMm && station <= b.EndMm) : IsAtStation(row, station) ? Math.Max(0, row.Count) : 0;
            double coverPx = Math.Max(3, Math.Min(sectionHeight * 0.22, Math.Max(0, coverMm) * sectionHeight / Math.Max(1, beamHeightMm)));
            double layerPx = Math.Max(2, Math.Min(sectionHeight * 0.15, Math.Max(0, layerGapMm) * sectionHeight / Math.Max(1, beamHeightMm)));
            double leftBars = x + sectionWidth * 0.12, barWidth = sectionWidth * 0.76;
            double topY = y + coverPx, bottomY = y + sectionHeight - coverPx;
            if (activeRow != null && activeRow.Key.StartsWith("top")) topY += (activeRow.Position - 1) * layerPx;
            if (activeRow != null && activeRow.Key.StartsWith("bottom")) bottomY -= (activeRow.Position - 1) * layerPx;
            BarRow top = rows.First(r => r.Key == "top"), bottom = rows.First(r => r.Key == "bottom");
            AddDots(canvas, leftBars, topY, barWidth, Blue, ActiveCount(top), BarDotSize(top.Type));
            AddDots(canvas, leftBars, bottomY, barWidth, Teal, ActiveCount(bottom), BarDotSize(bottom.Type));
            if(activeRow==top&&ActiveCount(top)>0)AddDotHighlight(canvas,leftBars,barWidth,topY,ActiveCount(top),Math.Min(top.SelectedBarIndex,ActiveCount(top)-1),BarDotSize(top.Type));
            if(activeRow==bottom&&ActiveCount(bottom)>0)AddDotHighlight(canvas,leftBars,barWidth,bottomY,ActiveCount(bottom),Math.Min(bottom.SelectedBarIndex,ActiveCount(bottom)-1),BarDotSize(bottom.Type));
            foreach (string key in new[] { "top1", "top2", "bottom1", "bottom2" })
            {
                BarRow row = rows.First(r => r.Key == key); bool upper = key.StartsWith("top");
                double fraction = key == "top1" ? 0.24 : key == "top2" ? 0.34 : key == "bottom2" ? 0.76 : 0.66;
                double barY = y + sectionHeight * fraction;
                if (row == activeRow) barY += (row.Position - 1) * (upper ? layerPx : -layerPx);
                int activeCount = ActiveCount(row);
                AddDots(canvas, leftBars, barY, barWidth, row == activeRow ? Blue : MutedBlue, activeCount, BarDotSize(row.Type));
                if (row == activeRow && activeCount > 0) AddDotHighlight(canvas,leftBars,barWidth,barY,activeCount,Math.Min(row.SelectedBarIndex,activeCount-1),BarDotSize(row.Type));
            }
            BarRow side = rows.First(r => r.Key == "side");
            AddDots(canvas, x + 4, y + sectionHeight * 0.25, 5, Teal, side.Count, BarDotSize(side.Type));
            AddDots(canvas, x + sectionWidth - 9, y + sectionHeight * 0.25, 5, Teal, side.Count, BarDotSize(side.Type));
            AddCanvasText(canvas, beamWidthMm.ToString("0") + " × " + beamHeightMm.ToString("0") + " mm", x, y + sectionHeight + 5, Brushes.SlateGray, 10);
        }
        private static double BarDotSize(RebarBarType type)
        {
            if (type == null) return 8;
            double mm=UnitUtils.ConvertFromInternalUnits(type.BarNominalDiameter,UnitTypeId.Millimeters);
            return Math.Max(5,Math.Min(13,mm*0.4));
        }
        private static void AddDots(Canvas canvas,double x,double y,double width,Brush brush,int count,double diameter=8)
        {
            count=Math.Max(0,Math.Min(12,count)); if(count==0)return; int cols=Math.Min(count,6), rows=(int)Math.Ceiling(count/(double)cols);
            for(int i=0;i<count;i++){int r=i/cols,c=i%cols;double xx=x+(cols==1?width/2:width*c/(cols-1.0));var dot=new Ellipse{Width=diameter,Height=diameter,Fill=brush,Stroke=Brushes.White,StrokeThickness=1};Canvas.SetLeft(dot,xx-diameter/2);Canvas.SetTop(dot,y+(r-(rows-1)/2.0)*12-diameter/2);canvas.Children.Add(dot);}
        }
        private static void AddDotHighlight(Canvas canvas,double x,double width,double y,int count,int index,double diameter)
        {
            if(count<=0)return; int cols=Math.Min(count,6), rows=(int)Math.Ceiling(count/(double)cols); index=Math.Max(0,Math.Min(count-1,index));
            int r=index/cols,c=index%cols; double xx=x+(cols==1?width/2:width*c/(cols-1.0)); double yy=y+(r-(rows-1)/2.0)*12;
            var ring=new Ellipse{Width=diameter+5,Height=diameter+5,Stroke=Blue,StrokeThickness=2,Fill=Brushes.Transparent}; Canvas.SetLeft(ring,xx-(diameter+5)/2);Canvas.SetTop(ring,yy-(diameter+5)/2);canvas.Children.Add(ring);
        }
        private static bool IsAtStation(BarRow row,double station) => row != null && station >= row.StartMm && station <= row.EndMm;
        private static double GetSectionStation(BarRow activeRow, int sectionIndex, double beamLengthMm)
        {
            double fraction = new[] { 0.12, 0.50, 0.88 }[Math.Max(0, Math.Min(2, sectionIndex))];
            double station = beamLengthMm * fraction;
            if (activeRow == null || activeRow.Count <= 0 || IsAtStation(activeRow, station)) return station;
            double start = Math.Max(0, Math.Min(beamLengthMm, activeRow.StartMm));
            double end = Math.Max(start, Math.Min(beamLengthMm, activeRow.EndMm));
            return end > start ? (start + end) * 0.5 : station;
        }

        private static void DrawBeam(Canvas canvas, IList<BarRow> rows, IList<RebarBarType> types,
            IList<ReinforceStraightBeamsCommand.BeamPreviewSupport> supports, bool visible, bool tiesVisible,
            BarRow activeRow, int sectionIndex, double beamLengthMm, bool zonedTies,
            double uniformSpacing, double endSpacing, double middleSpacing, double zoneLength,
            double highlightStartMm = -1, double highlightEndMm = -1, double beamWidthMm = 200, double beamHeightMm = 400,
            double supportOffsetMm = 50, double coverMm = 30,
            IList<ReinforceStraightBeamsCommand.BeamPreviewSection> beamSections = null)
        {
            canvas.Children.Clear();
            if (!visible) return;

            double w = Math.Max(500, canvas.ActualWidth);
            double h = Math.Max(360, canvas.ActualHeight);
            beamLengthMm = Math.Max(1, beamLengthMm);
            uniformSpacing = Math.Max(25, Math.Min(1000, uniformSpacing));
            endSpacing = Math.Max(25, Math.Min(1000, endSpacing));
            middleSpacing = Math.Max(25, Math.Min(1000, middleSpacing));
            zoneLength = Math.Max(0, zoneLength);

            double left = 68, right = w - 68;
            double top = Math.Max(100, h * 0.30);
            double beamHeight = Math.Max(64, Math.Min(104, h * 0.23));
            double bottom = top + beamHeight;
            var actualSections = (beamSections ?? new List<ReinforceStraightBeamsCommand.BeamPreviewSection>())
                .Where(section => section != null && section.EndMm > section.StartMm)
                .OrderBy(section => section.StartMm).ToList();
            if (actualSections.Count == 0)
                actualSections.Add(new ReinforceStraightBeamsCommand.BeamPreviewSection
                {
                    StartMm = 0, EndMm = beamLengthMm, WidthMm = beamWidthMm, HeightMm = beamHeightMm,
                    TopElevationMm = beamHeightMm, BottomElevationMm = 0, Label = beamWidthMm.ToString("0") + "×" + beamHeightMm.ToString("0") + " mm"
                });
            double maxElevation = actualSections.Max(section => section.TopElevationMm);
            double minElevation = actualSections.Min(section => section.BottomElevationMm);
            double elevationRange = Math.Max(1, maxElevation - minElevation);
            double commonTopElevation = actualSections.Min(section => section.TopElevationMm);
            double commonBottomElevation = actualSections.Max(section => section.BottomElevationMm);
            double SectionTop(double station)
            {
                var profile = actualSections.FirstOrDefault(section => station >= section.StartMm && station <= section.EndMm)
                    ?? actualSections.OrderBy(section => Math.Abs((section.StartMm + section.EndMm) / 2 - station)).First();
                return top + (maxElevation - profile.TopElevationMm) * beamHeight / elevationRange;
            }
            double SectionBottom(double station)
            {
                var profile = actualSections.FirstOrDefault(section => station >= section.StartMm && station <= section.EndMm)
                    ?? actualSections.OrderBy(section => Math.Abs((section.StartMm + section.EndMm) / 2 - station)).First();
                return top + (maxElevation - profile.BottomElevationMm) * beamHeight / elevationRange;
            }
            double dimY = top - 36;
            double axisY = Math.Min(h - 24, bottom + 96);
            double scale = (right - left) / beamLengthMm;
            var neutral = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            var thin = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            var concrete = new SolidColorBrush(Color.FromRgb(239, 242, 246));
            var concreteEdge = new SolidColorBrush(Color.FromRgb(112, 128, 148));
            var stirrup = new SolidColorBrush(Color.FromRgb(96, 106, 119));

            var visibleSupports = (supports ?? new List<ReinforceStraightBeamsCommand.BeamPreviewSupport>())
                .Where(s => s != null && s.EndMm >= 0 && s.StartMm <= beamLengthMm)
                .OrderBy(s => s.StartMm).ToList();

            var nodes = new List<double> { 0 };
            foreach (var support in visibleSupports)
            {
                double center = (support.StartMm + support.EndMm) * 0.5;
                if (center <= 150 || center >= beamLengthMm - 150) continue;
                if (!nodes.Any(x => Math.Abs(x - center) < 80)) nodes.Add(center);
            }
            nodes.Add(beamLengthMm);
            nodes = nodes.OrderBy(x => x).ToList();
            var cleanNodes = new List<double>();
            foreach (double node in nodes)
                if (cleanNodes.Count == 0 || node - cleanNodes[cleanNodes.Count - 1] > 80) cleanNodes.Add(node);
            if (cleanNodes.Count < 2) cleanNodes = new List<double> { 0, beamLengthMm };

            double X(double mm) => left + Math.Max(0, Math.Min(beamLengthMm, mm)) * scale;
            ReinforceStraightBeamsCommand.BeamPreviewSupport SupportAt(double mm)
            {
                return visibleSupports.OrderBy(s => Math.Abs((s.StartMm + s.EndMm) * 0.5 - mm)).FirstOrDefault(s =>
                    Math.Abs((s.StartMm + s.EndMm) * 0.5 - mm) < Math.Max(350, s.WidthMm * 0.75));
            }
            void AddDoubleArrow(double x1, double x2, double y, string label)
            {
                if (x2 - x1 < 9) return;
                canvas.Children.Add(new Line { X1 = x1, X2 = x2, Y1 = y, Y2 = y, Stroke = neutral, StrokeThickness = 1 });
                canvas.Children.Add(new Polygon
                {
                    Points = new PointCollection { new Point(x1, y), new Point(x1 + 7, y - 3.5), new Point(x1 + 7, y + 3.5) },
                    Fill = neutral
                });
                canvas.Children.Add(new Polygon
                {
                    Points = new PointCollection { new Point(x2, y), new Point(x2 - 7, y - 3.5), new Point(x2 - 7, y + 3.5) },
                    Fill = neutral
                });
                AddCanvasText(canvas, label, (x1 + x2) * 0.5 - 31, y - 19, neutral, 10);
            }
            void AddSpanBadge(double center, int index, bool selected)
            {
                var badge = new Border
                {
                    Width = 72, Height = 22,
                    Background = selected ? new SolidColorBrush(Color.FromRgb(235, 242, 255)) : Brushes.White,
                    BorderBrush = selected ? Blue : thin,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Child = new TextBlock
                    {
                        Text = "Nhịp " + (index + 1),
                        Foreground = selected ? Blue : Brushes.Black,
                        FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
                        FontSize = 10,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center
                    }
                };
                Canvas.SetLeft(badge, center - 36); Canvas.SetTop(badge, 4); canvas.Children.Add(badge);
            }
            void AddLeader(string label, double anchorX, double anchorY, double labelX, double labelY, Brush color)
            {
                var leader = new Polyline
                {
                    Stroke = color, StrokeThickness = 1,
                    Points = new PointCollection { new Point(anchorX, anchorY), new Point(anchorX + 12, labelY + 10), new Point(labelX, labelY + 10) }
                };
                canvas.Children.Add(leader);
                AddCanvasText(canvas, label, labelX + 3, labelY, color, 10);
            }

            double supportBottom = Math.Min(axisY - 28, bottom + 76);
            for (int i = 0; i < cleanNodes.Count; i++)
            {
                double x = X(cleanNodes[i]);
                var support = SupportAt(cleanNodes[i]);
                if (support == null || support.Kind != "Cột giao") continue;
                double widthMm = support?.WidthMm > 0 ? support.WidthMm : 300;
                double supportWidth = Math.Max(22, Math.Min(46, widthMm * scale));
                var column = new Rectangle
                {
                    Width = supportWidth,
                    Height = Math.Max(20, supportBottom - bottom + 12),
                    Fill = new SolidColorBrush(Color.FromRgb(232, 236, 241)),
                    Stroke = concreteEdge, StrokeThickness = 1
                };
                Canvas.SetLeft(column, x - supportWidth / 2); Canvas.SetTop(column, bottom - 2); canvas.Children.Add(column);

                var axis = new Line { X1 = x, X2 = x, Y1 = dimY + 9, Y2 = axisY - 12, Stroke = thin, StrokeThickness = 0.8, StrokeDashArray = new DoubleCollection { 3, 3 } };
                canvas.Children.Add(axis);
            }

            foreach (var profile in actualSections)
            {
                double x1 = X(profile.StartMm), x2 = X(profile.EndMm);
                var body = new Polygon
                {
                    Points = new PointCollection
                    {
                        new Point(x1, SectionTop((profile.StartMm + profile.EndMm) * 0.5)),
                        new Point(x2, SectionTop((profile.StartMm + profile.EndMm) * 0.5)),
                        new Point(x2, SectionBottom((profile.StartMm + profile.EndMm) * 0.5)),
                        new Point(x1, SectionBottom((profile.StartMm + profile.EndMm) * 0.5))
                    },
                    Fill = concrete, Stroke = concreteEdge, StrokeThickness = 1.2
                };
                canvas.Children.Add(body);
                double labelY = SectionTop((profile.StartMm + profile.EndMm) * 0.5) - 15;
                AddCanvasText(canvas, profile.WidthMm.ToString("0") + "×" + profile.HeightMm.ToString("0"),
                    (x1 + x2) * 0.5 - 24, labelY, neutral, 8);
            }

            if (highlightEndMm > highlightStartMm && highlightStartMm >= 0)
            {
                double x1 = X(highlightStartMm), x2 = X(highlightEndMm);
                var selectedSpan = new Rectangle
                {
                    Width = Math.Max(0, x2 - x1), Height = bottom - top,
                    Fill = new SolidColorBrush(Color.FromArgb(58, 39, 99, 245)),
                    Stroke = Blue, StrokeThickness = 1.2
                };
                Canvas.SetLeft(selectedSpan, x1); Canvas.SetTop(selectedSpan, top); canvas.Children.Add(selectedSpan);
            }

            for (double px = left + 10; px < right - 4; px += 22)
                for (double py = top + 8 + ((int)((px - left) / 22) % 2) * 7; py < bottom - 5; py += 18)
                {
                    var grain = new Ellipse { Width = 1.5, Height = 1.5, Fill = new SolidColorBrush(Color.FromRgb(195, 202, 211)) };
                    Canvas.SetLeft(grain, px); Canvas.SetTop(grain, py); canvas.Children.Add(grain);
                }

            for (int i = 0; i < cleanNodes.Count; i++)
            {
                double x = X(cleanNodes[i]);
                var support = SupportAt(cleanNodes[i]);
                if (support == null || support.Kind != "Cột giao") continue;
                double supportWidthMm = support?.WidthMm > 0 ? support.WidthMm : 300;
                double supportWidth = Math.Max(22, Math.Min(46, supportWidthMm * scale));
                var columnCap = new Rectangle { Width = supportWidth, Height = 10, Fill = concrete, Stroke = concreteEdge, StrokeThickness = 1 };
                Canvas.SetLeft(columnCap, x - supportWidth / 2); Canvas.SetTop(columnCap, top - 9); canvas.Children.Add(columnCap);
                double columnDimY = bottom + (tiesVisible ? 48 : 24);
                canvas.Children.Add(new Line { X1 = x - supportWidth / 2, X2 = x - supportWidth / 2, Y1 = bottom + 3, Y2 = columnDimY, Stroke = thin, StrokeThickness = 0.7 });
                canvas.Children.Add(new Line { X1 = x + supportWidth / 2, X2 = x + supportWidth / 2, Y1 = bottom + 3, Y2 = columnDimY, Stroke = thin, StrokeThickness = 0.7 });
                AddDoubleArrow(x - supportWidth / 2, x + supportWidth / 2, columnDimY, supportWidthMm.ToString("0") + " mm");
            }

            var tieStations = new List<double>();
            if (tiesVisible)
            {
                double tieDiameterMm = rows.First(r => r.Key == "tie").Type == null ? 10 :
                    UnitUtils.ConvertFromInternalUnits(rows.First(r => r.Key == "tie").Type.BarNominalDiameter, UnitTypeId.Millimeters);
                double edgeMm = Math.Max(0, coverMm + tieDiameterMm / 2.0);
                var columns = visibleSupports.Where(s => s.Kind == "Cột giao" && s.EndMm > 0 && s.StartMm < beamLengthMm)
                    .OrderBy(s => s.StartMm).ToList();
                var tieSpans = new List<Tuple<double, double>>();
                if (columns.Count == 0)
                {
                    tieSpans.Add(Tuple.Create(edgeMm, Math.Max(edgeMm, beamLengthMm - edgeMm)));
                }
                else
                {
                    double start = edgeMm;
                    double minGap = Math.Max(50, coverMm * 2);
                    foreach (var support in columns)
                    {
                        double supportStart = Math.Max(0, support.StartMm);
                        double supportEnd = Math.Min(beamLengthMm, support.EndMm);
                        double end = Math.Max(0, supportStart - Math.Max(0, supportOffsetMm));
                        if (end - start >= minGap) tieSpans.Add(Tuple.Create(start, end));
                        start = Math.Min(beamLengthMm, supportEnd + Math.Max(0, supportOffsetMm));
                    }
                    double finalEnd = beamLengthMm - edgeMm;
                    if (finalEnd - start >= minGap) tieSpans.Add(Tuple.Create(start, finalEnd));
                    if (tieSpans.Count == 0) tieSpans.Add(Tuple.Create(edgeMm, Math.Max(edgeMm, beamLengthMm - edgeMm)));
                }
                foreach (Tuple<double, double> span in tieSpans)
                {
                    double start = span.Item1, end = span.Item2;
                    double zone = Math.Min(zoneLength, (end - start) / 2);
                    if (zonedTies && zone > 0)
                    {
                        AddRepresentativeStations(tieStations, start, start + zone, 3);
                        if (end - zone - (start + zone) > 0.1)
                            AddRepresentativeStations(tieStations, start + zone, end - zone, 3);
                        AddRepresentativeStations(tieStations, end - zone, end, 3);
                    }
                    else AddRepresentativeStations(tieStations, start, end, 9);
                }
                foreach (double mm in tieStations.Distinct())
                {
                    double x = X(mm);
                    canvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = SectionTop(mm) + 8, Y2 = SectionBottom(mm) - 8, Stroke = stirrup, StrokeThickness = 0.85 });
                }
            }

            void DrawLayer(string key, double y, Brush color, bool topLayer)
            {
                BarRow row = rows.First(r => r.Key == key);
                if (row.Count <= 0) return;
                var dimensionedSegments = new HashSet<Tuple<double, double>>();
                var bars = row.Bars.Count > 0
                    ? row.Bars.Take(12).ToList()
                    : new List<BarGeometry> { new BarGeometry { StartMm = row.StartMm, EndMm = row.EndMm, LeftHookMm = row.LeftHookMm, RightHookMm = row.RightHookMm } };
                for (int i = 0; i < bars.Count; i++)
                {
                    BarGeometry bar = bars[i];
                    if (bar.EndMm <= bar.StartMm) continue;
                    bool splitBySpan = key == "top2" || key == "bottom1" || key == "bottom2";
                    var segments = new List<Tuple<double, double>>();
                    if (splitBySpan)
                    {
                        for (int span = 0; span < cleanNodes.Count - 1; span++)
                        {
                            double segmentStart = Math.Max(bar.StartMm, cleanNodes[span]);
                            double segmentEnd = Math.Min(bar.EndMm, cleanNodes[span + 1]);
                            if (segmentEnd - segmentStart >= 50) segments.Add(Tuple.Create(segmentStart, segmentEnd));
                        }
                    }
                    else segments.Add(Tuple.Create(bar.StartMm, bar.EndMm));
                    double offset = (i - (bars.Count - 1) / 2.0) * 1.8;
                    double hook = 8;
                    foreach (Tuple<double, double> segment in segments)
                    {
                        double x1 = X(segment.Item1), x2 = X(segment.Item2);
                        double topLeg = topLayer ? Math.Min(Math.Max(0, bar.HookDownMm * scale), Math.Max(0, bottom - y - 4)) : hook;
                        double leftLeg = key == "top" || segment.Item1 <= 1 ? topLeg : 0;
                        double rightLeg = key == "top" || segment.Item2 >= beamLengthMm - 1 ? topLeg : 0;
                        double leftFoot = key == "top" ? Math.Min(Math.Max(0, bar.LeftHookMm * scale), Math.Max(0, x2 - x1 - 5)) : 0;
                        double rightFoot = key == "top" ? Math.Min(Math.Max(0, bar.RightHookMm * scale), Math.Max(0, x2 - x1 - 5)) : 0;
                        var steel = new Polyline
                        {
                            Stroke = color,
                            StrokeThickness = row == activeRow && i == row.SelectedBarIndex ? 3 : (key == "top" || key == "bottom" ? 2.2 : 1.5),
                            StrokeLineJoin = PenLineJoin.Round,
                            Points = topLayer
                                ? new PointCollection { new Point(x1 + leftFoot, y + leftLeg), new Point(x1, y + leftLeg), new Point(x1, y), new Point(x2, y), new Point(x2, y + rightLeg), new Point(x2 - rightFoot, y + rightLeg) }
                                : new PointCollection { new Point(x1, y - hook), new Point(x1, y), new Point(x2, y), new Point(x2, y - hook) }
                        };
                        canvas.Children.Add(steel);
                            if ((key == "top1" || key == "top2" || key == "bottom1" || key == "bottom2") &&
                                dimensionedSegments.Add(segment))
                            {
                                double layerOffset = key.EndsWith("2", StringComparison.Ordinal) ? 44 : 0;
                                double dimensionY = topLayer ? top - 32 - layerOffset : bottom + 38 + layerOffset;
                                double startAxis = cleanNodes.OrderBy(node => Math.Abs(node - segment.Item1)).First();
                                double endAxis = cleanNodes.OrderBy(node => Math.Abs(node - segment.Item2)).First();
                                if (Math.Abs(startAxis - segment.Item1) > 1)
                                {
                                    canvas.Children.Add(new Line { X1 = X(startAxis), X2 = X(startAxis), Y1 = y, Y2 = dimensionY, Stroke = color, StrokeThickness = 0.6 });
                                    canvas.Children.Add(new Line { X1 = x1, X2 = x1, Y1 = y, Y2 = dimensionY, Stroke = color, StrokeThickness = 0.6 });
                                    DrawDimension(canvas, Math.Min(X(startAxis), x1), Math.Max(X(startAxis), x1),
                                        dimensionY, Math.Abs(segment.Item1 - startAxis).ToString("0") + " mm", -4);
                                }
                                if (Math.Abs(endAxis - segment.Item2) > 1)
                                {
                                    double endDimensionY = dimensionY - (startAxis == endAxis ? 18 : 0);
                                    canvas.Children.Add(new Line { X1 = X(endAxis), X2 = X(endAxis), Y1 = y, Y2 = endDimensionY, Stroke = color, StrokeThickness = 0.6 });
                                    canvas.Children.Add(new Line { X1 = x2, X2 = x2, Y1 = y, Y2 = endDimensionY, Stroke = color, StrokeThickness = 0.6 });
                                    DrawDimension(canvas, Math.Min(X(endAxis), x2), Math.Max(X(endAxis), x2),
                                        endDimensionY, Math.Abs(segment.Item2 - endAxis).ToString("0") + " mm", -4);
                                }
                            }
                        if (row == activeRow && i == row.SelectedBarIndex)
                        {
                            if (topLayer && leftLeg > 0)
                                DrawVerticalDimension(canvas, x1 - 16, y, y + leftLeg, bar.HookDownMm.ToString("0") + " mm");
                            if (topLayer && rightLeg > 0)
                                DrawVerticalDimension(canvas, x2 + 16, y, y + rightLeg, bar.HookDownMm.ToString("0") + " mm");
                            var handleXs = new List<double>();
                            if (!splitBySpan || Math.Abs(segment.Item1 - bar.StartMm) < 1) handleXs.Add(x1);
                            if (!splitBySpan || Math.Abs(segment.Item2 - bar.EndMm) < 1) handleXs.Add(x2);
                            foreach (double handleX in handleXs)
                            {
                                var handle = new Ellipse { Width = 12, Height = 12, Fill = Brushes.White, Stroke = color, StrokeThickness = 2, ToolTip = "Kéo để chỉnh chiều dài" };
                                Canvas.SetLeft(handle, handleX - 6); Canvas.SetTop(handle, y + offset - 6);
                                canvas.Children.Add(handle);
                            }
                        }
                    }
                }
            }

            double topMainY = top + (maxElevation - commonTopElevation) * beamHeight / elevationRange + 16;
            double bottomMainY = top + (maxElevation - commonBottomElevation) * beamHeight / elevationRange - 16;
            DrawLayer("top1", topMainY + 15, MutedBlue, true);
            DrawLayer("top2", topMainY + 28, MutedBlue, true);
            DrawLayer("top", topMainY, Blue, true);
            DrawLayer("bottom1", bottomMainY - 15, MutedBlue, false);
            DrawLayer("bottom2", bottomMainY - 28, MutedBlue, false);
            DrawLayer("bottom", bottomMainY, Teal, false);

            if (rows.First(r => r.Key == "side").Count > 0)
                canvas.Children.Add(new Line { X1 = left + 8, X2 = right - 8, Y1 = (top + bottom) / 2, Y2 = (top + bottom) / 2, Stroke = Teal, StrokeThickness = 1.2 });

            if (tiesVisible)
            {
                BarRow tieRow = rows.First(r => r.Key == "tie");
                string spacing = zonedTies
                    ? "a" + endSpacing.ToString("0") + " / a" + middleSpacing.ToString("0") + " (gối / giữa)"
                    : "a" + uniformSpacing.ToString("0");
                AddLeader(ShortType(tieRow.Type) + " " + spacing + " (đai)", X(beamLengthMm * 0.52), top + 5, X(beamLengthMm * 0.52) + 34, top - 1, stirrup);
            }
            BarRow topRow = rows.First(r => r.Key == "top");
            BarRow bottomRow = rows.First(r => r.Key == "bottom");
            string topLabel = topRow.Count + ShortType(topRow.Type) + " (trên)";
            string bottomLabel = bottomRow.Count + ShortType(bottomRow.Type) + " (dưới)";
            AddLeader(topLabel, X(beamLengthMm * 0.47), topMainY, X(beamLengthMm * 0.47) + 42, top + 1, Blue);
            AddLeader(bottomLabel, X(beamLengthMm * 0.42), bottomMainY, X(beamLengthMm * 0.42) + 42, bottom - 2, Teal);

            for (int i = 0; i < cleanNodes.Count - 1; i++)
            {
                double start = cleanNodes[i], end = cleanNodes[i + 1];
                double x1 = X(start), x2 = X(end), center = (x1 + x2) * 0.5;
                bool selected = highlightEndMm > highlightStartMm &&
                    Math.Abs(start - highlightStartMm) < 100 && Math.Abs(end - highlightEndMm) < 100;
                AddSpanBadge(center, i, selected);
                canvas.Children.Add(new Line { X1 = x1, X2 = x1, Y1 = dimY + 2, Y2 = top - 3, Stroke = thin, StrokeThickness = 0.8 });
                canvas.Children.Add(new Line { X1 = x2, X2 = x2, Y1 = dimY + 2, Y2 = top - 3, Stroke = thin, StrokeThickness = 0.8 });
                AddDoubleArrow(x1 + 3, x2 - 3, dimY, (end - start).ToString("N0") + " mm");
            }
            if (tiesVisible && tieStations.Count > 1)
            {
                double rangeStart = highlightEndMm > highlightStartMm && highlightStartMm >= 0 ? highlightStartMm : 0;
                double rangeEnd = highlightEndMm > highlightStartMm && highlightStartMm >= 0 ? highlightEndMm : beamLengthMm;
                var visibleTieStations = tieStations.Distinct().Where(mm => mm >= rangeStart && mm <= rangeEnd).OrderBy(mm => mm).ToList();
                if (visibleTieStations.Count < 2) visibleTieStations = tieStations.Distinct().OrderBy(mm => mm).ToList();
                var gap = visibleTieStations.Zip(visibleTieStations.Skip(1), (a, b) => new { A = a, B = b }).OrderByDescending(pair => pair.B - pair.A).FirstOrDefault();
                if (gap != null)
                {
                    double y = bottom + 19, x1 = X(gap.A), x2 = X(gap.B);
                    canvas.Children.Add(new Line { X1 = x1, X2 = x1, Y1 = bottom - 3, Y2 = y + 3, Stroke = stirrup, StrokeThickness = 0.8 });
                    canvas.Children.Add(new Line { X1 = x2, X2 = x2, Y1 = bottom - 3, Y2 = y + 3, Stroke = stirrup, StrokeThickness = 0.8 });
                    AddDoubleArrow(x1, x2, y, (gap.B - gap.A).ToString("0") + " mm");
                }
            }
            DrawVerticalDimension(canvas, left - 25, top, bottom, beamHeightMm.ToString("0") + " mm");

            var sectionBoundaries = new SortedSet<double> { 0, beamLengthMm };
            foreach (var profile in actualSections)
            {
                sectionBoundaries.Add(Math.Max(0, Math.Min(beamLengthMm, profile.StartMm)));
                sectionBoundaries.Add(Math.Max(0, Math.Min(beamLengthMm, profile.EndMm)));
            }
            foreach (BarRow row in rows.Where(row => row.Count > 0 && row.Key != "tie" && row.Key != "side"))
                foreach (BarGeometry bar in row.Bars)
                {
                    sectionBoundaries.Add(Math.Max(0, Math.Min(beamLengthMm, bar.StartMm)));
                    sectionBoundaries.Add(Math.Max(0, Math.Min(beamLengthMm, bar.EndMm)));
                }
            foreach (double node in cleanNodes) sectionBoundaries.Add(node);
            if (zonedTies)
                for (int spanIndex = 0; spanIndex + 1 < cleanNodes.Count; spanIndex++)
                {
                    double spanStart = cleanNodes[spanIndex], spanEnd = cleanNodes[spanIndex + 1];
                    double zone = Math.Min(zoneLength, (spanEnd - spanStart) / 2);
                    sectionBoundaries.Add(spanStart + zone);
                    sectionBoundaries.Add(spanEnd - zone);
                }
            var sectionKeys = new HashSet<string>();
            var boundaries = sectionBoundaries.ToList();
            int sectionCount = 0;
            var sectionStations = new List<double>();
            for (int interval = 0; interval + 1 < boundaries.Count; interval++)
            {
                if (boundaries[interval + 1] - boundaries[interval] < 1) continue;
                double station = (boundaries[interval] + boundaries[interval + 1]) / 2;
                var steelKeys = new List<string>();
                foreach (BarRow row in rows.Where(row => row.Count > 0 && row.Key != "tie"))
                {
                    var crossing = row.Bars.Where(bar => bar.StartMm <= station && bar.EndMm >= station).ToList();
                    if (row.Key == "side")
                        steelKeys.Add("side/" + row.Count + "/" + row.Type?.Id);
                    else if (row.HasMultipleIntervals && crossing.Count > 0)
                        steelKeys.Add(row.Key + "/" + row.Count + "/" + crossing[0].Type?.Id + "/" +
                            crossing[0].Position.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    else
                        foreach (var group in crossing.GroupBy(bar => new { TypeId = bar.Type?.Id, bar.Position }))
                            steelKeys.Add(row.Key + "/" + group.Count() + "/" + group.Key.TypeId + "/" +
                                group.Key.Position.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                }
                int containingSpan = Math.Max(0, cleanNodes.FindLastIndex(node => node <= station));
                containingSpan = Math.Min(containingSpan, cleanNodes.Count - 2);
                var actualProfile = actualSections.FirstOrDefault(profile => station >= profile.StartMm && station <= profile.EndMm)
                    ?? actualSections.OrderBy(profile => Math.Abs((profile.StartMm + profile.EndMm) / 2 - station)).First();
                steelKeys.Add("section/" + actualProfile.WidthMm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "/" +
                    actualProfile.HeightMm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                double currentZone = Math.Min(zoneLength, (cleanNodes[containingSpan + 1] - cleanNodes[containingSpan]) / 2);
                bool inEndZone = station < cleanNodes[containingSpan] + currentZone ||
                    station > cleanNodes[containingSpan + 1] - currentZone;
                double sectionSpacing = zonedTies ? (inEndZone ? endSpacing : middleSpacing) : uniformSpacing;
                steelKeys.Add("tie/" + rows.First(row => row.Key == "tie").Type?.Id + "/" +
                    sectionSpacing.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                string sectionKey = string.Join("|", steelKeys.OrderBy(key => key, StringComparer.Ordinal));
                if (!sectionKeys.Add(sectionKey)) continue;
                sectionCount++;
                sectionStations.Add(station);
                double sectionX = X(station);
                canvas.Children.Add(new Line { X1 = sectionX, X2 = sectionX, Y1 = top - 12, Y2 = bottom + 12,
                    Stroke = Blue, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 5, 3 } });
                AddCanvasText(canvas, sectionCount + "-" + sectionCount, sectionX - 10, top - 27, Blue, 10);
                AddCanvasText(canvas, sectionCount + "-" + sectionCount, sectionX - 10, bottom + 12, Blue, 10);
            }
            AddCanvasText(canvas, "Mặt cắt khác nhau: " + sectionCount, left, Math.Max(4, top - 55), Blue, 11);
            canvas.Tag = sectionStations;

            for (int i = 0; i < cleanNodes.Count; i++)
            {
                var axisSupport = SupportAt(cleanNodes[i]);
                if (axisSupport == null || axisSupport.Kind != "Cột giao" || string.IsNullOrWhiteSpace(axisSupport.AxisLabel)) continue;
                double x = X(cleanNodes[i]);
                var leader = new Line { X1 = x, X2 = x, Y1 = supportBottom, Y2 = axisY - 10, Stroke = thin, StrokeThickness = 0.8 };
                canvas.Children.Add(leader);
                var bubble = new Ellipse { Width = 22, Height = 22, Fill = Brushes.White, Stroke = neutral, StrokeThickness = 1 };
                Canvas.SetLeft(bubble, x - 11); Canvas.SetTop(bubble, axisY - 11); canvas.Children.Add(bubble);
                string axisName = SupportAt(cleanNodes[i])?.AxisLabel;
                double bubbleWidth = axisName.Length > 2 ? 42 : 28;
                bubble.Width = bubbleWidth; Canvas.SetLeft(bubble, x - bubbleWidth / 2);
                var axisText = new TextBlock { Text = axisName, Width = bubbleWidth, Height = 22, FontSize = 10, Foreground = Brushes.Black, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ToolTip = SupportAt(cleanNodes[i])?.Label };
                Canvas.SetLeft(axisText, x - bubbleWidth / 2); Canvas.SetTop(axisText, axisY - 11); canvas.Children.Add(axisText);
            }
        }
        private static void DrawHook(Canvas canvas,double x,double y,double lengthMm,double scale,bool leftSide,double direction)
        {
            double length=Math.Max(0,Math.Min(400,lengthMm))*scale;
            if(length<=0)return;
            double endX=x+(leftSide?length:-length);
            var hook=new Polyline{Stroke=Blue,StrokeThickness=1.5,Points=new PointCollection{new System.Windows.Point(x,y),new System.Windows.Point(x,y+direction*8),new System.Windows.Point(endX,y+direction*8)}};
            canvas.Children.Add(hook);
        }

        private static void DrawBarDimension(Canvas canvas, double x1, double x2, double y, string label)
        {
            if (x2 - x1 < 20) return;
            var line = new Line { X1 = x1, X2 = x2, Y1 = y, Y2 = y, Stroke = new SolidColorBrush(Color.FromRgb(105,126,158)), StrokeThickness = 0.8 };
            canvas.Children.Add(line);
            foreach (double x in new[] { x1, x2 })
            {
                var tick = new Line { X1 = x, X2 = x, Y1 = y - 3, Y2 = y + 3, Stroke = new SolidColorBrush(Color.FromRgb(105,126,158)), StrokeThickness = 0.8 };
                canvas.Children.Add(tick);
            }
            AddCanvasText(canvas, label, (x1 + x2) / 2 - 20, y - 12, new SolidColorBrush(Color.FromRgb(72,96,134)), 8);
        }

        private static void AddCanvasText(Canvas canvas, string text, double left, double top, Brush foreground, double size)
        {
            var label = new TextBlock { Text = text, Foreground = foreground, FontSize = size, Background = Brushes.White };
            Canvas.SetLeft(label, left); Canvas.SetTop(label, top); canvas.Children.Add(label);
        }
    }
}

