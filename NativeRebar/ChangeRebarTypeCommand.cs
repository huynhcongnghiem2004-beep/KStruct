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
    public sealed class ChangeRebarTypeCommand : IExternalCommand
    {
        private sealed class RebarFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Rebar;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class TypeChoice
        {
            public RebarBarType Type { get; private set; }
            public string Display { get; private set; }
            public TypeChoice(RebarBarType type, string display) { Type = type; Display = display; }
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
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new RebarFilter(), "Chọn các thanh thép cần đổi loại");
                    rebars = picked.Select(r => doc.GetElement(r)).OfType<Rebar>().Distinct().ToList();
                }
                if (rebars.Count == 0) return Result.Cancelled;

                List<RebarBarType> types = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType))
                    .Cast<RebarBarType>().OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (types.Count == 0) throw new InvalidOperationException("Project chưa có RebarBarType.");
                RebarBarType target = ChooseType(data.Application.MainWindowHandle, types);
                if (target == null) return Result.Cancelled;

                Parameter diameterParameter = target.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER);
                double diameterMm = diameterParameter != null && diameterParameter.HasValue
                    ? UnitUtils.ConvertFromInternalUnits(diameterParameter.AsDouble(), UnitTypeId.Millimeters) : 0;
                TaskDialogResult confirmation = TaskDialog.Show("KStruct - Đổi loại cốt thép",
                    "Đổi " + rebars.Count + " thanh sang '" + target.Name + "' (Ø" + diameterMm.ToString("0.##") + " mm)?\n" +
                    "Kiểm tra lại hình dạng, móc và chiều dài sau khi đổi.",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);
                if (confirmation != TaskDialogResult.Yes) return Result.Cancelled;

                int changed = 0;
                var skipped = new List<string>();
                using (Transaction transaction = new Transaction(doc, "KStruct - Đổi loại cốt thép"))
                {
                    transaction.Start();
                    foreach (Rebar rebar in rebars)
                    {
                        if (rebar.GetTypeId() == target.Id) continue;
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                rebar.ChangeTypeId(target.Id);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận loại thép mới.");
                                changed++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add(rebar.Id + ": " + ex.Message);
                            }
                        }
                    }
                    if (changed == 0)
                    {
                        transaction.RollBack();
                        TaskDialog.Show("KStruct - Đổi loại cốt thép", "Không có thanh nào cần đổi hoặc Revit từ chối loại mới.");
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi loại thép.");
                }
                string report = "Đã đổi loại cho " + changed + "/" + rebars.Count + " thanh.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " thanh không tương thích.";
                TaskDialog.Show("KStruct - Đổi loại cốt thép", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Đổi loại cốt thép", ex.Message);
                return Result.Failed;
            }
        }

        private static RebarBarType ChooseType(IntPtr owner, IList<RebarBarType> types)
        {
            var choices = types.Select(type =>
            {
                Parameter p = type.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER);
                double mm = p != null && p.HasValue ? UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Millimeters) : 0;
                return new TypeChoice(type, type.Name + "  |  Ø" + mm.ToString("0.##") + " mm");
            }).ToList();
            var window = new Window
            {
                Title = "Chọn loại cốt thép",
                Width = 420,
                Height = 170,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock { Text = "RebarBarType mới:", Margin = new Thickness(0, 0, 0, 6) });
            var combo = new System.Windows.Controls.ComboBox { MinWidth = 370, MinHeight = 26, DisplayMemberPath = "Display", ItemsSource = choices };
            combo.SelectedIndex = 0;
            root.Children.Add(combo);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "Chọn", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) => { window.DialogResult = true; };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            return window.ShowDialog() == true ? (combo.SelectedItem as TypeChoice)?.Type : null;
        }
    }
}