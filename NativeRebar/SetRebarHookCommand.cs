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
    public sealed class SetRebarHookCommand : IExternalCommand
    {
        private sealed class RebarFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Rebar;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class HookChoice
        {
            public RebarHookType Type { get; private set; }
            public string Name { get; private set; }
            public HookChoice(RebarHookType type) { Type = type; Name = type.Name; }
        }

        private sealed class HookSettings
        {
            public RebarHookType Type;
            public int End;
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
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new RebarFilter(), "Chọn các thanh Rebar cần đổi móc");
                    rebars = picked.Select(r => doc.GetElement(r)).OfType<Rebar>().Distinct().ToList();
                }
                if (rebars.Count == 0) return Result.Cancelled;
                List<RebarHookType> types = new FilteredElementCollector(doc).OfClass(typeof(RebarHookType))
                    .Cast<RebarHookType>().OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (types.Count == 0)
                {
                    TaskDialog.Show("KStruct - Móc cốt thép", "Project chưa có RebarHookType. Hãy nạp hoặc tạo loại móc trước.");
                    return Result.Cancelled;
                }
                HookSettings settings = ChooseSettings(data.Application.MainWindowHandle, types);
                if (settings == null) return Result.Cancelled;

                int changed = 0;
                var skipped = new List<string>();
                using (Transaction transaction = new Transaction(doc, "KStruct - Đổi móc cốt thép"))
                {
                    transaction.Start();
                    foreach (Rebar rebar in rebars)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                if (!rebar.CanUseHookType(settings.Type.Id))
                                    throw new InvalidOperationException("Loại móc không tương thích với hình dạng/thép này.");
                                rebar.SetHookTypeId(settings.End, settings.Type.Id);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận loại móc.");
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
                        TaskDialog.Show("KStruct - Móc cốt thép", "Không thanh nào nhận được loại móc đã chọn.");
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi.");
                }
                string endName = settings.End == 0 ? "đầu bắt đầu" : "đầu kết thúc";
                string report = "Đã gán móc '" + settings.Type.Name + "' cho " + changed + "/" + rebars.Count + " thanh (" + endName + ").";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " thanh không tương thích.";
                TaskDialog.Show("KStruct - Móc cốt thép", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Móc cốt thép", ex.Message);
                return Result.Failed;
            }
        }

        private static HookSettings ChooseSettings(IntPtr owner, IList<RebarHookType> types)
        {
            var choices = types.Select(t => new HookChoice(t)).ToList();
            var window = new Window
            {
                Title = "Đổi móc cốt thép",
                Width = 390,
                Height = 215,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock { Text = "Loại móc:", Margin = new Thickness(0, 0, 0, 4) });
            var hookCombo = new System.Windows.Controls.ComboBox { MinWidth = 350, MinHeight = 26, DisplayMemberPath = "Name", ItemsSource = choices };
            hookCombo.SelectedIndex = 0;
            root.Children.Add(hookCombo);
            root.Children.Add(new TextBlock { Text = "Đầu thanh:", Margin = new Thickness(0, 10, 0, 4) });
            var endCombo = new System.Windows.Controls.ComboBox { MinWidth = 350, MinHeight = 26, ItemsSource = new[] { "Đầu bắt đầu", "Đầu kết thúc" } };
            endCombo.SelectedIndex = 0;
            root.Children.Add(endCombo);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "Áp dụng", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) => { window.DialogResult = true; };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            if (window.ShowDialog() != true) return null;
            HookChoice choice = hookCombo.SelectedItem as HookChoice;
            return choice == null ? null : new HookSettings { Type = choice.Type, End = endCombo.SelectedIndex };
        }
    }
}
