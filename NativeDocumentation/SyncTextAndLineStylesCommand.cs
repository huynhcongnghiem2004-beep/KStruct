using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class SyncTextAndLineStylesCommand : IExternalCommand
    {
        private sealed class AnnotationFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is TextNote || element is DetailCurve;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class StyleChoice
        {
            public string Name { get; private set; }
            public ElementId Id { get; private set; }
            public StyleChoice(string name, ElementId id) { Name = name; Id = id; }
            public override string ToString() => Name;
        }

        private sealed class Options
        {
            public bool ChangeText;
            public bool ChangeLines;
            public TextNoteType TextType;
            public GraphicsStyle LineStyle;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Element> annotations = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .Where(e => e != null && e.OwnerViewId == ui.ActiveView.Id && (e is TextNote || e is DetailCurve))
                    .Distinct().ToList();
                if (annotations.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new AnnotationFilter(),
                        "Chọn TextNote và Detail Line cần đổi kiểu đồng loạt");
                    annotations = picked.Select(r => doc.GetElement(r)).Where(e => e != null &&
                        (e is TextNote || e is DetailCurve)).Distinct().ToList();
                }
                if (annotations.Count == 0) return Result.Cancelled;

                List<TextNoteType> textTypes = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType))
                    .Cast<TextNoteType>().OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                Category linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
                List<GraphicsStyle> lineStyles = linesCategory.SubCategories.Cast<Category>()
                    .Select(c => c.GetGraphicsStyle(GraphicsStyleType.Projection)).Where(s => s != null)
                    .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

                int textCount = annotations.OfType<TextNote>().Count();
                int lineCount = annotations.OfType<DetailCurve>().Count();
                Options options = ShowOptions(data.Application.MainWindowHandle, textTypes, lineStyles, textCount, lineCount);
                if (options == null || (!options.ChangeText && !options.ChangeLines)) return Result.Cancelled;
                if (options.ChangeText && options.TextType == null)
                    throw new InvalidOperationException("Project không có TextNoteType để áp dụng.");
                if (options.ChangeLines && options.LineStyle == null)
                    throw new InvalidOperationException("Project không có Line Style để áp dụng.");

                int changedText = 0, changedLines = 0;
                using (Transaction tx = new Transaction(doc, "KStruct - Đồng bộ kiểu Text và Line"))
                {
                    tx.Start();
                    foreach (TextNote note in annotations.OfType<TextNote>())
                    {
                        if (options.ChangeText && note.GetTypeId() != options.TextType.Id)
                        {
                            note.ChangeTypeId(options.TextType.Id);
                            changedText++;
                        }
                    }
                    foreach (DetailCurve line in annotations.OfType<DetailCurve>())
                    {
                        if (options.ChangeLines && line.LineStyle != options.LineStyle)
                        {
                            line.LineStyle = options.LineStyle;
                            changedLines++;
                        }
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi kiểu Text/Line.");
                }

                TaskDialog.Show("KStruct - Kiểu Text/Line", "Đã đổi kiểu " + changedText + " ghi chú và " +
                    changedLines + " đường chi tiết.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Kiểu Text/Line", ex.Message);
                return Result.Failed;
            }
        }

        private static Options ShowOptions(IntPtr owner, IList<TextNoteType> textTypes,
            IList<GraphicsStyle> lineStyles, int textCount, int lineCount)
        {
            Options result = null;
            var window = new Window
            {
                Title = "KStruct - Đổi kiểu Text và Line",
                Width = 470,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Background = System.Windows.Media.Brushes.White,
                Padding = new Thickness(16)
            };
            var root = new StackPanel();
            root.Children.Add(new TextBlock
            {
                Text = "Chọn kiểu áp dụng cho các đối tượng đã chọn trong view hiện hành.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var textCheck = new CheckBox
            {
                Content = "Đổi kiểu TextNote (đã chọn: " + textCount + ")",
                IsChecked = textCount > 0 && textTypes.Count > 0,
                IsEnabled = textCount > 0 && textTypes.Count > 0,
                Margin = new Thickness(0, 0, 0, 5)
            };
            var textCombo = new System.Windows.Controls.ComboBox
            {
                MinWidth = 420,
                MinHeight = 26,
                ItemsSource = textTypes,
                DisplayMemberPath = "Name",
                SelectedIndex = 0,
                IsEnabled = textCount > 0 && textTypes.Count > 0,
                Margin = new Thickness(18, 0, 0, 12)
            };
            var lineCheck = new CheckBox
            {
                Content = "Đổi Line Style (đã chọn: " + lineCount + ")",
                IsChecked = lineCount > 0 && lineStyles.Count > 0,
                IsEnabled = lineCount > 0 && lineStyles.Count > 0,
                Margin = new Thickness(0, 0, 0, 5)
            };
            var lineCombo = new System.Windows.Controls.ComboBox
            {
                MinWidth = 420,
                MinHeight = 26,
                ItemsSource = lineStyles,
                DisplayMemberPath = "Name",
                SelectedIndex = 0,
                IsEnabled = lineCount > 0 && lineStyles.Count > 0,
                Margin = new Thickness(18, 0, 0, 14)
            };
            textCheck.Checked += (s, e) => textCombo.IsEnabled = textTypes.Count > 0;
            textCheck.Unchecked += (s, e) => textCombo.IsEnabled = false;
            lineCheck.Checked += (s, e) => lineCombo.IsEnabled = lineStyles.Count > 0;
            lineCheck.Unchecked += (s, e) => lineCombo.IsEnabled = false;
            root.Children.Add(textCheck);
            root.Children.Add(textCombo);
            root.Children.Add(lineCheck);
            root.Children.Add(lineCombo);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var apply = new Button { Content = "Áp dụng", MinWidth = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 80, IsCancel = true };
            apply.Click += (s, e) =>
            {
                result = new Options
                {
                    ChangeText = textCheck.IsChecked == true,
                    ChangeLines = lineCheck.IsChecked == true,
                    TextType = textCombo.SelectedItem as TextNoteType,
                    LineStyle = lineCombo.SelectedItem as GraphicsStyle
                };
                window.DialogResult = true;
                window.Close();
            };
            buttons.Children.Add(apply);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            if (window.ShowDialog() != true) return null;
            return result;
        }
    }
}
