using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ArrayElements3DCommand : IExternalCommand
    {
        private sealed class ArrayOptions
        {
            public int Copies;
            public double Xmm, Ymm, Zmm;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<ElementId> ids = ui.Selection.GetElementIds().Where(id => doc.GetElement(id) != null && !(doc.GetElement(id) is ElementType)).Distinct().ToList();
                if (ids.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, "Chọn các phần tử cần sao chép theo mảng 3D");
                    ids = picked.Select(r => r.ElementId).Distinct().ToList();
                }
                if (ids.Count == 0) return Result.Cancelled;
                ArrayOptions options = AskOptions(data.Application.MainWindowHandle);
                if (options == null) return Result.Cancelled;
                XYZ step = new XYZ(
                    UnitUtils.ConvertToInternalUnits(options.Xmm, UnitTypeId.Millimeters),
                    UnitUtils.ConvertToInternalUnits(options.Ymm, UnitTypeId.Millimeters),
                    UnitUtils.ConvertToInternalUnits(options.Zmm, UnitTypeId.Millimeters));
                if (step.GetLength() < doc.Application.ShortCurveTolerance)
                {
                    TaskDialog.Show("KStruct - Mảng 3D", "Vector dịch chuyển phải khác 0.");
                    return Result.Cancelled;
                }

                int successfulCopies = 0;
                var skippedSteps = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Mảng sao chép 3D"))
                {
                    tx.Start();
                    for (int index = 1; index <= options.Copies; index++)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                XYZ offset = step * index;
                                ICollection<ElementId> copied = ElementTransformUtils.CopyElements(doc, ids, offset);
                                if (copied == null || copied.Count != ids.Count)
                                    throw new InvalidOperationException("Revit chỉ sao chép được " + (copied == null ? 0 : copied.Count) + "/" + ids.Count + " phần tử.");
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận bản sao.");
                                successfulCopies++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skippedSteps.Add("Bước " + index + ": " + ex.Message);
                            }
                        }
                    }
                    if (successfulCopies == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Mảng 3D", "Không sao chép được bước nào.\n" + string.Join("\n", skippedSteps.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các bản sao.");
                }

                string report = "Đã tạo " + successfulCopies + " bước × " + ids.Count + " phần tử.";
                if (skippedSteps.Count > 0) report += "\nBỏ qua " + skippedSteps.Count + " bước:\n" + string.Join("\n", skippedSteps.Take(8));
                TaskDialog.Show("KStruct - Mảng 3D", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Mảng 3D", ex.Message);
                return Result.Failed;
            }
        }

        private static ArrayOptions AskOptions(IntPtr owner)
        {
            var window = new Window
            {
                Title = "Mảng sao chép 3D",
                Width = 350,
                Height = 295,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            var countBox = AddRow(root, "Số bước sao chép:", "2");
            var xBox = AddRow(root, "Khoảng dịch X (mm):", "0");
            var yBox = AddRow(root, "Khoảng dịch Y (mm):", "0");
            var zBox = AddRow(root, "Khoảng dịch Z (mm):", "3000");
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "Tạo mảng", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) =>
            {
                int count;
                double x, y, z;
                if (!int.TryParse(countBox.Text, out count) || count < 1 || count > 500 ||
                    !double.TryParse(xBox.Text, out x) || !double.TryParse(yBox.Text, out y) || !double.TryParse(zBox.Text, out z) ||
                    Math.Abs(x) + Math.Abs(y) + Math.Abs(z) < 1e-9)
                {
                    TaskDialog.Show("KStruct - Mảng 3D", "Số bước phải từ 1 đến 500 và vector dịch chuyển phải khác 0.");
                    return;
                }
                window.DialogResult = true;
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            if (window.ShowDialog() != true) return null;
            return new ArrayOptions
            {
                Copies = int.Parse(countBox.Text),
                Xmm = double.Parse(xBox.Text),
                Ymm = double.Parse(yBox.Text),
                Zmm = double.Parse(zBox.Text)
            };
        }

        private static System.Windows.Controls.TextBox AddRow(System.Windows.Controls.Panel root, string label, string value)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 5, 0, 3) });
            var box = new System.Windows.Controls.TextBox { MinWidth = 310, MinHeight = 23, Text = value };
            root.Children.Add(box);
            return box;
        }
    }
}
