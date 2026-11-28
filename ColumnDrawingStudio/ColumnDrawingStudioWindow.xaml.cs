using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Shapes;
using Autodesk.Revit.DB.Structure;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.DB;

namespace ColumnRebarPro.V3
{
    // Separate UI entry point; delegates actual drawing production to the existing V3/V4 engine.
    internal partial class ColumnDrawingStudioWindow : Window
    {
        private readonly Document _document;
        private readonly List<FamilyInstance> _columns;
        private readonly V3DeploymentControl _settings;
        internal DeploymentSettingsFile Settings { get; private set; }

        internal ColumnDrawingStudioWindow(Document document, ICollection<ElementId> selection)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _columns = selection.Select(document.GetElement).OfType<FamilyInstance>().Where(c => c.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralColumns).ToList();
            InitializeComponent();
            _settings = new V3DeploymentControl();
            _settings.Initialize(document);
            _settings.PrepareForExistingRebar();
            SettingsHost.Content = _settings;
            ColumnPicker.ItemsSource = _columns.Select(c => (c.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "Cột") + " | ID " + c.Id.Value).ToList();
            ColumnPicker.SelectedIndex = _columns.Count > 0 ? 0 : -1;
            ColumnPicker.SelectionChanged += (sender, args) => RenderModel();
            CutPicker.SelectionChanged += (sender, args) => RenderModel();

