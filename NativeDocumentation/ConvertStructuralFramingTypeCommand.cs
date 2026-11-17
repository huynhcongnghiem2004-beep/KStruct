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
    public sealed class ConvertStructuralFramingTypeCommand : IExternalCommand
    {
        private sealed class BeamFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) =>
                element?.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class TypeChoice
        {
            public FamilySymbol Symbol { get; }
            public string Label { get; }
            public TypeChoice(FamilySymbol symbol)
            {
                Symbol = symbol;
                Label = (symbol.Family?.Name ?? "") + " : " + symbol.Name;
            }
            public override string ToString() => Label;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<FamilyInstance> beams = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<FamilyInstance>()
                    .Where(IsBeam).Distinct().ToList();
                if (beams.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new BeamFilter(),
                        "Chọn các dầm Structural Framing cần đổi loại");
                    beams = picked.Select(r => doc.GetElement(r)).OfType<FamilyInstance>().Where(IsBeam).Distinct().ToList();
                }
                if (beams.Count == 0) return Result.Cancelled;
                List<FamilySymbol> symbols = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                    .OrderBy(s => s.Family?.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (symbols.Count == 0) throw new InvalidOperationException("Project chưa có Structural Framing Family Type.");
                FamilySymbol targetType = ChooseType(data.Application.MainWindowHandle, symbols);
                if (targetType == null) return Result.Cancelled;

                int changed = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Đổi loại dầm"))
                {
                    tx.Start();
                    foreach (FamilyInstance beam in beams)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                if (beam.GetTypeId() != targetType.Id)
                                    beam.ChangeTypeId(targetType.Id);
                                if (beam.GetTypeId() != targetType.Id)
                                    throw new InvalidOperationException("Revit không áp dụng được Family Type đã chọn.");
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không hoàn tất đổi loại dầm.");
                                changed++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Dầm " + beam.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (changed == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Đổi loại dầm", "Không đổi được loại cho dầm nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận đổi loại dầm.");
                }
                string report = "Đã đổi " + changed + "/" + beams.Count + " dầm sang " + targetType.Family.Name + " : " + targetType.Name + ".";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " dầm:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Đổi loại dầm", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Đổi loại dầm", ex.Message);
                return Result.Failed;
            }
        }

        private static bool IsBeam(FamilyInstance instance) =>
            instance.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;

        private static FamilySymbol ChooseType(IntPtr owner, IList<FamilySymbol> symbols)
        {
            var choices = symbols.Select(s => new TypeChoice(s)).ToList();
            var window = new Window
            {
                Title = "Đổi loại dầm - KStruct",
                Width = 480,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock
            {
                Text = "Chọn Structural Framing Type mới. Revit giữ đường trục và các tham số instance mà type mới hỗ trợ.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            var combo = new System.Windows.Controls.ComboBox { ItemsSource = choices, SelectedIndex = 0, MinWidth = 440, DisplayMemberPath = "Label" };
            root.Children.Add(combo);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "Đổi loại", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (s, e) => { if (combo.SelectedItem != null) window.DialogResult = true; };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            return window.ShowDialog() == true ? (combo.SelectedItem as TypeChoice)?.Symbol : null;
        }
    }
}
