using System;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class SelectRebarByNumberCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            try
            {
                string input = ChooseNumber(data.Application.MainWindowHandle);
                if (input == null) return Result.Cancelled;
                int target;
                if (!Int32.TryParse(input, NumberStyles.Integer, CultureInfo.CurrentCulture, out target))
                {
                    TaskDialog.Show("KStruct - Chọn theo số thép", "Rebar Number phải là số nguyên.");
                    return Result.Cancelled;
                }

                var matches = new FilteredElementCollector(ui.Document)
                    .OfClass(typeof(Rebar)).WhereElementIsNotElementType().Cast<Rebar>()
                    .Where(rebar => ReadNumber(rebar) == target)
                    .Select(rebar => rebar.Id).ToList();
                if (matches.Count == 0)
                {
                    TaskDialog.Show("KStruct - Chọn theo số thép", "Không tìm thấy Rebar Number " + target + ".");
                    return Result.Cancelled;
                }

                ui.Selection.SetElementIds(matches);
                TaskDialog.Show("KStruct - Chọn theo số thép",
                    "Đã chọn " + matches.Count + " thanh có Rebar Number " + target + ".");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Chọn theo số thép", ex.Message);
                return Result.Failed;
            }
        }

        private static int ReadNumber(Rebar rebar)
        {
            Parameter parameter = rebar.get_Parameter(BuiltInParameter.REBAR_NUMBER);
            if (parameter == null || !parameter.HasValue) return Int32.MinValue;
            int value;
            string text = parameter.AsValueString() ?? parameter.AsString();
            if (Int32.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value)) return value;
            if (Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return value;
            return parameter.StorageType == StorageType.Integer ? parameter.AsInteger() : Int32.MinValue;
        }

        private static string ChooseNumber(IntPtr owner)
        {
            var window = new Window
            {
                Title = "Chọn Rebar theo số thép",
                Width = 340,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock
            {
                Text = "Nhập Rebar Number cần chọn trong toàn bộ project:",
                Margin = new Thickness(0, 0, 0, 6)
            });
            var input = new System.Windows.Controls.TextBox { MinWidth = 280, Text = "1" };
            root.Children.Add(input);
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            var ok = new Button { Content = "Chọn", Width = 76, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 76, IsCancel = true };
            ok.Click += (sender, args) =>
            {
                int value;
                if (!Int32.TryParse(input.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value))
                {
                    TaskDialog.Show("KStruct - Chọn theo số thép", "Rebar Number phải là số nguyên.");
                    return;
                }
                window.DialogResult = true;
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            return window.ShowDialog() == true ? input.Text : null;
        }
    }
}
