using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class TagSelectedRebarCommand : IExternalCommand
    {
        private sealed class RebarFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Rebar;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class TagTypeChoice
        {
            public ElementType Type { get; private set; }
            public string Display { get; private set; }
            public TagTypeChoice(ElementType type) { Type = type; Display = type.Name; }
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            View view = doc.ActiveView;
            try
            {
                List<Rebar> rebars = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Rebar>().Distinct().ToList();
                if (rebars.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new RebarFilter(), "Chọn các thanh thép cần gắn tag");
                    rebars = picked.Select(r => doc.GetElement(r)).OfType<Rebar>().Distinct().ToList();
                }
                if (rebars.Count == 0) return Result.Cancelled;

                List<ElementType> tagTypes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_RebarTags).WhereElementIsElementType()
                    .Cast<ElementType>().OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (tagTypes.Count == 0)
                {
                    TaskDialog.Show("KStruct - Tag cốt thép", "Project chưa có family/type tag cho Rebar. Hãy nạp Rebar Tag family trước.");
                    return Result.Cancelled;
                }
                ElementType tagType = ChooseTagType(data.Application.MainWindowHandle, tagTypes);
                if (tagType == null) return Result.Cancelled;

                int created = 0;
                var skipped = new List<string>();
                using (Transaction transaction = new Transaction(doc, "KStruct - Gắn tag cốt thép"))
                {
                    transaction.Start();
                    foreach (Rebar rebar in rebars)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                BoundingBoxXYZ box = rebar.get_BoundingBox(view) ?? rebar.get_BoundingBox(null);
                                if (box == null) throw new InvalidOperationException("Không xác định được vị trí thanh trong view hiện tại.");
                                XYZ center = (box.Min + box.Max) * 0.5;
                                XYZ head = center + view.RightDirection * 1.0 + view.UpDirection * 0.5;
                                IndependentTag tag = IndependentTag.Create(doc, tagType.Id, view.Id,
                                    new Reference(rebar), true, TagOrientation.Horizontal, head);
                                if (tag == null) throw new InvalidOperationException("Revit không tạo được tag.");
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận tag.");
                                created++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add(rebar.Id + ": " + ex.Message);
                            }
                        }
                    }
                    if (created == 0)
                    {
                        transaction.RollBack();
                        TaskDialog.Show("KStruct - Tag cốt thép", "Không tạo được tag nào trong view hiện tại.");
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi.");
                }
                string report = "Đã tạo " + created + "/" + rebars.Count + " tag cốt thép.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " thanh không gắn được trong view hiện tại.";
                TaskDialog.Show("KStruct - Tag cốt thép", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Tag cốt thép", ex.Message);
                return Result.Failed;
            }
        }

        private static ElementType ChooseTagType(IntPtr owner, IList<ElementType> types)
        {
            List<TagTypeChoice> choices = types.Select(t => new TagTypeChoice(t)).ToList();
            var window = new Window
            {
                Title = "Chọn Rebar Tag",
                Width = 420,
                Height = 170,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock { Text = "Loại tag sẽ gắn:", Margin = new Thickness(0, 0, 0, 6) });
            var combo = new System.Windows.Controls.ComboBox { MinWidth = 370, MinHeight = 26, DisplayMemberPath = "Display", ItemsSource = choices };
            combo.SelectedIndex = 0;
            root.Children.Add(combo);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "Gắn tag", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) => { window.DialogResult = true; };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            return window.ShowDialog() == true ? (combo.SelectedItem as TagTypeChoice)?.Type : null;
        }
    }
}
