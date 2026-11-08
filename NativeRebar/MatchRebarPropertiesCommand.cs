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

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class MatchRebarPropertiesCommand : IExternalCommand
    {
        private sealed class RebarFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Rebar;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class SameShapeFilter : ISelectionFilter
        {
            private readonly ElementId _shapeId;
            public SameShapeFilter(ElementId shapeId) { _shapeId = shapeId; }
            public bool AllowElement(Element element) => element is Rebar rebar && rebar.IsRebarShapeDriven() && rebar.GetShapeId() == _shapeId;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                Reference sourceReference = ui.Selection.PickObject(ObjectType.Element, new RebarFilter(),
                    "Chọn Rebar mẫu để khớp thuộc tính");
                Rebar source = doc.GetElement(sourceReference) as Rebar;
                if (source == null) return Result.Cancelled;
                if (!source.IsRebarShapeDriven())
                {
                    TaskDialog.Show("KStruct - Khớp Rebar", "Thanh mẫu là FreeForm nên không có RebarShape để khớp.");
                    return Result.Cancelled;
                }
                bool setTargetShape;
                bool copyParameters;
                if (!AskOptions(data.Application.MainWindowHandle, out setTargetShape, out copyParameters)) return Result.Cancelled;
                ISelectionFilter targetFilter = setTargetShape
                    ? (ISelectionFilter)new RebarFilter()
                    : new SameShapeFilter(source.GetShapeId());
                string selectionPrompt = setTargetShape
                    ? "Chọn các Rebar shape-driven cần đổi sang RebarShape của thanh mẫu; nhấn Finish để áp dụng"
                    : "Chọn các Rebar đích cùng RebarShape; nhấn Finish để áp dụng";
                IList<Reference> targetReferences = ui.Selection.PickObjects(ObjectType.Element, targetFilter, selectionPrompt);
                List<Rebar> targets = targetReferences.Select(r => doc.GetElement(r)).OfType<Rebar>()
                    .Where(r => r.Id != source.Id).Distinct().ToList();
                if (targets.Count == 0) return Result.Cancelled;

                int changed = 0;
                int copiedParameters = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Match Rebar"))
                {
                    tx.Start();
                    foreach (Rebar target in targets)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                if (!target.IsRebarShapeDriven())
                                    throw new InvalidOperationException("Rebar FreeForm không có RebarShape để khớp.");
                                if (target.GetShapeId() != source.GetShapeId())
                                {
                                    if (!setTargetShape)
                                        throw new InvalidOperationException("RebarShape khác thanh mẫu.");
                                    target.GetShapeDrivenAccessor().SetRebarShapeId(source.GetShapeId());
                                }

                                ForgeTypeId requiredGroup = copyParameters
                                    ? null
                                    : GroupTypeId.Geometry;
                                int targetCopied = CopyCompatibleParameters(source, target, requiredGroup);
                                if (target.GetTypeId() != source.GetTypeId())
                                    target.ChangeTypeId(source.GetTypeId());
                                CopyPartition(source, target);
                                for (int end = 0; end <= 1; end++)
                                {
                                    ElementId hookId = source.GetHookTypeId(end);
                                    if (hookId == ElementId.InvalidElementId)
                                        target.SetHookTypeId(end, ElementId.InvalidElementId);
                                    else
                                    {
                                        if (!target.CanUseHookType(hookId))
                                            throw new InvalidOperationException("Thanh đích không dùng được móc ở đầu " + (end == 0 ? "đầu" : "cuối") + ".");
                                        target.SetHookTypeId(end, hookId);
                                        target.SetHookOrientation(end, source.GetHookOrientation(end));
                                    }
                                }
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không hoàn tất cập nhật thanh.");
                                copiedParameters += targetCopied;
                                changed++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Rebar " + target.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (changed == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Match Rebar", "Không cập nhật được Rebar nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các thay đổi Rebar.");
                }
                string report = "Đã áp dụng type, tham số nhóm Rebar, Partition và móc từ Rebar " + source.Id.Value +
                    " cho " + changed + "/" + targets.Count + " thanh cùng RebarShape.";
                if (setTargetShape)
                    report = "Đã đổi RebarShape đích theo thanh mẫu và áp dụng type, tham số nhóm Rebar, Partition, móc cho " +
                        changed + "/" + targets.Count + " thanh.";
                if (copyParameters)
                    report += "\nĐã chép " + copiedParameters + " giá trị tham số instance tương thích.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " thanh:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Match Rebar", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Match Rebar", ex.Message);
                return Result.Failed;
            }
        }

        private static void CopyPartition(Rebar source, Rebar target)
        {
            Parameter sourceParameter = source.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
            Parameter targetParameter = target.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
            if (sourceParameter == null || targetParameter == null || targetParameter.IsReadOnly ||
                targetParameter.StorageType != StorageType.String)
                throw new InvalidOperationException("Không thể ghi Partition.");
            string value = sourceParameter.AsString() ?? "";
            if (!string.Equals(targetParameter.AsString() ?? "", value, StringComparison.Ordinal))
                targetParameter.Set(value);
        }

        private static int CopyCompatibleParameters(Rebar source, Rebar target, ForgeTypeId requiredGroup)
        {
            int copied = 0;
            foreach (Parameter sourceParameter in source.GetOrderedParameters())
            {
                if (!sourceParameter.HasValue || sourceParameter.IsReadOnly ||
                    sourceParameter.Definition == null || sourceParameter.StorageType == StorageType.None)
                    continue;
                if (requiredGroup != null &&
                    !sourceParameter.Definition.GetGroupTypeId().Equals(requiredGroup))
                    continue;

                Parameter targetParameter = target.get_Parameter(sourceParameter.Definition);
                if (targetParameter == null || targetParameter.IsReadOnly ||
                    targetParameter.StorageType != sourceParameter.StorageType)
                    continue;

                bool changed;
                switch (sourceParameter.StorageType)
                {
                    case StorageType.Integer:
                        changed = targetParameter.Set(sourceParameter.AsInteger());
                        break;
                    case StorageType.Double:
                        changed = targetParameter.Set(sourceParameter.AsDouble());
                        break;
                    case StorageType.String:
                        changed = targetParameter.Set(sourceParameter.AsString() ?? string.Empty);
                        break;
                    case StorageType.ElementId:
                        changed = targetParameter.Set(sourceParameter.AsElementId());
                        break;
                    default:
                        changed = false;
                        break;
                }
                if (changed) copied++;
            }
            return copied;
        }

        private static bool AskOptions(IntPtr owner, out bool setTargetShape, out bool copyParameters)
        {
            var window = new Window
            {
                Title = "Khớp thuộc tính Rebar - KStruct",
                Width = 510,
                Height = 210,
                MinWidth = 470,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock
            {
                Text = "KStruct sẽ khớp type, Partition, móc và các tham số thuộc nhóm Geometry.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });
            var sameShape = new RadioButton
            {
                Content = "Chỉ nhận thanh đã có cùng RebarShape",
                IsChecked = true,
                Margin = new Thickness(0, 0, 0, 5)
            };
            var changeShape = new RadioButton
            {
                Content = "Đổi RebarShape của thanh đích theo thanh mẫu",
                Margin = new Thickness(0, 0, 0, 10)
            };
            root.Children.Add(sameShape);
            root.Children.Add(changeShape);
            var copyCheck = new CheckBox
            {
                Content = new TextBlock
                {
                    Text = "Chép thêm mọi tham số instance có thể ghi và cùng kiểu dữ liệu (có thể đổi Schedule Mark hoặc hình học).",
                    TextWrapping = TextWrapping.Wrap
                },
                IsChecked = false,
                Margin = new Thickness(0, 0, 0, 12)
            };
            root.Children.Add(copyCheck);
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var apply = new Button { Content = "Tiếp tục", Width = 86, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            apply.Click += (sender, args) => { window.DialogResult = true; };
            buttons.Children.Add(apply);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            bool? accepted = window.ShowDialog();
            setTargetShape = changeShape.IsChecked == true;
            copyParameters = copyCheck.IsChecked == true;
            return accepted == true;
        }
    }
}
