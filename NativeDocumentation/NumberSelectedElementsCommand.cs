using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class NumberSelectedElementsCommand : IExternalCommand
    {
        private sealed class NumberingOptions
        {
            public string Prefix;
            public int Start;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Element> selected = ui.Selection.GetElementIds().Select(doc.GetElement).Where(x => x != null).Distinct().ToList();
                if (selected.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, "Chọn các cấu kiện cần đánh mã");
                    selected = picked.Select(reference => doc.GetElement(reference)).Where(x => x != null).Distinct().ToList();
                }
                if (selected.Count == 0) return Result.Cancelled;
                NumberingOptions options = AskOptions(data.Application.MainWindowHandle);
                if (options == null) return Result.Cancelled;

                HashSet<ElementId> selectedIds = new HashSet<ElementId>(selected.Select(x => x.Id));
                HashSet<string> usedMarks = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                    .Where(element => !selectedIds.Contains(element.Id))
                    .Select(element => element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK))
                    .Where(parameter => parameter != null && parameter.HasValue)
                    .Select(parameter => parameter.AsString())
                    .Where(mark => !string.IsNullOrWhiteSpace(mark))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                List<Element> ordered = selected.OrderBy(x => x.Id.Value).ToList();
                var assignments = new List<KeyValuePair<Element, string>>();
                int sequence = options.Start;
                foreach (Element element in ordered)
                {
                    Parameter markParameter = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                    if (markParameter == null || markParameter.IsReadOnly || markParameter.StorageType != StorageType.String) continue;
                    string candidate;
                    do { candidate = options.Prefix + sequence.ToString("D3"); sequence++; }
                    while (usedMarks.Contains(candidate));
                    usedMarks.Add(candidate);
                    assignments.Add(new KeyValuePair<Element, string>(element, candidate));
                }
                if (assignments.Count == 0)
                    throw new InvalidOperationException("Các phần tử đã chọn không có tham số Mark ghi được.");

                using (Transaction transaction = new Transaction(doc, "KStruct - Đánh mã cấu kiện"))
                {
                    transaction.Start();
                    foreach (var assignment in assignments)
                        assignment.Key.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).Set(assignment.Value);
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận mã cấu kiện.");
                }
                TaskDialog.Show("KStruct - Đánh mã cấu kiện", "Đã đánh mã cho " + assignments.Count + " phần tử từ '" + assignments.First().Value + "' đến '" + assignments.Last().Value + "'.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Đánh mã cấu kiện", ex.Message);
                return Result.Failed;
            }
        }

        private static NumberingOptions AskOptions(IntPtr owner)
        {
            var window = new Window
            {
                Title = "Đánh mã cấu kiện",
                Width = 360,
                Height = 190,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock { Text = "Tiền tố mã:", Margin = new Thickness(0, 0, 0, 4) });
            var prefix = new System.Windows.Controls.TextBox { Text = "KS-", MinHeight = 24 };
            root.Children.Add(prefix);
            root.Children.Add(new TextBlock { Text = "Số bắt đầu:", Margin = new Thickness(0, 8, 0, 4) });
            var start = new System.Windows.Controls.TextBox { Text = "1", MinHeight = 24 };
            root.Children.Add(start);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "Đánh mã", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) =>
            {
                int value;
                if (!int.TryParse(start.Text, out value) || value < 0)
                {
                    TaskDialog.Show("KStruct", "Số bắt đầu phải là số nguyên không âm.");
                    return;
                }
                window.DialogResult = true;
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            if (window.ShowDialog() != true) return null;
            return new NumberingOptions { Prefix = prefix.Text ?? string.Empty, Start = int.Parse(start.Text) };
        }
    }
}