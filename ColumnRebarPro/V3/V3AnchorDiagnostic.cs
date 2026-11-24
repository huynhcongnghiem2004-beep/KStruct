using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ColumnRebarPro.V3
{
    internal sealed class V3AnchorErrorRow
    {
        public int Bar { get; set; }
        public string Segment { get; set; }
        public string Probe { get; set; }
        public string Start { get; set; }
        public string End { get; set; }
        public double GapMm { get; set; }
    }

    internal sealed class V3AnchorDiagnosticException : InvalidOperationException
    {
        internal string Context { get; }
        internal List<V3AnchorErrorRow> Rows { get; }
        internal V3AnchorDiagnosticException(string context, List<V3AnchorErrorRow> rows)
            : base("Neo móng không đạt kiểm tra hình học: " + rows.Count + " đường kiểm tra lỗi; thanh dự kiến #" +
                   rows[0].Bar + ", " + rows[0].Segment + ", " + rows[0].Probe + ".")
        { Context = context; Rows = rows; }
    }

    internal static class V3AnchorDiagnostic
    {
        internal static bool Show(Window owner, Exception error)
        {
            var report = error as V3AnchorDiagnosticException;
            if (report == null) return error.InnerException != null && Show(owner, error.InnerException);
            CreateWindow(owner, report).ShowDialog(); return true;
        }

        internal static Window CreateWindow(Window owner, V3AnchorDiagnosticException report)
        {
            var window = new Window { Title = "KStruct - Bảng lỗi neo móng", Width = 1150, Height = 620,
                MinWidth = 700, MinHeight = 380, WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                Background = System.Windows.Media.Brushes.White, Foreground = System.Windows.Media.Brushes.Black };
            if (owner != null) window.Owner = owner;
            window.Width = Math.Min(window.Width, SystemParameters.WorkArea.Width);
            window.Height = Math.Min(window.Height, SystemParameters.WorkArea.Height);
            var panel = new DockPanel { Margin = new Thickness(12), Background = System.Windows.Media.Brushes.White };
            var heading = new TextBlock { Text = report.Context + "\n" + report.Message +
                "\nTọa độ XYZ theo hệ nội bộ Revit, đơn vị mm. Thanh dự kiến chưa có ElementId. Trái/phải nhìn theo hướng chân neo; X/Y theo trục cột. " +
                "Mỗi dòng là khoảng hở đầu tiên trên một đường kiểm tra bao cover; không phải khoảng thiếu cover vuông góc mặt móng.",
                TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Black, Margin = new Thickness(0,0,0,12) };
            DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var export = new Button { Content = "Xuất CSV…", Padding = new Thickness(15,7,15,7), Margin = new Thickness(5) };
            export.Click += (s,e) => {
                var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "CSV UTF-8 (*.csv)|*.csv", FileName = "KStruct-Anchor-Errors.csv" };
                if (dialog.ShowDialog(window) != true) return;
                try {
                    var lines = new List<string> { Csv(report.Context), "Thanh,Đoạn,Đường kiểm tra,XYZ bắt đầu lỗi (mm),XYZ kết thúc lỗi (mm),Chiều dài khoảng hở đầu tiên (mm)" };
                    lines.AddRange(report.Rows.Select(r => string.Join(",", r.Bar.ToString(CultureInfo.InvariantCulture), Csv(r.Segment), Csv(r.Probe), Csv(r.Start), Csv(r.End), r.GapMm.ToString("0.###", CultureInfo.InvariantCulture))));
                    File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(true));
                } catch (Exception ex) { MessageBox.Show(window, ex.Message, "Không xuất được log"); }
            };
            var close = new Button { Content = "Đóng", IsCancel = true, Padding = new Thickness(15,7,15,7), Margin = new Thickness(5) };
            close.Click += (s,e) => window.Close();
            buttons.Children.Add(export); buttons.Children.Add(close);
            DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
            var grid = new DataGrid { ItemsSource = report.Rows, IsReadOnly = true, AutoGenerateColumns = false,
                CanUserAddRows = false, CanUserDeleteRows = false, SelectionUnit = DataGridSelectionUnit.CellOrRowHeader };
            string[] properties = { "Bar", "Segment", "Probe", "Start", "End", "GapMm" };
            string[] labels = { "Thanh #", "Đoạn lỗi", "Đường kiểm tra", "XYZ bắt đầu lỗi (mm)", "XYZ kết thúc lỗi (mm)", "Khoảng hở đầu tiên (mm)" };
            double[] widths = { 65, 130, 180, 205, 205, 180 };
            for (int i=0; i<properties.Length; i++) grid.Columns.Add(new DataGridTextColumn {
                Header = new TextBlock { Text = labels[i], TextWrapping = TextWrapping.Wrap, MaxWidth = widths[i] - 12 },
                Binding = new Binding(properties[i]) { StringFormat = i == 5 ? "0.###" : null },
                Width = new DataGridLength(widths[i]), MinWidth = 65 });
            panel.Children.Add(grid); window.Content = panel; return window;
        }

        private static string Csv(string value)
        {
            value = value ?? "";
            if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
