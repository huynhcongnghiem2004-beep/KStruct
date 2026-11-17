using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.UI;

namespace SDK_HD.KStruct.NativeRebar
{
    [Serializable]
    public sealed class RebarStockInventoryItem
    {
        public string Id { get; set; }
        public string RebarTypeName { get; set; }
        public double DiameterMm { get; set; }
        public double LengthMm { get; set; }
        public int Quantity { get; set; }
        public string Warehouse { get; set; }
        public string Bin { get; set; }
        public string ReceiptReference { get; set; }
        public DateTime ReceivedAtUtc { get; set; }
        public DateTime? QcInspectedAtUtc { get; set; }
        public string QcInspector { get; set; }
        public string QcNote { get; set; }
        public bool Available { get; set; }
        public bool QcPassed { get; set; }

        public RebarStockInventoryItem()
        {
            Id = Guid.NewGuid().ToString("N");
            RebarTypeName = "";
            Warehouse = "";
            Bin = "";
            ReceiptReference = "";
            ReceivedAtUtc = DateTime.UtcNow;
            QcInspector = "";
            QcNote = "Chờ QC";
            Available = false;
            QcPassed = false;
        }
    }

    [Serializable]
    [XmlRoot("KStructRebarCutStock")]
    public sealed class RebarStockInventoryData
    {
        [XmlArray("Items"), XmlArrayItem("Stock")]
        public ObservableCollection<RebarStockInventoryItem> Items { get; set; } = new ObservableCollection<RebarStockInventoryItem>();
    }

    internal static class RebarStockInventoryStore
    {
        public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KStruct", "RebarCutStock.xml");

        public static RebarStockInventoryData Load()
        {
            if (!File.Exists(FilePath)) return new RebarStockInventoryData();
            using (var stream = File.OpenRead(FilePath))
            {
                var data = (RebarStockInventoryData)new XmlSerializer(typeof(RebarStockInventoryData)).Deserialize(stream);
                foreach (RebarStockInventoryItem item in data.Items)
                    if (string.IsNullOrWhiteSpace(item.Id)) item.Id = Guid.NewGuid().ToString("N");
                return data;
            }
        }

        public static void Save(RebarStockInventoryData data)
        {
            string folder = Path.GetDirectoryName(FilePath);
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            using (var stream = File.Create(FilePath))
                new XmlSerializer(typeof(RebarStockInventoryData)).Serialize(stream, data);
        }
    }

