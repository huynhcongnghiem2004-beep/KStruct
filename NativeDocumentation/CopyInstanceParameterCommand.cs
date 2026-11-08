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
    public sealed class CopyInstanceParameterCommand : IExternalCommand
    {
        private sealed class ParameterChoice
        {
            public string Name;
            public string Display;
            public StorageType Storage;
            public ParameterChoice(string name, string display, StorageType storage) { Name = name; Display = display; Storage = storage; }
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                Reference sourceReference = ui.Selection.PickObject(ObjectType.Element, "Chọn phần tử nguồn");
                Element source = doc.GetElement(sourceReference);
                if (source == null) return Result.Cancelled;
                IList<Reference> targetReferences = ui.Selection.PickObjects(ObjectType.Element, "Chọn các phần tử đích");
                List<Element> targets = targetReferences.Select(reference => doc.GetElement(reference))
                    .Where(element => element != null && element.Id != source.Id).Distinct().ToList();
                if (targets.Count == 0) return Result.Cancelled;

                List<ParameterChoice> choices = source.Parameters.Cast<Parameter>()
                    .Where(parameter => parameter.Definition != null && !parameter.IsReadOnly && parameter.HasValue && parameter.StorageType != StorageType.None)
                    .GroupBy(parameter => parameter.Definition.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .OrderBy(parameter => parameter.Definition.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(parameter => new ParameterChoice(parameter.Definition.Name,
                        parameter.Definition.Name + "  |  " + parameter.StorageType, parameter.StorageType)).ToList();
                if (choices.Count == 0) throw new InvalidOperationException("Phần tử nguồn không có tham số instance ghi được.");
                ParameterChoice choice = ChooseParameter(data.Application.MainWindowHandle, choices);
                if (choice == null) return Result.Cancelled;
                Parameter sourceParameter = source.Parameters.Cast<Parameter>().FirstOrDefault(parameter =>
                    parameter.Definition != null && !parameter.IsReadOnly && parameter.StorageType == choice.Storage &&
                    string.Equals(parameter.Definition.Name, choice.Name, StringComparison.OrdinalIgnoreCase));
                if (sourceParameter == null) return Result.Cancelled;

                int copied = 0;
                var skipped = new List<string>();
                using (Transaction transaction = new Transaction(doc, "KStruct - Sao chép tham số"))
                {
                    transaction.Start();
                    foreach (Element target in targets)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                Parameter destination = target.Parameters.Cast<Parameter>().FirstOrDefault(parameter =>
                                    parameter.Definition != null && string.Equals(parameter.Definition.Name, choice.Name, StringComparison.OrdinalIgnoreCase));
                                if (destination == null || destination.IsReadOnly || destination.StorageType != sourceParameter.StorageType)
                                    throw new InvalidOperationException("Không có tham số ghi được cùng kiểu.");
                                CopyValue(sourceParameter, destination);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận giá trị.");
                                copied++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add(target.Id + ": " + ex.Message);
                            }
                        }
                    }
                    if (copied == 0)
                    {
                        transaction.RollBack();
                        TaskDialog.Show("KStruct - Sao chép tham số", "Không có phần tử đích tương thích với tham số đã chọn.");
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi tham số.");
                }
                string report = "Đã sao chép '" + choice.Name + "' sang " + copied + "/" + targets.Count + " phần tử.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " phần tử không tương thích.";
                TaskDialog.Show("KStruct - Sao chép tham số", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Sao chép tham số", ex.Message);
                return Result.Failed;
            }
        }

        private static void CopyValue(Parameter source, Parameter destination)
        {
            switch (source.StorageType)
            {
                case StorageType.String: destination.Set(source.AsString() ?? string.Empty); break;
                case StorageType.Integer: destination.Set(source.AsInteger()); break;
                case StorageType.Double: destination.Set(source.AsDouble()); break;
                case StorageType.ElementId: destination.Set(source.AsElementId()); break;
                default: throw new InvalidOperationException("Kiểu tham số không hỗ trợ.");
            }
        }

        private static ParameterChoice ChooseParameter(IntPtr owner, IList<ParameterChoice> choices)
        {
            var window = new Window
            {
                Title = "Chọn tham số cần sao chép",
                Width = 420,
                Height = 170,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock { Text = "Tham số instance của phần tử nguồn:", Margin = new Thickness(0, 0, 0, 6) });
            var combo = new System.Windows.Controls.ComboBox { MinWidth = 370, MinHeight = 26, DisplayMemberPath = "Display", ItemsSource = choices };
            combo.SelectedIndex = 0;
            root.Children.Add(combo);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "Sao chép", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) => { window.DialogResult = true; };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            return window.ShowDialog() == true ? combo.SelectedItem as ParameterChoice : null;
        }
    }
}