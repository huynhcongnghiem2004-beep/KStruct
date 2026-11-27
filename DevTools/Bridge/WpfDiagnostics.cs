using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace SDK_HD.KStruct.DevTools
{
    // Opt-in, in-process, read-only diagnostics. No control values or model data are retained.
    public static class WpfDiagnostics
    {
        private static bool enabled;
        public static bool Enabled { get { return enabled; } set { enabled = value; if (value) LastSnapshot = null; } }
        public static WpfSnapshot LastSnapshot { get; private set; }

        public static void Attach(Window window, string kind)
        {
            if (!Enabled || window == null) return;
            try
            {
                var counts = new Dictionary<string, int>();
                RoutedEventHandler click = (s, e) => Increment(counts, "click");
                TextChangedEventHandler text = (s, e) => Increment(counts, "text_changed");
                SelectionChangedEventHandler selection = (s, e) => Increment(counts, "selection_changed");
                window.AddHandler(ButtonBase.ClickEvent, click, true);
                window.AddHandler(TextBoxBase.TextChangedEvent, text, true);
                window.AddHandler(Selector.SelectionChangedEvent, selection, true);
                window.Closed += (s, e) => {
                    try { LastSnapshot = Capture(window, kind, counts); }
                    catch { /* Diagnostics must never affect the product. */ }
                    finally {
                        window.RemoveHandler(ButtonBase.ClickEvent, click);
                        window.RemoveHandler(TextBoxBase.TextChangedEvent, text);
                        window.RemoveHandler(Selector.SelectionChangedEvent, selection);
                    }
                };
            }
            catch { /* Diagnostics must never prevent a dialog from opening. */ }
        }

        private static void Increment(Dictionary<string, int> counts, string name)
        {
            counts[name] = counts.TryGetValue(name, out int value) ? value + 1 : 1;
        }

        private static WpfSnapshot Capture(Window window, string kind, Dictionary<string, int> counts)
        {
            var snapshot = new WpfSnapshot { Kind = kind, WidthDip = window.ActualWidth,
                HeightDip = window.ActualHeight, CapturedUtc = DateTime.UtcNow.ToString("o"),
                Events = new Dictionary<string, int>(counts) };
            var dpi = VisualTreeHelper.GetDpi(window);
            snapshot.DpiX = dpi.PixelsPerInchX;
            snapshot.DpiY = dpi.PixelsPerInchY;
            Walk(window, snapshot);
            return snapshot;
        }

        private static void Walk(DependencyObject node, WpfSnapshot snapshot)
        {
            snapshot.VisualCount++;
            if (node is FrameworkElement element && !string.IsNullOrEmpty(element.Name)) snapshot.NamedCount++;
            if (node is Control) snapshot.ControlCount++;
            var values = node.GetLocalValueEnumerator();
            while (values.MoveNext()) if (BindingOperations.IsDataBound(node, values.Current.Property)) snapshot.BindingCount++;
            if (node is Canvas canvas) { snapshot.CanvasCount++; snapshot.CanvasChildren += canvas.Children.Count; }
            if (node is Viewport3D viewport) { snapshot.Viewport3DCount++; snapshot.Viewport3DChildren += viewport.Children.Count; }
            if (Validation.GetHasError(node)) snapshot.ValidationErrorCount++;
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++) Walk(VisualTreeHelper.GetChild(node, i), snapshot);
        }
    }

    public sealed class WpfSnapshot
    {
        public string Kind { get; set; }
        public string CapturedUtc { get; set; }
        public double WidthDip { get; set; }
        public double HeightDip { get; set; }
        public double DpiX { get; set; }
        public double DpiY { get; set; }
        public int VisualCount { get; set; }
        public int NamedCount { get; set; }
        public int ControlCount { get; set; }
        public int BindingCount { get; set; }
        public int CanvasCount { get; set; }
        public int CanvasChildren { get; set; }
        public int Viewport3DCount { get; set; }
        public int Viewport3DChildren { get; set; }
        public int ValidationErrorCount { get; set; }
        public Dictionary<string, int> Events { get; set; }
    }
}
