using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfPanel = System.Windows.Controls.Panel;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ReinforceStripFootingsCommand : IExternalCommand
    {
        private sealed class FootingFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is WallFoundation;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class BarChoice
        {
            public RebarBarType Type { get; private set; }
            public string Display { get; private set; }
            public BarChoice(RebarBarType type)
            {
                Type = type;
                Display = type.Name + " | Ø" + UnitUtils.ConvertFromInternalUnits(type.BarNominalDiameter, UnitTypeId.Millimeters).ToString("0.#") + " mm";
            }
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<WallFoundation> footings = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<WallFoundation>().Distinct().ToList();
                if (footings.Count == 0)
                    footings = ui.Selection.PickObjects(ObjectType.Element, new FootingFilter(), "Chọn một hoặc nhiều Wall Foundation làm móng băng")
                        .Select(reference => doc.GetElement(reference)).OfType<WallFoundation>().Distinct().ToList();
                if (footings.Count == 0) return Result.Cancelled;
                List<RebarBarType> types = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
                    .OrderBy(type => type.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (types.Count == 0) throw new InvalidOperationException("Project chưa có RebarBarType.");
                StripFootingOptions chosen = ChooseOptions(data.Application.MainWindowHandle, types);
                if (chosen == null) return Result.Cancelled;

                var skipped = new List<string>();
                int completed = 0, created = 0;
                using (Transaction transaction = new Transaction(doc, "KStruct - Cốt thép móng băng"))
                {
                    transaction.Start();
                    foreach (WallFoundation footing in footings)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                Wall wall = doc.GetElement(footing.WallId) as Wall;
                                Line axisLine = (wall?.Location as LocationCurve)?.Curve as Line;
                                if (wall == null || axisLine == null)
                                    throw new InvalidOperationException("Wall Foundation phải gắn với tường có tim thẳng.");
                                XYZ axis = axisLine.Direction.Normalize();
                                XYZ normal = wall.Orientation.Normalize();
                                if (Math.Abs(axis.Z) > 1e-3 || Math.Abs(normal.Z) > 1e-3 || Math.Abs(axis.DotProduct(normal)) > 1e-3)
                                    throw new InvalidOperationException("Chỉ hỗ trợ móng dưới tường đứng, thẳng.");
                                var settings = new ReinforceRetainingWallCommand.Options
                                {
                                    CoverTopBottomMm = chosen.CoverMm,
                                    FootingTypeAlong = chosen.AlongType,
                                    FootingTypeCross = chosen.CrossType,
                                    FootingSpacingAlongMm = chosen.AlongSpacingMm,
                                    FootingSpacingCrossMm = chosen.CrossSpacingMm
                                };
                                if (chosen.ReplaceExisting) RemoveGeneratedFootingRebar(doc, footing.Id);
                                int count = ReinforceRetainingWallCommand.ReinforceFooting(doc, footing, settings, normal, axis);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận cốt thép móng băng.");
                                completed++;
                                created += count;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Móng " + footing.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (completed == 0)
                    {
                        transaction.RollBack();
                        TaskDialog.Show("KStruct - Cốt thép móng băng", "Không gia cường được móng nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi cốt thép móng băng.");
                }
                string report = "Đã tạo " + created + " thanh thép cho " + completed + "/" + footings.Count + " Wall Foundation, gồm lưới hai phương ở lớp trên và dưới.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " móng:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Cốt thép móng băng", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Cốt thép móng băng", ex.Message);
                return Result.Failed;
            }
        }

        private sealed class StripFootingOptions
        {
            public RebarBarType AlongType, CrossType;
            public double CoverMm, AlongSpacingMm, CrossSpacingMm;
            public bool ReplaceExisting;
        }

        private static int RemoveGeneratedFootingRebar(Document doc, ElementId footingId)
        {
            string prefix = "KSTR_RW_" + footingId.Value + "_F_";
            List<ElementId> ids = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
                .Where(bar =>
                {
                    Parameter partition = bar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
                    return partition != null && partition.StorageType == StorageType.String &&
                        (partition.AsString() ?? "").StartsWith(prefix, StringComparison.Ordinal);
                })
                .Select(bar => bar.Id).ToList();
            if (ids.Count > 0) doc.Delete(ids);
            return ids.Count;
        }

        private static StripFootingOptions ChooseOptions(IntPtr owner, IList<RebarBarType> types)
        {
            var choices = types.Select(type => new BarChoice(type)).ToList();
            var window = new Window
            {
                Title = "Cốt thép móng băng - KStruct", Width = 430, Height = 320,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            WpfComboBox along = AddType(root, "Thép dọc móng:", choices, 0);
            WpfComboBox cross = AddType(root, "Thép ngang móng:", choices, Math.Min(1, choices.Count - 1));
            WpfTextBox cover = AddInput(root, "Lớp bảo vệ (mm):", "50");
            WpfTextBox alongSpacing = AddInput(root, "Bước thép dọc (mm):", "150");
            WpfTextBox crossSpacing = AddInput(root, "Bước thép ngang (mm):", "150");
            var replace = new CheckBox { Content = "Thay thép móng băng KStruct đã tạo trước đó", IsChecked = true, Margin = new Thickness(0, 3, 0, 2) };
            root.Children.Add(replace);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var ok = new Button { Content = "Tạo thép", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) =>
            {
                double c, sa, sc;
                if (along.SelectedItem == null || cross.SelectedItem == null ||
                    !double.TryParse(cover.Text, out c) || c <= 0 ||
                    !double.TryParse(alongSpacing.Text, out sa) || sa <= 0 ||
                    !double.TryParse(crossSpacing.Text, out sc) || sc <= 0)
                {
                    TaskDialog.Show("KStruct - Cốt thép móng băng", "Chọn loại thép và nhập cover/bước thép lớn hơn 0.");
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
            return new StripFootingOptions
            {
                AlongType = ((BarChoice)along.SelectedItem).Type,
                CrossType = ((BarChoice)cross.SelectedItem).Type,
                CoverMm = double.Parse(cover.Text),
                AlongSpacingMm = double.Parse(alongSpacing.Text),
                CrossSpacingMm = double.Parse(crossSpacing.Text),
                ReplaceExisting = replace.IsChecked == true
            };
        }

        private static WpfComboBox AddType(WpfPanel root, string label, IList<BarChoice> choices, int selected)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2) });
            var combo = new WpfComboBox { MinWidth = 390, MinHeight = 24, DisplayMemberPath = "Display", ItemsSource = choices, SelectedIndex = selected, Margin = new Thickness(0, 0, 0, 5) };
            root.Children.Add(combo);
            return combo;
        }

        private static WpfTextBox AddInput(WpfPanel root, string label, string value)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 3, 0, 2) });
            var box = new WpfTextBox { MinWidth = 390, MinHeight = 22, Text = value };
            root.Children.Add(box);
            return box;
        }
    }
}
