using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ColumnTransition
{
    internal sealed class ColumnTransitionWindow : Window
    {
        private readonly List<TransitionColumnSegment> _segments;
        private readonly List<double> _diameters;
        private readonly Dictionary<long, TransitionRebarConfig> _configs = new Dictionary<long, TransitionRebarConfig>();
        private readonly ListBox _segmentList = new ListBox();
        private readonly ComboBox _barsX = new ComboBox();
        private readonly ComboBox _barsY = new ComboBox();
        private readonly ComboBox _mainDiameter = new ComboBox();
        private readonly ComboBox _tieDiameter = new ComboBox();
        private readonly TextBox _cover = new TextBox();
        private readonly TextBox _tieSpacing = new TextBox();
        private readonly TextBox _lapLength = new TextBox();
        private readonly TextBox _transitionLength = new TextBox();
        private readonly Canvas _section = new Canvas();
        private readonly Canvas _elevation = new Canvas();
        private readonly TextBlock _summary = new TextBlock();
        private bool _loading;
        private long? _activeId;

        public bool Accepted { get; private set; }
        public Dictionary<long, TransitionRebarConfig> Configurations
        {
            get { return _configs.ToDictionary(x => x.Key, x => x.Value.Copy()); }
        }

        public ColumnTransitionWindow(IList<TransitionColumnSegment> segments, IList<double> diameters)
        {
            _segments = segments.OrderBy(x => x.BottomMm).ToList();
            _diameters = diameters.ToList();
            double mainDefault = Nearest(20), tieDefault = Nearest(8);
            foreach (var segment in _segments)
                _configs[segment.Id.Value] = new TransitionRebarConfig
                {
                    MainDiameter = mainDefault,
                    TieDiameter = tieDefault
                };

            Title = "KStruct - Thép cột chuyển tiếp";
            Width = 930;
            Height = 620;
            MinWidth = 800;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.White;
            Content = BuildLayout();
            _segmentList.ItemsSource = _segments;
            _segmentList.SelectionChanged += SegmentList_SelectionChanged;
            _segmentList.SelectedIndex = 0;
        }

        private UIElement BuildLayout()
        {
            var root = new Grid { Margin = new Thickness(16) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var left = new DockPanel { Margin = new Thickness(0, 0, 16, 0) };
            var heading = new TextBlock
            {
                Text = "Tuyến cột (từ dưới lên)",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            DockPanel.SetDock(heading, Dock.Top);
            left.Children.Add(heading);
            _segmentList.DisplayMemberPath = "ToString";
            _segmentList.BorderBrush = new SolidColorBrush(Color.FromRgb(215, 221, 229));
            left.Children.Add(_segmentList);
            Grid.SetColumn(left, 0);
            root.Children.Add(left);

            var right = new Grid();
            right.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            right.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var sectionLabel = new TextBlock
            {
                Text = "Mặt cắt và cấu hình thép",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetColumnSpan(sectionLabel, 2);
            right.Children.Add(sectionLabel);

            _section.MinHeight = 260;
            _section.Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
            _section.SizeChanged += (s, e) => DrawPreviews();
            _elevation.MinHeight = 260;
            _elevation.Background = new SolidColorBrush(Color.FromRgb(248, 250, 252));
            _elevation.SizeChanged += (s, e) => DrawPreviews();
            var previews = new TabControl();
            previews.Items.Add(new TabItem { Header = "Mặt cắt", Content = _section });
            previews.Items.Add(new TabItem { Header = "Sơ đồ đứng", Content = _elevation });
            Grid.SetRow(previews, 1);
            right.Children.Add(previews);

            var editor = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
            AddField(editor, "Thanh mỗi cạnh X", _barsX, Enumerable.Range(2, 19).Select(x => (object)x));
            AddField(editor, "Thanh mỗi cạnh Y", _barsY, Enumerable.Range(2, 19).Select(x => (object)x));
            AddField(editor, "Thép dọc (mm)", _mainDiameter, _diameters.Select(x => (object)x));
            AddField(editor, "Đai (mm)", _tieDiameter, _diameters.Select(x => (object)x));
            AddField(editor, "Lớp bảo vệ (mm)", _cover);
            AddField(editor, "Bước đai (mm)", _tieSpacing);
            AddField(editor, "Chiều dài nối (mm)", _lapLength);
            AddField(editor, "Chiều cao chuyển thép (mm)", _transitionLength);
            foreach (ComboBox control in new[] { _barsX, _barsY, _mainDiameter, _tieDiameter })
                control.SelectionChanged += Editor_Changed;
            foreach (TextBox control in new[] { _cover, _tieSpacing, _lapLength, _transitionLength })
                control.LostFocus += Editor_LostFocus;
            Grid.SetColumn(editor, 1);
            Grid.SetRow(editor, 1);
            right.Children.Add(editor);

            _summary.Margin = new Thickness(0, 9, 0, 0);
            _summary.Foreground = Brushes.DimGray;
            Grid.SetRow(_summary, 2);
            Grid.SetColumnSpan(_summary, 2);
            right.Children.Add(_summary);
            Grid.SetColumn(right, 1);
            root.Children.Add(right);

            var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
            var apply = new Button { Content = "Áp dụng cho tất cả tầng", Padding = new Thickness(12, 7, 12, 7) };
            apply.Click += ApplyAll_Click;
            DockPanel.SetDock(apply, Dock.Left);
            footer.Children.Add(apply);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Button { Content = "Hủy", MinWidth = 86, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 8, 0) };
            cancel.Click += (s, e) => { DialogResult = false; };
            var accept = new Button { Content = "Hoàn tất cấu hình", MinWidth = 140, Padding = new Thickness(10, 7, 10, 7), IsDefault = true };
            accept.Click += Accept_Click;
            buttons.Children.Add(cancel);
            buttons.Children.Add(accept);
            footer.Children.Add(buttons);
            Grid.SetRow(footer, 1);
            Grid.SetColumnSpan(footer, 2);
            root.Children.Add(footer);
            return root;
        }

        private static void AddField(Panel parent, string label, Control input, IEnumerable<object> choices = null)
        {
            parent.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 4, 0, 3), Foreground = Brushes.DimGray });
            if (input is ComboBox combo)
            {
                combo.ItemsSource = choices;
                combo.Margin = new Thickness(0, 0, 0, 4);
                combo.MinHeight = 26;
            }
            else if (input is TextBox text)
            {
                text.Margin = new Thickness(0, 0, 0, 4);
                text.MinHeight = 26;
                text.VerticalContentAlignment = VerticalAlignment.Center;
            }
            parent.Children.Add(input);
        }

        private double Nearest(double target)
        {
            return _diameters.OrderBy(x => Math.Abs(x - target)).First();
        }

        private TransitionColumnSegment ActiveSegment => _segmentList.SelectedItem as TransitionColumnSegment;
        private TransitionRebarConfig ActiveConfig => ActiveSegment == null ? null : _configs[ActiveSegment.Id.Value];

        private void SegmentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            if (!SaveActive())
            {
                _loading = true;
                _segmentList.SelectedItem = _activeId.HasValue
                    ? _segments.FirstOrDefault(x => x.Id.Value == _activeId.Value) : null;
                _loading = false;
                return;
            }
            LoadActive();
        }

        private void LoadActive()
        {
            TransitionColumnSegment segment = ActiveSegment;
            TransitionRebarConfig config = ActiveConfig;
            if (segment == null || config == null) return;
            _activeId = segment.Id.Value;
            _loading = true;
            _barsX.SelectedItem = config.BarsX;
            _barsY.SelectedItem = config.BarsY;
            _mainDiameter.SelectedItem = config.MainDiameter;
            _tieDiameter.SelectedItem = config.TieDiameter;
            _cover.Text = config.Cover.ToString(CultureInfo.InvariantCulture);
            _tieSpacing.Text = config.TieSpacing.ToString(CultureInfo.InvariantCulture);
            _lapLength.Text = config.LapLength.ToString(CultureInfo.InvariantCulture);
            _transitionLength.Text = config.TransitionLength.ToString(CultureInfo.InvariantCulture);
            _summary.Text = string.Format(CultureInfo.InvariantCulture,
                "{0} | {1} × {2} mm | cao độ {3:0}–{4:0} mm", segment.Level,
                segment.WidthMm, segment.DepthMm, segment.BottomMm, segment.TopMm);
            _loading = false;
            DrawPreviews();
        }

        private bool SaveActive()
        {
            if (_loading) return true;
            TransitionColumnSegment segment = _activeId.HasValue
                ? _segments.FirstOrDefault(x => x.Id.Value == _activeId.Value)
                : null;
            if (segment == null) return true;
            try
            {
                int barsX, barsY;
                double cover, spacing, lap, transition;
                if (_barsX.SelectedItem == null || _barsY.SelectedItem == null ||
                    _mainDiameter.SelectedItem == null || _tieDiameter.SelectedItem == null)
                    return false;
                if (!int.TryParse(_barsX.SelectedItem.ToString(), out barsX) ||
                    !int.TryParse(_barsY.SelectedItem.ToString(), out barsY) ||
                    !ReadPositive(_cover.Text, out cover) || !ReadPositive(_tieSpacing.Text, out spacing) ||
                    !ReadPositive(_lapLength.Text, out lap) || !ReadPositive(_transitionLength.Text, out transition)) return false;
                _configs[segment.Id.Value] = new TransitionRebarConfig
                {
                    BarsX = barsX, BarsY = barsY,
                    MainDiameter = Convert.ToDouble(_mainDiameter.SelectedItem, CultureInfo.InvariantCulture),
                    TieDiameter = Convert.ToDouble(_tieDiameter.SelectedItem, CultureInfo.InvariantCulture),
                    Cover = cover, TieSpacing = spacing, LapLength = lap, TransitionLength = transition
                };
                DrawPreviews();
                return true;
            }
            catch { return false; }
        }

        private static bool ReadPositive(string text, out double value)
        {
            return double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value) && value > 0 && !double.IsInfinity(value) && !double.IsNaN(value);
        }

        private void Editor_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_loading) { SaveActive(); LoadSummaryAndDraw(); }
        }

        private void Editor_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!_loading) { SaveActive(); LoadSummaryAndDraw(); }
        }

        private void LoadSummaryAndDraw()
        {
            if (ActiveSegment == null) return;
            _summary.Text = string.Format(CultureInfo.InvariantCulture,
                "{0} | {1:0} × {2:0} mm | cao độ {3:0}–{4:0} mm", ActiveSegment.Level,
                ActiveSegment.WidthMm, ActiveSegment.DepthMm, ActiveSegment.BottomMm, ActiveSegment.TopMm);
            DrawPreviews();
        }

        private void ApplyAll_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveActive())
            {
                MessageBox.Show(this, "Kiểm tra các thông số trước khi áp dụng.", "KStruct", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            TransitionRebarConfig source = ActiveConfig;
            foreach (TransitionColumnSegment segment in _segments)
                _configs[segment.Id.Value] = source.Copy();
            LoadActive();
        }

        private void Accept_Click(object sender, RoutedEventArgs e)
        {
            if (!SaveActive())
            {
                MessageBox.Show(this, "Thông số chưa hợp lệ. Hãy nhập số dương và chọn đủ loại thép.",
                    "KStruct", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            List<string> errors = ValidateConfigurations();
            if (errors.Count > 0)
            {
                MessageBox.Show(this, string.Join("\n", errors), "KStruct - Kiểm tra cấu hình",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Accepted = true;
            DialogResult = true;
        }

        private List<string> ValidateConfigurations()
        {
            var errors = new List<string>();
            foreach (TransitionColumnSegment segment in _segments)
            {
                TransitionRebarConfig config = _configs[segment.Id.Value];
                double inset = config.Cover + config.TieDiameter + config.MainDiameter / 2;
                double availableX = segment.WidthMm - 2 * inset;
                double availableY = segment.DepthMm - 2 * inset;
                if (availableX <= 0 || availableY <= 0)
                {
                    errors.Add(segment.Level + ": thép không vừa tiết diện " +
                        Math.Round(segment.WidthMm) + "×" + Math.Round(segment.DepthMm) + " mm.");
                    continue;
                }
                if (segment.TopMm - segment.BottomMm <= 2 * config.Cover + config.TieDiameter)
                    errors.Add(segment.Level + ": chiều cao đoạn cột không đủ cho lớp bảo vệ và đai.");
                if (config.BarsX > 2 && availableX / (config.BarsX - 1) < config.MainDiameter)
                    errors.Add(segment.Level + ": khoảng cách thép theo cạnh X nhỏ hơn đường kính thanh.");
                if (config.BarsY > 2 && availableY / (config.BarsY - 1) < config.MainDiameter)
                    errors.Add(segment.Level + ": khoảng cách thép theo cạnh Y nhỏ hơn đường kính thanh.");
            }
            return errors;
        }

        private void DrawPreviews()
        {
            DrawSection();
            DrawElevation();
        }

        private void DrawSection()
        {
            _section.Children.Clear();
            TransitionColumnSegment segment = ActiveSegment;
            TransitionRebarConfig config = ActiveConfig;
            if (segment == null || config == null || _section.ActualWidth < 100 || _section.ActualHeight < 100) return;
            double factor = Math.Min((_section.ActualWidth - 90) / segment.WidthMm,
                (_section.ActualHeight - 70) / segment.DepthMm);
            if (factor <= 0) return;
            double width = segment.WidthMm * factor, height = segment.DepthMm * factor;
            double left = (_section.ActualWidth - width) / 2, top = (_section.ActualHeight - height) / 2;
            _section.Children.Add(new Rectangle
            {
                Width = width, Height = height, Stroke = Brushes.SeaGreen, StrokeThickness = 3,
                Fill = Brushes.White
            });
            Canvas.SetLeft(_section.Children[_section.Children.Count - 1], left);
            Canvas.SetTop(_section.Children[_section.Children.Count - 1], top);
            double inset = (config.Cover + config.TieDiameter / 2) * factor;
            double x1 = left + inset, x2 = left + width - inset;
            double y1 = top + inset, y2 = top + height - inset;
            if (x2 <= x1 || y2 <= y1) return;
            var tie = new Rectangle { Width = x2 - x1, Height = y2 - y1, Stroke = Brushes.Teal, StrokeThickness = 2, Fill = Brushes.Transparent };
            Canvas.SetLeft(tie, x1); Canvas.SetTop(tie, y1); _section.Children.Add(tie);
            double barInset = (config.MainDiameter / 2 + config.TieDiameter / 2) * factor;
            for (int i = 0; i < config.BarsX; i++)
            {
                double x = x1 + barInset + (x2 - x1 - 2 * barInset) * i / Math.Max(1, config.BarsX - 1);
                AddBar(x, y1 + barInset); AddBar(x, y2 - barInset);
            }
            for (int i = 1; i < config.BarsY - 1; i++)
            {
                double y = y1 + barInset + (y2 - y1 - 2 * barInset) * i / Math.Max(1, config.BarsY - 1);
                AddBar(x1 + barInset, y); AddBar(x2 - barInset, y);
            }
        }

        private void AddBar(double x, double y)
        {
            var bar = new Ellipse { Width = 10, Height = 10, Fill = Brushes.Firebrick, Stroke = Brushes.DarkRed };
            Canvas.SetLeft(bar, x - 5); Canvas.SetTop(bar, y - 5); _section.Children.Add(bar);
        }

        private void DrawElevation()
        {
            _elevation.Children.Clear();
            if (_segments.Count == 0 || _elevation.ActualWidth < 160 || _elevation.ActualHeight < 160) return;
            double minZ = _segments.Min(x => x.BottomMm);
            double maxZ = _segments.Max(x => x.TopMm);
            double range = maxZ - minZ;
            if (range <= 0) return;
            const double margin = 30;
            double scale = (_elevation.ActualHeight - 2 * margin) / range;
            double centerX = _elevation.ActualWidth * .48;
            double maxWidth = Math.Max(60, _segments.Max(x => x.WidthMm));
            double widthScale = Math.Min(2.0, (_elevation.ActualWidth * .38) / maxWidth);

            // Show a schematic bar transfer between each pair of adjacent storeys.
            // The diagram is informational; it does not certify splice or anchorage design.
            for (int i = 0; i + 1 < _segments.Count; i++)
            {
                TransitionColumnSegment lower = _segments[i];
                TransitionColumnSegment upper = _segments[i + 1];
                TransitionRebarConfig lowerConfig = _configs[lower.Id.Value];
                TransitionRebarConfig upperConfig = _configs[upper.Id.Value];
                double jointZ = (lower.TopMm + upper.BottomMm) / 2;
                double transitionMm = Math.Min(lowerConfig.TransitionLength, upperConfig.TransitionLength);
                double y0 = margin + (maxZ - (jointZ - transitionMm / 2)) * scale;
                double y1 = margin + (maxZ - (jointZ + transitionMm / 2)) * scale;
                List<double> lowerBars = BarOffsets(lower.WidthMm, lowerConfig);
                List<double> upperBars = BarOffsets(upper.WidthMm, upperConfig);
                List<Tuple<int, int>> pairs = MatchBars(lowerBars, upperBars);
                foreach (Tuple<int, int> pair in pairs)
                {
                    double x0 = centerX + lowerBars[pair.Item1] * widthScale;
                    double x1 = centerX + upperBars[pair.Item2] * widthScale;
                    _elevation.Children.Add(new Line
                    {
                        X1 = x0, X2 = x1, Y1 = y0, Y2 = y1,
                        Stroke = Brushes.DarkOrange, StrokeThickness = 2
                    });
                }
            }

            foreach (TransitionColumnSegment segment in _segments)
            {
                double top = margin + (maxZ - segment.TopMm) * scale;
                double bottom = margin + (maxZ - segment.BottomMm) * scale;
                double width = Math.Max(26, segment.WidthMm * widthScale);
                double x = centerX - width / 2;
                var body = new Rectangle
                {
                    Width = width,
                    Height = Math.Max(2, bottom - top),
                    Stroke = Brushes.SeaGreen,
                    StrokeThickness = 1.5,
                    Fill = new SolidColorBrush(Color.FromArgb(35, 46, 139, 87))
                };
                Canvas.SetLeft(body, x); Canvas.SetTop(body, top); _elevation.Children.Add(body);
                var label = new TextBlock
                {
                    Text = segment.Level + "  " + Math.Round(segment.WidthMm) + "×" + Math.Round(segment.DepthMm),
                    FontSize = 10,
                    Foreground = Brushes.DimGray
                };
                Canvas.SetLeft(label, x + width + 7); Canvas.SetTop(label, (top + bottom) / 2 - 8);
                _elevation.Children.Add(label);

                TransitionRebarConfig config = _configs[segment.Id.Value];
                double inset = Math.Min(width * .3, (config.Cover + config.MainDiameter / 2) * widthScale);
                List<double> offsets = BarOffsets(segment.WidthMm, config);
                for (int i = 0; i < offsets.Count; i++)
                {
                    double bx = centerX + offsets[i] * widthScale;
                    var bar = new Line
                    {
                        X1 = bx, X2 = bx, Y1 = top + 2, Y2 = Math.Max(top + 3, bottom - 2),
                        Stroke = Brushes.Firebrick, StrokeThickness = 1.4
                    };
                    _elevation.Children.Add(bar);
                }
            }
        }

        private static List<double> BarOffsets(double widthMm, TransitionRebarConfig config)
        {
            double inset = config.Cover + config.TieDiameter + config.MainDiameter / 2;
            double usable = Math.Max(0, widthMm / 2 - inset);
            return Enumerable.Range(0, config.BarsX)
                .Select(i => -usable + (2 * usable * i / Math.Max(1, config.BarsX - 1))).ToList();
        }

        private static List<Tuple<int, int>> MatchBars(IList<double> lower, IList<double> upper)
        {
            var available = new HashSet<int>(Enumerable.Range(0, upper.Count));
            var pairs = new List<Tuple<int, int>>();
            foreach (int lowerIndex in Enumerable.Range(0, lower.Count)
                .OrderBy(i => lower[i]))
            {
                if (available.Count == 0) break;
                int upperIndex = available.OrderBy(i => Math.Abs(lower[lowerIndex] - upper[i])).First();
                available.Remove(upperIndex);
                pairs.Add(Tuple.Create(lowerIndex, upperIndex));
            }
            return pairs;
        }
    }
}
