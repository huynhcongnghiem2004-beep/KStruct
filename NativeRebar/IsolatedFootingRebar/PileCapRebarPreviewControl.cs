using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SDK_HD.KStruct.NativeRebar
{
    /// <summary>Live schematic preview for the footing/pile-cap reinforcement settings dialog.</summary>
    internal sealed class PileCapRebarPreviewControl : Border
    {
        private readonly Canvas _canvas;
        private readonly TextBlock _summary;

        public PileCapRebarPreviewControl()
        {
            Padding = new Thickness(10);
            Background = Brushes.White;
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "MINH HỌA BỐ TRÍ THÉP",
                        FontSize = 14,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(2, 2, 2, 8)
                    },
                    (_canvas = new Canvas { Width = 470, Height = 590, Background = Brushes.White }),
                    (_summary = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Brushes.DimGray,
                        Margin = new Thickness(2, 8, 2, 2),
                        MaxWidth = 460
                    })
                }
            };
        }

        public void Update(double lengthX, double widthY, double height,
            double coverBottom, double coverTop, double coverSide,
            double spacingX, double spacingY, bool bottom, bool middle, bool top,
            bool sideVertical, bool sideHorizontal, bool sideCStirrups, bool dowels,
            double diameterX, double diameterY, double dowelAnchorage, double dowelProjection)
        {
            _canvas.Children.Clear();
            lengthX = SafePositive(lengthX, 1800);
            widthY = SafePositive(widthY, 1200);
            height = SafePositive(height, 450);
            coverBottom = SafePositive(coverBottom, 50);
            coverTop = SafePositive(coverTop, 50);
            coverSide = SafePositive(coverSide, 50);
            spacingX = SafePositive(spacingX, 150);
            spacingY = SafePositive(spacingY, 150);
            diameterX = SafePositive(diameterX, 12);
            diameterY = SafePositive(diameterY, 12);
            dowelAnchorage = SafePositive(dowelAnchorage, 400);
            dowelProjection = SafePositive(dowelProjection, 600);

            Brush xBrush = new SolidColorBrush(Color.FromRgb(35, 102, 190));
            Brush yBrush = new SolidColorBrush(Color.FromRgb(205, 65, 65));
            Brush cageBrush = new SolidColorBrush(Color.FromRgb(38, 135, 95));
            Brush dowelBrush = new SolidColorBrush(Color.FromRgb(218, 125, 35));

            AddText("MẶT BẰNG · lưới thép X/Y", 8, 4, 12, Brushes.Black);
            double planX = 50, planY = 62, planW = 400, planH = 160;
            double planScale = Math.Min(planW / lengthX, planH / widthY);
            double drawnW = lengthX * planScale, drawnH = widthY * planScale;
            double left = planX + (planW - drawnW) / 2, upper = planY + (planH - drawnH) / 2;
            AddHorizontalDimension(left, left + drawnW, upper - 13, upper, "X = " + FormatDimension(lengthX) + " mm");
            AddVerticalDimension(left - 15, upper, upper + drawnH, left, "Y = " + FormatDimension(widthY) + " mm");
            AddBox(left, upper, drawnW, drawnH, Brushes.Black, new SolidColorBrush(Color.FromRgb(245, 245, 242)), 2);

            double insetX = Math.Min(drawnW / 2, coverSide * planScale);
            double insetY = Math.Min(drawnH / 2, coverSide * planScale);
            double innerLeft = left + insetX, innerRight = left + drawnW - insetX;
            double innerTop = upper + insetY, innerBottom = upper + drawnH - insetY;
            int countX = BarCount(widthY - 2 * coverSide, spacingX);
            int countY = BarCount(lengthX - 2 * coverSide, spacingY);
            if (bottom || middle || top)
            {
                DrawDistributed(countX, innerTop, innerBottom, i => AddLine(innerLeft, i, innerRight, i, xBrush, 1.8));
                DrawDistributed(countY, innerLeft, innerRight, i => AddLine(i, innerTop, i, innerBottom, yBrush, 1.8));
            }

            // Generic 4-pile and central-column symbols are illustrative only; the command does not read pile locations.
            double pileRadius = Math.Max(5, Math.Min(drawnW, drawnH) * 0.055);
            AddPile(left + drawnW * 0.27, upper + drawnH * 0.28, pileRadius);
            AddPile(left + drawnW * 0.73, upper + drawnH * 0.28, pileRadius);
            AddPile(left + drawnW * 0.27, upper + drawnH * 0.72, pileRadius);
            AddPile(left + drawnW * 0.73, upper + drawnH * 0.72, pileRadius);
            double columnW = Math.Max(15, drawnW * 0.14), columnH = Math.Max(13, drawnH * 0.18);
            double columnLeft = left + (drawnW - columnW) / 2;
            double columnTop = upper + (drawnH - columnH) / 2;
            AddBox(columnLeft, columnTop, columnW, columnH,
                Brushes.DarkSlateGray, Brushes.White, 1.5);
            if (dowels)
            {
                double dot = Math.Max(4, Math.Min(7, columnW * 0.18));
                foreach (double sx in new[] { .22, .78 })
                    foreach (double sy in new[] { .24, .76 })
                    {
                        var mark = new Ellipse { Width = dot, Height = dot, Fill = dowelBrush, Stroke = Brushes.White, StrokeThickness = 0.7 };
                        Canvas.SetLeft(mark, columnLeft + columnW * sx - dot / 2);
                        Canvas.SetTop(mark, columnTop + columnH * sy - dot / 2);
                        _canvas.Children.Add(mark);
                    }
            }
            AddText("cọc minh họa", 18, 229, 9, Brushes.DimGray);
            AddPile(80, 234, 5);
            AddText("cột", 128, 229, 9, Brushes.DimGray);
            AddBox(113, 231, 10, 8, Brushes.DarkSlateGray, Brushes.White, 1);
            AddText("X " + countX + " thanh  ·  Y " + countY + " thanh", 225, 229, 9, Brushes.DimGray);

            AddText("MẶT CẮT · cấu tạo lưới móng và thép chờ cột", 8, 258, 12, Brushes.Black);
            double sectionX = 25, sectionY = 326, sectionW = 390, sectionH = 192;
            double scale = Math.Min(sectionW / lengthX, sectionH / height);
            double concreteW = lengthX * scale, concreteH = height * scale;
            double concreteLeft = sectionX + (sectionW - concreteW) / 2;
            double concreteTop = sectionY + sectionH - concreteH - 20;
            AddVerticalDimension(concreteLeft + concreteW + 27, concreteTop, concreteTop + concreteH,
                concreteLeft + concreteW, "H = " + FormatDimension(height) + " mm", 14);
            AddBox(concreteLeft, concreteTop, concreteW, concreteH, Brushes.Black,
                new SolidColorBrush(Color.FromRgb(238, 238, 235)), 2);

            double bottomCoverPx = Math.Min(concreteH / 3, coverBottom * scale);
            double topCoverPx = Math.Min(concreteH / 3, coverTop * scale);
            AddLine(concreteLeft + 4, concreteTop + concreteH - bottomCoverPx,
                concreteLeft + concreteW - 4, concreteTop + concreteH - bottomCoverPx, Brushes.Gray, 1, true);
            AddLine(concreteLeft + 4, concreteTop + topCoverPx,
                concreteLeft + concreteW - 4, concreteTop + topCoverPx, Brushes.Gray, 1, true);
            double layerSeparation = Math.Max(3, (Math.Max(diameterX, diameterY) + 2) * scale);
            if (bottom)
            {
                double y = concreteTop + concreteH - bottomCoverPx - Math.Max(2, diameterX * scale / 2);
                AddLine(concreteLeft + 8, y, concreteLeft + concreteW - 8, y, xBrush, 3);
                AddLine(concreteLeft + 8, y - layerSeparation, concreteLeft + concreteW - 8, y - layerSeparation, yBrush, 3);
            }
            if (middle)
            {
                double y = concreteTop + concreteH * 0.53;
                AddLine(concreteLeft + 8, y, concreteLeft + concreteW - 8, y, xBrush, 3);
                AddLine(concreteLeft + 8, y - 3, concreteLeft + concreteW - 8, y - 3, yBrush, 3);
            }
            if (top)
            {
                double y = concreteTop + topCoverPx + Math.Max(2, diameterX * scale / 2);
                AddLine(concreteLeft + 8, y, concreteLeft + concreteW - 8, y, xBrush, 3);
                AddLine(concreteLeft + 8, y + layerSeparation, concreteLeft + concreteW - 8, y + layerSeparation, yBrush, 3);
            }
            if (sideVertical)
            {
                AddLine(concreteLeft + 10, concreteTop + 8, concreteLeft + 10, concreteTop + concreteH - 8, cageBrush, 2.5);
                AddLine(concreteLeft + concreteW - 10, concreteTop + 8, concreteLeft + concreteW - 10, concreteTop + concreteH - 8, cageBrush, 2.5);
            }
            if (sideHorizontal || sideCStirrups)
            {
                double inset = Math.Min(12, concreteW * 0.08);
                AddBox(concreteLeft + inset, concreteTop + 10, Math.Max(1, concreteW - 2 * inset),
                    Math.Max(1, concreteH - 20), cageBrush, null, 1.5);
            }
            if (dowels)
            {
                double cx = concreteLeft + concreteW / 2;
                double barOffset = Math.Max(5, Math.Min(10, concreteW * 0.035));
                double projectionPx = Math.Max(24, Math.Min(58, dowelProjection * scale));
                double barBottom = concreteTop + concreteH - bottomCoverPx - 2;
                double hookRun = Math.Max(16, Math.Min(Math.Max(16, concreteW / 2 - barOffset - 10), dowelAnchorage * scale));
                double columnWidth = Math.Max(34, Math.Min(68, concreteW * 0.22));
                AddBox(cx - columnWidth / 2, concreteTop - 42, columnWidth, 42,
                    Brushes.DarkSlateGray, new SolidColorBrush(Color.FromRgb(224, 230, 234)), 1.5);
                AddLine(cx - barOffset, concreteTop - projectionPx, cx - barOffset, barBottom, dowelBrush, 2.5);
                AddLine(cx + barOffset, concreteTop - projectionPx, cx + barOffset, barBottom, dowelBrush, 2.5);
                // Neo móc quay vào lõi móng, phía dưới lưới thép đáy.
                AddLine(cx - barOffset, barBottom, cx - barOffset + hookRun, barBottom, dowelBrush, 2.5);
                AddLine(cx + barOffset, barBottom, cx + barOffset - hookRun, barBottom, dowelBrush, 2.5);
                AddText("Nhô " + FormatDimension(dowelProjection) + " mm", cx + columnWidth / 2 + 4,
                    concreteTop - projectionPx, 8, dowelBrush);
                AddText("Neo " + FormatDimension(dowelAnchorage) + " mm", cx + hookRun + 5,
                    barBottom - 14, 8, dowelBrush);
            }

            double pileW = Math.Max(12, Math.Min(28, concreteW * 0.075));
            foreach (double fraction in new[] { 0.27, 0.73 })
                AddBox(concreteLeft + concreteW * fraction - pileW / 2, concreteTop + concreteH - 2,
                    pileW, Math.Max(18, sectionH - concreteH + 5), Brushes.DimGray, Brushes.Gainsboro, 1.2);
            AddText("cọc (minh họa)", concreteLeft + concreteW * 0.73 + pileW / 2 + 3,
                concreteTop + concreteH + 3, 9, Brushes.DimGray);

            double legendY = 550;
            AddLine(16, legendY + 5, 38, legendY + 5, xBrush, 3); AddText("X", 42, legendY - 2, 10, Brushes.Black);
            AddLine(70, legendY + 5, 92, legendY + 5, yBrush, 3); AddText("Y", 96, legendY - 2, 10, Brushes.Black);
            AddLine(124, legendY + 5, 146, legendY + 5, cageBrush, 3); AddText("thép cạnh/đai", 150, legendY - 2, 10, Brushes.Black);
            AddLine(250, legendY + 5, 272, legendY + 5, dowelBrush, 3); AddText("thép chờ", 276, legendY - 2, 10, Brushes.Black);
            _summary.Text = string.Format(CultureInfo.CurrentCulture,
                "Đài X × Y × H: {0:0} × {1:0} × {2:0} mm · cover cạnh {3:0} mm · bước X/Y {4:0}/{5:0} mm. Mặt bằng hiển thị 4 thép chờ ở góc cột; mặt cắt minh họa đoạn neo móc quay vào lõi móng và chiều nhô. Cọc/cột không lấy từ vị trí thực trong mô hình.",
                lengthX, widthY, height, coverSide, spacingX, spacingY);
        }

        private void AddHorizontalDimension(double x1, double x2, double y, double referenceY, string label)
        {
            AddLine(x1, referenceY, x1, y, Brushes.SlateGray, 1);
            AddLine(x2, referenceY, x2, y, Brushes.SlateGray, 1);
            AddLine(x1, y, x2, y, Brushes.SlateGray, 1);
            AddLine(x1, y, x1 + 5, y - 3, Brushes.SlateGray, 1);
            AddLine(x1, y, x1 + 5, y + 3, Brushes.SlateGray, 1);
            AddLine(x2, y, x2 - 5, y - 3, Brushes.SlateGray, 1);
            AddLine(x2, y, x2 - 5, y + 3, Brushes.SlateGray, 1);
            var text = new TextBlock
            {
                Text = label, Width = Math.Max(90, x2 - x1), TextAlignment = TextAlignment.Center,
                FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = Brushes.SlateGray,
                Background = Brushes.White
            };
            Canvas.SetLeft(text, (x1 + x2 - text.Width) / 2);
            Canvas.SetTop(text, y - 17);
            _canvas.Children.Add(text);
        }

        private void AddVerticalDimension(double x, double y1, double y2, double referenceX, string label, double labelOffset = -14)
        {
            AddLine(referenceX, y1, x, y1, Brushes.SlateGray, 1);
            AddLine(referenceX, y2, x, y2, Brushes.SlateGray, 1);
            AddLine(x, y1, x, y2, Brushes.SlateGray, 1);
            AddLine(x, y1, x - 3, y1 + 5, Brushes.SlateGray, 1);
            AddLine(x, y1, x + 3, y1 + 5, Brushes.SlateGray, 1);
            AddLine(x, y2, x - 3, y2 - 5, Brushes.SlateGray, 1);
            AddLine(x, y2, x + 3, y2 - 5, Brushes.SlateGray, 1);

            var text = new TextBlock
            {
                Text = label, Width = 105, Height = 16, TextAlignment = TextAlignment.Center,
                FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = Brushes.SlateGray,
                Background = Brushes.White, RenderTransform = new RotateTransform(-90, 52.5, 8)
            };
            Canvas.SetLeft(text, x + labelOffset - 52.5);
            Canvas.SetTop(text, (y1 + y2 - text.Height) / 2);
            _canvas.Children.Add(text);
        }

        private static string FormatDimension(double value) => value.ToString("0", CultureInfo.CurrentCulture);

        private void AddText(string text, double x, double y, double size, Brush brush)
        {
            var block = new TextBlock { Text = text, FontSize = size, Foreground = brush };
            Canvas.SetLeft(block, x); Canvas.SetTop(block, y); _canvas.Children.Add(block);
        }

        private void AddLine(double x1, double y1, double x2, double y2, Brush brush, double thickness, bool dashed = false)
        {
            var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = thickness };
            if (dashed) line.StrokeDashArray = new DoubleCollection { 3, 2 };
            _canvas.Children.Add(line);
        }

        private void AddBox(double x, double y, double width, double height, Brush stroke, Brush fill, double thickness)
        {
            var box = new Rectangle { Width = width, Height = height, Stroke = stroke, StrokeThickness = thickness, Fill = fill ?? Brushes.Transparent };
            Canvas.SetLeft(box, x); Canvas.SetTop(box, y); _canvas.Children.Add(box);
        }

        private void AddPile(double centerX, double centerY, double radius)
        {
            var pile = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = Brushes.Gainsboro, Stroke = Brushes.DimGray, StrokeThickness = 1.2 };
            Canvas.SetLeft(pile, centerX - radius); Canvas.SetTop(pile, centerY - radius); _canvas.Children.Add(pile);
        }

        private static void DrawDistributed(int count, double start, double end, Action<double> draw)
        {
            int shown = Math.Min(count, 48);
            for (int i = 0; i < shown; i++)
                draw(start + (end - start) * i / Math.Max(1, shown - 1));
        }

        private static int BarCount(double span, double spacing)
        {
            if (span <= 0 || spacing <= 0 || double.IsNaN(span) || double.IsInfinity(span)) return 1;
            return Math.Max(2, Math.Min(10000, (int)Math.Ceiling(span / spacing) + 1));
        }

        private static double SafePositive(double value, double fallback) =>
            value > 0 && !double.IsNaN(value) && !double.IsInfinity(value) ? value : fallback;
    }
}
