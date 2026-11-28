using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace ColumnRebarPro.V3
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CreateColumnDrawingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
            IndependentColumnDrawingService.ExecuteV3(data, ref message, false, true);
    }

    internal sealed class ColumnDrawingSettingsWindow : Window
    {
        private readonly Document _document;
        private readonly V3DeploymentControl _settingsControl;
        private readonly Canvas _reviewCanvas;
        private readonly TextBlock _reviewStatus;

        internal DeploymentSettingsFile Settings { get; private set; }

        internal ColumnDrawingSettingsWindow(Document document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            Title = "KStruct - Thông số triển khai hồ sơ cột";
            Width = Math.Min(1400, SystemParameters.WorkArea.Width);
            Height = Math.Min(820, SystemParameters.WorkArea.Height);
            MinWidth = Math.Min(1040, Width);
            MinHeight = Math.Min(620, Height);
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var layout = new System.Windows.Controls.Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _settingsControl = new V3DeploymentControl();
            _settingsControl.Initialize(document);
            _settingsControl.PrepareForExistingRebar();

            var content = new System.Windows.Controls.Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28, GridUnitType.Star) });
            content.Children.Add(_settingsControl);

            var review = new Border
            {
                Background = System.Windows.Media.Brushes.WhiteSmoke,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(210, 221, 233)),
                BorderThickness = new Thickness(1, 0, 0, 0),
                Padding = new Thickness(12)
            };
            var reviewLayout = new DockPanel();
            var reviewHeader = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 44, 70)),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(11),
                Margin = new Thickness(0, 0, 0, 9),
                Child = new TextBlock
                {
                    Text = "REVIEW · TAG & DIM",
                    Foreground = System.Windows.Media.Brushes.White,
                    FontWeight = FontWeights.Bold
                }
            };
            DockPanel.SetDock(reviewHeader, Dock.Top);
            reviewLayout.Children.Add(reviewHeader);

            var reviewFooter = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(234, 243, 251)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(184, 209, 235)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(9, 7, 9, 7),
                Margin = new Thickness(0, 9, 0, 0)
            };
            _reviewStatus = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(41, 69, 101))
            };
            reviewFooter.Child = _reviewStatus;
            DockPanel.SetDock(reviewFooter, Dock.Bottom);
            reviewLayout.Children.Add(reviewFooter);

            _reviewCanvas = new Canvas
            {
                Background = System.Windows.Media.Brushes.White,
                ClipToBounds = true,
                MinHeight = 440
            };
            _reviewCanvas.SizeChanged += (sender, args) => DrawTagDimensionReview();
            reviewLayout.Children.Add(_reviewCanvas);
            review.Child = reviewLayout;
            System.Windows.Controls.Grid.SetColumn(review, 1);
            content.Children.Add(review);
            layout.Children.Add(content);

            _settingsControl.AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,
                new SelectionChangedEventHandler((sender, args) => DrawTagDimensionReview()), true);
            _settingsControl.AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent,
                new RoutedEventHandler((sender, args) => DrawTagDimensionReview()), true);
            _settingsControl.AddHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent,
                new RoutedEventHandler((sender, args) => DrawTagDimensionReview()), true);
            Loaded += (sender, args) => DrawTagDimensionReview();

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(12)
            };
            var cancel = new Button
            {
                Content = "Hủy",
                MinWidth = 90,
                Padding = new Thickness(14, 7, 14, 7),
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true
            };
            var accept = new Button
            {
                Content = "Tiếp tục tạo bản vẽ",
                MinWidth = 170,
                Padding = new Thickness(14, 7, 14, 7),
                IsDefault = true
            };
            accept.Click += Accept_Click;
            actions.Children.Add(cancel);
            actions.Children.Add(accept);
            System.Windows.Controls.Grid.SetRow(actions, 1);
            layout.Children.Add(actions);
            Content = layout;
        }

        private void DrawTagDimensionReview()
        {
            if (_reviewCanvas == null || _reviewStatus == null) return;
            _reviewCanvas.Children.Clear();
            double width = _reviewCanvas.ActualWidth;
            double height = _reviewCanvas.ActualHeight;
            if (width < 220 || height < 360) return;

            var navy = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(22, 44, 70));
            var blue = System.Windows.Media.Brushes.SteelBlue;
            var steel = System.Windows.Media.Brushes.Firebrick;
            var concrete = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(231, 237, 244));
            double split = Math.Min(height * .44, 270);

            AddReviewText("MẶT CẮT NGANG", 8, 7, navy, 11, true);
            double boxW = Math.Min(112, width * .33);
            double boxH = Math.Min(94, split - 88);
            double boxX = Math.Max(54, width * .25 - boxW / 2);
            double boxY = 40;
            AddReviewRectangle(boxX, boxY, boxW, boxH, concrete, navy, 1.1);
            AddReviewRectangle(boxX + 9, boxY + 9, boxW - 18, boxH - 18,
                System.Windows.Media.Brushes.Transparent, steel, 1.4);
            foreach (var point in new[]
            {
                new[] { boxX + 12, boxY + 12 }, new[] { boxX + boxW - 12, boxY + 12 },
                new[] { boxX + 12, boxY + boxH - 12 }, new[] { boxX + boxW - 12, boxY + boxH - 12 }
            }) AddReviewDot(point[0], point[1], steel);

            bool crossShowTag = _settingsControl.IsOptionChecked("CrossShowTag");
            bool crossShowValue = _settingsControl.IsOptionChecked("CrossShowValue");
            if (crossShowValue)
            {
                // Builder thật đặt kích thước bề rộng ở phía trên và chiều sâu ở bên trái.
                double topDimY = boxY - 17;
                AddReviewLine(boxX, boxY, boxX, topDimY - 3, blue, .8);
                AddReviewLine(boxX + boxW, boxY, boxX + boxW, topDimY - 3, blue, .8);
                AddReviewLine(boxX, topDimY, boxX + boxW, topDimY, blue, 1.1);
                AddReviewText("400", boxX + boxW / 2 - 13, topDimY - 16, blue, 9, false);
                double leftDimX = boxX - 18;
                AddReviewLine(leftDimX, boxY, leftDimX, boxY + boxH, blue, 1.1);
                AddReviewLine(leftDimX, boxY, boxX, boxY, blue, .8);
                AddReviewLine(leftDimX, boxY + boxH, boxX, boxY + boxH, blue, .8);
                AddReviewText("500", Math.Max(2, leftDimX - 23), boxY + boxH / 2 - 6, blue, 9, false);
            }

            if (crossShowTag)
            {
                V3TagHorizontalSide stirrupSide = _settingsControl.SelectedTagSide("CrossStirrupTag",
                    V3TagHorizontalSide.Right);
                bool stirrupRight = stirrupSide == V3TagHorizontalSide.Right;
                double stirrupTargetX = stirrupRight ? boxX + boxW - 8 : boxX + 8;
                double stirrupTagX = stirrupRight ? Math.Min(width - 105, boxX + boxW + 38) : 5;
                double stirrupLineX = stirrupRight ? stirrupTagX - 7 : stirrupTagX + 58;
                AddReviewLine(stirrupTargetX, boxY + 8, stirrupLineX, boxY + 8, steel, 1);
                AddReviewText("Ø8a100", stirrupTagX, boxY - 7, steel, 9, true);

                // MRA giữ đường kích thước theo dãy thanh; đầu tag dịch trái/phải theo token T/P.
                V3TagHorizontalSide mainSide = _settingsControl.SelectedTagSide("CrossMainTag",
                    V3TagHorizontalSide.Left);
                double mraY = boxY + boxH + 18;
                AddReviewLine(boxX + 8, mraY, boxX + boxW - 8, mraY, steel, 1);
                double mainTagX = mainSide == V3TagHorizontalSide.Right
                    ? Math.Min(width - 82, boxX + boxW + 20) : Math.Max(2, boxX - 58);
                AddReviewText("4D20 · MRA", mainTagX, mraY - 8, steel, 8, true);
            }

            double longTop = split + 12;
            AddReviewLine(8, longTop, width - 8, longTop, System.Windows.Media.Brushes.LightGray, .8);
            AddReviewText("MẶT CẮT ĐỨNG (DỌC)", 8, longTop + 10, navy, 11, true);
            double columnX = Math.Max(62, width * .28);
            double columnY = longTop + 43;
            double columnW = Math.Min(76, width * .23);
            double columnH = Math.Max(120, height - columnY - 38);
            AddReviewRectangle(columnX, columnY, columnW, columnH, concrete, navy, 1.1);
            AddReviewLine(columnX + 13, columnY + 5, columnX + 13, columnY + columnH - 5, steel, 2);
            AddReviewLine(columnX + columnW - 13, columnY + 5, columnX + columnW - 13, columnY + columnH - 5, steel, 2);
            for (double y = columnY + 15; y < columnY + columnH - 5; y += 23)
                AddReviewLine(columnX + 8, y, columnX + columnW - 8, y, steel, .9);

            bool longShowTag = _settingsControl.IsOptionChecked("LongShowTag");
            bool longShowValue = _settingsControl.IsOptionChecked("LongShowValue");
            if (longShowTag)
            {
                V3TagHorizontalSide stirrupSide = _settingsControl.SelectedTagSide("LongStirrupTag",
                    V3TagHorizontalSide.Left);
                bool stirrupRight = stirrupSide == V3TagHorizontalSide.Right;
                var stirrupLabel = new TextBlock
                {
                    Text = "Ø8a200", Foreground = steel, FontSize = 9, FontWeight = FontWeights.SemiBold,
                    RenderTransform = new System.Windows.Media.RotateTransform(-90)
                };
                Canvas.SetLeft(stirrupLabel, stirrupRight
                    ? Math.Min(width - 22, columnX + columnW + 19) : Math.Max(3, columnX - 39));
                Canvas.SetTop(stirrupLabel, columnY + columnH / 2 + 25);
                _reviewCanvas.Children.Add(stirrupLabel);
            }
            if (longShowValue)
            {
                // Thép dọc được triển khai bên phải; dim ở trái hình thanh và tag ở phải, không leader.
                double shapeX = Math.Min(width - 58, columnX + columnW + 42);
                double shapeTop = columnY + 15;
                double shapeBottom = columnY + columnH - 15;
                AddReviewLine(shapeX, shapeTop, shapeX, shapeBottom, steel, 1.2);
                AddReviewLine(shapeX, shapeTop, shapeX + 14, shapeTop, steel, 1.2);
                AddReviewLine(shapeX - 10, shapeTop, shapeX - 10, shapeBottom, blue, .9);
                AddReviewText("L", shapeX - 23, (shapeTop + shapeBottom) / 2 - 6, blue, 9, false);
                V3TagHorizontalSide mainSide = _settingsControl.SelectedTagSide("LongMainTag",
                    V3TagHorizontalSide.Right);
                double mainTagX = mainSide == V3TagHorizontalSide.Right ? shapeX + 18 : shapeX - 54;
                AddReviewText("4D20", mainTagX, (shapeTop + shapeBottom) / 2 - 7, steel, 9, true);
            }

            string crossDim = crossShowValue ? ShortChoice("CrossDimension") : "Tắt";
            string crossTie = crossShowTag ? ShortChoice("CrossStirrupTag") : "Tắt";
            string crossMain = crossShowTag ? ShortChoice("CrossMainTag") : "Tắt";
            string longDim = longShowValue ? ShortChoice("LongDimension") : "Tắt";
            string longTie = longShowTag ? ShortChoice("LongStirrupTag") : "Tắt";
            string longMain = longShowValue ? ShortChoice("LongMainTag") : "Tắt";
            _reviewStatus.Text =
                "MCN · Dim: " + crossDim + "\nTag đai: " + crossTie + " · Tag dọc: " + crossMain +
                "\nMCD · Dim: " + longDim + "\nTag đai: " + longTie + " · Tag dọc: " + longMain +
                "\nQuy tắc hướng: token P trong tên loại đặt tag bên phải; token T đặt bên trái. " +
                "Tên không có P/T giữ hướng mặc định của từng loại ghi chú.";
        }

        private string ShortChoice(string name)
        {
            string label = _settingsControl.SelectedChoiceLabel(name);
            int separator = label.LastIndexOf(" : ", StringComparison.Ordinal);
            if (separator >= 0) label = label.Substring(separator + 3);
            int viewType = label.LastIndexOf(" [", StringComparison.Ordinal);
            if (viewType > 0) label = label.Substring(0, viewType);
            return label;
        }

        private void AddReviewRectangle(double x, double y, double width, double height,
            System.Windows.Media.Brush fill, System.Windows.Media.Brush stroke, double thickness)
        {
            var shape = new System.Windows.Shapes.Rectangle
            {
                Width = Math.Max(1, width), Height = Math.Max(1, height), Fill = fill,
                Stroke = stroke, StrokeThickness = thickness
            };
            Canvas.SetLeft(shape, x); Canvas.SetTop(shape, y); _reviewCanvas.Children.Add(shape);
        }

        private void AddReviewLine(double x1, double y1, double x2, double y2,
            System.Windows.Media.Brush stroke, double thickness)
        {
            _reviewCanvas.Children.Add(new System.Windows.Shapes.Line
            {
                X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = thickness
            });
        }

        private void AddReviewDot(double x, double y, System.Windows.Media.Brush fill)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = fill };
            Canvas.SetLeft(dot, x - 3.5); Canvas.SetTop(dot, y - 3.5); _reviewCanvas.Children.Add(dot);
        }

        private void AddReviewText(string text, double x, double y, System.Windows.Media.Brush color,
            double size, bool bold)
        {
            var label = new TextBlock
            {
                Text = text, Foreground = color, FontSize = size,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = Math.Max(70, _reviewCanvas.ActualWidth - x - 5)
            };
            Canvas.SetLeft(label, x); Canvas.SetTop(label, y); _reviewCanvas.Children.Add(label);
        }

        private void Accept_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Settings = _settingsControl.CaptureValidated();
                V3DeploymentControl.PersistSettings(_document, Settings);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "KStruct - Thông số triển khai",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    internal static class IndependentColumnDrawingService
    {
        private static readonly Guid SchemaId = new Guid("B411042C-5B2A-4C1B-AF4C-762628B7CC1E");
        private sealed class ColumnFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is FamilyInstance instance &&
                instance.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralColumns;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        // Use the same full KStruct V3 documentation pipeline as "Thép cột". This keeps the
        // ribbon's standalone create/update drawing buttons from falling back to the old
        // two-view placeholder implementation below.
        internal static Result ExecuteV3(ExternalCommandData data, ref string message, bool update, bool studio = false)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<FamilyInstance> picked = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .OfType<FamilyInstance>().Where(c => new ColumnFilter().AllowElement(c)).Distinct().ToList();
                if (studio && picked.Count == 0)
                {
                    TaskDialog.Show("KStruct | Column Drawing Studio",
                        "Hãy chọn ít nhất một cột kết cấu trong mô hình trước, sau đó mở lại lệnh Column Drawing Studio.");
                    return Result.Cancelled;
                }
                DeploymentSettingsFile settings;
                if (studio)
                {
                    var window = new ColumnDrawingStudioWindow(doc, picked.Select(c => c.Id).ToList());
                    new WindowInteropHelper(window).Owner = data.Application.MainWindowHandle;
                    if (window.ShowDialog() != true) return Result.Cancelled;
                    settings = window.Settings;
                }
                else
                {
                    var settingsWindow = new ColumnDrawingSettingsWindow(doc);
                    new WindowInteropHelper(settingsWindow).Owner = data.Application.MainWindowHandle;
                    if (settingsWindow.ShowDialog() != true) return Result.Cancelled;
                    settings = settingsWindow.Settings;
                }

                if (picked.Count == 0)
                    picked = ui.Selection.PickObjects(ObjectType.Element, new ColumnFilter(),
                            "Chọn cột KStruct cần " + (update ? "cập nhật" : "triển khai") + " bản vẽ")
                        .Select(r => doc.GetElement(r)).OfType<FamilyInstance>().Distinct().ToList();
                if (picked.Count == 0) return Result.Cancelled;

                var seen = new HashSet<ElementId>();
                var jobs = new List<List<V3SegmentWork>>();
                foreach (FamilyInstance selected in picked)
                {
                    if (seen.Contains(selected.Id)) continue;
                    var segments = ColumnStackFinder.Find(doc, selected);
                    var works = new List<V3SegmentWork>();
                    foreach (ColumnSegment segment in segments)
                    {
                        var column = doc.GetElement(segment.Id) as FamilyInstance;
                        if (column == null) continue;
                        seen.Add(column.Id);
                        V3RebarConfig config = V3RebarConfigStorage.Read(column);
                        if (config == null)
                            throw new InvalidOperationException("Cột " + column.Id.Value + " chưa có cấu hình KStruct V3 đã lưu. Hãy tạo thép bằng KStruct trước.");
                        bool hasKStructBars = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
                            .Any(r => string.Equals(r.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM)?.AsString(),
                                    V3RebarBuilder.Partition(column.Id), StringComparison.Ordinal));
                        if (!hasKStructBars)
                            throw new InvalidOperationException("Cột " + column.Id.Value + " chưa có thép KStruct V3 để triển khai.");
                        works.Add(new V3SegmentWork { Column = column, Segment = segment,
                            Geometry = V3GeometryReader.Read(column), Config = config });
                    }
                    if (works.Count > 0) jobs.Add(works.OrderBy(w => w.Geometry.Bottom).ToList());
                }

                if (jobs.Count == 0) return Result.Cancelled;
                var builders = jobs.Select(_ => new V3DrawingBuilder(settings, update, true)).ToList();
                for (int i = 0; i < jobs.Count; i++)
                {
                    // Document existing bars, including foundation-hosted bars identified
                    // by partition. Do not run the creation/anchorage preflight again.
                    builders[i].UseSheetIdentity(jobs[i], i, jobs.Count);
                    builders[i].Validate(doc, jobs[i]);
                }

                int completed = 0;
                var failures = new List<string>();
                using (var group = new TransactionGroup(doc, update ? "KStruct - Cập nhật triển khai cột" : "KStruct - Triển khai bản vẽ cột"))
                {
                    group.Start();
                    for (int i = 0; i < jobs.Count; i++)
                    {
                        using (var tx = new Transaction(doc, "KStruct - Bản vẽ tuyến cột " + (i + 1)))
                        {
                            tx.Start();
                            try
                            {
                                builders[i].Build(doc, jobs[i]);
                                if (tx.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận bản vẽ.");
                                completed++;
                            }
                            catch (Exception ex)
                            {
                                if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                                failures.Add("Tuyến " + (i + 1) + ": " + ex.Message);
                            }
                        }
                    }
                    if (completed > 0) group.Assimilate(); else group.RollBack();
                }

                if (completed == 0)
                    throw new InvalidOperationException("Không triển khai được tuyến cột nào.\n" + string.Join("\n", failures.Take(8)));
                string report = (update ? "Đã cập nhật " : "Đã tạo ") + completed + "/" + jobs.Count +
                    " bộ bản vẽ V3 dạng DRAFT (mặt cắt dọc, mặt cắt ngang, kích thước, tag và bảng thép theo thiết lập). " +
                    "DRAFT không thay thế phê duyệt thiết kế hoặc kiểm tra phát hành.";
                if (failures.Count > 0) report += "\nBỏ qua:\n" + string.Join("\n", failures.Take(8));
                TaskDialog.Show("KStruct - Triển khai cột", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Triển khai cột", ex.Message);
                return Result.Failed;
            }
        }

        private sealed class DrawingTypes { public FamilySymbol TitleBlock; public ViewFamilyType SectionType; }
        private sealed class Bounds
        {
            public double MinX = double.MaxValue, MinY = double.MaxValue, MinZ = double.MaxValue;
            public double MaxX = double.MinValue, MaxY = double.MinValue, MaxZ = double.MinValue;
        }

        internal static Result Execute(ExternalCommandData data, ref string message, bool update)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<FamilyInstance> columns = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .OfType<FamilyInstance>().Where(c => new ColumnFilter().AllowElement(c)).Distinct().ToList();
                if (columns.Count == 0)
                    columns = ui.Selection.PickObjects(ObjectType.Element, new ColumnFilter(),
                            "Chọn cột cần " + (update ? "cập nhật" : "tạo") + " bản vẽ")
                        .Select(r => doc.GetElement(r)).OfType<FamilyInstance>().Distinct().ToList();
                if (columns.Count == 0) return Result.Cancelled;
                DrawingTypes types = ChooseTypes(data.Application.MainWindowHandle, doc);
                if (types == null) return Result.Cancelled;
                int completed = 0;
                var errors = new List<string>();
                using (Transaction tx = new Transaction(doc, update ? "KStruct - Cập nhật bản vẽ cột" : "KStruct - Tạo bản vẽ cột"))
                {
                    tx.Start();
                    foreach (FamilyInstance column in columns)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                List<ViewSheet> old = FindSheets(doc, column.UniqueId);
                                if (update && old.Count == 0) throw new InvalidOperationException("Chưa có bản vẽ KStruct để cập nhật.");
                                if (!update && old.Count > 0) throw new InvalidOperationException("Đã có bản vẽ KStruct; dùng lệnh Cập nhật.");
                                List<Rebar> bars = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
                                    .Where(r => r.GetHostId() == column.Id).ToList();
                                if (bars.Count == 0) throw new InvalidOperationException("Cột chưa có Rebar host.");
                                DeleteOld(doc, old);
                                Build(doc, column, bars, types);
                                if (sub.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không chấp nhận bản vẽ.");
                                completed++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                errors.Add("Cột " + column.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (completed == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Bản vẽ cột", "Không tạo/cập nhật được bản vẽ nào.\n" + string.Join("\n", errors.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không chấp nhận bản vẽ cột.");
                }
                string report = (update ? "Đã cập nhật " : "Đã tạo ") + completed + "/" + columns.Count + " bản vẽ cột.";
                if (errors.Count > 0) report += "\nBỏ qua:\n" + string.Join("\n", errors.Take(8));
                TaskDialog.Show("KStruct - Bản vẽ cột", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex) { message = ex.ToString(); TaskDialog.Show("KStruct - Bản vẽ cột", ex.Message); return Result.Failed; }
        }

        private static void Build(Document doc, FamilyInstance column, IList<Rebar> bars, DrawingTypes types)
        {
            LocationPoint loc = column.Location as LocationPoint;
            if (loc == null) throw new InvalidOperationException("Chỉ hỗ trợ cột đứng dạng điểm.");
            Transform basis = column.GetTransform();
            if (Math.Abs(basis.BasisZ.Z) < .999) throw new InvalidOperationException("Cột nghiêng chưa được hỗ trợ.");
            XYZ x = new XYZ(basis.BasisX.X, basis.BasisX.Y, 0).Normalize();
            XYZ y = XYZ.BasisZ.CrossProduct(x).Normalize();
            Bounds b = ReadBounds(column, bars, loc.Point, x, y);
            double margin = UnitUtils.ConvertToInternalUnits(200, UnitTypeId.Millimeters);
            double depth = UnitUtils.ConvertToInternalUnits(40, UnitTypeId.Millimeters);
            Transform crossT = Transform.Identity;
            crossT.Origin = new XYZ(loc.Point.X, loc.Point.Y, (b.MinZ + b.MaxZ) / 2);
            crossT.BasisX = x; crossT.BasisY = y; crossT.BasisZ = XYZ.BasisZ;
            var crossBox = new BoundingBoxXYZ { Transform = crossT,
                Min = new XYZ(b.MinX - margin, b.MinY - margin, -depth / 2),
                Max = new XYZ(b.MaxX + margin, b.MaxY + margin, depth / 2) };
            ViewSection cross = ViewSection.CreateSection(doc, types.SectionType.Id, crossBox);
            XYZ normal = x.CrossProduct(XYZ.BasisZ).Normalize();
            Transform longT = Transform.Identity;
            longT.Origin = new XYZ(loc.Point.X, loc.Point.Y, b.MinZ);
            longT.BasisX = x; longT.BasisY = XYZ.BasisZ; longT.BasisZ = normal;
            var longBox = new BoundingBoxXYZ { Transform = longT,
                Min = new XYZ(b.MinX - margin, -margin, -b.MaxY - margin),
                Max = new XYZ(b.MaxX + margin, b.MaxZ - b.MinZ + margin, -b.MinY + margin) };
            ViewSection longitudinal = ViewSection.CreateSection(doc, types.SectionType.Id, longBox);
            string mark = column.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
            string label = string.IsNullOrWhiteSpace(mark) ? "C" + column.Id.Value : mark.Trim();
            cross.Name = UniqueViewName(doc, "KSTR COL MCN " + label);
            longitudinal.Name = UniqueViewName(doc, "KSTR COL MCD " + label);
            foreach (ViewSection view in new[] { cross, longitudinal })
            {
                view.DetailLevel = ViewDetailLevel.Fine; view.Scale = 20;
                view.CropBoxActive = true; view.CropBoxVisible = false;
            }
            doc.Regenerate();
            ViewSheet sheet = ViewSheet.Create(doc, types.TitleBlock.Id);
            sheet.SheetNumber = UniqueSheetNumber(doc, "KC-" + Sanitize(mark, column.Id.Value.ToString(CultureInfo.InvariantCulture)));
            sheet.Name = "Bản vẽ cột " + label;
            doc.Regenerate();
            BoundingBoxUV outline = sheet.Outline;
            double width = outline.Max.U - outline.Min.U, yCenter = (outline.Max.V + outline.Min.V) / 2;
            Viewport.Create(doc, sheet.Id, cross.Id, new XYZ(outline.Min.U + width * .28, yCenter, 0));
            Viewport.Create(doc, sheet.Id, longitudinal.Id, new XYZ(outline.Min.U + width * .72, yCenter, 0));
            Entity entity = new Entity(GetSchema());
            entity.Set(GetSchema().GetField("ColumnUniqueId"), column.UniqueId);
            sheet.SetEntity(entity);
        }

        private static Bounds ReadBounds(FamilyInstance column, IList<Rebar> bars, XYZ origin, XYZ x, XYZ y)
        {
            var result = new Bounds();
            foreach (Element element in new[] { (Element)column }.Concat(bars))
            {
                BoundingBoxXYZ box = element.get_BoundingBox(null);
                if (box == null) continue;
                for (int a = 0; a < 2; a++) for (int c = 0; c < 2; c++) for (int d = 0; d < 2; d++)
                {
                    XYZ p = box.Transform.OfPoint(new XYZ(a == 0 ? box.Min.X : box.Max.X,
                        c == 0 ? box.Min.Y : box.Max.Y, d == 0 ? box.Min.Z : box.Max.Z));
                    XYZ rel = p - origin;
                    double px = rel.DotProduct(x), py = rel.DotProduct(y);
                    result.MinX = Math.Min(result.MinX, px); result.MaxX = Math.Max(result.MaxX, px);
                    result.MinY = Math.Min(result.MinY, py); result.MaxY = Math.Max(result.MaxY, py);
                    result.MinZ = Math.Min(result.MinZ, p.Z); result.MaxZ = Math.Max(result.MaxZ, p.Z);
                }
            }
            if (result.MinX == double.MaxValue || result.MaxZ - result.MinZ <= 1e-6)
                throw new InvalidOperationException("Không đọc được phạm vi cột và Rebar.");
            return result;
        }

        private static List<ViewSheet> FindSheets(Document doc, string uniqueId) =>
            new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Where(sheet =>
            {
                try
                {
                    Entity e = sheet.GetEntity(GetSchema());
                    return e.IsValid() && e.Get<string>(GetSchema().GetField("ColumnUniqueId")) == uniqueId;
                }
                catch { return false; }
            }).ToList();

        private static void DeleteOld(Document doc, IList<ViewSheet> sheets)
        {
            foreach (ViewSheet sheet in sheets)
            {
                List<ElementId> views = sheet.GetAllPlacedViews().ToList();
                doc.Delete(sheet.Id);
                foreach (ElementId id in views) if (doc.GetElement(id) != null) doc.Delete(id);
            }
        }

        private static Schema GetSchema()
        {
            Schema schema = Schema.Lookup(SchemaId);
            if (schema != null) return schema;
            var builder = new SchemaBuilder(SchemaId);
            builder.SetSchemaName("KStructIndependentColumnDrawing");
            builder.SetReadAccessLevel(AccessLevel.Public); builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField("ColumnUniqueId", typeof(string));
            return builder.Finish();
        }

        private static DrawingTypes ChooseTypes(IntPtr owner, Document doc)
        {
            List<FamilySymbol> titleBlocks = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().OrderBy(t => t.FamilyName + " : " + t.Name).ToList();
            List<ViewFamilyType> sections = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>().Where(t => t.ViewFamily == ViewFamily.Section).OrderBy(t => t.Name).ToList();
            if (titleBlocks.Count == 0) throw new InvalidOperationException("Project chưa nạp Title Block.");
            if (sections.Count == 0) throw new InvalidOperationException("Project chưa có Section View Family Type.");
            var window = new Window { Title = "Thiết lập bản vẽ cột", Width = 440, Height = 190,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock { Text = "Khung tên:" });
            var title = new System.Windows.Controls.ComboBox { ItemsSource = titleBlocks, DisplayMemberPath = "Name", SelectedIndex = 0, Margin = new Thickness(0, 3, 0, 7) };
            root.Children.Add(title); root.Children.Add(new TextBlock { Text = "Loại mặt cắt:" });
            var section = new System.Windows.Controls.ComboBox { ItemsSource = sections, DisplayMemberPath = "Name", SelectedIndex = 0, Margin = new Thickness(0, 3, 0, 9) };
            root.Children.Add(section);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Tiếp tục", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (s, e) => window.DialogResult = true;
            buttons.Children.Add(ok); buttons.Children.Add(cancel); root.Children.Add(buttons);
            window.Content = root; new WindowInteropHelper(window).Owner = owner;
            if (window.ShowDialog() != true) return null;
            return new DrawingTypes { TitleBlock = title.SelectedItem as FamilySymbol, SectionType = section.SelectedItem as ViewFamilyType };
        }

        private static string UniqueViewName(Document doc, string baseName)
        {
            var names = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
            if (!names.Contains(baseName)) return baseName;
            for (int i = 2; ; i++) if (!names.Contains(baseName + " (" + i + ")")) return baseName + " (" + i + ")";
        }
        private static string UniqueSheetNumber(Document doc, string baseNumber)
        {
            var numbers = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(s => s.SheetNumber), StringComparer.OrdinalIgnoreCase);
            if (!numbers.Contains(baseNumber)) return baseNumber;
            for (int i = 2; ; i++) if (!numbers.Contains(baseNumber + "-" + i)) return baseNumber + "-" + i;
        }
        private static string Sanitize(string value, string fallback)
        {
            string source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            string clean = new string(source.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' ? ch : '-').ToArray()).Trim('-');
            return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
        }
    }
}
