using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ColumnRebarPro.V3
{
    internal sealed class V4BatchReuseCandidate
    {
        internal int Index { get; set; }
        internal string Label { get; set; }
    }

    // A single batch selection replaces the V3 all-or-nothing Yes/No dialog.
    // Each unchecked stack retains its own configuration editor.
    internal static class V4BatchReuseDialog
    {
        internal static HashSet<int> Choose(IntPtr owner, IList<V4BatchReuseCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0) return new HashSet<int>();
            var window = new Window
            {
                Title = "KStruct V4 - Chọn tuyến áp dụng cấu hình",
                Width = 630, Height = Math.Min(610, 235 + candidates.Count * 35),
                MinWidth = 500, MinHeight = 270,
                ResizeMode = ResizeMode.CanResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brushes.White,
                ShowInTaskbar = false
            };
            new System.Windows.Interop.WindowInteropHelper(window).Owner = owner;

            var root = new DockPanel { Margin = new Thickness(16) };
            window.Content = root;
            var header = new TextBlock
            {
                Text = "Những tuyến dưới đây cùng số tầng và kích thước với tuyến đầu. " +
                    "Chọn tuyến muốn sử dụng lại cấu hình thép và hồ sơ đã nhập. " +
                    "Tuyến không chọn sẽ mở giao diện cấu hình riêng.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var footer = new StackPanel { Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            var items = candidates.Select(candidate => new
            {
                candidate.Index,
                Check = new CheckBox
                {
                    Content = candidate.Label, IsChecked = true,
                    Margin = new Thickness(4, 6, 4, 6),
                    VerticalContentAlignment = VerticalAlignment.Center
                }
            }).ToList();

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var rows = new StackPanel();
            foreach (var item in items) rows.Children.Add(item.Check);
            scroll.Content = rows;
            root.Children.Add(scroll);

            var selectAll = new Button { Content = "Chọn tất cả", MinWidth = 105,
                Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(4) };
            selectAll.Click += (sender, args) =>
            {
                foreach (var item in items) item.Check.IsChecked = true;
            };
            footer.Children.Add(selectAll);
            var clear = new Button { Content = "Bỏ chọn", MinWidth = 90,
                Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(4) };
            clear.Click += (sender, args) =>
            {
                foreach (var item in items) item.Check.IsChecked = false;
            };
            footer.Children.Add(clear);
            var cancel = new Button { Content = "Hủy", IsCancel = true, MinWidth = 75,
                Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(4) };
            cancel.Click += (sender, args) => window.DialogResult = false;
            footer.Children.Add(cancel);
            var accept = new Button { Content = "Tiếp tục", IsDefault = true, MinWidth = 95,
                Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(4) };
            accept.Click += (sender, args) => window.DialogResult = true;
            footer.Children.Add(accept);

            if (window.ShowDialog() != true) return null;
            return new HashSet<int>(items.Where(item => item.Check.IsChecked == true).Select(item => item.Index));
        }
    }
}
