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
    public sealed class AssignRebarPartitionCommand : IExternalCommand
    {
        private sealed class RebarFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Rebar;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Rebar> rebars = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Rebar>().Distinct().ToList();
                if (rebars.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new RebarFilter(), "Chọn các thanh thép cần gán Partition");
                    rebars = picked.Select(r => doc.GetElement(r)).OfType<Rebar>().Distinct().ToList();
                }
                if (rebars.Count == 0) return Result.Cancelled;
                string partition = AskForPartition(data.Application.MainWindowHandle);
                if (partition == null) return Result.Cancelled;
                partition = partition.Trim();

                int changed = 0;
                var skipped = new List<string>();
                using (Transaction transaction = new Transaction(doc, "KStruct - Gán Partition cốt thép"))
                {
                    transaction.Start();
                    foreach (Rebar rebar in rebars)
                    {
                        Parameter parameter = rebar.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                        if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.String)
                        {
                            skipped.Add(rebar.Id.ToString());
                            continue;
                        }
                        if (string.Equals(parameter.AsString(), partition, StringComparison.Ordinal)) continue;
                        parameter.Set(partition);
                        changed++;
                    }
                    if (changed == 0)
                    {
                        transaction.RollBack();
                        TaskDialog.Show("KStruct - Gán Partition", "Không có thanh thép nào cần cập nhật được thay đổi.");
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi Partition.");
                }
                string report = "Đã gán Partition '" + partition + "' cho " + changed + " thanh.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " thanh không có tham số Partition ghi được.";
                TaskDialog.Show("KStruct - Gán Partition", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Gán Partition", ex.Message);
                return Result.Failed;
            }
        }

        private static string AskForPartition(IntPtr owner)
        {
            var window = new Window
            {
                Title = "Gán Partition cho Rebar",
                Width = 360,
                Height = 145,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock { Text = "Tên Partition:", Margin = new Thickness(0, 0, 0, 6) });
            var input = new System.Windows.Controls.TextBox { MinWidth = 310, MinHeight = 24 };
            root.Children.Add(input);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "Áp dụng", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) => { window.DialogResult = true; };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            return window.ShowDialog() == true ? input.Text : null;
        }
    }
}