using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SDK_HD.KStruct
{
    // All KStruct Ribbon images are drawn from the same flat, negative-space palette.
    // Rendered at native 16px and 32px: no external PNG files, blurry bitmap scaling,
    // missing-manifest-resource failures, gradients, 3D effects or image dependencies.
    internal static class FlatRibbonIcons
    {
        internal const string NavyHex = "#183248";
        internal const string EarthHex = "#B57B56";
        internal const string IvoryHex = "#F8F2E7";

        private static readonly Brush Navy = FrozenBrush(NavyHex);
        private static readonly Brush Earth = FrozenBrush(EarthHex);
        private static readonly Brush Ivory = FrozenBrush(IvoryHex);
        private static readonly Pen Main = FrozenPen(Ivory, 2.0);
        private static readonly Pen Accent = FrozenPen(Earth, 2.0);
        private static readonly Pen Fine = FrozenPen(Ivory, 1.35);
        private static readonly Dictionary<string, ImageSource> Cache =
            new Dictionary<string, ImageSource>(StringComparer.Ordinal);

        private static Brush FrozenBrush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        private static Pen FrozenPen(Brush brush, double width)
        {
            var pen = new Pen(brush, width)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            pen.Freeze();
            return pen;
        }

        internal static ImageSource Create(string commandId, string family, int pixels)
        {
            if (pixels != 16 && pixels != 32)
                throw new ArgumentOutOfRangeException(nameof(pixels), "Ribbon icons must be 16px or 32px.");
            string key = commandId + "|" + family + "|" + pixels;
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out ImageSource cached)) return cached;
                var drawing = new DrawingVisual();
                using (DrawingContext dc = drawing.RenderOpen())
                {
                    dc.PushTransform(new ScaleTransform(pixels / 32.0, pixels / 32.0));
                    // Solid navy tile and the cream line art form the inverse design.
                    dc.DrawRoundedRectangle(Navy, null,
                        new Rect(0.5, 0.5, 31, 31), 5, 5);
                    DrawSubject(dc, commandId ?? "", family ?? "");
                    DrawAction(dc, commandId ?? "");
                    dc.Pop();
                }
                var bitmap = new RenderTargetBitmap(
                    pixels, pixels, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(drawing);
                bitmap.Freeze();
                Cache[key] = bitmap;
                return bitmap;
            }
        }

        private static void Line(DrawingContext dc, Pen pen, double x1, double y1, double x2, double y2) =>
            dc.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));

        private static void Path(DrawingContext dc, Pen pen, string data) =>
            dc.DrawGeometry(null, pen, Geometry.Parse(data));

        private static void DrawSubject(DrawingContext dc, string id, string family)
        {
            string name = id.ToLowerInvariant();
            string group = family.ToLowerInvariant();
            // Prefer the actual command's discipline over its legacy shared icon key.
            if (name.Contains("slab") || name.Contains("floor") || name.Contains("uboot") ||
                name.Contains("bubble") || name.Contains("mushroom"))
                group = "floor";
            else if (name.Contains("wall") || name.Contains("pilaster"))
                group = "wall";
            else if (name.Contains("stair")) group = "stair";
            else if (name.Contains("footing") || name.Contains("foundation"))
                group = "footing";
            else if (name.Contains("column")) group = "column";
            else if (name.Contains("beam") || name.Contains("lintel"))
                group = "beam";
            else if (name.Contains("schedule") || name.Contains("count") ||
                name.Contains("cutlist")) group = "schedule";
            else if (name.Contains("setting") || name.Contains("parameter") ||
                name.Contains("manager")) group = "settings";
            else if (name.Contains("tag") || name.Contains("number") ||
                name.Contains("drawings") || name.Contains("sheet"))
                group = "document";

            switch (group)
            {
                case "column":
                case "rebarcolumn":
                case "rebarcolumnv3":
                    dc.DrawRoundedRectangle(null, Main, new Rect(10, 5.5, 12, 21), 2, 2);
                    Line(dc, Accent, 13, 7.5, 13, 24);
                    Line(dc, Accent, 19, 7.5, 19, 24);
                    Line(dc, Fine, 11, 11, 21, 11);
                    Line(dc, Fine, 11, 17, 21, 17);
                    Line(dc, Fine, 11, 23, 21, 23);
                    break;
                case "beam":
                    dc.DrawRoundedRectangle(null, Main, new Rect(5, 10, 22, 12), 2, 2);
                    Line(dc, Accent, 7.5, 14, 24.5, 14);
                    Line(dc, Accent, 7.5, 19, 24.5, 19);
                    Line(dc, Fine, 12, 12, 12, 20);
                    Line(dc, Fine, 20, 12, 20, 20);
                    break;
                case "floor":
                    Path(dc, Main, "M 5,13 L 21,8 L 27,13 L 11,18 Z");
                    Line(dc, Accent, 8, 16, 8, 23);
                    Line(dc, Accent, 11, 18, 11, 25);
                    Line(dc, Main, 11, 25, 27, 20);
                    Line(dc, Main, 27, 13, 27, 20);
                    Line(dc, Fine, 11, 13, 22, 10);
                    Line(dc, Fine, 16, 16, 24, 13);
                    break;
                case "footing":
                    Path(dc, Main, "M 5,19 L 27,19 L 24,26 L 8,26 Z");
                    dc.DrawRoundedRectangle(null, Main, new Rect(12, 6, 8, 13), 1, 1);
                    Line(dc, Accent, 8, 22, 24, 22);
                    Line(dc, Accent, 12, 24.5, 12, 27);
                    Line(dc, Accent, 20, 24.5, 20, 27);
                    break;
                case "wall":
                    dc.DrawRoundedRectangle(null, Main, new Rect(6, 8, 20, 16), 1, 1);
                    Line(dc, Accent, 6, 15, 26, 15);
                    Line(dc, Fine, 16, 8, 16, 15);
                    Line(dc, Fine, 11, 15, 11, 24);
                    Line(dc, Fine, 21, 15, 21, 24);
                    break;
                case "stair":
                    Path(dc, Main, "M 5,25 L 11,25 L 11,20 L 17,20 L 17,15 L 23,15 L 23,9 L 27,9");
                    Line(dc, Accent, 7, 13, 24, 6);
                    break;
                case "schedule":
                case "document":
                    dc.DrawRoundedRectangle(null, Main, new Rect(8, 5.5, 16, 21), 2, 2);
                    Line(dc, Accent, 12, 11, 20, 11);
                    Line(dc, Fine, 12, 15.5, 20, 15.5);
                    Line(dc, Fine, 12, 20, 18, 20);
                    break;
                case "settings":
                    dc.DrawEllipse(null, Main, new Point(16, 15.5), 8, 8);
                    dc.DrawEllipse(null, Accent, new Point(16, 15.5), 3, 3);
                    for (int i = 0; i < 8; i++)
                    {
                        double angle = Math.PI * i / 4;
                        double dx = Math.Cos(angle), dy = Math.Sin(angle);
                        Line(dc, Main, 16 + dx * 9, 15.5 + dy * 9,
                            16 + dx * 11.5, 15.5 + dy * 11.5);
                    }
                    break;
                default:
                    // Universal reinforcement: bent steel with identifiable two-leg hook.
                    Path(dc, Main, "M 7,7 L 7,22 Q 7,25 10,25 L 22,25 Q 25,25 25,22 L 25,8");
                    Line(dc, Accent, 11, 10, 11, 21);
                    Line(dc, Accent, 11, 21, 21, 21);
                    Line(dc, Accent, 21, 21, 21, 12);
                    break;
            }
        }

        private static void DrawAction(DrawingContext dc, string id)
        {
            string s = id.ToLowerInvariant();
            // Smaller terracotta action tile visually differentiates related commands
            // without changing any command ID, handler, group or tooltip.
            string action = "";
            if (s.Contains("delete") || s.Contains("remove") || s.Contains("explode")) action = "delete";
            else if (s.Contains("update") || s.Contains("refresh") || s.Contains("match")) action = "update";
            else if (s.Contains("setting") || s.Contains("config") || s.Contains("manage") ||
                     s.Contains("manager")) action = "settings";
            else if (s.Contains("select") || s.Contains("filter")) action = "select";
            else if (s.Contains("tag") || s.Contains("mark") || s.Contains("number")) action = "tag";
            else if (s.Contains("dimension") || s.Contains("length")) action = "dimension";
            else if (s.Contains("export")) action = "export";
            else if (s.Contains("copy")) action = "copy";
            else if (s.Contains("hide") || s.Contains("visibility")) action = "hide";
            else if (s.Contains("color") || s.Contains("paint")) action = "color";
            else if (s.Contains("check") || s.Contains("preview")) action = "check";
            else if (s.Contains("create") || s.Contains("draw") || s.Contains("place") ||
                     s.Contains("reinforce") || s.Contains("add")) action = "add";
            else if (s.Contains("split") || s.Contains("bend") || s.Contains("extend")) action = "edit";
            else if (s.Contains("optimize")) action = "check";
            if (action.Length == 0) return;

            dc.DrawRoundedRectangle(Earth, null, new Rect(20, 20, 10, 10), 2, 2);
            var white = new Pen(Ivory, 1.55)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            switch (action)
            {
                case "delete":
                    Line(dc, white, 23, 23, 27, 27);
                    Line(dc, white, 27, 23, 23, 27);
                    break;
                case "update":
                    Path(dc, white, "M 23,24 A 2.7,2.7 0 1 1 25,27.7 M 23,24 L 23,22");
                    break;
                case "select":
                    dc.DrawRectangle(null, white, new Rect(23, 23, 4, 4));
                    break;
                case "settings":
                    dc.DrawEllipse(null, white, new Point(25, 25), 2.5, 2.5);
                    dc.DrawEllipse(Ivory, null, new Point(25, 25), 0.8, 0.8);
                    break;
                case "tag":
                    Path(dc, white, "M 23,22.5 L 27,22.5 L 27.5,25 L 25,27.5 L 22.5,25 Z");
                    break;
                case "dimension":
                    Line(dc, white, 22.5, 25, 27.5, 25);
                    Line(dc, white, 22.5, 23, 22.5, 27);
                    Line(dc, white, 27.5, 23, 27.5, 27);
                    break;
                case "export":
                    Line(dc, white, 25, 22, 25, 27);
                    Line(dc, white, 23, 25, 25, 27);
                    Line(dc, white, 27, 25, 25, 27);
                    break;
                case "copy":
                    dc.DrawRectangle(null, white, new Rect(22, 22, 4.5, 4.5));
                    dc.DrawRectangle(null, white, new Rect(24, 24, 4, 4));
                    break;
                case "hide":
                    dc.DrawEllipse(null, white, new Point(25, 25), 2, 1.4);
                    Line(dc, white, 22, 28, 28, 22);
                    break;
                case "color":
                    dc.DrawEllipse(Ivory, null, new Point(24, 23.5), 1, 1);
                    dc.DrawEllipse(Ivory, null, new Point(27, 24.5), 1, 1);
                    dc.DrawEllipse(Ivory, null, new Point(24.5, 27), 1, 1);
                    break;
                case "check":
                    Path(dc, white, "M 22.5,25 L 24.5,27 L 27.5,23");
                    break;
                case "add":
                    Line(dc, white, 25, 22.5, 25, 27.5);
                    Line(dc, white, 22.5, 25, 27.5, 25);
                    break;
                default:
                    Path(dc, white, "M 23,27 L 27,23 M 25.5,22.5 L 27.5,24.5");
                    break;
            }
        }
    }
}