            _settings.AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,
                new SelectionChangedEventHandler((sender, args) => RefreshSummary()), true);
            _settings.AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent,
                new RoutedEventHandler((sender, args) => RefreshSummary()), true);
            _settings.AddHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent,
                new RoutedEventHandler((sender, args) => RefreshSummary()), true);
            _settings.AddHandler(TextBox.TextChangedEvent,
                new TextChangedEventHandler((sender, args) => RefreshSummary()), true);
            Loaded += (sender, args) => { RefreshSummary(); RenderModel(); };
        }

        private void RefreshSummary()
        {
            if (_settings == null) return;
            LongSummary.Text = "Dim: " + _settings.SelectedChoiceLabel("LongDimension") +
                "  ·  Đai: " + _settings.SelectedChoiceLabel("LongStirrupTag") +
                "  ·  Hiện tag: " + (_settings.IsOptionChecked("LongShowTag") ? "Có" : "Không");
            CrossSummary.Text = "Dim: " + _settings.SelectedChoiceLabel("CrossDimension") +
                "  ·  Đai: " + _settings.SelectedChoiceLabel("CrossStirrupTag") +
                "  ·  Hiện tag: " + (_settings.IsOptionChecked("CrossShowTag") ? "Có" : "Không");
            var snapshot = _settings.Capture();
            Func<string, string> entry = name => snapshot.Entries.FirstOrDefault(x => x.Name == name)?.Value ?? "";
            SheetSummary.Text = "Sheet: " + entry("SheetNumber") + "  ·  " + entry("SheetName") +
                "  |  MCD 1:" + entry("LongScale") + " · MCN 1:" + entry("CrossScale");
        }

        private void RenderModel()
        {
            LongCanvas.Children.Clear();
            CrossCanvas.Children.Clear();
            if (_columns.Count == 0)
            {
                ModelSummary.Text = "Chưa chọn cột. Đóng cửa sổ, chọn cột rồi mở lại để xem preview.";
                return;
            }
            try
            {
                var column = _columns[Math.Max(0, ColumnPicker.SelectedIndex)];
                var g = V3GeometryReader.Read(column);
                var bars = new FilteredElementCollector(_document).OfClass(typeof(Rebar)).Cast<Rebar>()
                    .Where(b => b.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM)?.AsString() == V3RebarBuilder.Partition(column.Id)).ToList();
                double height = g.Top - g.Bottom;
                if (height <= 0 || g.Width <= 0 || g.Depth <= 0) throw new InvalidOperationException("Hình học cột không hợp lệ.");
                double ls = Math.Min(230 / g.Width, 340 / height);
                double cs = Math.Min(185 / g.Width, 240 / g.Depth);
                XYZ y = new XYZ(-g.X.Y, g.X.X, 0);
                DrawOutline(LongCanvas, 155 - g.Width * ls / 2, 30, g.Width * ls, height * ls);
                DrawOutline(CrossCanvas, 122.5 - g.Width * cs / 2, 160 - g.Depth * cs / 2, g.Width * cs, g.Depth * cs);
                int skipped = 0, rendered = 0;
                double cut = g.Bottom + height * (CutPicker.SelectedIndex == 0 ? 0.12 : CutPicker.SelectedIndex == 2 ? 0.88 : 0.5);
                foreach (var bar in bars)
                {
                    try
                    {
                        int n = bar.NumberOfBarPositions;
                        for (int i = 0; i < n; i++)
                        {
                            if (!bar.DoesBarExistAtPosition(i)) continue;
                            var curves = bar.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeAllMultiplanarCurves, i);
                            foreach (var curve in curves)
                            {
                                var points = curve.Tessellate();
                                for (int j = 1; j < points.Count; j++)
                                {
                                    XYZ a = points[j-1], b = points[j];
                                    double ax = (a-g.Center).DotProduct(g.X), bx = (b-g.Center).DotProduct(g.X);
                                    DrawLine(LongCanvas, 155+ax*ls, 30+(g.Top-a.Z)*ls, 155+bx*ls, 30+(g.Top-b.Z)*ls);
                                    if (Math.Abs(a.Z-b.Z) < 1e-7 && Math.Abs(a.Z-cut) < 0.5/304.8)
                                        DrawLine(CrossCanvas,122.5+ax*cs,160-(a-g.Center).DotProduct(y)*cs,122.5+bx*cs,160-(b-g.Center).DotProduct(y)*cs);
                                    if ((a.Z <= cut && b.Z > cut) || (b.Z <= cut && a.Z > cut))
                                    {
                                        XYZ q = a + (b-a)*((cut-a.Z)/(b.Z-a.Z));
                                        var dot = new System.Windows.Shapes.Ellipse { Width=5, Height=5, Fill=Brushes.IndianRed };
                                        Canvas.SetLeft(dot, 120+(q-g.Center).DotProduct(g.X)*cs);
                                        Canvas.SetTop(dot, 157.5-(q-g.Center).DotProduct(y)*cs);
                                        CrossCanvas.Children.Add(dot);
                                    }
                                }
                            }
                            rendered++;
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException) { skipped++; }
                }
                ModelSummary.Text = string.Format("Cột {0} | {1} cột chọn | {2:0} × {3:0} mm | H={4:0} mm | {5} vị trí thép đọc được | {6} bộ lỗi. MCN tại cao độ đã chọn; đường tâm và giao điểm.", column.Id.Value, _columns.Count, g.Width*304.8, g.Depth*304.8, height*304.8, rendered, skipped);
            }
            catch (Exception ex) { ModelSummary.Text = "Không thể xem trước: " + ex.Message; }
        }

        private static void DrawOutline(Canvas canvas, double x, double y, double w, double h)
        {
            var rect = new System.Windows.Shapes.Rectangle { Width=w, Height=h, Fill=Brushes.Gainsboro, Stroke=Brushes.SlateGray };
            Canvas.SetLeft(rect,x); Canvas.SetTop(rect,y); canvas.Children.Add(rect);
        }

        private static void DrawLine(Canvas canvas, double x1, double y1, double x2, double y2)
        {
            canvas.Children.Add(new System.Windows.Shapes.Line { X1=x1,Y1=y1,X2=x2,Y2=y2,Stroke=Brushes.IndianRed,StrokeThickness=1 });
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog { Filter="JSON (*.json)|*.json", FileName="KStruct-ColumnDrawing-Diagnostic.json" };
                if (dialog.ShowDialog(this) != true) return;
                var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(StudioDiagnostic));
                using (var stream = File.Create(dialog.FileName))
                    serializer.WriteObject(stream, new StudioDiagnostic { Version="2.0", ColumnIds=_columns.Select(c=>c.Id.Value).ToArray(), PreviewStatus=ModelSummary.Text });
                StatusText.Text = "Đã xuất chẩn đoán.";
            }
            catch (Exception ex) { StatusText.Text = ex.Message; }
        }

        [System.Runtime.Serialization.DataContract]
        public sealed class StudioDiagnostic
        {
            [System.Runtime.Serialization.DataMember] public string Version { get; set; }
            [System.Runtime.Serialization.DataMember] public long[] ColumnIds { get; set; }
            [System.Runtime.Serialization.DataMember] public string PreviewStatus { get; set; }
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            SettingsHost.Content = null;
            var dialog = new Window
            {
                Title = "KStruct | Cấu hình triển khai bản vẽ",
                Owner = this, Width = 1050, Height = 720,
                MaxWidth = SystemParameters.WorkArea.Width,
                MaxHeight = SystemParameters.WorkArea.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var panel = new DockPanel();
            var close = new Button { Content = "Quay lại Preview", Margin = new Thickness(12), Padding = new Thickness(12,6,12,6), IsCancel = true };
            close.Click += (s, args) => dialog.Close();
            DockPanel.SetDock(close, Dock.Bottom);
            panel.Children.Add(close);
            panel.Children.Add(_settings);
            dialog.Content = panel;
            try { dialog.ShowDialog(); }
            finally { panel.Children.Remove(_settings); SettingsHost.Content = _settings; RefreshSummary(); }
        }

        private void Run_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Settings = _settings.CaptureValidated();
                // Existing deployment settings persistence is intentionally shared with V3/V4.
                V3DeploymentControl.PersistSettings(_document, Settings);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                StatusText.Text = ex.Message;
                MessageBox.Show(this, ex.Message, "KStruct - Column Drawing Studio",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}