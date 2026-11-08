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
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class RibbonPanelManagerCommand : IExternalCommand
    {
        private sealed class PanelChoice
        {
            public RibbonPanel Panel;
            public CheckBox CheckBox;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            IList<RibbonPanel> panels = data.Application.GetRibbonPanels("KStruct");
            List<RibbonPanel> managed = panels.Where(p => p.Name != "KStruct Thiết lập").ToList();
            if (managed.Count == 0)
            {
                TaskDialog.Show("KStruct", "Không tìm thấy panel KStruct để quản lý.");
                return Result.Cancelled;
            }

            var choices = new List<PanelChoice>();
            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock
            {
                Text = "Chọn các panel KStruct muốn hiển thị trên ribbon:",
                Margin = new Thickness(0, 0, 0, 8)
            });
            foreach (RibbonPanel panel in managed.OrderBy(p => p.Name))
            {
                var check = new CheckBox
                {
                    Content = panel.Name,
                    IsChecked = panel.Visible,
                    Margin = new Thickness(2, 3, 2, 3)
                };
                root.Children.Add(check);
                choices.Add(new PanelChoice { Panel = panel, CheckBox = check });
            }

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var apply = new Button { Content = "Áp dụng", Width = 84, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            buttons.Children.Add(apply);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);

            var window = new Window
            {
                Title = "Quản lý panel KStruct",
                Width = 360,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false,
                Content = root
            };
            new WindowInteropHelper(window).Owner = data.Application.MainWindowHandle;
            apply.Click += (sender, args) => window.DialogResult = true;

            if (window.ShowDialog() != true) return Result.Cancelled;
            foreach (PanelChoice choice in choices)
                choice.Panel.Visible = choice.CheckBox.IsChecked == true;
            return Result.Succeeded;
        }
    }
}
