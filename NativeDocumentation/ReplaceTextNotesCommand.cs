using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ReplaceTextNotesCommand : IExternalCommand
    {
        private enum SearchScope { SelectedTextNotes, ActiveView, EntireProject }

        private sealed class Options
        {
            public string Find;
            public string Replace;
            public SearchScope Scope;
            public bool MatchCase;
            public bool WholeWord;
        }

        private sealed class ScopeChoice
        {
            public SearchScope Scope { get; }
            public string Label { get; }
            public ScopeChoice(SearchScope scope, string label) { Scope = scope; Label = label; }
            public override string ToString() => Label;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                Options options = ChooseOptions(data.Application.MainWindowHandle);
                if (options == null) return Result.Cancelled;
                List<TextNote> notes = GetNotes(ui, options.Scope);
                var changes = new List<Tuple<TextNote, FormattedText, int>>();
                int totalMatches = 0;
                foreach (TextNote note in notes)
                {
                    FormattedText formatted = note.GetFormattedText();
                    int count = CountMatches(formatted, options);
                    if (count == 0) continue;
                    changes.Add(Tuple.Create(note, formatted, count));
                    totalMatches += count;
                }
                if (totalMatches == 0)
                {
                    TaskDialog.Show("KStruct - Thay văn bản", "Không tìm thấy nội dung cần thay trong phạm vi đã chọn.");
                    return Result.Succeeded;
                }
                if (TaskDialog.Show("Xác nhận thay văn bản",
                    "Phạm vi: " + ScopeLabel(options.Scope) +
                    "\nTextNote bị ảnh hưởng: " + changes.Count +
                    "\nSố lần thay: " + totalMatches +
                    "\n\nTìm: " + options.Find +
                    "\nThay bằng: " + options.Replace +
                    "\n\nTiếp tục?", TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No) != TaskDialogResult.Yes)
                    return Result.Cancelled;

                int updated = 0, replaced = 0;
                var skipped = new List<string>();
                using (Transaction transaction = new Transaction(doc, "KStruct - Thay văn bản TextNote"))
                {
                    transaction.Start();
                    foreach (Tuple<TextNote, FormattedText, int> item in changes)
                    {
                        try
                        {
                            FormattedText formatted = item.Item2;
                            int count = ReplaceMatches(formatted, options);
                            if (count == 0) continue;
                            item.Item1.SetFormattedText(formatted);
                            updated++;
                            replaced += count;
                        }
                        catch (Exception ex) { skipped.Add(item.Item1.Id.Value + ": " + ex.Message); }
                    }
                    if (updated == 0)
                    {
                        transaction.RollBack();
                        TaskDialog.Show("KStruct - Thay văn bản", "Không TextNote nào được cập nhật.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi văn bản.");
                }
                string report = "Đã thay " + replaced + " vị trí trong " + updated + " TextNote.";
                if (skipped.Count > 0) report += "\nKhông cập nhật được " + skipped.Count + " TextNote:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Thay văn bản", report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Thay văn bản", ex.Message);
                return Result.Failed;
            }
        }

        private static List<TextNote> GetNotes(UIDocument ui, SearchScope scope)
        {
            if (scope == SearchScope.SelectedTextNotes)
                return ui.Selection.GetElementIds().Select(ui.Document.GetElement).OfType<TextNote>().ToList();
            IEnumerable<TextNote> notes = new FilteredElementCollector(ui.Document)
                .OfClass(typeof(TextNote)).WhereElementIsNotElementType().Cast<TextNote>();
            if (scope == SearchScope.ActiveView) notes = notes.Where(note => note.OwnerViewId == ui.ActiveView.Id);
            return notes.ToList();
        }

        private static int CountMatches(FormattedText formatted, Options options)
        {
            int count = 0, start = 0;
            string text = formatted.GetPlainText();
            while (start <= text.Length)
            {
                TextRange match = formatted.Find(options.Find, start, options.MatchCase, options.WholeWord);
                if (match.Length <= 0) break;
                count++;
                start = match.End;
            }
            return count;
        }

        private static int ReplaceMatches(FormattedText formatted, Options options)
        {
            int count = 0, start = 0;
            string plainText = formatted.GetPlainText();
            while (start <= plainText.Length)
            {
                TextRange match = formatted.Find(options.Find, start, options.MatchCase, options.WholeWord);
                if (match.Length <= 0) break;
                int next = match.Start + options.Replace.Length;
                formatted.SetPlainText(match, options.Replace);
                count++;
                plainText = formatted.GetPlainText();
                start = next;
            }
            return count;
        }

        private static string ScopeLabel(SearchScope scope)
        {
            switch (scope)
            {
                case SearchScope.SelectedTextNotes: return "TextNote đang chọn";
                case SearchScope.ActiveView: return "View hiện tại";
                default: return "Toàn dự án";
            }
        }

        private static Options ChooseOptions(IntPtr owner)
        {
            var window = new Window
            {
                Title = "Thay văn bản - KStruct",
                Width = 520,
                Height = 365,
                MinWidth = 480,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            new WindowInteropHelper(window).Owner = owner;
            var root = new WpfGrid { Margin = new Thickness(14) };
            for (int i = 0; i < 9; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var find = new WpfTextBox { MinHeight = 27, Margin = new Thickness(0, 3, 0, 8) };
            var replace = new WpfTextBox { MinHeight = 27, Margin = new Thickness(0, 3, 0, 8) };
            var scope = new WpfComboBox
            {
                ItemsSource = new[]
                {
                    new ScopeChoice(SearchScope.SelectedTextNotes, "TextNote đang chọn"),
                    new ScopeChoice(SearchScope.ActiveView, "View hiện tại"),
                    new ScopeChoice(SearchScope.EntireProject, "Toàn dự án")
                },
                MinHeight = 27,
                Margin = new Thickness(0, 3, 0, 8),
                SelectedIndex = 0
            };
            var matchCase = new CheckBox { Content = "Phân biệt chữ hoa/thường", Margin = new Thickness(0, 2, 0, 5) };
            var wholeWord = new CheckBox { Content = "Chỉ khớp nguyên từ", Margin = new Thickness(0, 2, 0, 8) };
            var info = new TextBlock
            {
                Text = "Thao tác giữ định dạng của các đoạn văn bản còn lại trong TextNote; nội dung thay thế nhận định dạng tại vị trí khớp.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            root.Children.Add(new TextBlock { Text = "Tìm nội dung:", FontWeight = FontWeights.SemiBold });
            WpfGrid.SetRow(find, 1); root.Children.Add(find);
            var replaceLabel = new TextBlock { Text = "Thay bằng:", FontWeight = FontWeights.SemiBold };
            WpfGrid.SetRow(replaceLabel, 2); root.Children.Add(replaceLabel);
            WpfGrid.SetRow(replace, 3); root.Children.Add(replace);
            var scopeLabel = new TextBlock { Text = "Phạm vi:", FontWeight = FontWeights.SemiBold };
            WpfGrid.SetRow(scopeLabel, 4); root.Children.Add(scopeLabel);
            WpfGrid.SetRow(scope, 5); root.Children.Add(scope);
            WpfGrid.SetRow(matchCase, 6); root.Children.Add(matchCase);
            // The final row contains the second option and action buttons.
            var footer = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 4, 0, 0) };
            DockPanel.SetDock(wholeWord, Dock.Left); footer.Children.Add(wholeWord);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Xem trước / tiếp tục", MinWidth = 145, IsDefault = true, Margin = new Thickness(0, 0, 7, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 70, IsCancel = true };
            Options result = null;
            ok.Click += (sender, args) =>
            {
                ScopeChoice selectedScope = scope.SelectedItem as ScopeChoice;
                if (string.IsNullOrWhiteSpace(find.Text) || find.Text.Contains("\n") || find.Text.Contains("\r") ||
                    replace.Text.Contains("\n") || replace.Text.Contains("\r") || selectedScope == null)
                {
                    MessageBox.Show(window, "Chuỗi tìm/thay phải nằm trên một dòng và chuỗi tìm không được để trống.",
                        "KStruct - Thay văn bản", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                result = new Options
                {
                    Find = find.Text,
                    Replace = replace.Text,
                    Scope = selectedScope.Scope,
                    MatchCase = matchCase.IsChecked == true,
                    WholeWord = wholeWord.IsChecked == true
                };
                window.DialogResult = true;
            };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Right); footer.Children.Add(buttons);
            WpfGrid.SetRow(info, 7); root.Children.Add(info);
            WpfGrid.SetRow(footer, 8); root.Children.Add(footer);
            window.Content = root;
            return window.ShowDialog() == true ? result : null;
        }
    }
}
