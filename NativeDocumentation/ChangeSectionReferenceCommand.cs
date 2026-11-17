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
    public sealed class ChangeSectionReferenceCommand : IExternalCommand
    {
        private sealed class ReferenceFilter : ISelectionFilter
        {
            private readonly Document _doc;
            public ReferenceFilter(Document doc) { _doc = doc; }

            public bool AllowElement(Element element)
            {
                ElementId referencedId;
                return element != null && TryGetReferencedView(_doc, element.Id, out referencedId);
            }

            public bool AllowReference(Reference reference, XYZ position) { return false; }
        }

        private sealed class ViewChoice
        {
            public View View { get; private set; }
            public bool IsCurrent { get; private set; }
            public string Display { get { return View.Name + " | " + View.ViewType + " | ID " + View.Id.Value + (IsCurrent ? " [hiện tại]" : ""); } }
            public ViewChoice(View view, bool isCurrent) { View = view; IsCurrent = isCurrent; }
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<ElementId> referenceIds = ui.Selection.GetElementIds()
                    .Where(id => { ElementId target; return TryGetReferencedView(doc, id, out target); })
                    .Distinct().ToList();
                if (referenceIds.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new ReferenceFilter(doc),
                        "Chọn một hoặc nhiều ký hiệu mặt cắt tham chiếu cần đổi view đích");
                    referenceIds = picked.Select(x => x.ElementId).Distinct().ToList();
                }
                if (referenceIds.Count == 0) return Result.Cancelled;

                HashSet<ElementId> oldTargets = new HashSet<ElementId>(referenceIds
                    .Select(id => ReferenceableViewUtils.GetReferencedViewId(doc, id)));
                List<ViewChoice> choices = new FilteredElementCollector(doc).OfClass(typeof(View))
                    .Cast<View>()
                    .Where(v => !v.IsTemplate && (v.ViewType == ViewType.Section || v.ViewType == ViewType.Detail || v.ViewType == ViewType.DraftingView))
                    .Where(v => v.Id != ui.ActiveView.Id)
                    .Where(v => v.ViewType == ViewType.DraftingView || v.CropBoxActive)
                    .OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(v => new ViewChoice(v, oldTargets.Contains(v.Id))).ToList();
                if (choices.Count == 0) throw new InvalidOperationException("Không có section/detail/drafting view đủ điều kiện để tham chiếu.");

                ViewChoice target = ChooseView(data.Application.MainWindowHandle, choices, oldTargets);
                if (target == null) return Result.Cancelled;
                using (Transaction tx = new Transaction(doc, "KStruct - Đổi view tham chiếu mặt cắt"))
                {
                    tx.Start();
                    foreach (ElementId referenceId in referenceIds)
                        ReferenceableViewUtils.ChangeReferencedView(doc, referenceId, target.View.Id);
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi view tham chiếu.");
                }
                TaskDialog.Show("KStruct - Tham chiếu mặt cắt",
                    "Đã đổi " + referenceIds.Count + " ký hiệu sang view: " + target.View.Name);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Đổi tham chiếu mặt cắt", ex.Message);
                return Result.Failed;
            }
        }

        private static bool TryGetReferencedView(Document doc, ElementId referenceId, out ElementId viewId)
        {
            viewId = ElementId.InvalidElementId;
            try
            {
                viewId = ReferenceableViewUtils.GetReferencedViewId(doc, referenceId);
                return viewId != null && viewId != ElementId.InvalidElementId;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        private static ViewChoice ChooseView(IntPtr owner, IList<ViewChoice> views, HashSet<ElementId> oldTargets)
        {
            var window = new Window
            {
                Title = "Đổi view đích của ký hiệu tham chiếu - KStruct",
                Width = 560,
                Height = 430,
                MinWidth = 440,
                MinHeight = 300,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false
            };
            new WindowInteropHelper(window).Owner = owner;
            var root = new DockPanel { Margin = new Thickness(12) };
            var description = new TextBlock
            {
                Text = "Chọn view đích mới cho " + oldTargets.Count + " ký hiệu đang chọn. Các view đang được tham chiếu được đánh dấu [hiện tại].",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            DockPanel.SetDock(description, Dock.Top);
            root.Children.Add(description);
            var list = new ListBox { ItemsSource = views };
            list.DisplayMemberPath = "Display";
            root.Children.Add(list);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom);
            var ok = new Button { Content = "Đổi view", MinWidth = 85, Padding = new Thickness(10, 4, 10, 4), IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 75, Padding = new Thickness(10, 4, 10, 4), IsCancel = true };
            ok.Click += (s, e) =>
            {
                if (list.SelectedItem == null) { TaskDialog.Show("KStruct", "Hãy chọn view đích."); return; }
                window.DialogResult = true;
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            if (window.ShowDialog() != true) return null;
            return list.SelectedItem as ViewChoice;
        }
    }
}
