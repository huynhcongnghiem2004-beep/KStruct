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

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class SplitStructuralColumnAtLevelCommand : IExternalCommand
    {
        private sealed class ColumnFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) =>
                element?.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralColumns;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class LevelChoice
        {
            public Level Level { get; }
            public string Label { get; }
            public LevelChoice(Level level)
            {
                Level = level;
                Label = level.Name + "  |  " + UnitUtils.ConvertFromInternalUnits(level.Elevation, UnitTypeId.Meters).ToString("0.###") + " m";
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
                FamilyInstance column = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<FamilyInstance>()
                    .FirstOrDefault(IsColumn);
                if (column == null)
                {
                    Reference picked = ui.Selection.PickObject(ObjectType.Element, new ColumnFilter(),
                        "Chọn cột đứng cần chia tại một Level");
                    column = doc.GetElement(picked) as FamilyInstance;
                }
                if (column == null) return Result.Cancelled;
                if (!(column.Location is LocationPoint))
                    throw new InvalidOperationException("Chỉ hỗ trợ cột đứng dạng điểm.");
                if (new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
                    .Any(rebar => rebar.GetHostId() == column.Id))
                    throw new InvalidOperationException("Cột đang host Rebar. Hãy xử lý cốt thép trước khi chia cột.");

                Parameter baseLevelParameter = column.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM);
                Parameter topLevelParameter = column.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
                if (baseLevelParameter == null || topLevelParameter == null ||
                    baseLevelParameter.StorageType != StorageType.ElementId || topLevelParameter.StorageType != StorageType.ElementId)
                    throw new InvalidOperationException("Cột phải có ràng buộc Base Level và Top Level.");
                Level baseLevel = doc.GetElement(baseLevelParameter.AsElementId()) as Level;
                Level topLevel = doc.GetElement(topLevelParameter.AsElementId()) as Level;
                if (baseLevel == null || topLevel == null)
                    throw new InvalidOperationException("Không đọc được khoảng level chân–đỉnh của cột.");
                double baseOffset = ReadDouble(column, BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
                double topOffset = ReadDouble(column, BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
                double actualBase = baseLevel.Elevation + baseOffset;
                double actualTop = topLevel.Elevation + topOffset;
                if (actualTop <= actualBase)
                    throw new InvalidOperationException("Cao độ đỉnh thực tế phải cao hơn chân cột.");

                List<Level> intermediate = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .Where(level => level.Elevation > actualBase + 1e-6 && level.Elevation < actualTop - 1e-6)
                    .OrderBy(level => level.Elevation).ToList();
                if (intermediate.Count == 0)
                    throw new InvalidOperationException("Không có Level hiện hữu nằm giữa chân và đỉnh cột.");
                Level splitLevel = ChooseLevel(data.Application.MainWindowHandle, intermediate);
                if (splitLevel == null) return Result.Cancelled;

                bool wasPinned = column.Pinned;
                FamilyInstance upperColumn = null;
                using (Transaction tx = new Transaction(doc, "KStruct - Chia cột tại Level"))
                {
                    tx.Start();
                    if (wasPinned) column.Pinned = false;
                    ICollection<ElementId> copies = ElementTransformUtils.CopyElement(doc, column.Id, XYZ.Zero);
                    upperColumn = copies.Select(doc.GetElement).OfType<FamilyInstance>()
                        .FirstOrDefault(candidate => candidate.Id != column.Id && IsColumn(candidate));
                    if (upperColumn == null) throw new InvalidOperationException("Không tạo được đoạn cột phía trên.");
                    if (upperColumn.Pinned) upperColumn.Pinned = false;

                    SetElementId(column, BuiltInParameter.FAMILY_TOP_LEVEL_PARAM, splitLevel.Id);
                    SetDouble(column, BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM, 0);
                    SetElementId(upperColumn, BuiltInParameter.FAMILY_BASE_LEVEL_PARAM, splitLevel.Id);
                    SetDouble(upperColumn, BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM, 0);
                    ClearDuplicateMark(column, upperColumn);
                    if (wasPinned)
                    {
                        column.Pinned = true;
                        upperColumn.Pinned = true;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận chia cột.");
                }
                TaskDialog.Show("KStruct - Chia cột",
                    "Đã chia cột " + column.Id.Value + " tại Level '" + splitLevel.Name +
                    "'. Đoạn phía trên có ElementId " + upperColumn.Id.Value + "; tọa độ và type được giữ nguyên.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Chia cột", ex.Message);
                return Result.Failed;
            }
        }

        private static bool IsColumn(FamilyInstance instance) =>
            instance?.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralColumns;

        private static void SetElementId(Element element, BuiltInParameter id, ElementId value)
        {
            Parameter parameter = element.get_Parameter(id);
            if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.ElementId)
                throw new InvalidOperationException("Không thể sửa tham số " + id + ".");
            parameter.Set(value);
        }

        private static void SetDouble(Element element, BuiltInParameter id, double value)
        {
            Parameter parameter = element.get_Parameter(id);
            if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.Double)
                throw new InvalidOperationException("Không thể sửa tham số " + id + ".");
            parameter.Set(value);
        }

        private static double ReadDouble(Element element, BuiltInParameter id)
        {
            Parameter parameter = element.get_Parameter(id);
            return parameter != null && parameter.HasValue && parameter.StorageType == StorageType.Double
                ? parameter.AsDouble() : 0;
        }

        private static void ClearDuplicateMark(Element original, Element copy)
        {
            Parameter source = original.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            Parameter target = copy.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            if (source != null && target != null && target.StorageType == StorageType.String && !target.IsReadOnly &&
                !string.IsNullOrWhiteSpace(source.AsString()))
                target.Set("");
        }

        private static Level ChooseLevel(IntPtr owner, IList<Level> levels)
        {
            var choices = levels.Select(level => new LevelChoice(level)).ToList();
            var window = new Window
            {
                Title = "Chia cột tại Level - KStruct",
                Width = 380,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock
            {
                Text = "Chọn Level nằm giữa chân và đỉnh cột. Mark của đoạn phía trên sẽ được xóa để tránh trùng mã.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            var combo = new System.Windows.Controls.ComboBox
            {
                ItemsSource = choices,
                SelectedIndex = 0,
                MinWidth = 340,
                DisplayMemberPath = "Label"
            };
            root.Children.Add(combo);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new System.Windows.Controls.Button { Content = "Chia cột", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (s, e) => { if (combo.SelectedItem != null) window.DialogResult = true; };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            return window.ShowDialog() == true ? (combo.SelectedItem as LevelChoice)?.Level : null;
        }
    }
}
