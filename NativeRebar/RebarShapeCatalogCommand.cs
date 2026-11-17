using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class RebarShapeCatalogCommand : IExternalCommand
    {
        private sealed class ShapeRow
        {
            public RebarShape Shape { get; set; }
            public string Name { get { return Shape.Name; } }
            public string Usage { get; set; }
            public override string ToString() { return Name + "   |   " + Usage; }
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<RebarShape> shapes = new FilteredElementCollector(doc).OfClass(typeof(RebarShape))
                    .Cast<RebarShape>().OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (shapes.Count == 0)
                {
                    TaskDialog.Show("KStruct - Thư viện hình thép", "Project chưa có RebarShape.");
                    return Result.Cancelled;
                }
                var usage = new Dictionary<ElementId, int>();
                foreach (Rebar bar in new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>())
                {
                    try
                    {
                        ElementId shapeId = bar.GetShapeId();
                        if (shapeId == ElementId.InvalidElementId) continue;
                        int count; usage.TryGetValue(shapeId, out count);
                        usage[shapeId] = count + Math.Max(1, bar.Quantity);
                    }
                    catch { }
                }
                List<ShapeRow> rows = shapes.Select(shape =>
                {
                    int count; usage.TryGetValue(shape.Id, out count);
                    return new ShapeRow { Shape = shape, Usage = count + " thanh trong project" };
                }).ToList();

                var window = new Window
                {
                    Title = "KStruct - Thư viện hình thép",
                    Width = 760, Height = 540, MinWidth = 560, MinHeight = 360,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ResizeMode = ResizeMode.CanResize, ShowInTaskbar = false
                };
                new WindowInteropHelper(window).Owner = data.Application.MainWindowHandle;
                var root = new System.Windows.Controls.Grid { Margin = new Thickness(12) };
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var info = new TextBlock
                {
                    Text = "Project có " + rows.Count + " RebarShape. Chọn shape để xem ảnh minh họa và thống kê thanh đang dùng.",
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
                };
                System.Windows.Controls.Grid.SetRow(info, 0);
                System.Windows.Controls.Grid.SetColumnSpan(info, 2);
                root.Children.Add(info);
                var list = new ListBox { ItemsSource = rows, Margin = new Thickness(0, 0, 10, 0) };
                System.Windows.Controls.Grid.SetRow(list, 1); System.Windows.Controls.Grid.SetColumn(list, 0); root.Children.Add(list);
                var image = new Image { Stretch = System.Windows.Media.Stretch.Uniform, Margin = new Thickness(8) };
                var previewText = new TextBlock { Text = "Chọn shape để xem ảnh.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
                var previewPanel = new DockPanel();
                DockPanel.SetDock(previewText, Dock.Bottom);
                previewPanel.Children.Add(previewText);
                previewPanel.Children.Add(image);
                var border = new Border { BorderBrush = System.Windows.Media.Brushes.Gray, BorderThickness = new Thickness(1), Child = previewPanel };
                System.Windows.Controls.Grid.SetRow(border, 1); System.Windows.Controls.Grid.SetColumn(border, 1); root.Children.Add(border);
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
                System.Windows.Controls.Grid.SetRow(buttons, 2); System.Windows.Controls.Grid.SetColumnSpan(buttons, 2); root.Children.Add(buttons);
                var select = new Button { Content = "Chọn Rebar dùng shape", MinWidth = 180, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
                var close = new Button { Content = "Đóng", MinWidth = 80, Padding = new Thickness(10, 5, 10, 5), IsCancel = true };
                buttons.Children.Add(select); buttons.Children.Add(close);
                list.SelectionChanged += (s, e) =>
                {
                    ShapeRow row = list.SelectedItem as ShapeRow;
                    image.Source = null;
                    if (row == null) { info.Text = "Chọn shape để xem ảnh minh họa."; return; }
                    info.Text = row.Name + "  |  ID " + row.Shape.Id.Value + "  |  " + row.Usage;
                    string path = RebarShapeImageManagerCommand.FindImage(row.Name);
                    if (path == null) previewText.Text = "Chưa có ảnh minh họa. Dùng lệnh Quản lý ảnh hình thép để gán ảnh.";
                    else
                    {
                        previewText.Text = Path.GetFileName(path);
                        try
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(path); bitmap.EndInit();
                            image.Source = bitmap;
                        }
                        catch (Exception ex) { previewText.Text = "Không đọc được ảnh: " + ex.Message; }
                    }
                };
                select.Click += (s, e) => window.DialogResult = true;
                list.SelectedIndex = 0;
                window.Content = root;
                if (window.ShowDialog() != true) return Result.Cancelled;
                ShapeRow selected = list.SelectedItem as ShapeRow;
                if (selected == null) return Result.Cancelled;
                List<ElementId> bars = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
                    .Where(bar => { try { return bar.GetShapeId() == selected.Shape.Id; } catch { return false; } })
                    .Select(bar => bar.Id).ToList();
                ui.Selection.SetElementIds(bars);
                TaskDialog.Show("KStruct - Thư viện hình thép", "Đã chọn " + bars.Count + " Rebar dùng shape " + selected.Name + ".");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Thư viện hình thép", ex.Message);
                return Result.Failed;
            }
        }
    }
}
