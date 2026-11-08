using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SDK_HD.KStruct.FootingRebar
{
    public partial class FootingRebarWindow : Window
    {
        private FootingModel _model;
        public CoverSettings OutputCover { get; private set; }
        public BottomLayerSettings OutputBottom { get; private set; }
        public FootingRebarFullSettings OutputFull { get; private set; }

        public FootingRebarWindow(FootingModel model, List<double> availableDiameters, bool hasLeanConcrete,
            FootingRebarPreference saved = null, string historySummary = null, bool remember = true,
            int ownedCount = 0, int otherCount = 0, bool restoreSavedLean = false)
        {
            InitializeComponent();
            _model = model;
            Rect workArea = SystemParameters.WorkArea;
            Width = workArea.Width > 320 ? Math.Min(1380, workArea.Width - 32) : 1380;
            Height = workArea.Height > 320 ? Math.Min(900, workArea.Height - 32) : 900;
            MinWidth = Math.Min(1040, Width); MinHeight = Math.Min(660, Height);
            TxtHostName.Text = string.IsNullOrWhiteSpace(model.Name) ? "Móng từ mô hình" : model.Name;
            TxtHostName.ToolTip = TxtHostName.Text;

            TxtLengthX.Text = _model.LengthX.ToString("0");
            TxtWidthY.Text = _model.WidthY.ToString("0");
            TxtHeight.Text = _model.Height.ToString("0");
            ChkHasLeanConcrete.IsChecked = hasLeanConcrete;
            TxtCoverBottom.Text = hasLeanConcrete ? "50" : "70";

            var diams = availableDiameters != null && availableDiameters.Count > 0 
                ? availableDiameters 
                : new List<double> { 10, 12, 14, 16, 18, 20, 22, 25 };

            CboDiameterX.ItemsSource = diams;
            CboDiameterY.ItemsSource = diams;
            CboDiameterTop.ItemsSource = diams;
            CboDiameterTie.ItemsSource = diams;

            bool isPileCap = _model.FootingType == FootingType.PileCap;
            CboFootingType.SelectedIndex = isPileCap ? 1 : 0;

            double defaultBottomD = isPileCap ? (diams.Contains(14) ? 14 : diams.First()) : (diams.Contains(12) ? 12 : diams.First());
            CboDiameterX.SelectedItem = defaultBottomD;
            CboDiameterY.SelectedItem = defaultBottomD;
            CboDiameterTop.SelectedItem = diams.Contains(12) ? 12 : diams.First();
            CboDiameterTie.SelectedItem = diams.Contains(10) ? 10 : diams.First();

            CboHookX.SelectedIndex = 0; // Bẻ neo LÊN
            CboHookY.SelectedIndex = 0; // Bẻ neo LÊN
            CboTopHook.SelectedIndex = 0; // Bẻ neo XUỐNG
            TxtHookLengthX.Text = "200";
            TxtHookLengthY.Text = "200";
            TxtTopHookLength.Text = "200";

            if (isPileCap)
            {
                TxtSpacingX.Text = "150";
                TxtSpacingY.Text = "150";
                ChkEnableTop.IsChecked = true;
                ExpanderTop.IsExpanded = true;
            }

            if (_model.Height > 700)
            {
                ChkEnableSideTies.IsChecked = true;
                ExpanderSideTies.IsExpanded = true;
            }

            InitializeHistory(saved, historySummary, remember, ownedCount, otherCount, restoreSavedLean);
            RenderPreview();
        }

        private void OnFootingTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || _restoringPreference) return;
            bool isPileCap = CboFootingType.SelectedIndex == 1;
            _model = new FootingModel(_model.LengthX, _model.WidthY, _model.Height,
                isPileCap ? FootingType.PileCap : FootingType.SpreadFooting, _model.Name);

            if (isPileCap)
            {
                CboHookX.SelectedIndex = 0;
                CboHookY.SelectedIndex = 0;
                CboTopHook.SelectedIndex = 0;
                ChkEnableTop.IsChecked = true;
                ExpanderTop.IsExpanded = true;
            }

            RenderPreview();
        }

        private void OnLeanConcreteChanged(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || _restoringPreference) return;
            bool hasLean = ChkHasLeanConcrete.IsChecked == true;
            if (hasLean && TxtCoverBottom.Text == "70")
            {
                TxtCoverBottom.Text = "50";
            }
            else if (!hasLean && (TxtCoverBottom.Text == "50" || TxtCoverBottom.Text == "35"))
            {
                TxtCoverBottom.Text = "70";
            }
            RenderPreview();
        }

        private void OnParameterChanged(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || _restoringPreference) return;
            RenderPreview();
        }

        private void RenderPreview()
        {
            PreviewCanvas.Children.Clear();
            if (!TryParseInputs(out FootingRebarFullSettings full)) return;

            try
            {
                var builder = new BottomLayerBuilder();
                RebarPlan plan = builder.Build(_model, full);

                CoverSettings cover = full.Cover;

                // Tỷ lệ Scale vẽ vừa Canvas (640x420)
                double margin = 45;
                double drawWidth = PreviewCanvas.Width - 2 * margin;
                double drawHeight = PreviewCanvas.Height - 2 * margin;

                double scaleX = drawWidth / _model.LengthX;
                double scaleZ = drawHeight / (_model.Height + (cover.HasLeanConcrete ? 100 : 0));
                double scale = Math.Min(scaleX, scaleZ);

                double totalH = _model.Height + (cover.HasLeanConcrete ? 100 : 0);
                double offsetX = (PreviewCanvas.Width - _model.LengthX * scale) / 2.0;
                double offsetZ = (PreviewCanvas.Height - totalH * scale) / 2.0;

                double ToCanvasX(double mmX) => offsetX + mmX * scale;
                double ToCanvasZ(double mmZ) => PreviewCanvas.Height - (offsetZ + mmZ * scale);

                // 1. Vẽ lớp bê tông lót (nếu có)
                if (cover.HasLeanConcrete)
                {
                    double leanH = 100;
                    double leanOverhang = 100;
                    var leanRect = new Rectangle
                    {
                        Width = (_model.LengthX + 2 * leanOverhang) * scale,
                        Height = leanH * scale,
                        Stroke = Brushes.Goldenrod,
                        StrokeDashArray = new DoubleCollection { 3, 2 },
                        StrokeThickness = 1.5,
                        Fill = new SolidColorBrush(Color.FromArgb(50, 255, 193, 7))
                    };
                    Canvas.SetLeft(leanRect, ToCanvasX(-leanOverhang));
                    Canvas.SetTop(leanRect, ToCanvasZ(0));
                    PreviewCanvas.Children.Add(leanRect);
                }

                // 2. Vẽ bê tông móng
                var footingRect = new Rectangle
                {
                    Width = _model.LengthX * scale,
                    Height = _model.Height * scale,
                    Stroke = Brushes.Black,
                    StrokeThickness = 2,
                    Fill = new SolidColorBrush(Color.FromRgb(242, 244, 247))
                };
                Canvas.SetLeft(footingRect, ToCanvasX(0));
                Canvas.SetTop(footingRect, ToCanvasZ(_model.Height));
                PreviewCanvas.Children.Add(footingRect);

                // 3. Đường bao lớp bảo vệ (Dash line)
                var coverRect = new Rectangle
                {
                    Width = Math.Max(0, (_model.LengthX - 2 * cover.Side) * scale),
                    Height = Math.Max(0, (_model.Height - cover.Bottom - cover.Top) * scale),
                    Stroke = Brushes.LightGray,
                    StrokeDashArray = new DoubleCollection { 4, 3 },
                    StrokeThickness = 1
                };
                Canvas.SetLeft(coverRect, ToCanvasX(cover.Side));
                Canvas.SetTop(coverRect, ToCanvasZ(_model.Height - cover.Top));
                PreviewCanvas.Children.Add(coverRect);

                // 4. Vẽ Thép đáy phương X (Bottom-X - Blue, bẻ móc lên hoặc xuống)
                var setX = plan.Sets.FirstOrDefault(s => s.Name == "Bottom-X");
                if (setX != null && setX.FirstBarPoints != null && setX.FirstBarPoints.Count >= 2)
                {
                    var polyline = new Polyline
                    {
                        Stroke = Brushes.RoyalBlue,
                        StrokeThickness = Math.Max(2.5, setX.Diameter * scale),
                        StrokeLineJoin = PenLineJoin.Round
                    };
                    foreach (var pt in setX.FirstBarPoints)
                    {
                        polyline.Points.Add(new Point(ToCanvasX(pt.X), ToCanvasZ(pt.Z)));
                    }
                    PreviewCanvas.Children.Add(polyline);
                }

                // 5. Vẽ Thép đáy phương Y (Bottom-Y - Chấm tròn Red)
                var setY = plan.Sets.FirstOrDefault(s => s.Name == "Bottom-Y");
                if (setY != null && setY.FirstBarPoints != null && setY.FirstBarPoints.Count > 0)
                {
                    int zIdx = setY.FirstBarPoints.Count > 2 ? 1 : 0;
                    double zCenter = setY.FirstBarPoints[zIdx].Z;
                    double radiusPx = Math.Max(3, (setY.Diameter / 2.0) * scale);

                    double xFirst = cover.Side + setY.Diameter / 2.0;
                    double span = _model.LengthX - 2 * xFirst;
                    SpacingCalculator.Compute(span, full.Bottom.AlongY.Spacing, full.Bottom.AlongY.Mode, out int count, out double step);

                    for (int i = 0; i < count; i++)
                    {
                        double xMm = xFirst + i * step;
                        var circle = new Ellipse
                        {
                            Width = radiusPx * 2,
                            Height = radiusPx * 2,
                            Fill = Brushes.Crimson,
                            Stroke = Brushes.DarkRed,
                            StrokeThickness = 1
                        };
                        Canvas.SetLeft(circle, ToCanvasX(xMm) - radiusPx);
                        Canvas.SetTop(circle, ToCanvasZ(zCenter) - radiusPx);
                        PreviewCanvas.Children.Add(circle);
                    }
                }

                // 6. Vẽ Thép lớp trên (Top Rebar - Orange, bẻ móc xuống hoặc lên)
                var setTopX = plan.Sets.FirstOrDefault(s => s.Name == "Top-X");
                if (setTopX != null && setTopX.FirstBarPoints != null && setTopX.FirstBarPoints.Count >= 2)
                {
                    var polyline = new Polyline
                    {
                        Stroke = Brushes.DarkOrange,
                        StrokeThickness = Math.Max(2, setTopX.Diameter * scale),
                        StrokeLineJoin = PenLineJoin.Round
                    };
                    foreach (var pt in setTopX.FirstBarPoints)
                    {
                        polyline.Points.Add(new Point(ToCanvasX(pt.X), ToCanvasZ(pt.Z)));
                    }
                    PreviewCanvas.Children.Add(polyline);
                }

                var setTopY = plan.Sets.FirstOrDefault(s => s.Name == "Top-Y");
                if (setTopY != null && setTopY.FirstBarPoints != null && setTopY.FirstBarPoints.Count > 0)
                {
                    int zIdx = setTopY.FirstBarPoints.Count > 2 ? 1 : 0;
                    double zCenter = setTopY.FirstBarPoints[zIdx].Z;
                    double radiusPx = Math.Max(2.5, (setTopY.Diameter / 2.0) * scale);
                    double xFirst = cover.Side + setTopY.Diameter / 2.0;
                    double span = _model.LengthX - 2 * xFirst;
                    SpacingCalculator.Compute(span, full.Top.AlongY.Spacing, full.Top.AlongY.Mode, out int count, out double step);

                    for (int i = 0; i < count; i++)
                    {
                        double xMm = xFirst + i * step;
                        var circle = new Ellipse
                        {
                            Width = radiusPx * 2,
                            Height = radiusPx * 2,
                            Fill = Brushes.DarkOrange,
                            Stroke = Brushes.OrangeRed,
                            StrokeThickness = 1
                        };
                        Canvas.SetLeft(circle, ToCanvasX(xMm) - radiusPx);
                        Canvas.SetTop(circle, ToCanvasZ(zCenter) - radiusPx);
                        PreviewCanvas.Children.Add(circle);
                    }
                }

                // 7. Vẽ Thép đai cấu tạo hông (Side Ties - Purple)
                var setTies = plan.Sets.FirstOrDefault(s => s.Name == "SideTies");
                if (setTies != null)
                {
                    double tieRadius = Math.Max(2, (setTies.Diameter / 2.0) * scale);
                    for (int i = 0; i < setTies.Count; i++)
                    {
                        double zMm = setTies.FirstStart.Z + i * setTies.Step.Z;
                        double leftX = cover.Side + setTies.Diameter / 2.0;
                        double rightX = _model.LengthX - cover.Side - setTies.Diameter / 2.0;

                        var dotLeft = new Ellipse
                        {
                            Width = tieRadius * 2,
                            Height = tieRadius * 2,
                            Fill = Brushes.Purple,
                            Stroke = Brushes.Indigo,
                            StrokeThickness = 1
                        };
                        Canvas.SetLeft(dotLeft, ToCanvasX(leftX) - tieRadius);
                        Canvas.SetTop(dotLeft, ToCanvasZ(zMm) - tieRadius);
                        PreviewCanvas.Children.Add(dotLeft);

                        var dotRight = new Ellipse
                        {
                            Width = tieRadius * 2,
                            Height = tieRadius * 2,
                            Fill = Brushes.Purple,
                            Stroke = Brushes.Indigo,
                            StrokeThickness = 1
                        };
                        Canvas.SetLeft(dotRight, ToCanvasX(rightX) - tieRadius);
                        Canvas.SetTop(dotRight, ToCanvasZ(zMm) - tieRadius);
                        PreviewCanvas.Children.Add(dotRight);
                    }
                }

                string hookXDesc = full.Bottom.AlongX.HookType == HookTypeOption.HookUp ? $"Bẻ lên {full.Bottom.AlongX.HookLength:0}mm"
                                 : full.Bottom.AlongX.HookType == HookTypeOption.HookDown ? $"Bẻ xuống {full.Bottom.AlongX.HookLength:0}mm" : "Thẳng";

                TxtPreviewInfo.Text = $"{(_model.FootingType == FootingType.PileCap ? "Móng cọc / Đài cọc" : "Móng đơn")} | " +
                                      $"{_model.LengthX:0} x {_model.WidthY:0} x {_model.Height:0} mm | " +
                                      $"Neo đáy X: {hookXDesc}";
            }
            catch
            {
                // Bỏ qua lỗi vẽ preview khi đang nhập dở
            }
        }

        private bool TryParseInputs(out FootingRebarFullSettings full)
        {
            try { return ParseInputs(out full); }
            catch (ArgumentException) { full = null; return false; }
        }

        private bool ParseInputs(out FootingRebarFullSettings full)
        {
            full = null;
            if (!double.TryParse(TxtCoverBottom.Text, out double cb) ||
                !double.TryParse(TxtCoverSide.Text, out double cs) ||
                !double.TryParse(TxtCoverTop.Text, out double ct) ||
                !double.TryParse(TxtSpacingX.Text, out double sx) ||
                !double.TryParse(TxtSpacingY.Text, out double sy))
                return false;

            bool enableX = ChkEnableX.IsChecked == true;
            bool enableY = ChkEnableY.IsChecked == true;
            bool hasLean = ChkHasLeanConcrete.IsChecked == true;

            if (enableX && !(CboDiameterX.SelectedItem is double) || enableY && !(CboDiameterY.SelectedItem is double) ||
                ChkEnableTop.IsChecked == true && !(CboDiameterTop.SelectedItem is double) ||
                ChkEnableSideTies.IsChecked == true && !(CboDiameterTie.SelectedItem is double)) return false;

            double dx = CboDiameterX.SelectedItem != null ? (double)CboDiameterX.SelectedItem : 12;
            double dy = CboDiameterY.SelectedItem != null ? (double)CboDiameterY.SelectedItem : 12;

            if (!IsFinite(cb) || !IsFinite(cs) || !IsFinite(ct) || cb < 0 || cs < 0 || ct < 0 ||
                (enableX && (!IsFinite(dx) || dx <= 0 || !IsFinite(sx) || sx <= 0)) ||
                (enableY && (!IsFinite(dy) || dy <= 0 || !IsFinite(sy) || sy <= 0)) ||
                (!enableX && !enableY))
                return false;

            // Đọc móc neo phương X
            HookTypeOption hookTypeX = CboHookX.SelectedIndex == 0 ? HookTypeOption.HookUp
                                     : CboHookX.SelectedIndex == 1 ? HookTypeOption.HookDown
                                     : HookTypeOption.None;
            double hookLenX = double.TryParse(TxtHookLengthX.Text, out double hx) ? hx : 0;

            // Đọc móc neo phương Y
            HookTypeOption hookTypeY = CboHookY.SelectedIndex == 0 ? HookTypeOption.HookUp
                                     : CboHookY.SelectedIndex == 1 ? HookTypeOption.HookDown
                                     : HookTypeOption.None;
            double hookLenY = double.TryParse(TxtHookLengthY.Text, out double hy) ? hy : 0;

            var cover = new CoverSettings(cb, ct, cs, hasLean);
            var bottom = new BottomLayerSettings(
                new BarLayerSettings(enableX, dx, sx, SpacingMode.EvenWithinMaxSpacing, hookTypeX != HookTypeOption.None, hookLenX, hookTypeX),
                new BarLayerSettings(enableY, dy, sy, SpacingMode.EvenWithinMaxSpacing, hookTypeY != HookTypeOption.None, hookLenY, hookTypeY)
            );

            // Thép trên (Top)
            bool enableTop = ChkEnableTop.IsChecked == true;
            double dTop = CboDiameterTop.SelectedItem != null ? (double)CboDiameterTop.SelectedItem : 12;
            double sTop = double.TryParse(TxtSpacingTop.Text, out double st) ? st : 200;

            HookTypeOption topHookType = CboTopHook.SelectedIndex == 0 ? HookTypeOption.HookDown
                                       : CboTopHook.SelectedIndex == 1 ? HookTypeOption.HookUp
                                       : HookTypeOption.None;
            double topHookLen = double.TryParse(TxtTopHookLength.Text, out double htop) ? htop : 0;

            var top = new TopLayerSettings(
                enableTop,
                new BarLayerSettings(enableTop, dTop, sTop, SpacingMode.EvenWithinMaxSpacing, topHookType != HookTypeOption.None, topHookLen, topHookType),
                new BarLayerSettings(enableTop, dTop, sTop, SpacingMode.EvenWithinMaxSpacing, topHookType != HookTypeOption.None, topHookLen, topHookType)
            );

            // Thép đai hông (Side Ties)
            bool enableTies = ChkEnableSideTies.IsChecked == true;
            double dTie = CboDiameterTie.SelectedItem != null ? (double)CboDiameterTie.SelectedItem : 10;
            double sTie = double.TryParse(TxtSpacingTie.Text, out double stie) ? stie : 200;

            if (!IsFinite(hookLenX) || !IsFinite(hookLenY) || !IsFinite(topHookLen) || hookLenX < 0 || hookLenY < 0 || topHookLen < 0 ||
                !IsFinite(sx) || !IsFinite(sy) || !IsFinite(sTop) || !IsFinite(sTie) || sx <= 0 || sy <= 0 || sTop <= 0 || sTie <= 0) return false;
            if (enableTop && !double.TryParse(TxtSpacingTop.Text, out _) || enableTies && !double.TryParse(TxtSpacingTie.Text, out _)) return false;
            if (hookTypeX != HookTypeOption.None && !double.TryParse(TxtHookLengthX.Text, out _) ||
                hookTypeY != HookTypeOption.None && !double.TryParse(TxtHookLengthY.Text, out _) ||
                enableTop && topHookType != HookTypeOption.None && !double.TryParse(TxtTopHookLength.Text, out _)) return false;
            var sideTies = new SideTiesSettings(enableTies, dTie, sTie, true);

            full = new FootingRebarFullSettings(_model.FootingType, cover, bottom, top, sideTies);
            return true;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private void OnBtnAcceptClick(object sender, RoutedEventArgs e)
        {
            if (ChkReplaceOwned.Visibility == Visibility.Visible && !ReplaceOwnedRebar)
            { MessageBox.Show(this, "Móng đã có thép của lệnh này. Hãy bật Cập nhật để thay bộ đó; lệnh không tự tạo bộ trùng.", "KStruct"); return; }
            if (ChkAllowAlongside.Visibility == Visibility.Visible && !AllowAlongsideOtherRebar)
            { MessageBox.Show(this, "Móng có thép chưa nhận diện, có thể là thép tạo bằng phiên bản cũ hoặc thép thủ công. Chỉ bật Cho phép tạo cạnh nếu bạn chủ động muốn giữ thép đó và tạo thêm bộ mới.", "KStruct"); return; }
            if (TryParseInputs(out FootingRebarFullSettings full))
            {
                OutputFull = full;
                OutputCover = full.Cover;
                OutputBottom = full.Bottom;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Vui lòng kiểm tra lại các thông số nhập vào.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
