using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class SectionViewManagerCommand : IExternalCommand
    {
        private sealed class ViewChoice
        {
            public View View { get; private set; }
            public string Name { get { return View.Name; } }
            public string Description { get; private set; }
            public ViewChoice(View view)
            {
                View = view;
                Description = view.ViewType + " | " + view.Name;
            }
        }

        private sealed class DialogResultData
        {
            public View View;
            public string Rename;
            public bool Open;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<View> views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                    .Where(v => !v.IsTemplate && (v.ViewType == ViewType.Section || v.ViewType == ViewType.Detail))
                    .OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (views.Count == 0)
                {
                    TaskDialog.Show("KStruct - Quản lý mặt cắt", "Project chưa có section hoặc detail view.");
                    return Result.Cancelled;
                }
                DialogResultData action = ShowManager(data.Application.MainWindowHandle, views);
                if (action == null || action.View == null) return Result.Cancelled;
                if (action.Open)
                {
                    ui.RequestViewChange(action.View);
                    return Result.Succeeded;
                }
                string newName = (action.Rename ?? string.Empty).Trim();
                if (newName.Length == 0) return Result.Cancelled;
                bool duplicate = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                    .Any(v => v.Id != action.View.Id && string.Equals(v.Name, newName, StringComparison.OrdinalIgnoreCase));
                if (duplicate) throw new InvalidOperationException("Tên view đã tồn tại: " + newName);
                using (Transaction tx = new Transaction(doc, "KStruct - Đổi tên mặt cắt"))
                {
                    tx.Start();
                    action.View.Name = newName;
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận tên mới.");
                }
                TaskDialog.Show("KStruct - Quản lý mặt cắt", "Đã đổi tên view thành: " + newName);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Quản lý mặt cắt", ex.Message);
                return Result.Failed;
            }
        }

        private static DialogResultData ShowManager(IntPtr owner, IList<View> views)
        {
            var choices = views.Select(v => new ViewChoice(v)).ToList();
            var window = new Window
            {
                Title = "Quản lý mặt cắt KStruct",
                Width = 520,
                Height = 430,
                MinWidth = 520,
                MinHeight = 430,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new DockPanel { Margin = new Thickness(12) };
            var footer = new StackPanel { Orientation = Orientation.Vertical };
            DockPanel.SetDock(footer, Dock.Bottom);
            footer.Children.Add(new TextBlock { Text = "Tên mới (để trống nếu chỉ mở view):", Margin = new Thickness(0, 8, 0, 3) });
            var nameBox = new System.Windows.Controls.TextBox { MinHeight = 24 };
            footer.Children.Add(nameBox);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var open = new Button { Content = "Mở view", Width = 88, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var rename = new Button { Content = "Đổi tên", Width = 88, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Đóng", Width = 72, IsCancel = true };
            buttons.Children.Add(open);
            buttons.Children.Add(rename);
            buttons.Children.Add(cancel);
            footer.Children.Add(buttons);
            root.Children.Add(footer);
            var list = new ListBox { ItemsSource = choices, DisplayMemberPath = "Description", MinHeight = 250 };
            list.SelectedIndex = 0;
            list.SelectionChanged += (sender, args) =>
            {
                ViewChoice choice = list.SelectedItem as ViewChoice;
                nameBox.Text = choice == null ? string.Empty : choice.View.Name;
            };
            root.Children.Add(list);
            DialogResultData result = null;
            open.Click += (sender, args) =>
            {
                ViewChoice choice = list.SelectedItem as ViewChoice;
                if (choice == null) return;
                result = new DialogResultData { View = choice.View, Open = true };
                window.DialogResult = true;
            };
            rename.Click += (sender, args) =>
            {
                ViewChoice choice = list.SelectedItem as ViewChoice;
                if (choice == null) return;
                if (string.IsNullOrWhiteSpace(nameBox.Text))
                {
                    TaskDialog.Show("KStruct - Quản lý mặt cắt", "Nhập tên view mới.");
                    return;
                }
                result = new DialogResultData { View = choice.View, Rename = nameBox.Text };
                window.DialogResult = true;
            };
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            return window.ShowDialog() == true ? result : null;
        }
    }
}
