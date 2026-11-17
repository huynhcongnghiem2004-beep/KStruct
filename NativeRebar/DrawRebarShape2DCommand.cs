using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class DrawRebarShape2DCommand : IExternalCommand
    {
        private sealed class RebarFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Rebar;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class BarShape
        {
            public List<XYZ> Points;
            public string Type;
            public string Mark;
            public double DiameterMm;
            public double LengthMm;
            public int Quantity;
            public Rebar TagBar;
            public bool IsStandard;
        }

        internal sealed class Options
        {
            public TextNoteType TextType;
            public bool ShowSegmentLength;
            public bool ShowBarInfo;
            public double GapMm;
            public bool CreateRebarTag;
            public ElementType StandardTagType;
            public ElementType StirrupTagType;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            View view = ui.ActiveView;
            try
            {
                if (view.ViewType == ViewType.ThreeD || view.ViewType == ViewType.DrawingSheet ||
                    view.ViewType == ViewType.Schedule || view.ViewType == ViewType.ProjectBrowser ||
                    view.ViewType == ViewType.SystemBrowser)
                    throw new InvalidOperationException("Mở một view 2D như mặt bằng, mặt đứng, mặt cắt hoặc Drafting View.");

                List<Rebar> bars = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Rebar>().Distinct().ToList();
                if (bars.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new RebarFilter(), "Chọn một hoặc nhiều Rebar để vẽ hình 2D");
                    bars = picked.Select(r => doc.GetElement(r)).OfType<Rebar>().Distinct().ToList();
                }
                if (bars.Count == 0) return Result.Cancelled;
                List<BarShape> shapes = CollectShapes(doc, bars, view);
                if (shapes.Count == 0) throw new InvalidOperationException("Không đọc được hình thanh từ Rebar đã chọn.");

                List<TextNoteType> textTypes = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType))
                    .Cast<TextNoteType>().OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (textTypes.Count == 0) throw new InvalidOperationException("Project chưa có TextNoteType.");
                List<ElementType> rebarTagTypes = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RebarTags)
                    .WhereElementIsElementType().Cast<ElementType>()
                    .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                Options options = ShowOptions(data.Application.MainWindowHandle, textTypes, rebarTagTypes, RebarShapeDrawingSettings.Load(doc));
                if (options == null) return Result.Cancelled;
                XYZ insertion = ui.Selection.PickPoint("Chọn điểm đặt sơ đồ hình thanh Rebar");
                double scale = Math.Max(1, view.Scale);
                double gap = UnitUtils.ConvertToInternalUnits(options.GapMm, UnitTypeId.Millimeters) * scale;
                GraphicsStyle lineStyle = FindShapeLineStyle(doc);
                int lineCount = 0, labelCount = 0, tagCount = 0, tagSkipped = 0;

                using (Transaction tx = new Transaction(doc, "KStruct - Vẽ hình thép 2D"))
                {
                    tx.Start();
                    RebarShapeDrawingSettings.Save(doc, options);
                    double cursor = 0;
                    foreach (BarShape shape in shapes)
                    {
                        List<XYZ> local = shape.Points;
                        double minX = local.Min(p => p.X), maxX = local.Max(p => p.X);
                        double minY = local.Min(p => p.Y), maxY = local.Max(p => p.Y);
                        Func<XYZ, XYZ> project = p => insertion + view.RightDirection * ((cursor + p.X - minX) * scale) +
                            view.UpDirection * ((p.Y - minY) * scale);

                        for (int i = 0; i + 1 < local.Count; i++)
                        {
                            XYZ a = project(local[i]), b = project(local[i + 1]);
                            if (a.DistanceTo(b) < doc.Application.ShortCurveTolerance) continue;
                            DetailCurve detail = doc.Create.NewDetailCurve(view, Line.CreateBound(a, b));
                            if (lineStyle != null) detail.LineStyle = lineStyle;
                            lineCount++;
                            if (options.ShowSegmentLength)
                            {
                                double segmentMm = UnitUtils.ConvertFromInternalUnits(local[i].DistanceTo(local[i + 1]), UnitTypeId.Millimeters);
                                XYZ midpoint = (a + b) * 0.5 + view.UpDirection * (UnitUtils.ConvertToInternalUnits(15, UnitTypeId.Millimeters) * scale);
                                TextNote.Create(doc, view.Id, midpoint,
                                    Math.Round(segmentMm).ToString(CultureInfo.InvariantCulture), options.TextType.Id);
                                labelCount++;
                            }
                        }

                        XYZ label = insertion + view.RightDirection * (cursor * scale) +
                            view.UpDirection * (-gap * 0.45 - UnitUtils.ConvertToInternalUnits(8, UnitTypeId.Millimeters) * scale);
                        if (options.CreateRebarTag)
                        {
                            ElementType tagType = shape.IsStandard
                                ? options.StandardTagType : options.StirrupTagType;
                            if (tagType == null) tagSkipped++;
                            else
                            {
                                using (SubTransaction sub = new SubTransaction(doc))
                                {
                                    sub.Start();
                                    try
                                    {
                                        IndependentTag tag = IndependentTag.Create(doc, tagType.Id, view.Id,
                                            new Reference(shape.TagBar), false, TagOrientation.Horizontal, label);
                                        if (tag == null) throw new InvalidOperationException("Revit không tạo được Rebar Tag.");
                                        tag.TagHeadPosition = label;
                                        if (sub.Commit() == TransactionStatus.Committed) tagCount++;
                                        else tagSkipped++;
                                    }
                                    catch
                                    {
                                        if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                        tagSkipped++;
                                    }
                                }
                            }
                        }
                        if (options.ShowBarInfo)
                        {
                            string info = "Ø" + shape.DiameterMm.ToString("0.#", CultureInfo.InvariantCulture) +
                                "  L=" + Math.Round(shape.LengthMm).ToString(CultureInfo.InvariantCulture) + " mm  x" + shape.Quantity;
                            if (!string.IsNullOrWhiteSpace(shape.Mark)) info = shape.Mark.Trim() + "  " + info;
                            XYZ infoPoint = label - view.UpDirection *
                                (options.CreateRebarTag ? UnitUtils.ConvertToInternalUnits(10, UnitTypeId.Millimeters) * scale : 0);
                            TextNote.Create(doc, view.Id, infoPoint, info, options.TextType.Id);
                            labelCount++;
                        }
                        cursor += Math.Max(maxX - minX, UnitUtils.ConvertToInternalUnits(80, UnitTypeId.Millimeters)) + gap;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận sơ đồ hình thép.");
                }

                TaskDialog.Show("KStruct - Hình thép 2D", "Đã vẽ " + shapes.Count + " hình thanh, " + lineCount +
                    " đoạn nét, " + labelCount + " ghi chú và " + tagCount + " Rebar Tag trong view hiện hành." + (tagSkipped > 0 ? " Bỏ qua tag: " + tagSkipped + "." : ""));
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Hình thép 2D", ex.Message);
                return Result.Failed;
            }
        }

        private static List<BarShape> CollectShapes(Document doc, IList<Rebar> bars, View view)
        {
            var groups = new Dictionary<string, BarShape>(StringComparer.Ordinal);
            foreach (Rebar bar in bars)
            {
                IList<Curve> curves;
                try { curves = bar.GetCenterlineCurves(false, true, true, MultiplanarOption.IncludeAllMultiplanarCurves, 0); }
                catch { continue; }
                if (curves == null || curves.Count == 0) continue;

                List<XYZ> world = TessellateInOrder(curves);
                if (world.Count < 2) continue;
                XYZ origin = world[0];
                List<XYZ> local = world.Select(p => new XYZ((p - origin).DotProduct(view.RightDirection),
                    (p - origin).DotProduct(view.UpDirection), 0)).ToList();
                double minX = local.Min(p => p.X), minY = local.Min(p => p.Y);
                List<XYZ> normalized = local.Select(p => new XYZ(p.X - minX, p.Y - minY, 0)).ToList();
                string signature = string.Join(";", normalized.Select(p =>
                    Math.Round(UnitUtils.ConvertFromInternalUnits(p.X, UnitTypeId.Millimeters)).ToString(CultureInfo.InvariantCulture) + "," +
                    Math.Round(UnitUtils.ConvertFromInternalUnits(p.Y, UnitTypeId.Millimeters)).ToString(CultureInfo.InvariantCulture)));
                RebarBarType type = doc.GetElement(bar.GetTypeId()) as RebarBarType;
                RebarShape rebarShape = doc.GetElement(bar.GetShapeId()) as RebarShape;
                bool isStandard = rebarShape == null || rebarShape.RebarStyle == RebarStyle.Standard;
                string diaKey = type == null ? "0" : Math.Round(UnitUtils.ConvertFromInternalUnits(type.BarNominalDiameter, UnitTypeId.Millimeters), 1).ToString(CultureInfo.InvariantCulture);
                string key = diaKey + "|" + signature;
                BarShape shape;
                if (!groups.TryGetValue(key, out shape))
                {
                    shape = new BarShape
                    {
                        Points = normalized,
                        TagBar = bar,
                        IsStandard = isStandard,
                        Type = type == null ? "" : type.Name,
                        Mark = bar.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString(),
                        DiameterMm = type == null ? 0 : UnitUtils.ConvertFromInternalUnits(type.BarNominalDiameter, UnitTypeId.Millimeters),
                        LengthMm = UnitUtils.ConvertFromInternalUnits(curves.Sum(c => c.Length), UnitTypeId.Millimeters),
                        Quantity = 0
                    };
                    groups.Add(key, shape);
                }
                shape.Quantity += Math.Max(1, bar.Quantity);
            }
            return groups.Values.ToList();
        }

        private static List<XYZ> TessellateInOrder(IList<Curve> curves)
        {
            var points = new List<XYZ>();
            foreach (Curve curve in curves)
            {
                IList<XYZ> tess = curve.Tessellate();
                if (tess == null || tess.Count < 2) continue;
                if (points.Count == 0)
                {
                    points.AddRange(tess);
                    continue;
                }
                XYZ last = points[points.Count - 1];
                bool reverse = last.DistanceTo(tess[tess.Count - 1]) < last.DistanceTo(tess[0]);
                if (reverse)
                {
                    for (int i = tess.Count - 1; i >= 0; i--)
                        if (points[points.Count - 1].DistanceTo(tess[i]) > 1e-7) points.Add(tess[i]);
                }
                else
                {
                    for (int i = 0; i < tess.Count; i++)
                        if (points[points.Count - 1].DistanceTo(tess[i]) > 1e-7) points.Add(tess[i]);
                }
            }
            return points;
        }

        private static GraphicsStyle FindShapeLineStyle(Document doc)
        {
            Category lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            if (lines == null) return null;
            foreach (Category category in lines.SubCategories)
            {
                string name = category.Name.ToLowerInvariant();
                if (name.Contains("dash") || name.Contains("hidden") || name.Contains("đứt"))
                    return category.GetGraphicsStyle(GraphicsStyleType.Projection);
            }
            return null;
        }

        internal static Options ShowOptions(IntPtr owner, IList<TextNoteType> textTypes, IList<ElementType> rebarTagTypes, RebarShapeDrawingSettings.Values defaults, string actionText = "Vẽ hình")
        {
            Options result = null;
            var window = new Window
            {
                Title = "Vẽ hình thép 2D - KStruct", Width = 420, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock { Text = "Kiểu chữ:" });
            var textCombo = new System.Windows.Controls.ComboBox
            {
                ItemsSource = textTypes, DisplayMemberPath = "Name", SelectedItem = textTypes.FirstOrDefault(t => defaults != null && string.Equals(t.Name, defaults.TextTypeName, StringComparison.OrdinalIgnoreCase)) ?? textTypes.FirstOrDefault(),
                MinWidth = 380, MinHeight = 25, Margin = new Thickness(0, 3, 0, 8)
            };
            root.Children.Add(textCombo);
            var lengthCheck = new CheckBox { Content = "Ghi chiều dài từng đoạn", IsChecked = defaults != null && defaults.ShowSegmentLength, Margin = new Thickness(0, 0, 0, 4) };
            var infoCheck = new CheckBox { Content = "Ghi mã, đường kính, chiều dài tổng và số lượng", IsChecked = defaults == null || defaults.ShowBarInfo, Margin = new Thickness(0, 0, 0, 8) };
            root.Children.Add(lengthCheck);
            root.Children.Add(infoCheck);
            var createTagCheck = new CheckBox
            {
                Content = "Gắn Rebar Tag vào từng hình",
                IsChecked = defaults != null && defaults.CreateRebarTag,
                Margin = new Thickness(0, 0, 0, 4)
            };
            root.Children.Add(createTagCheck);
            root.Children.Add(new TextBlock { Text = "Tag thép thanh tiêu chuẩn:" });
            var standardTagCombo = new System.Windows.Controls.ComboBox
            {
                ItemsSource = rebarTagTypes,
                DisplayMemberPath = "Name",
                SelectedItem = rebarTagTypes.FirstOrDefault(t => defaults != null &&
                    string.Equals(t.Name, defaults.StandardTagTypeName, StringComparison.OrdinalIgnoreCase)) ?? rebarTagTypes.FirstOrDefault(),
                MinWidth = 380,
                Margin = new Thickness(0, 3, 0, 7)
            };
            root.Children.Add(standardTagCombo);
            root.Children.Add(new TextBlock { Text = "Tag thép đai/móc:" });
            var stirrupTagCombo = new System.Windows.Controls.ComboBox
            {
                ItemsSource = rebarTagTypes,
                DisplayMemberPath = "Name",
                SelectedItem = rebarTagTypes.FirstOrDefault(t => defaults != null &&
                    string.Equals(t.Name, defaults.StirrupTagTypeName, StringComparison.OrdinalIgnoreCase)) ?? rebarTagTypes.FirstOrDefault(),
                MinWidth = 380,
                Margin = new Thickness(0, 3, 0, 8)
            };
            root.Children.Add(stirrupTagCombo);
            root.Children.Add(new TextBlock { Text = "Khoảng cách giữa các hình (mm trên giấy):" });
            var gapBox = new System.Windows.Controls.TextBox { Text = (defaults == null ? 60.0 : defaults.GapMm).ToString(CultureInfo.CurrentCulture), MinWidth = 380, MinHeight = 24, Margin = new Thickness(0, 3, 0, 10) };
            root.Children.Add(gapBox);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var draw = new Button { Content = actionText, MinWidth = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 70, IsCancel = true };
            draw.Click += (s, e) =>
            {
                double gap;
                if (textCombo.SelectedItem == null || !double.TryParse(gapBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out gap) || gap < 0)
                {
                    TaskDialog.Show("KStruct - Hình thép 2D", "Khoảng cách giữa các hình phải là số không âm.");
                    return;
                }
                if (createTagCheck.IsChecked.GetValueOrDefault() &&
                    (standardTagCombo.SelectedItem == null || stirrupTagCombo.SelectedItem == null))
                {
                    TaskDialog.Show("KStruct - Hình thép 2D", "Chọn Rebar Tag cho thép thanh và thép đai, hoặc bỏ chọn tùy chọn gắn tag.");
                    return;
                }
                result = new Options
                {
                    TextType = (TextNoteType)textCombo.SelectedItem,
                    ShowSegmentLength = lengthCheck.IsChecked.GetValueOrDefault(),
                    ShowBarInfo = infoCheck.IsChecked.GetValueOrDefault(),
                    GapMm = gap,
                    CreateRebarTag = createTagCheck.IsChecked.GetValueOrDefault(),
                    StandardTagType = standardTagCombo.SelectedItem as ElementType,
                    StirrupTagType = stirrupTagCombo.SelectedItem as ElementType
                };
                window.DialogResult = true;
            };
            buttons.Children.Add(draw);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            return window.ShowDialog() == true ? result : null;
        }
    }
}


