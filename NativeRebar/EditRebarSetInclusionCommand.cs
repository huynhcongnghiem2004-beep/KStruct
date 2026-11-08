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
    public sealed class EditRebarSetInclusionCommand : IExternalCommand
    {
        private sealed class RebarFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Rebar;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class InclusionOptions
        {
            public bool Include;
            public List<int> Positions;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                Rebar rebar = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Rebar>().FirstOrDefault();
                if (rebar == null)
                {
                    Reference picked = ui.Selection.PickObject(ObjectType.Element, new RebarFilter(),
                        "Chọn Rebar set cần loại trừ hoặc khôi phục vị trí");
                    rebar = doc.GetElement(picked) as Rebar;
                }
                if (rebar == null) return Result.Cancelled;
                int count = rebar.NumberOfBarPositions;
                if (count < 2)
                {
                    TaskDialog.Show("KStruct - Vị trí Rebar", "Rebar được chọn là thanh đơn, không có vị trí để loại trừ.");
                    return Result.Cancelled;
                }
                int includedCount = Enumerable.Range(0, count).Count(i => rebar.DoesBarExistAtPosition(i));
                InclusionOptions options = ChooseOptions(data.Application.MainWindowHandle, count, includedCount);
                if (options == null) return Result.Cancelled;

                int changed = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, options.Include
                    ? "KStruct - Khôi phục vị trí Rebar"
                    : "KStruct - Loại trừ vị trí Rebar"))
                {
                    tx.Start();
                    foreach (int zeroBased in options.Positions)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                bool exists = rebar.DoesBarExistAtPosition(zeroBased);
                                if (exists != options.Include)
                                {
                                    rebar.SetBarIncluded(options.Include, zeroBased);
                                    changed++;
                                }
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không hoàn tất thay đổi vị trí.");
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Vị trí " + (zeroBased + 1) + ": " + ex.Message);
                            }
                        }
                    }
                    if (changed == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Vị trí Rebar",
                            skipped.Count == 0
                                ? "Các vị trí đã chọn đã ở đúng trạng thái."
                                : "Không cập nhật được vị trí nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi vị trí Rebar.");
                }
                int newCount = rebar.Quantity;
                string report = (options.Include ? "Đã khôi phục " : "Đã loại trừ ") + changed +
                    " vị trí. Quantity của Rebar set hiện tại: " + newCount + ".";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " vị trí:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Vị trí Rebar", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Vị trí Rebar", ex.Message);
                return Result.Failed;
            }
        }

        private static InclusionOptions ChooseOptions(IntPtr owner, int positionCount, int includedCount)
        {
            InclusionOptions result = null;
            var window = new Window
            {
                Title = "Loại trừ vị trí Rebar - KStruct",
                Width = 430,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock
            {
                Text = "Rebar set có " + positionCount + " vị trí tiềm năng; hiện có " + includedCount +
                    " thanh được tính vào Quantity. Nhập số vị trí 1-based, phân cách bằng dấu phẩy.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            var action = new System.Windows.Controls.ComboBox
            {
                ItemsSource = new[] { "Loại trừ khỏi Quantity", "Khôi phục vào Quantity" },
                SelectedIndex = 0,
                MinWidth = 390
            };
            root.Children.Add(action);
            root.Children.Add(new TextBlock { Text = "Vị trí (ví dụ: 1, 4, 7):", Margin = new Thickness(0, 8, 0, 3) });
            var positions = new System.Windows.Controls.TextBox { Text = "", MinWidth = 390, MinHeight = 24 };
            root.Children.Add(positions);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var apply = new System.Windows.Controls.Button { Content = "Áp dụng", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Hủy", Width = 72, IsCancel = true };
            apply.Click += (s, e) =>
            {
                List<int> parsed;
                string error;
                if (!TryParsePositions(positions.Text, positionCount, out parsed, out error))
                {
                    TaskDialog.Show("KStruct - Vị trí Rebar", error);
                    return;
                }
                result = new InclusionOptions
                {
                    Include = action.SelectedIndex == 1,
                    Positions = parsed
                };
                window.DialogResult = true;
            };
            buttons.Children.Add(apply);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            return window.ShowDialog() == true ? result : null;
        }

        private static bool TryParsePositions(string text, int count, out List<int> values, out string error)
        {
            values = new List<int>();
            error = null;
            string[] tokens = (text ?? "").Split(new[] { ',', ';', ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                error = "Nhập ít nhất một số vị trí.";
                return false;
            }
            foreach (string token in tokens)
            {
                int oneBased;
                if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.CurrentCulture, out oneBased) &&
                    !int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out oneBased))
                {
                    error = "Không đọc được số vị trí '" + token + "'.";
                    return false;
                }
                if (oneBased < 1 || oneBased > count)
                {
                    error = "Vị trí phải nằm trong khoảng 1–" + count + ".";
                    return false;
                }
                int zeroBased = oneBased - 1;
                if (!values.Contains(zeroBased)) values.Add(zeroBased);
            }
            return true;
        }
    }
}
