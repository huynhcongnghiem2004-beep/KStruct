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

        private double _zoomScale = 1.0;
        private Point _lastPanPoint;
        private bool _isPanning = false;

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
                isPileCap ? FootingType.PileCap : FootingType.SpreadFooting, _model.Name, _model.Piles);

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
            bool valid = TryParseInputs(out FootingRebarFullSettings full);
            InputStatusBanner.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
            BtnAcceptRebar.IsEnabled = valid;
            if (!valid)
            {
                TxtInputStatus.Text = "Chưa đủ dữ liệu để xem trước. Kiểm tra các ô trong tab Cốt thép và Bảo vệ: chọn ít nhất một phương đáy, đường kính và khoảng cách phải lớn hơn 0; lớp bảo vệ và đoạn neo không được âm.";
                TxtConfigurationSummary.Text = "Đang nhập thông số…";
                TxtPreviewInfo.Text = "Sơ đồ sẽ xuất hiện khi dữ liệu nhập hợp lệ.";
                return;
            }
            var summary = new List<string>();
            if (full.Bottom.AlongX.Enabled) summary.Add($"Đáy X: Ø{full.Bottom.AlongX.Diameter:0.#} a{full.Bottom.AlongX.Spacing:0.#}");
            if (full.Bottom.AlongY.Enabled) summary.Add($"Đáy Y: Ø{full.Bottom.AlongY.Diameter:0.#} a{full.Bottom.AlongY.Spacing:0.#}");
            if (ChkEnableTop.IsChecked == true) summary.Add($"Trên: Ø{CboDiameterTop.SelectedItem} a{TxtSpacingTop.Text}");
            if (ChkEnableSideTies.IsChecked == true) summary.Add($"Đai hông: Ø{CboDiameterTie.SelectedItem} a{TxtSpacingTie.Text}");
            TxtConfigurationSummary.Text = string.Join("   ·   ", summary);

            try
            {
                var builder = new BottomLayerBuilder();
                RebarPlan plan = builder.Build(_model, full);

                CoverSettings cover = full.Cover;

                // Tỷ lệ Scale vẽ vừa Canvas (640x420)
                // Dành margin rộng hơn để vẽ Dimension và Tag bên ngoài đài
                double marginLeft = 70;
                double marginRight = 70;
                double marginTop = 45;
                double marginBottom = 70;
                double drawWidth = PreviewCanvas.Width - marginLeft - marginRight;
                double drawHeight = PreviewCanvas.Height - marginTop - marginBottom;

                double scaleX = drawWidth / _model.LengthX;
                double totalH = _model.Height + (cover.HasLeanConcrete ? 100 : 0);
                double scaleZ = drawHeight / totalH;
                double scale = Math.Min(scaleX, scaleZ);

                double offsetX = marginLeft + (drawWidth - _model.LengthX * scale) / 2.0;
                double offsetZ = marginBottom + (drawHeight - totalH * scale) / 2.0;

                double ToCanvasX(double mmX) => offsetX + mmX * scale;
                double ToCanvasZ(double mmZ) => PreviewCanvas.Height - (offsetZ + mmZ * scale);

                // 1. Vẽ cọc (Pile) nếu là đài cọc và có dữ liệu cọc
                if (_model.FootingType == FootingType.PileCap)
                {
                    var pilesToDraw = new List<Tuple<double, double>>(); // (localX, width)
                    if (_model.Piles != null && _model.Piles.Count > 0)
                    {
                        foreach (var p in _model.Piles)
                        {
                            pilesToDraw.Add(Tuple.Create(p.CenterX, p.DiameterOrWidth));
                        }
                    }
                    else
                    {
                        // Giả lập vị trí cọc tiêu biểu cho đài cọc khi chưa kết nối cọc thực tế trong mô hình
                        double dPile = 350;
                        double edgeDist = Math.Max(250, _model.LengthX * 0.22);
                        if (_model.LengthX > 1800)
                        {
                            pilesToDraw.Add(Tuple.Create(edgeDist, dPile));
                            pilesToDraw.Add(Tuple.Create(_model.LengthX / 2.0, dPile));
                            pilesToDraw.Add(Tuple.Create(_model.LengthX - edgeDist, dPile));
                        }
                        else
                        {
                            pilesToDraw.Add(Tuple.Create(edgeDist, dPile));
                            pilesToDraw.Add(Tuple.Create(_model.LengthX - edgeDist, dPile));
                        }
                    }

                    // Loại trừ các cọc trùng tọa độ X trên mặt cắt chiếu
                    var distinctX = pilesToDraw.GroupBy(p => Math.Round(p.Item1 / 10.0)).Select(g => g.First()).ToList();
                    double pileEmbedDepth = 100; // Ngàm vào đài 100mm theo chuẩn TCVN
                    double pileVisibleH = 80;   // Chiều cao cọc thò xuống dưới để thấy ký hiệu

                    foreach (var pile in distinctX)
                    {
                        double pX = pile.Item1;
                        double pW = pile.Item2;
                        double leftMm = pX - pW / 2.0;
                        double rightMm = pX + pW / 2.0;

                        // Thân cọc cắm vào đài
                        var pileRect = new Rectangle
                        {
                            Width = Math.Max(10, pW * scale),
                            Height = (pileEmbedDepth + pileVisibleH) * scale,
                            Stroke = new SolidColorBrush(Color.FromRgb(2, 132, 199)),
                            StrokeThickness = 1.5,
                            Fill = new SolidColorBrush(Color.FromArgb(50, 56, 189, 248))
                        };
                        Canvas.SetLeft(pileRect, ToCanvasX(leftMm));
                        Canvas.SetTop(pileRect, ToCanvasZ(pileEmbedDepth));
                        PreviewCanvas.Children.Add(pileRect);

                        // Trục tim cọc (Dash-dot line)
                        var centerLine = new Line
                        {
                            X1 = ToCanvasX(pX),
                            Y1 = ToCanvasZ(pileEmbedDepth + 25),
                            X2 = ToCanvasX(pX),
                            Y2 = ToCanvasZ(-pileVisibleH - 15),
                            Stroke = new SolidColorBrush(Color.FromRgb(14, 165, 233)),
                            StrokeThickness = 1,
                            StrokeDashArray = new DoubleCollection { 6, 2, 2, 2 }
                        };
                        PreviewCanvas.Children.Add(centerLine);

                        // Ký hiệu tim cọc (chữ C hoặc Ø)
                        var pileText = new TextBlock
                        {
                            Text = $"Cọc Ø{pW:0}",
                            FontSize = 9,
                            Foreground = new SolidColorBrush(Color.FromRgb(3, 105, 161)),
                            FontWeight = FontWeights.SemiBold
                        };
                        Canvas.SetLeft(pileText, ToCanvasX(pX) - 20);
                        Canvas.SetTop(pileText, ToCanvasZ(-pileVisibleH - 12));
                        PreviewCanvas.Children.Add(pileText);
                    }
                }

                // 2. Vẽ lớp bê tông lót (nếu có)
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

                // 3. Vẽ bê tông móng
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

                // 4. Đường bao lớp bảo vệ (Dash line)
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

                // 5. Vẽ Thép đáy phương X (Bottom-X - Blue, bẻ móc lên hoặc xuống)
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

                    // Tag Thép đáy X
                    string tagX = $"Đáy X: Ø{setX.Diameter:0.#} a{full.Bottom.AlongX.Spacing:0.#}";
                    DrawLeaderTag(ToCanvasX(_model.LengthX * 0.3), ToCanvasZ(cover.Bottom), -35, -45, tagX, Brushes.RoyalBlue);
                }

                // 6. Vẽ Thép đáy phương Y (Bottom-Y - Chấm tròn Red)
                var setY = plan.Sets.FirstOrDefault(s => s.Name == "Bottom-Y");
                if (setY != null && setY.FirstBarPoints != null && setY.FirstBarPoints.Count > 0)
                {
                    int zIdx = setY.FirstBarPoints.Count > 2 ? 1 : 0;
                    double zCenter = setY.FirstBarPoints[zIdx].Z;
                    double radiusPx = Math.Max(3, (setY.Diameter / 2.0) * scale);

                    double xFirst = cover.Side + setY.Diameter / 2.0;
                    double span = _model.LengthX - 2 * xFirst;
                    SpacingCalculator.Compute(span, full.Bottom.AlongY.Spacing, full.Bottom.AlongY.Mode, out int count, out double step);

                    double tagDotX = 0;
                    double tagDotZ = 0;
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

                        if (i == Math.Min(3, count - 1))
                        {
                            tagDotX = ToCanvasX(xMm);
                            tagDotZ = ToCanvasZ(zCenter);
                        }
                    }

                    // Tag Thép đáy Y
                    if (count > 0)
                    {
                        string tagY = $"Đáy Y: Ø{setY.Diameter:0.#} a{full.Bottom.AlongY.Spacing:0.#}";
                        DrawLeaderTag(tagDotX, tagDotZ, 35, -35, tagY, Brushes.Crimson);
                    }
                }

                // 7. Vẽ Thép lớp trên (Top Rebar - Orange, bẻ móc xuống hoặc lên)
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

                    // Tag Thép trên
                    string tagTop = $"Thép trên: Ø{setTopX.Diameter:0.#} a{full.Top.AlongX.Spacing:0.#}";
                    DrawLeaderTag(ToCanvasX(_model.LengthX * 0.65), ToCanvasZ(_model.Height - cover.Top), 40, -35, tagTop, Brushes.DarkOrange);
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

                // 8. Vẽ Thép đai cấu tạo hông (Side Ties - Purple)
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

                // 9. Vẽ đường gióng kích thước (DIMENSION)
                // 9.1 Dim ngang chiều dài móng Lx
                DrawHorizontalDimension(ToCanvasX(0), ToCanvasX(_model.LengthX), ToCanvasZ(_model.Height) - 22, $"{_model.LengthX:0}");
                // 9.2 Dim dọc chiều cao móng H
                DrawVerticalDimension(ToCanvasZ(0), ToCanvasZ(_model.Height), ToCanvasX(_model.LengthX) + 24, $"{_model.Height:0}");
                // 9.3 Dim lớp bảo vệ cạnh và đáy
                DrawHorizontalDimension(ToCanvasX(0), ToCanvasX(cover.Side), ToCanvasZ(0) + 18, $"C={cover.Side:0}", 8);
                DrawVerticalDimension(ToCanvasZ(0), ToCanvasZ(cover.Bottom), ToCanvasX(0) - 20, $"C={cover.Bottom:0}", 8);

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

        private void DrawLeaderTag(double targetX, double targetY, double dx, double dy, string text, Brush brush)
        {
            double endX = targetX + dx;
            double endY = targetY + dy;
            double shoulderLen = dx >= 0 ? 30 : -30;
            double shoulderEndX = endX + shoulderLen;

            // Đường chỉ dẫn (Leader)
            var leader = new Polyline
            {
                Stroke = brush,
                StrokeThickness = 1.2,
                Points = new PointCollection
                {
                    new Point(targetX, targetY),
                    new Point(endX, endY),
                    new Point(shoulderEndX, endY)
                }
            };
            PreviewCanvas.Children.Add(leader);

            // Chấm tròn tại đầu thanh thép
            var dot = new Ellipse
            {
                Width = 4,
                Height = 4,
                Fill = brush
            };
            Canvas.SetLeft(dot, targetX - 2);
            Canvas.SetTop(dot, targetY - 2);
            PreviewCanvas.Children.Add(dot);

            // Chữ text
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = brush
            };
            Canvas.SetLeft(tb, dx >= 0 ? endX + 4 : shoulderEndX);
            Canvas.SetTop(tb, endY - 14);
            PreviewCanvas.Children.Add(tb);
        }

        private void DrawHorizontalDimension(double x1, double x2, double y, string text, double fontSize = 10)
        {
            double minX = Math.Min(x1, x2);
            double maxX = Math.Max(x1, x2);
            if (maxX - minX < 2) return;

            // Đường dim ngang
            var line = new Line
            {
                X1 = minX, Y1 = y,
                X2 = maxX, Y2 = y,
                Stroke = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                StrokeThickness = 1
            };
            PreviewCanvas.Children.Add(line);

            // Hai gạch chéo 45 độ ở hai đầu dim (Tick mark kiến trúc/kết cấu)
            double tick = 4;
            var tick1 = new Line { X1 = minX - tick, Y1 = y + tick, X2 = minX + tick, Y2 = y - tick, Stroke = line.Stroke, StrokeThickness = 1.2 };
            var tick2 = new Line { X1 = maxX - tick, Y1 = y + tick, X2 = maxX + tick, Y2 = y - tick, Stroke = line.Stroke, StrokeThickness = 1.2 };
            PreviewCanvas.Children.Add(tick1);
            PreviewCanvas.Children.Add(tick2);

            // Text số đo
            var tb = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = FontWeights.Medium,
                Foreground = line.Stroke
            };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, (minX + maxX) / 2.0 - tb.DesiredSize.Width / 2.0);
            Canvas.SetTop(tb, y - tb.DesiredSize.Height - 1);
            PreviewCanvas.Children.Add(tb);
        }

        private void DrawVerticalDimension(double y1, double y2, double x, string text, double fontSize = 10)
        {
            double minY = Math.Min(y1, y2);
            double maxY = Math.Max(y1, y2);
            if (maxY - minY < 2) return;

            // Đường dim dọc
            var line = new Line
            {
                X1 = x, Y1 = minY,
                X2 = x, Y2 = maxY,
                Stroke = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                StrokeThickness = 1
            };
            PreviewCanvas.Children.Add(line);

            // Hai gạch chéo 45 độ ở hai đầu dim
            double tick = 4;
            var tick1 = new Line { X1 = x - tick, Y1 = minY + tick, X2 = x + tick, Y2 = minY - tick, Stroke = line.Stroke, StrokeThickness = 1.2 };
            var tick2 = new Line { X1 = x - tick, Y1 = maxY + tick, X2 = x + tick, Y2 = maxY - tick, Stroke = line.Stroke, StrokeThickness = 1.2 };
            PreviewCanvas.Children.Add(tick1);
            PreviewCanvas.Children.Add(tick2);

            // Text số đo
            var tb = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = FontWeights.Medium,
                Foreground = line.Stroke
            };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(tb, x + 4);
            Canvas.SetTop(tb, (minY + maxY) / 2.0 - tb.DesiredSize.Height / 2.0);
            PreviewCanvas.Children.Add(tb);
        }

        #region Zoom and Pan Preview

        private void OnBtnZoomInClick(object sender, RoutedEventArgs e) => ApplyZoom(1.2);

        private void OnBtnZoomOutClick(object sender, RoutedEventArgs e) => ApplyZoom(1.0 / 1.2);

        private void OnBtnZoomResetClick(object sender, RoutedEventArgs e)
        {
            _zoomScale = 1.0;
            CanvasScaleTransform.ScaleX = 1.0;
            CanvasScaleTransform.ScaleY = 1.0;
            CanvasTranslateTransform.X = 0;
            CanvasTranslateTransform.Y = 0;
        }

        private void ApplyZoom(double factor, Point? center = null)
        {
            double newScale = _zoomScale * factor;
            if (newScale < 0.3) newScale = 0.3;
            if (newScale > 5.0) newScale = 5.0;

            double actualFactor = newScale / _zoomScale;
            _zoomScale = newScale;

            if (center.HasValue)
            {
                // Zoom hướng về tâm con trỏ chuột
                Point c = center.Value;
                CanvasTranslateTransform.X = c.X - (c.X - CanvasTranslateTransform.X) * actualFactor;
                CanvasTranslateTransform.Y = c.Y - (c.Y - CanvasTranslateTransform.Y) * actualFactor;
            }

            CanvasScaleTransform.ScaleX = _zoomScale;
            CanvasScaleTransform.ScaleY = _zoomScale;
        }

        private void OnPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            Point mousePos = e.GetPosition(PreviewViewport);
            if (e.Delta > 0)
                ApplyZoom(1.15, mousePos);
            else if (e.Delta < 0)
                ApplyZoom(1.0 / 1.15, mousePos);

            e.Handled = true;
        }

        private void OnPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Middle || e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                _isPanning = true;
                _lastPanPoint = e.GetPosition(PreviewViewport);
                PreviewViewport.CaptureMouse();
                PreviewViewport.Cursor = System.Windows.Input.Cursors.SizeAll;
            }
        }

        private void OnPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isPanning)
            {
                Point currentPoint = e.GetPosition(PreviewViewport);
                Vector delta = currentPoint - _lastPanPoint;
                CanvasTranslateTransform.X += delta.X;
                CanvasTranslateTransform.Y += delta.Y;
                _lastPanPoint = currentPoint;
            }
        }

        private void OnPreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                PreviewViewport.ReleaseMouseCapture();
                PreviewViewport.Cursor = System.Windows.Input.Cursors.Arrow;
            }
        }

        #endregion

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
