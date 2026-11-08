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
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ManageStructuralJoinsCommand : IExternalCommand
    {
        private enum JoinAction { Toggle, Join, Unjoin, SwitchOrder }

        private sealed class StructuralFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => IsStructural(element);
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class ActionChoice
        {
            public JoinAction Action { get; }
            public string Label { get; }
            public ActionChoice(JoinAction action, string label) { Action = action; Label = label; }
            public override string ToString() => Label;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Element> selected = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .Where(IsStructural).Distinct().ToList();
                if (selected.Count < 2)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new StructuralFilter(),
                        "Chọn ít nhất hai cấu kiện kết cấu để Join/Unjoin hình học");
                    selected = picked.Select(reference => doc.GetElement(reference)).Where(IsStructural).Distinct().ToList();
                }
                if (selected.Count < 2) return Result.Cancelled;
                JoinAction? action = ChooseAction(data.Application.MainWindowHandle, selected.Count);
                if (!action.HasValue) return Result.Cancelled;

                var pairs = new List<Tuple<Element, Element>>();
                for (int i = 0; i < selected.Count; i++)
                    for (int j = i + 1; j < selected.Count; j++) pairs.Add(Tuple.Create(selected[i], selected[j]));
                if (pairs.Count > 1 && TaskDialog.Show("Xác nhận Join/Unjoin",
                    "Số cấu kiện: " + selected.Count + "\nSố cặp cần rà: " + pairs.Count +
                    "\n\nRevit chỉ nối được các phần tử có hình học tương thích/giao nhau. Tiếp tục?",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No) != TaskDialogResult.Yes)
                    return Result.Cancelled;

                int joined = 0, unjoined = 0, switched = 0, unchanged = 0, failed = 0;
                using (Transaction transaction = new Transaction(doc, "KStruct - Quản lý Join hình học"))
                {
                    transaction.Start();
                    foreach (Tuple<Element, Element> pair in pairs)
                    {
                        bool wasJoined;
                        try { wasJoined = JoinGeometryUtils.AreElementsJoined(doc, pair.Item1, pair.Item2); }
                        catch { failed++; continue; }
                        JoinAction effective = action.Value == JoinAction.Toggle
                            ? (wasJoined ? JoinAction.Unjoin : JoinAction.Join) : action.Value;
                        if ((effective == JoinAction.Join && wasJoined) ||
                            (effective == JoinAction.Unjoin && !wasJoined) ||
                            (effective == JoinAction.SwitchOrder && !wasJoined))
                        {
                            unchanged++;
                            continue;
                        }
                        if (effective == JoinAction.Join && !BoundingBoxesIntersect(pair.Item1, pair.Item2,
                            UnitUtils.ConvertToInternalUnits(2, UnitTypeId.Millimeters)))
                        {
                            unchanged++;
                            continue;
                        }

                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                switch (effective)
                                {
                                    case JoinAction.Join: JoinGeometryUtils.JoinGeometry(doc, pair.Item1, pair.Item2); joined++; break;
                                    case JoinAction.Unjoin: JoinGeometryUtils.UnjoinGeometry(doc, pair.Item1, pair.Item2); unjoined++; break;
                                    case JoinAction.SwitchOrder: JoinGeometryUtils.SwitchJoinOrder(doc, pair.Item1, pair.Item2); switched++; break;
                                }
                                if (sub.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không xác nhận cặp cấu kiện.");
                            }
                            catch
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                failed++;
                                if (effective == JoinAction.Join) joined--;
                                else if (effective == JoinAction.Unjoin) unjoined--;
                                else if (effective == JoinAction.SwitchOrder) switched--;
                            }
                        }
                    }
                    if (joined + unjoined + switched == 0)
                    {
                        transaction.RollBack();
                    }
                    else if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận cập nhật Join hình học.");
                }

                string report = "Đã Join: " + joined + "\nĐã Unjoin: " + unjoined +
                    "\nĐã đổi thứ tự cắt: " + switched + "\nKhông cần thay đổi/không giao nhau: " + unchanged +
                    "\nRevit từ chối: " + failed;
                TaskDialog.Show("KStruct - Join/Unjoin", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Join/Unjoin", ex.Message);
                return Result.Failed;
            }
        }

        private static bool IsStructural(Element element)
        {
            if (element?.Category == null || element is ElementType || element is RevitLinkInstance) return false;
            long id = element.Category.Id.Value;
            return id == (long)BuiltInCategory.OST_Walls || id == (long)BuiltInCategory.OST_Floors ||
                id == (long)BuiltInCategory.OST_StructuralFraming || id == (long)BuiltInCategory.OST_StructuralColumns ||
                id == (long)BuiltInCategory.OST_StructuralFoundation;
        }

        private static bool BoundingBoxesIntersect(Element first, Element second, double tolerance)
        {
            BoundingBoxXYZ a = ToWorldBounds(first.get_BoundingBox(null));
            BoundingBoxXYZ b = ToWorldBounds(second.get_BoundingBox(null));
            return a != null && b != null && a.Min.X <= b.Max.X + tolerance && a.Max.X + tolerance >= b.Min.X &&
                a.Min.Y <= b.Max.Y + tolerance && a.Max.Y + tolerance >= b.Min.Y &&
                a.Min.Z <= b.Max.Z + tolerance && a.Max.Z + tolerance >= b.Min.Z;
        }

        private static BoundingBoxXYZ ToWorldBounds(BoundingBoxXYZ box)
        {
            if (box == null) return null;
            Transform transform = box.Transform ?? Transform.Identity;
            XYZ[] corners = new XYZ[]
            {
                new XYZ(box.Min.X, box.Min.Y, box.Min.Z), new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Min.X, box.Max.Y, box.Min.Z), new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
                new XYZ(box.Max.X, box.Min.Y, box.Min.Z), new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
                new XYZ(box.Max.X, box.Max.Y, box.Min.Z), new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
            }.Select(transform.OfPoint).ToArray();
            return new BoundingBoxXYZ
            {
                Min = new XYZ(corners.Min(point => point.X), corners.Min(point => point.Y), corners.Min(point => point.Z)),
                Max = new XYZ(corners.Max(point => point.X), corners.Max(point => point.Y), corners.Max(point => point.Z))
            };
        }

        private static JoinAction? ChooseAction(IntPtr owner, int count)
        {
            var window = new Window
            {
                Title = "Join/Unjoin kết cấu - KStruct",
                Width = 450,
                Height = 205,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            new WindowInteropHelper(window).Owner = owner;
            var root = new System.Windows.Controls.Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(new TextBlock
            {
                Text = "Chọn thao tác cho các cặp trong " + count + " cấu kiện. Nếu chọn nhiều phần tử, KStruct sẽ xét mọi cặp.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            var combo = new WpfComboBox
            {
                ItemsSource = new[]
                {
                    new ActionChoice(JoinAction.Toggle, "Tự động Join / Unjoin"),
                    new ActionChoice(JoinAction.Join, "Join Geometry"),
                    new ActionChoice(JoinAction.Unjoin, "Unjoin Geometry"),
                    new ActionChoice(JoinAction.SwitchOrder, "Đổi thứ tự phần tử cắt")
                },
                SelectedIndex = 0,
                MinHeight = 27
            };
            System.Windows.Controls.Grid.SetRow(combo, 1);
            root.Children.Add(combo);
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Thực hiện", MinWidth = 85, IsDefault = true, Margin = new Thickness(0, 0, 7, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 70, IsCancel = true };
            JoinAction? result = null;
            ok.Click += (sender, args) =>
            {
                if (combo.SelectedItem is ActionChoice choice)
                {
                    result = choice.Action;
                    window.DialogResult = true;
                }
            };
            footer.Children.Add(ok); footer.Children.Add(cancel);
            System.Windows.Controls.Grid.SetRow(footer, 3);
            root.Children.Add(footer);
            window.Content = root;
            return window.ShowDialog() == true ? result : null;
        }
    }
}
