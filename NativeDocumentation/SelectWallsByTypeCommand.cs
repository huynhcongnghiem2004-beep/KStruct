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
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class SelectWallsByTypeCommand : IExternalCommand
    {
        private sealed class WallFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Wall;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class ScopeChoice
        {
            public string Label { get; }
            public bool CurrentView { get; }
            public ScopeChoice(string label, bool currentView) { Label = label; CurrentView = currentView; }
            public override string ToString() => Label;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                Wall sample = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Wall>().FirstOrDefault();
                if (sample == null)
                {
                    Reference picked = ui.Selection.PickObject(ObjectType.Element, new WallFilter(),
                        "Chọn tường mẫu để chọn các tường cùng WallType");
                    sample = doc.GetElement(picked) as Wall;
                }
                if (sample == null) return Result.Cancelled;
                ScopeChoice scope = ChooseScope(data.Application.MainWindowHandle);
                if (scope == null) return Result.Cancelled;

                FilteredElementCollector collector = scope.CurrentView
                    ? new FilteredElementCollector(doc, ui.ActiveView.Id)
                    : new FilteredElementCollector(doc);
                List<ElementId> matches = collector.OfClass(typeof(Wall)).WhereElementIsNotElementType()
                    .Cast<Wall>().Where(wall => wall.GetTypeId() == sample.GetTypeId())
                    .Select(wall => wall.Id).ToList();
                if (matches.Count == 0)
                {
                    TaskDialog.Show("KStruct - Chọn tường", "Không tìm thấy tường nào cùng WallType với tường mẫu.");
                    return Result.Cancelled;
                }
                ui.Selection.SetElementIds(matches);
                ui.ShowElements(matches);
                TaskDialog.Show("KStruct - Chọn tường",
                    "Đã chọn " + matches.Count + " tường cùng WallType '" +
                    (doc.GetElement(sample.GetTypeId()) as WallType)?.Name + "'" +
                    (scope.CurrentView ? " trong view hiện tại." : " trong toàn project."));
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Chọn tường", ex.Message);
                return Result.Failed;
            }
        }

        private static ScopeChoice ChooseScope(IntPtr owner)
        {
            var scopes = new List<ScopeChoice>
            {
                new ScopeChoice("View hiện tại", true),
                new ScopeChoice("Toàn project", false)
            };
            var window = new Window
            {
                Title = "Chọn tường cùng WallType - KStruct",
                Width = 360,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock
            {
                Text = "Chọn phạm vi để tìm mọi Wall có cùng WallType với tường mẫu.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            var combo = new System.Windows.Controls.ComboBox { ItemsSource = scopes, SelectedIndex = 0, MinWidth = 320 };
            root.Children.Add(combo);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var select = new System.Windows.Controls.Button { Content = "Chọn", Width = 76, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Hủy", Width = 72, IsCancel = true };
            select.Click += (s, e) => { if (combo.SelectedItem != null) window.DialogResult = true; };
            buttons.Children.Add(select);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            return window.ShowDialog() == true ? combo.SelectedItem as ScopeChoice : null;
        }
    }
}
