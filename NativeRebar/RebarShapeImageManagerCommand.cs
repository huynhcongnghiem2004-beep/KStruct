using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class RebarShapeImageManagerCommand : IExternalCommand
    {
        private sealed class ShapeItem
        {
            public RebarShape Shape { get; private set; }
            public string Name { get { return Shape.Name; } }
            public ShapeItem(RebarShape shape) { Shape = shape; }
            public override string ToString() { return Name; }
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            try
            {
                var items = new FilteredElementCollector(ui.Document).OfClass(typeof(RebarShape))
                    .Cast<RebarShape>().OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(x => new ShapeItem(x)).ToList();
                if (items.Count == 0)
                {
                    TaskDialog.Show("KStruct - Ảnh hình thép", "Project chưa có RebarShape.");
                    return Result.Cancelled;
                }

                string folder = GetImageFolder();
                Directory.CreateDirectory(folder);
                var window = new Window
                {
                    Title = "KStruct - Quản lý ảnh hình thép",
                    Width = 720,
                    Height = 500,
                    MinWidth = 550,
                    MinHeight = 360,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ShowInTaskbar = false
                };
                new WindowInteropHelper(window).Owner = data.Application.MainWindowHandle;
                var root = new System.Windows.Controls.Grid { Margin = new Thickness(12) };
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var list = new ListBox { ItemsSource = items, DisplayMemberPath = "Name", Margin = new Thickness(0, 0, 10, 0) };
                System.Windows.Controls.Grid.SetRow(list, 0); System.Windows.Controls.Grid.SetColumn(list, 0); root.Children.Add(list);
                var preview = new Image { Stretch = System.Windows.Media.Stretch.Uniform, Margin = new Thickness(8) };
                var previewBorder = new Border { BorderBrush = System.Windows.Media.Brushes.Gray, BorderThickness = new Thickness(1), Child = preview };
                System.Windows.Controls.Grid.SetRow(previewBorder, 0); System.Windows.Controls.Grid.SetColumn(previewBorder, 1); root.Children.Add(previewBorder);
                var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
                System.Windows.Controls.Grid.SetRow(status, 1); System.Windows.Controls.Grid.SetColumn(status, 0); root.Children.Add(status);
                var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
                System.Windows.Controls.Grid.SetRow(controls, 1); System.Windows.Controls.Grid.SetColumn(controls, 1); root.Children.Add(controls);
                var assign = new Button { Content = "Gán ảnh...", MinWidth = 95, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 8, 0) };
                var remove = new Button { Content = "Xóa ảnh", MinWidth = 80, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 8, 0) };
                var close = new Button { Content = "Đóng", MinWidth = 75, Padding = new Thickness(8, 4, 8, 4), IsCancel = true };
                controls.Children.Add(assign); controls.Children.Add(remove); controls.Children.Add(close);

                Action refresh = () =>
                {
                    ShapeItem item = list.SelectedItem as ShapeItem;
                    string path = item == null ? null : FindImage(item.Shape.Name);
                    preview.Source = null;
                    status.Text = item == null ? "Chọn RebarShape để xem hoặc gán ảnh." :
                        (path == null ? item.Name + " — chưa có ảnh minh họa." : item.Name + " — " + Path.GetFileName(path));
                    if (path != null)
                    {
                        try
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(path); bitmap.EndInit();
                            preview.Source = bitmap;
                        }
                        catch (Exception ex) { status.Text += "\nKhông đọc được ảnh: " + ex.Message; }
                    }
                };
                list.SelectionChanged += (s, e) => refresh();
                assign.Click += (s, e) =>
                {
                    ShapeItem item = list.SelectedItem as ShapeItem;
                    if (item == null) { status.Text = "Hãy chọn một RebarShape trước."; return; }
                    using (var dialog = new System.Windows.Forms.OpenFileDialog())
                    {
                        dialog.Title = "Chọn ảnh minh họa cho " + item.Name;
                        dialog.Filter = "Ảnh|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff";
                        dialog.CheckFileExists = true;
                        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                        string ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                        string target = Path.Combine(folder, ShapeKey(item.Shape.Name) + ext);
                        foreach (string old in Directory.GetFiles(folder, ShapeKey(item.Shape.Name) + ".*")) File.Delete(old);
                        File.Copy(dialog.FileName, target, true);
                        refresh();
                    }
                };
                remove.Click += (s, e) =>
                {
                    ShapeItem item = list.SelectedItem as ShapeItem;
                    if (item == null) return;
                    foreach (string old in Directory.GetFiles(folder, ShapeKey(item.Shape.Name) + ".*")) File.Delete(old);
                    refresh();
                };
                list.SelectedIndex = 0;
                window.Content = root;
                window.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Quản lý ảnh hình thép", ex.Message);
                return Result.Failed;
            }
        }

        internal static string FindImage(string shapeName)
        {
            return Directory.Exists(GetImageFolder()) ? Directory.GetFiles(GetImageFolder(), ShapeKey(shapeName) + ".*")
                .FirstOrDefault(path => new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" }
                    .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) : null;
        }

        private static string GetImageFolder() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KStruct", "RebarShapeImages"); }

        private static string ShapeKey(string name)
        {
            string readable = Regex.Replace(name ?? "shape", "[^A-Za-z0-9_-]+", "_").Trim('_');
            if (readable.Length > 40) readable = readable.Substring(0, 40);
            if (readable.Length == 0) readable = "shape";
            using (var sha = SHA256.Create())
            {
                string hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(name ?? ""))).Replace("-", "").Substring(0, 10);
                return readable + "_" + hash;
            }
        }
    }
}



