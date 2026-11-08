using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class PreviewBeamRebarSplicesCommand : IExternalCommand
    {
        private sealed class BeamOrRebarFilter : ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                if (element is Rebar) return true;
                return element?.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;
            }
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class PreviewOptions
        {
            public double StockLengthMm, LapLengthMm;
            public bool Stagger;
        }

        private sealed class SpliceRow
        {
            public string Rebar { get; set; }
            public string Host { get; set; }
            public string BarType { get; set; }
            public string Position { get; set; }
            public string Geometry { get; set; }
            public string Length { get; set; }
            public string PieceCount { get; set; }
            public string SpliceStations { get; set; }
            internal string SortKey { get; set; }
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Element> selected = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .Where(element => element is Rebar || element?.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFraming).ToList();
                if (selected.Count == 0)
                    selected = ui.Selection.PickObjects(ObjectType.Element, new BeamOrRebarFilter(),
                        "Chọn Rebar hoặc dầm để xem trước nối chồng").Select(reference => doc.GetElement(reference)).ToList();
                if (selected.Count == 0) return Result.Cancelled;

                var bars = selected.OfType<Rebar>().ToList();
                HashSet<ElementId> hostIds = selected.Where(element => !(element is Rebar)).Select(element => element.Id).ToHashSet();
                if (hostIds.Count > 0)
                    bars.AddRange(new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
                        .Where(bar => hostIds.Contains(bar.GetHostId())));
                bars = bars.GroupBy(bar => bar.Id).Select(group => group.First()).ToList();
                if (bars.Count == 0)
                {
                    TaskDialog.Show("KStruct - Preview nối thép", "Không tìm thấy Rebar trên dầm hoặc trong lựa chọn.");
                    return Result.Cancelled;
                }

                PreviewOptions options = AskOptions(data.Application.MainWindowHandle);
                if (options == null) return Result.Cancelled;
                double stock = UnitUtils.ConvertToInternalUnits(options.StockLengthMm, UnitTypeId.Millimeters);
                double lap = UnitUtils.ConvertToInternalUnits(options.LapLengthMm, UnitTypeId.Millimeters);
                if (lap >= stock) throw new InvalidOperationException("Chiều dài nối phải nhỏ hơn chiều dài cây thép.");

                List<SpliceRow> rows = BuildRows(doc, bars, stock, lap, options.Stagger, out int skipped);
                if (rows.Count == 0)
                {
                    TaskDialog.Show("KStruct - Preview nối thép", "Không đọc được centerline hợp lệ cho các vị trí Rebar đã chọn.");
                    return Result.Cancelled;
                }
                ShowPreview(data.Application.MainWindowHandle, rows, options, skipped);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Preview nối thép", ex.Message);
                return Result.Failed;
            }
        }

        private static List<SpliceRow> BuildRows(Document doc, IList<Rebar> bars, double stock, double lap, bool stagger, out int skipped)
        {
            skipped = 0;
            var result = new List<SpliceRow>();
            var groupIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (Rebar rebar in bars.OrderBy(bar => bar.GetHostId().Value).ThenBy(bar => bar.Id.Value))
            {
                int positions = Math.Max(1, rebar.NumberOfBarPositions);
                if (result.Count + positions > 5000) throw new InvalidOperationException("Preview vượt quá 5000 vị trí thanh.");
                Element host = doc.GetElement(rebar.GetHostId());
                string hostMark = ReadString(host, BuiltInParameter.ALL_MODEL_MARK);
                string hostLabel = host == null ? "(không có host)" :
                    (string.IsNullOrWhiteSpace(hostMark) ? host.Id.Value.ToString() : hostMark + " [" + host.Id.Value + "]");
                string partition = ReadString(rebar, BuiltInParameter.NUMBER_PARTITION_PARAM);
                string key = rebar.GetHostId().Value + "|" + rebar.GetTypeId().Value + "|" + partition;
                int groupPosition = groupIndex.ContainsKey(key) ? groupIndex[key] : 0;
                RebarBarType barType = doc.GetElement(rebar.GetTypeId()) as RebarBarType;

                for (int position = 0; position < positions; position++)
                {
                    IList<Curve> curves;
                    try { curves = rebar.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeAllMultiplanarCurves, position); }
                    catch { skipped++; continue; }
                    if (curves == null || curves.Count == 0 || curves.Any(curve => curve == null || !curve.IsBound)) { skipped++; continue; }
                    double length = curves.Sum(curve => curve.Length);
                    if (length <= 0) { skipped++; continue; }
                    bool straight = curves.Count == 1 && curves[0] is Line;
                    string geometry = straight ? "Line thẳng" :
                        (curves.Any(curve => curve is Arc) ? "Có cung/móc; rà hình học" : curves.Count + " đoạn; rà hình học");
                    bool offset = stagger && (groupPosition % 2 == 1);
                    List<double> joints = CalculateJointStations(length, stock, lap, offset);
                    string diameter = barType == null ? string.Empty :
                        UnitUtils.ConvertFromInternalUnits(barType.BarNominalDiameter, UnitTypeId.Millimeters).ToString("0.##", CultureInfo.CurrentCulture);
                    result.Add(new SpliceRow
                    {
                        Rebar = rebar.Id.Value + (positions > 1 ? " (set)" : ""),
                        Host = hostLabel,
                        BarType = (barType == null ? rebar.Name : barType.Name) + (diameter.Length > 0 ? " Ø" + diameter : ""),
                        Position = (position + 1).ToString(CultureInfo.CurrentCulture) + "/" + positions,
                        Geometry = geometry,
                        Length = ToMm(length).ToString("0", CultureInfo.CurrentCulture),
                        PieceCount = (joints.Count + 1).ToString(CultureInfo.CurrentCulture),
                        SpliceStations = joints.Count == 0 ? "—" : string.Join("; ", joints.Select(station =>
                            ToMm(station).ToString("0", CultureInfo.CurrentCulture) + "–" + ToMm(station + lap).ToString("0", CultureInfo.CurrentCulture))),
                        SortKey = key + "|" + groupPosition.ToString("D8", CultureInfo.InvariantCulture)
                    });
                    groupPosition++;
                }
                groupIndex[key] = groupPosition;
            }
            return result.OrderBy(row => row.SortKey, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<double> CalculateJointStations(double length, double stock, double lap, bool stagger)
        {
            var joints = new List<double>();
            if (length <= stock) return joints;
            double advance = stock - lap;
            double station = stagger ? advance / 2.0 : advance;
            while (station + lap < length)
            {
                joints.Add(station);
                if (length - station <= stock) break;
                station += advance;
                if (joints.Count > 200) throw new InvalidOperationException("Một thanh cần số mối nối vượt giới hạn preview.");
            }
            if (joints.Count == 0) joints.Add(stagger ? Math.Min(advance / 2.0, length - lap - 1e-6) : advance);
            return joints;
        }

        private static PreviewOptions AskOptions(IntPtr owner)
        {
            var window = new Window
            {
                Title = "Thông số preview nối thép - KStruct", Width = 370, Height = 275,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            var stock = AddInput(root, "Chiều dài cây thép tối đa (mm):", "12000");
            var lap = AddInput(root, "Chiều dài nối chồng (mm):", "800");
            var stagger = new CheckBox { Content = "So le vị trí nối giữa các thanh cùng nhóm", IsChecked = true, Margin = new Thickness(0, 8, 0, 4) };
            root.Children.Add(stagger);
            var note = new TextBlock
            {
                Text = "Preview chỉ tính toán; không cắt hoặc sửa Rebar trong mô hình. Chiều dài lấy theo tổng centerline; với thanh uốn/móc, khoảng nối tính dọc centerline và cần rà để nằm trên đoạn thẳng phù hợp.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8)
            };
            root.Children.Add(note);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Xem preview", Width = 96, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            buttons.Children.Add(ok); buttons.Children.Add(cancel); root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            ok.Click += (sender, args) =>
            {
                double s, l;
                if (!double.TryParse(stock.Text, out s) || !double.TryParse(lap.Text, out l) || s <= 0 || l <= 0 || l >= s)
                {
                    TaskDialog.Show("KStruct - Preview nối thép", "Chiều dài cây và chiều dài nối phải dương; chiều dài nối phải nhỏ hơn chiều dài cây.");
                    return;
                }
                window.DialogResult = true;
            };
            if (window.ShowDialog() != true) return null;
            return new PreviewOptions
            {
                StockLengthMm = double.Parse(stock.Text), LapLengthMm = double.Parse(lap.Text), Stagger = stagger.IsChecked == true
            };
        }

        private static void ShowPreview(IntPtr owner, IList<SpliceRow> rows, PreviewOptions options, int skipped)
        {
            var window = new Window
            {
                Title = "Preview nối chồng Rebar - KStruct", Width = 980, Height = 560,
                MinWidth = 800, MinHeight = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            var root = new DockPanel { Margin = new Thickness(12) };
            var footer = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(footer, Dock.Bottom);
            footer.Children.Add(new TextBlock
            {
                Text = rows.Count + " vị trí | thép cây " + options.StockLengthMm.ToString("0", CultureInfo.CurrentCulture) +
                    " mm | nối " + options.LapLengthMm.ToString("0", CultureInfo.CurrentCulture) + " mm" +
                    (skipped > 0 ? " | bỏ qua " + skipped + " vị trí không đọc được centerline" : "") +
                    " | với thanh có móc/uốn, khoảng nối tính theo tổng centerline; cần rà để nối nằm trên đoạn thẳng phù hợp.",
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0)
            });
            var export = new Button { Content = "Xuất CSV", Width = 84, Margin = new Thickness(0, 0, 8, 0) };
            var close = new Button { Content = "Đóng", Width = 72, IsCancel = true };
            footer.Children.Add(export); footer.Children.Add(close); root.Children.Add(footer);
            var view = new ListView { ItemsSource = rows };
            var grid = new GridView();
            AddColumn(grid, "Rebar", "Rebar", 115); AddColumn(grid, "Host", "Host", 150); AddColumn(grid, "Loại thép", "BarType", 190);
            AddColumn(grid, "Position", "Position", 70); AddColumn(grid, "Hình học", "Geometry", 155);
            AddColumn(grid, "Chiều dài (mm)", "Length", 105);
            AddColumn(grid, "Số đoạn", "PieceCount", 65); AddColumn(grid, "Khoảng nối (mm, từ–đến)", "SpliceStations", 230);
            view.View = grid; root.Children.Add(view); window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            export.Click += (sender, args) =>
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Xuất preview nối thép", Filter = "CSV UTF-8 (*.csv)|*.csv", FileName = "KStruct_RebarSplicePreview.csv",
                    AddExtension = true, DefaultExt = ".csv", OverwritePrompt = true
                };
                if (dialog.ShowDialog(window) != true) return;
                try
                {
                    var text = new StringBuilder("Rebar,Host,BarType,Position,Geometry,LengthMm,PieceCount,SpliceStationsMm\r\n");
                    foreach (SpliceRow row in rows)
                        text.Append(Csv(row.Rebar)).Append(',').Append(Csv(row.Host)).Append(',').Append(Csv(row.BarType)).Append(',')
                            .Append(Csv(row.Position)).Append(',').Append(Csv(row.Geometry)).Append(',').Append(Csv(row.Length)).Append(',').Append(Csv(row.PieceCount)).Append(',')
                            .Append(Csv(row.SpliceStations)).Append("\r\n");
                    System.IO.File.WriteAllText(dialog.FileName, text.ToString(), new UTF8Encoding(true));
                    TaskDialog.Show("KStruct - Preview nối thép", "Đã xuất " + rows.Count + " dòng preview.");
                }
                catch (Exception ex) { TaskDialog.Show("KStruct - Preview nối thép", ex.Message); }
            };
            window.ShowDialog();
        }

        private static void AddColumn(GridView view, string title, string path, double width)
        {
            view.Columns.Add(new GridViewColumn { Header = title, Width = width, DisplayMemberBinding = new System.Windows.Data.Binding(path) });
        }

        private static string ReadString(Element element, BuiltInParameter parameter)
        {
            Parameter value = element?.get_Parameter(parameter);
            return value != null && value.HasValue ? value.AsString() ?? string.Empty : string.Empty;
        }

        private static double ToMm(double value) => UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.Millimeters);

        private static string Csv(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

        private static System.Windows.Controls.TextBox AddInput(System.Windows.Controls.Panel root, string label, string value)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 5, 0, 2) });
            var box = new System.Windows.Controls.TextBox { Text = value, MinWidth = 330, MinHeight = 23 };
            root.Children.Add(box);
            return box;
        }
    }
}
