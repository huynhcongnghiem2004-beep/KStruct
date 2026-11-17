using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CreateSectionReferenceCommand : IExternalCommand
    {
        private sealed class ViewChoice
        {
            public ViewSection View { get; private set; }
            public string Name { get { return View.Name; } }
            public string Kind { get { return View.ViewType.ToString(); } }
            public string Display { get { return Name + "  |  " + Kind + "  |  ID " + View.Id.Value; } }
            public ViewChoice(ViewSection view) { View = view; }
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            View parent = ui.ActiveView;
            try
            {
                if (parent.IsTemplate || parent.ViewType == ViewType.DrawingSheet || parent.ViewType == ViewType.Schedule ||
                    parent.ViewType == ViewType.ThreeD || parent.ViewType == ViewType.ProjectBrowser || parent.ViewType == ViewType.SystemBrowser)
                    throw new InvalidOperationException("Mở mặt bằng, mặt đứng hoặc mặt cắt để đặt ký hiệu tham chiếu.");
                List<ViewChoice> sections = new FilteredElementCollector(doc).OfClass(typeof(ViewSection)).Cast<ViewSection>()
                    .Where(v => !v.IsTemplate && v.Id != parent.Id && v.ViewType == ViewType.Section)
                    .OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase).Select(v => new ViewChoice(v)).ToList();
                if (sections.Count == 0) throw new InvalidOperationException("Project chưa có section view để tham chiếu.");
                ViewChoice choice = ChooseView(data.Application.MainWindowHandle, sections);
                if (choice == null) return Result.Cancelled;
                XYZ head = ui.Selection.PickPoint("Chọn đầu ký hiệu tham chiếu mặt cắt");
                XYZ tail = ui.Selection.PickPoint("Chọn đầu mũi tên/kết thúc ký hiệu tham chiếu");
                if (head.DistanceTo(tail) < doc.Application.ShortCurveTolerance)
                    throw new InvalidOperationException("Hai điểm đặt ký hiệu phải cách nhau.");
                using (Transaction tx = new Transaction(doc, "KStruct - Tạo tham chiếu mặt cắt"))
                {
                    tx.Start();
                    ViewSection.CreateReferenceSection(doc, parent.Id, choice.View.Id, head, tail);
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận ký hiệu tham chiếu.");
                }
                TaskDialog.Show("KStruct - Tham chiếu mặt cắt", "Đã đặt tham chiếu tới view: " + choice.View.Name);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Tham chiếu mặt cắt", ex.Message);
                return Result.Failed;
            }
        }

        private static ViewChoice ChooseView(IntPtr owner, IList<ViewChoice> views)
        {
            var window = new Window
            {
                Title = "Chọn mặt cắt để tham chiếu - KStruct",
                Width = 520,
                Height = 420,
                MinWidth = 420,
                MinHeight = 300,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false
            };
            new WindowInteropHelper(window).Owner = owner;
            var root = new DockPanel { Margin = new Thickness(12) };
            var description = new TextBlock
            {
                Text = "Chọn section view đích. Sau đó chọn hai điểm trong view hiện hành để đặt ký hiệu.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            DockPanel.SetDock(description, Dock.Top);
            root.Children.Add(description);
            var list = new ListBox { ItemsSource = views, DisplayMemberPath = "Display" };
            root.Children.Add(list);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            var ok = new Button { Content = "Chọn", MinWidth = 85, Padding = new Thickness(10, 4, 10, 4), IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 75, Padding = new Thickness(10, 4, 10, 4), IsCancel = true };
            ok.Click += (s, e) =>
            {
                if (list.SelectedItem == null) { TaskDialog.Show("KStruct", "Hãy chọn một view."); return; }
                window.DialogResult = true;
            };
            buttons.Children.Add(ok); buttons.Children.Add(cancel); root.Children.Add(buttons);
            window.Content = root;
            if (window.ShowDialog() != true) return null;
            return list.SelectedItem as ViewChoice;
        }
    }
}