    internal static class RebarStockInventoryEditor
    {
        public static bool Show(IntPtr owner)
        {
            RebarStockInventoryData data = RebarStockInventoryStore.Load();
            var window = new Window
            {
                Title = "Kho phôi cốt thép - KStruct",
                Width = 920,
                Height = 540,
                MinWidth = 780,
                MinHeight = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new DockPanel { Margin = new Thickness(12) };
            var note = new TextBlock
            {
                Text = "Chỉ phôi Available và QC passed mới được dùng trong phương án cắt. Mỗi dòng là số lượng phôi đồng nhất theo loại thép, đường kính, chiều dài và kho/vị trí.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            DockPanel.SetDock(note, Dock.Top);
            root.Children.Add(note);

            var grid = new DataGrid
            {
                ItemsSource = data.Items,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                IsReadOnly = false,
                Margin = new Thickness(0, 0, 0, 8)
            };
            grid.Columns.Add(new DataGridTextColumn { Header = "Loại thép / grade", Binding = new System.Windows.Data.Binding("RebarTypeName"), Width = new DataGridLength(1.6, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Ø mm", Binding = new System.Windows.Data.Binding("DiameterMm"), Width = 75 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Dài mm", Binding = new System.Windows.Data.Binding("LengthMm"), Width = 95 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Số cây", Binding = new System.Windows.Data.Binding("Quantity"), Width = 75 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Kho", Binding = new System.Windows.Data.Binding("Warehouse"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Ô/kệ", Binding = new System.Windows.Data.Binding("Bin"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Phiếu nhập", Binding = new System.Windows.Data.Binding("ReceiptReference"), Width = 120 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Nhập UTC", Binding = new System.Windows.Data.Binding("ReceivedAtUtc") { StringFormat = "yyyy-MM-dd HH:mm" }, Width = 120, IsReadOnly = true });
            grid.Columns.Add(new DataGridTextColumn { Header = "Người QC", Binding = new System.Windows.Data.Binding("QcInspector"), Width = 110, IsReadOnly = true });
            grid.Columns.Add(new DataGridTextColumn { Header = "QC UTC", Binding = new System.Windows.Data.Binding("QcInspectedAtUtc") { StringFormat = "yyyy-MM-dd HH:mm" }, Width = 120, IsReadOnly = true });
            grid.Columns.Add(new DataGridTextColumn { Header = "Ghi chú QC", Binding = new System.Windows.Data.Binding("QcNote"), Width = 160, IsReadOnly = true });
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Available", Binding = new System.Windows.Data.Binding("Available"), Width = 85, IsReadOnly = true });
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "QC passed", Binding = new System.Windows.Data.Binding("QcPassed"), Width = 90, IsReadOnly = true });
            root.Children.Add(grid);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var add = new Button { Content = "Nhập phôi…", MinWidth = 92, Margin = new Thickness(0, 0, 8, 0) };
            var qc = new Button { Content = "Ghi nhận QC…", MinWidth = 105, Margin = new Thickness(0, 0, 8, 0) };
            var remove = new Button { Content = "Xóa dòng", MinWidth = 92, Margin = new Thickness(0, 0, 8, 0) };
            var save = new Button { Content = "Lưu kho", MinWidth = 92, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Đóng", MinWidth = 76, IsCancel = true };
            add.Click += (sender, args) =>
            {
                RebarStockInventoryItem received = ShowReceiveDialog(new WindowInteropHelper(window).Handle);
                if (received == null) return;
                data.Items.Add(received);
                grid.SelectedItem = data.Items.LastOrDefault();
                grid.ScrollIntoView(grid.SelectedItem);
            };
            qc.Click += (sender, args) =>
            {
                List<RebarStockInventoryItem> selected = grid.SelectedItems.Cast<object>().OfType<RebarStockInventoryItem>().ToList();
                if (selected.Count == 0)
                {
                    TaskDialog.Show("KStruct - QC kho phôi", "Chọn ít nhất một lô phôi cần ghi nhận QC.");
                    return;
                }
                MessageBoxResult decision = MessageBox.Show("Yes = QC đạt và Available; No = QC không đạt, cách ly; Cancel = không đổi.",
                    "Ghi nhận QC cho " + selected.Count + " lô", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
                if (decision == MessageBoxResult.Cancel) return;
                bool passed = decision == MessageBoxResult.Yes;
                DateTime inspected = DateTime.UtcNow;
                foreach (RebarStockInventoryItem item in selected)
                {
                    item.QcPassed = passed;
                    item.Available = passed;
                    item.QcInspectedAtUtc = inspected;
                    item.QcInspector = Environment.UserName;
                    item.QcNote = passed ? "QC đạt" : "QC không đạt - cách ly";
                }
                grid.Items.Refresh();
            };
            remove.Click += (sender, args) =>
            {
                foreach (RebarStockInventoryItem item in grid.SelectedItems.Cast<object>().OfType<RebarStockInventoryItem>().ToList())
                    data.Items.Remove(item);
            };
            bool saved = false;
            save.Click += (sender, args) =>
            {
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
                if (data.Items.Any(item => string.IsNullOrWhiteSpace(item.RebarTypeName) || item.DiameterMm <= 0 ||
                    item.LengthMm <= 0 || item.Quantity < 0))
                {
                    TaskDialog.Show("KStruct - Kho phôi", "Mỗi dòng cần có loại thép, đường kính và chiều dài dương; số cây không được âm.");
                    return;
                }
                try
                {
                    RebarStockInventoryStore.Save(data);
                    saved = true;
                    window.DialogResult = true;
                }
                catch (Exception ex) { TaskDialog.Show("KStruct - Kho phôi", ex.Message); }
            };
            buttons.Children.Add(add);
            buttons.Children.Add(qc);
            buttons.Children.Add(remove);
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            window.ShowDialog();
            return saved;
        }

        private static RebarStockInventoryItem ShowReceiveDialog(IntPtr owner)
        {
            var window = new Window
            {
                Title = "Nhập lô phôi cốt thép",
                Width = 410,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(14) };
            var type = AddField(root, "Rebar Type name (đúng tên trong project):", "");
            var diameter = AddField(root, "Đường kính (mm):", "16");
            var length = AddField(root, "Chiều dài cây (mm):", "12000");
            var quantity = AddField(root, "Số cây nhận:", "1");
            var warehouse = AddField(root, "Kho:", "Kho 1");
            var bin = AddField(root, "Ô/kệ:", "");
            var receipt = AddField(root, "Mã phiếu nhập:", "");
            var note = new TextBlock { Text = "Lô mới sẽ ở trạng thái chờ QC và không được đưa vào phương án cắt.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) };
            root.Children.Add(note);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var accept = new Button { Content = "Nhập lô", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            RebarStockInventoryItem result = null;
            accept.Click += (sender, args) =>
            {
                double d, l;
                int q;
                if (string.IsNullOrWhiteSpace(type.Text) || !double.TryParse(diameter.Text, out d) || d <= 0 ||
                    !double.TryParse(length.Text, out l) || l <= 0 || !int.TryParse(quantity.Text, out q) || q <= 0)
                {
                    TaskDialog.Show("KStruct - Nhập kho", "Nhập đúng Rebar Type, đường kính/chiều dài dương và số cây lớn hơn 0.");
                    return;
                }
                result = new RebarStockInventoryItem
                {
                    RebarTypeName = type.Text.Trim(), DiameterMm = d, LengthMm = l, Quantity = q,
                    Warehouse = warehouse.Text.Trim(), Bin = bin.Text.Trim(), ReceiptReference = receipt.Text.Trim(),
                    ReceivedAtUtc = DateTime.UtcNow, Available = false, QcPassed = false, QcNote = "Chờ QC"
                };
                window.DialogResult = true;
            };
            buttons.Children.Add(accept);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            return window.ShowDialog() == true ? result : null;
        }

        private static System.Windows.Controls.TextBox AddField(Panel panel, string label, string value)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 3, 0, 1) });
            var input = new System.Windows.Controls.TextBox { Text = value, MinWidth = 360, MinHeight = 22, Margin = new Thickness(0, 0, 0, 3) };
            panel.Children.Add(input);
            return input;
        }
    }
}
