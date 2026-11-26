using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ColumnRebarPro.V3
{
    // Preview-only WPF drawings. No Revit transaction is created here.
    // The verified whole-route elevation remains V3VerifiedPathSnapshot-based.
    public partial class ColumnRebarV3Window
    {
        private static readonly Brush PreviewSteel = Brushes.Firebrick;
        private static readonly Brush PreviewConcrete = new SolidColorBrush(Color.FromRgb(231, 237, 244));
        private static readonly Brush PreviewNavy = new SolidColorBrush(Color.FromRgb(22, 44, 70));
        private static readonly Brush PreviewBlue = Brushes.SteelBlue;
        private static readonly Brush PreviewWarning = Brushes.DarkOrange;

        private static void PLine(Canvas canvas, double x, double y, double xx, double yy, Brush brush, double stroke = 2)
        {
            canvas.Children.Add(new Line { X1 = x, Y1 = y, X2 = xx, Y2 = yy,
                Stroke = brush, StrokeThickness = stroke });
        }

        private static void PBox(Canvas canvas, double x, double y, double w, double h, Brush fill, Brush stroke, double size = 1)
        {
            if (w <= 0 || h <= 0) return;
            var box = new Rectangle { Width = w, Height = h, Fill = fill,
                Stroke = stroke, StrokeThickness = size };
            Canvas.SetLeft(box, x); Canvas.SetTop(box, y);
            canvas.Children.Add(box);
        }

        private static void PText(Canvas canvas, string text, double x, double y, Brush color, double size = 12)
        {
            var label = new TextBlock { Text = text, Foreground = color, FontSize = size,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = Math.Max(20, canvas.ActualWidth - Math.Max(2, x) - 4), ToolTip = text };
            Canvas.SetLeft(label, Math.Max(2, x)); Canvas.SetTop(label, Math.Max(0, y));
            canvas.Children.Add(label);
        }

        private static void PDot(Canvas canvas, double x, double y)
        {
            var dot = new Ellipse { Width = 6, Height = 6, Fill = PreviewSteel };
            Canvas.SetLeft(dot, x - 3); Canvas.SetTop(dot, y - 3);
            canvas.Children.Add(dot);
        }

        private void DrawV4DynamicPreviews()
        {
            if (_loading || ActiveConfig == null || ActiveRow == null) return;
            DrawTieDynamicPreview();
            DrawSpliceDynamicPreview();
            DrawJointDynamicPreview();
            DrawRoofDynamicPreview();
            DrawDrawingDynamicPreview();
        }

        private void DrawTieDynamicPreview()
        {
            var c = ActiveConfig;
            var seg = ActiveRow.Segment;
            var plan = TiePlanPreviewCanvas;
            if (plan != null)
            {
                plan.Children.Clear();
                double width = plan.ActualWidth, height = plan.ActualHeight;
                if (width > 130 && height > 120 && seg.WidthMm > 0 && seg.DepthMm > 0)
                {
                    double factor = Math.Min((width - 105) / seg.WidthMm, (height - 55) / seg.DepthMm);
                    double w = seg.WidthMm * factor, h = seg.DepthMm * factor;
                    double left = (width - w) / 2, top = (height - h) / 2;
                    PBox(plan, left, top, w, h, PreviewConcrete, Brushes.SlateGray);
                    double inset = (c.Cover + c.TieDiameter / 2) * factor;
                    double bx = (c.Cover + c.TieDiameter + c.MainDiameter / 2) * factor;
                    double x1 = left + bx, x2 = left + w - bx, y1 = top + bx, y2 = top + h - bx;
                    if (w > 2 * inset && h > 2 * inset && x2 > x1 && y2 > y1)
                    {
                        PBox(plan, left + inset, top + inset, w - 2 * inset, h - 2 * inset,
                            Brushes.Transparent, PreviewSteel, 2);
                        int nx = Math.Max(2, c.Cx), ny = Math.Max(2, c.Cy);
                        if (c.CrossTies)
                        {
                            for (int i = 1; i < nx - 1; i++)
                            {
                                double x = x1 + (x2 - x1) * i / (nx - 1);
                                PLine(plan, x, y1, x, y2, PreviewSteel, 1.4);
                            }
                            for (int j = 1; j < ny - 1; j++)
                            {
                                double y = y1 + (y2 - y1) * j / (ny - 1);
                                PLine(plan, x1, y, x2, y, PreviewSteel, 1.4);
                            }
                        }
                        for (int i = 0; i < nx; i++)
                        {
                            double x = x1 + (x2 - x1) * i / (nx - 1);
                            PDot(plan, x, y1); PDot(plan, x, y2);
                        }
                        for (int j = 1; j < ny - 1; j++)
                        {
                            double y = y1 + (y2 - y1) * j / (ny - 1);
                            PDot(plan, x1, y); PDot(plan, x2, y);
                        }
                    }
                    PText(plan, "MẶT CẮT · " + seg.LevelName, 7, 5, PreviewNavy);
                    PText(plan, Math.Round(seg.WidthMm) + " × " + Math.Round(seg.DepthMm) + " mm", 7, height - 20, PreviewBlue);
                }
            }
            var elev = TieElevationPreviewCanvas;
            if (elev != null)
            {
                elev.Children.Clear();
                double width = elev.ActualWidth, height = elev.ActualHeight;
                if (width > 130 && height > 120)
                {
                    double x1 = width * .34, x2 = width * .6, y1 = 28, y2 = height - 28;
                    double H = Math.Max(1, seg.TopMm - seg.BottomMm);
                    double zone = Math.Min(Math.Max(0, c.EndZone), H / 2);
                    PBox(elev, x1, y1, x2 - x1, y2 - y1, PreviewConcrete, Brushes.SlateGray);
                    PLine(elev, x1 + 6, y1, x1 + 6, y2, PreviewSteel);
                    PLine(elev, x2 - 6, y1, x2 - 6, y2, PreviewSteel);
                    var marks = new List<double>();
                    Action<double, double, double> add = (start, end, spacing) =>
                    {
                        if (spacing <= 0 || end < start) return;
                        int n = Math.Min(100, Math.Max(1, (int)Math.Ceiling((end - start) / spacing)));
                        for (int i = 0; i <= n; i++) marks.Add(start + (end - start) * i / n);
                    };
                    add(0, zone, c.EndSpacing);
                    add(zone, H - zone, c.MidSpacing);
                    add(H - zone, H, c.EndSpacing);
                    foreach (double z in marks.Distinct())
                    {
                        double y = y2 - (y2 - y1) * z / H;
                        PLine(elev, x1 + 3, y, x2 - 3, y, PreviewSteel, 1.3);
                    }
                    PText(elev, "MẶT ĐỨNG ĐAI", 7, 5, PreviewNavy);
                    PText(elev, "a" + Math.Round(c.EndSpacing), x2 + 9, y1 + 15, PreviewSteel);
                    PText(elev, "a" + Math.Round(c.MidSpacing), x2 + 9, (y1 + y2) / 2, PreviewSteel);
                    PText(elev, "H " + Math.Round(H) + " mm", 7, height - 20, PreviewBlue);
                }
            }
            if (TiePreviewStatus != null)
                TiePreviewStatus.Text = seg.LevelName + " · " + c.BarCount + "D" + c.MainDiameter.ToString("0.#") +
                    " · đai D" + c.TieDiameter.ToString("0.#") + " · " +
                    (c.CrossTies ? "có đai phụ" : "không đai phụ") + " · " +
                    (c.ShowJointTies ? "giữ đai nút" : "bỏ đai nút") +
                    ". Mô phỏng Maximum Spacing; chưa tạo Rebar 3D.";
        }

        private void DrawSpliceDynamicPreview()
        {
            var canvas = SpliceDetailCanvas;
            if (canvas == null) return;
            canvas.Children.Clear();
            var c = ActiveConfig;
            int i = SpliceLengthSelector == null ? 0 : Math.Max(0, SpliceLengthSelector.SelectedIndex);
            double factor = i == 1 ? 55 : i == 2 ? 60 : 50;
            double lap = Math.Max(600, factor * c.MainDiameter);
            bool enabled = SplicePanelToggle != null && SplicePanelToggle.IsChecked == true;
            PBox(canvas, 62, 7, 80, 112, PreviewConcrete, Brushes.SlateGray);
            double visible = Math.Max(25, Math.Min(83,
                lap / Math.Max(1, ActiveRow.Segment.TopMm - ActiveRow.Segment.BottomMm) * 110));
            double top = 61 - visible / 2, bottom = 61 + visible / 2;
            Brush steel = enabled ? PreviewSteel : Brushes.LightGray;
            PLine(canvas, 91, top, 91, 117, steel, 3);
            PLine(canvas, 112, 9, 112, bottom, steel, 3);
            PBox(canvas, 84, top, 35, visible, Brushes.Transparent,
                enabled ? PreviewWarning : Brushes.LightGray);
            PText(canvas, enabled ? "Lₛ " + Math.Round(lap) : "TẮT NỐI", 148, 47,
                enabled ? PreviewNavy : PreviewWarning);
            PText(canvas, "D" + c.MainDiameter.ToString("0.#"), 7, 8, PreviewSteel);
            PText(canvas, "MÔ PHỎNG", 6, 108, PreviewWarning, 9);
        }

        private void DrawJointDynamicPreview()
        {
            var canvas = JointDetailCanvas;
            if (canvas == null) return;
            canvas.Children.Clear();
            var c = ActiveConfig;
            double H = Math.Max(1, ActiveRow.Segment.TopMm - ActiveRow.Segment.BottomMm);
            double beam = Math.Max(0, c.JointBeamHeight);
            double band = beam > 0 ? Math.Max(13, Math.Min(62, beam / H * 90)) : 25;
            double y = 50 - band / 2;
            PBox(canvas, 72, 3, 40, 94, PreviewConcrete, Brushes.SlateGray);
            PBox(canvas, 5, y, 174, band, PreviewConcrete, Brushes.SlateGray);
            PLine(canvas, 82, 5, 82, 95, PreviewSteel, 2.5);
            PLine(canvas, 102, 5, 102, 95, PreviewSteel, 2.5);
            for (int i = 0; i < 7; i++)
            {
                double yy = 10 + i * 13;
                if (!c.ShowJointTies && yy >= y && yy <= y + band) continue;
                PLine(canvas, 79, yy, 105, yy, PreviewSteel, 1.5);
            }
            PText(canvas, "DẦM", 10, y + 2, PreviewNavy, 9);
            PText(canvas, "h " + Math.Round(beam), 123, y + 2, PreviewBlue, 9);
            PText(canvas, c.ShowJointTies ? "GIỮ ĐAI" : "BỎ ĐAI", 117, 82,
                c.ShowJointTies ? PreviewNavy : PreviewWarning, 9);
            Tuple<bool, V3BeamJointInfo, string> detection;
            bool detected = _beamDetectionCache.TryGetValue(ActiveRow.Segment.Id.Value, out detection) &&
                detection != null && detection.Item1;
            PText(canvas, detected ? "DẦM ĐÃ NHẬN DIỆN" : "DẦM: CHƯA XÁC MINH",
                3, 2, detected ? PreviewNavy : PreviewWarning, 8);
        }

        private void DrawRoofDynamicPreview()
        {
            var canvas = RoofDetailCanvas;
            if (canvas == null) return;
            canvas.Children.Clear();
            var seg = _segments[_segments.Count - 1];
            var c = _configs[seg.Id.Value];
            bool enabled = c.TerminalInwardHook90;
            double width = Math.Max(1, seg.WidthMm);
            double free = Math.Max(0, width - 2 * c.Cover - c.MainDiameter);
            double leg = Math.Max(0, c.TerminalHookLegMm);
            PBox(canvas, 38, 8, 76, 87, PreviewConcrete, Brushes.SlateGray);
            PLine(canvas, 53, 92, 53, 21, PreviewSteel, 3);
            PLine(canvas, 99, 92, 99, 21, PreviewSteel, 3);
            if (enabled)
            {
                double px = Math.Min(35, leg / width * 76);
                PLine(canvas, 53, 21, 53 + px, 21, PreviewSteel, 3);
                PLine(canvas, 99, 21, 99 - px, 21, PreviewSteel, 3);
            }
            PText(canvas, enabled ? "90°" : "THẲNG", 4, 5, PreviewNavy);
            PText(canvas, "Lm " + Math.Round(leg), 114, 28, PreviewBlue, 9);
            PText(canvas, enabled && leg > free ? "! COVER" : "SƠ BỘ", 107, 69,
                enabled && leg > free ? PreviewWarning : PreviewNavy, 9);
        }

        private void DrawDrawingDynamicPreview()
        {
            var canvas = DrawingPreviewCanvas;
            if (canvas == null) return;
            canvas.Children.Clear();
            double w = canvas.ActualWidth, h = canvas.ActualHeight;
            if (w < 145 || h < 170) return;
            double pageW = Math.Min(w - 30, (h - 35) * .72);
            double pageH = Math.Min(h - 35, pageW / .72);
            double x = (w - pageW) / 2, y = (h - pageH) / 2;
            PBox(canvas, x, y, pageW, pageH, Brushes.White, PreviewNavy, 1.4);
            PBox(canvas, x + 9, y + 9, pageW - 18, pageH - 18,
                Brushes.Transparent, Brushes.SlateGray, .7);
            PText(canvas, "KSTRUCT · DRAFT", x + 15, y + 12, PreviewNavy);
            double axisX = x + pageW * .35, top = y + 43, bottom = y + pageH * .7;
            double min = _segments.Min(s => s.BottomMm), max = _segments.Max(s => s.TopMm);
            Func<double, double> yy = z => bottom - (z - min) / Math.Max(1, max - min) * (bottom - top);
            foreach (var seg in _segments)
            {
                double y1 = yy(seg.TopMm), y2 = yy(seg.BottomMm);
                PBox(canvas, axisX - 10, y1, 20, Math.Max(1, y2 - y1), PreviewConcrete, Brushes.SlateGray, .7);
                PLine(canvas, axisX - 6, y1 + 1, axisX - 6, y2 - 1, PreviewSteel, 1.2);
                PLine(canvas, axisX + 6, y1 + 1, axisX + 6, y2 - 1, PreviewSteel, 1.2);
                PLine(canvas, x + 16, y2, x + pageW - 16, y2, PreviewBlue, .6);
                if (_activeId.HasValue && seg.Id.Value == _activeId.Value)
                    PText(canvas, seg.LevelName, axisX + 15, (y1 + y2) / 2 - 5, PreviewNavy, 9);
            }
            double sy = y + pageH * .75;
            PBox(canvas, x + 17, sy, pageW * .3, pageW * .24, PreviewConcrete, Brushes.SlateGray);
            PBox(canvas, x + 22, sy + 5, pageW * .3 - 10, pageW * .24 - 10,
                Brushes.Transparent, PreviewSteel, 1.5);
            PText(canvas, "Cx " + ActiveConfig.Cx + " · Cy " + ActiveConfig.Cy,
                x + pageW * .51, sy + 4, PreviewNavy, 9);
            PText(canvas, "D" + ActiveConfig.MainDiameter.ToString("0.#") +
                " / Đ" + ActiveConfig.TieDiameter.ToString("0.#"),
                x + pageW * .51, sy + 22, PreviewSteel, 9);
            bool requested = _deployment != null && _deployment.IsDeploymentEnabled;
            var settings = _deployment == null ? null : _deployment.Capture();
            Func<string, string> value = key => settings == null ? "" :
                (settings.Entries.FirstOrDefault(entry => entry.Name == key)?.Value ?? "");
            string sheet = value("SheetNumber"), crossScale = value("CrossScale"), longScale = value("LongScale");
            if (!string.IsNullOrWhiteSpace(sheet))
                PText(canvas, "SHEET " + sheet, x + 14, y + pageH - 17, PreviewNavy, 9);
            if (DrawingPreviewStatus != null)
                DrawingPreviewStatus.Text = (requested ? "Đã chọn triển khai · " : "Chưa chọn triển khai · ") +
                    _segments.Count + " tầng · " + ActiveRow.Segment.LevelName +
                    " · MC 1:" + crossScale + " · MD 1:" + longScale +
                    ". Bố cục mô phỏng; chưa tạo View/Sheet và chưa kiểm tra Tag/Dim.";
        }
    }
}
