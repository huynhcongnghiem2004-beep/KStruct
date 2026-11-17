using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class MultiCategoryQuantityReportCommand : IExternalCommand
    {
        private sealed class CategoryChoice
        {
            public long Id;
            public string Name;
            public int Count;
            public bool Selected;
        }

        private sealed class SummaryRow
        {
            public string Category;
            public string Family;
            public string Type;
            public long TypeId;
            public int Count;
            public double AreaSquareMeters;
            public double VolumeCubicMeters;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Element> instances = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(element => element.Category != null && !element.ViewSpecific)
                    .ToList();
                List<CategoryChoice> categories = instances.GroupBy(element => element.Category.Id.Value)
                    .Select(group => new CategoryChoice
                    {
                        Id = group.Key,
                        Name = group.First().Category.Name,
                        Count = group.Count()
                    })
                    .OrderBy(choice => choice.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                if (categories.Count == 0) return Result.Cancelled;
                if (!ChooseCategories(data.Application.MainWindowHandle, categories)) return Result.Cancelled;

                HashSet<long> selectedCategoryIds = new HashSet<long>(categories.Where(choice => choice.Selected).Select(choice => choice.Id));
                List<Element> selected = instances.Where(element => selectedCategoryIds.Contains(element.Category.Id.Value))
                    .OrderBy(element => element.Category.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(element => element.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(element => element.Id.Value).ToList();
                if (selected.Count == 0) return Result.Cancelled;

                var dialog = new SaveFileDialog
                {
                    Title = "Xuất báo cáo nhiều category KStruct",
                    Filter = "Excel Workbook (*.xlsx)|*.xlsx|CSV UTF-8 (*.csv)|*.csv",
                    FileName = "KStruct_MultiCategoryReport.xlsx",
                    AddExtension = true,
                    DefaultExt = ".xlsx",
                    OverwritePrompt = true
                };
                if (dialog.ShowDialog() != true) return Result.Cancelled;

                List<string> instanceParameters = selected.SelectMany(element => element.Parameters.Cast<Parameter>())
                    .Where(parameter => parameter.Definition != null)
                    .Select(parameter => parameter.Definition.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
                List<string> typeParameters = selected.Select(element => doc.GetElement(element.GetTypeId()))
                    .Where(type => type != null)
                    .SelectMany(type => type.Parameters.Cast<Parameter>())
                    .Where(parameter => parameter.Definition != null)
                    .Select(parameter => parameter.Definition.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();

                var headers = new List<string>
                {
                    "ElementId", "UniqueId", "Category", "Family", "Type", "Level", "Mark", "Count", "Area (m2)", "Volume (m3)"
                };
                headers.AddRange(instanceParameters.Select(name => "Instance: " + name));
                headers.AddRange(typeParameters.Select(name => "Type: " + name));
                var detailTable = new List<List<string>> { headers };
                foreach (Element element in selected)
                {
                    Element type = doc.GetElement(element.GetTypeId());
                    Element level = element.LevelId != ElementId.InvalidElementId ? doc.GetElement(element.LevelId) : null;
                    var values = new List<string>
                    {
                        element.Id.Value.ToString(CultureInfo.InvariantCulture),
                        element.UniqueId ?? string.Empty,
                        element.Category.Name,
                        GetFamilyName(type),
                        type?.Name ?? element.Name ?? string.Empty,
                        level?.Name ?? string.Empty,
                        ReadParameter(element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK), doc),
                        "1",
                        ReadAreaSquareMeters(element).ToString("0.###", CultureInfo.InvariantCulture),
                        ReadVolumeCubicMeters(element).ToString("0.###", CultureInfo.InvariantCulture)
                    };
                    foreach (string name in instanceParameters)
                        values.Add(ReadNamedParameter(element, name, doc));
                    foreach (string name in typeParameters)
                        values.Add(type == null ? string.Empty : ReadNamedParameter(type, name, doc));
                    detailTable.Add(values);
                }

                List<SummaryRow> summaries = selected.GroupBy(element =>
                {
                    Element type = doc.GetElement(element.GetTypeId());
                    return element.Category.Id.Value.ToString(CultureInfo.InvariantCulture) + "\u001f" +
                        element.GetTypeId().Value.ToString(CultureInfo.InvariantCulture);
                }).Select(group =>
                {
                    Element first = group.First();
                    Element type = doc.GetElement(first.GetTypeId());
                    return new SummaryRow
                    {
                        Category = first.Category.Name,
                        Family = GetFamilyName(type),
                        Type = type?.Name ?? first.Name ?? "",
                        TypeId = first.GetTypeId().Value,
                        Count = group.Count(),
                        AreaSquareMeters = group.Sum(ReadAreaSquareMeters),
                        VolumeCubicMeters = group.Sum(ReadVolumeCubicMeters)
                    };
                }).OrderBy(row => row.Category, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(row => row.Type, StringComparer.CurrentCultureIgnoreCase).ToList();

                if (string.Equals(Path.GetExtension(dialog.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
                    ExportSelectionCsvCommand.WriteXlsx(dialog.FileName, detailTable);
                else
                    File.WriteAllLines(dialog.FileName, detailTable.Select(row => string.Join(",", row.Select(EscapeCsv))), new UTF8Encoding(true));
                string summaryPath = Path.Combine(Path.GetDirectoryName(dialog.FileName),
                    Path.GetFileNameWithoutExtension(dialog.FileName) + "_Summary.csv");
                WriteSummary(summaryPath, summaries);
                int createdSchedules = 0, updatedSchedules = 0, skippedSchedules = 0;
                var scheduleErrors = new List<string>();
                if (TaskDialog.Show("KStruct - Tạo Revit Schedule",
                    "Tạo hoặc cập nhật Revit Schedule cho từng category đã chọn? Các schedule sẽ được lưu trong project.",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No) == TaskDialogResult.Yes)
                {
                    using (Transaction scheduleTransaction = new Transaction(doc, "KStruct - Tạo Schedule theo category"))
                    {
                        scheduleTransaction.Start();
                        foreach (long categoryId in selectedCategoryIds)
                        {
                            using (SubTransaction sub = new SubTransaction(doc))
                            {
                                sub.Start();
                                try
                                {
                                    Category category = Category.GetCategory(doc, new ElementId(categoryId));
                                    if (category == null) throw new InvalidOperationException("Không đọc được category Id " + categoryId + ".");
                                    string scheduleName = BuildScheduleName(category.Name, categoryId);
                                    ViewSchedule schedule = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule))
                                        .Cast<ViewSchedule>().FirstOrDefault(view => !view.IsTemplate &&
                                            string.Equals(view.Name, scheduleName, StringComparison.OrdinalIgnoreCase));
                                    bool isNew = schedule == null;
                                    if (isNew) schedule = ViewSchedule.CreateSchedule(doc, category.Id);
                                    ScheduleDefinition definition = schedule.Definition;
                                    if (definition.CategoryId != category.Id)
                                        throw new InvalidOperationException("Tên Schedule đang được dùng cho category khác: " + scheduleName);
                                    if (!isNew)
                                    {
                                        definition.ClearFilters();
                                        definition.ClearSortGroupFields();
                                        definition.ClearFields();
                                    }

                                    IList<SchedulableField> available = definition.GetSchedulableFields();
                                    BuiltInParameter[] preferredParameters =
                                    {
                                        BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM,
                                        BuiltInParameter.LEVEL_PARAM,
                                        BuiltInParameter.ALL_MODEL_MARK,
                                        BuiltInParameter.HOST_AREA_COMPUTED,
                                        BuiltInParameter.HOST_VOLUME_COMPUTED
                                    };
                                    var addedFields = new List<ScheduleField>();
                                    foreach (BuiltInParameter builtIn in preferredParameters)
                                    {
                                        ElementId parameterId = new ElementId((long)builtIn);
                                        SchedulableField field = available.FirstOrDefault(item => item.ParameterId == parameterId);
                                        if (field == null) continue;
                                        addedFields.Add(definition.AddField(field));
                                    }
                                    if (addedFields.Count == 0)
                                        throw new InvalidOperationException("Category này không có field schedule tương thích.");
                                    try
                                    {
                                        ScheduleField countField = definition.AddField(ScheduleFieldType.Count);
                                        countField.ColumnHeading = "Số lượng";
                                    }
                                    catch { }
                                    definition.IsItemized = false;
                                    definition.AddSortGroupField(new ScheduleSortGroupField(addedFields[0].FieldId, ScheduleSortOrder.Ascending));
                                    schedule.Name = scheduleName;
                                    if (sub.Commit() != TransactionStatus.Committed)
                                        throw new InvalidOperationException("Revit không chấp nhận Schedule " + scheduleName + ".");
                                    if (isNew) createdSchedules++; else updatedSchedules++;
                                }
                                catch (Exception ex)
                                {
                                    if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                    skippedSchedules++;
                                    if (skippedSchedules <= 5)
                                        TaskDialog.Show("KStruct - Bỏ qua Schedule", ex.Message);
                                }
                            }
                        }
                        if (createdSchedules + updatedSchedules == 0)
                            scheduleTransaction.RollBack();
                        else if (scheduleTransaction.Commit() != TransactionStatus.Committed)
                            throw new InvalidOperationException("Revit không lưu được các Schedule theo category.");
                    }
                }

                TaskDialog.Show("KStruct - Thống kê nhiều category",
                    "Đã xuất " + selected.Count.ToString(CultureInfo.InvariantCulture) + " phần tử thuộc " +
                    selectedCategoryIds.Count.ToString(CultureInfo.InvariantCulture) + " category." +
                    "\nChi tiết: " + dialog.FileName +
                    "\nTổng hợp theo category/type: " + summaryPath +
                    (createdSchedules + updatedSchedules > 0 ? "\nRevit Schedule mới/cập nhật: " + createdSchedules + "/" + updatedSchedules : "") +
                    (skippedSchedules > 0 ? "\nSchedule bỏ qua: " + skippedSchedules : ""));
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Thống kê nhiều category", ex.Message);
                return Result.Failed;
            }
        }

        private static bool ChooseCategories(IntPtr owner, IList<CategoryChoice> categories)
        {
            var window = new Window
            {
                Title = "Chọn category để thống kê - KStruct",
                Width = 470,
                Height = 650,
                MinWidth = 380,
                MinHeight = 450,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            new WindowInteropHelper(window).Owner = owner;
            var root = new DockPanel { Margin = new Thickness(12) };
            var top = new StackPanel();
            top.Children.Add(new TextBlock { Text = "Tìm category:", Margin = new Thickness(0, 0, 0, 4) });
            var filter = new System.Windows.Controls.TextBox { MinHeight = 25, Margin = new Thickness(0, 0, 0, 8) };
            top.Children.Add(filter);
            var quick = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var structural = new Button { Content = "Nhóm kết cấu", Margin = new Thickness(0, 0, 6, 0) };
            var all = new Button { Content = "Tất cả", Margin = new Thickness(0, 0, 6, 0) };
            var clear = new Button { Content = "Bỏ chọn" };
            quick.Children.Add(structural);
            quick.Children.Add(all);
            quick.Children.Add(clear);
            top.Children.Add(quick);
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);

            var checks = new StackPanel();
            var scroll = new ScrollViewer { Content = checks, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            root.Children.Add(scroll);
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "Xuất báo cáo", MinWidth = 105, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 75, IsCancel = true };
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);
            footer.Children.Add(ok);
            footer.Children.Add(cancel);

            bool accepted = false;
            Action refresh = () =>
            {
                checks.Children.Clear();
                string query = (filter.Text ?? "").Trim();
                foreach (CategoryChoice choice in categories.Where(item => string.IsNullOrEmpty(query) ||
                    item.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0))
                {
                    var check = new CheckBox
                    {
                        Content = choice.Name + "  (" + choice.Count.ToString("N0", CultureInfo.CurrentCulture) + ")",
                        IsChecked = choice.Selected,
                        Margin = new Thickness(2, 2, 2, 2)
                    };
                    check.Checked += (sender, args) => choice.Selected = true;
                    check.Unchecked += (sender, args) => choice.Selected = false;
                    checks.Children.Add(check);
                }
            };
            filter.TextChanged += (sender, args) => refresh();
            structural.Click += (sender, args) =>
            {
                long[] structuralIds =
                {
                    (long)BuiltInCategory.OST_StructuralColumns,
                    (long)BuiltInCategory.OST_StructuralFraming,
                    (long)BuiltInCategory.OST_Floors,
                    (long)BuiltInCategory.OST_Walls,
                    (long)BuiltInCategory.OST_StructuralFoundation,
                    (long)BuiltInCategory.OST_Rebar,
                    (long)BuiltInCategory.OST_Stairs
                };
                foreach (CategoryChoice choice in categories) choice.Selected = structuralIds.Contains(choice.Id);
                refresh();
            };
            all.Click += (sender, args) => { foreach (CategoryChoice choice in categories) choice.Selected = true; refresh(); };
            clear.Click += (sender, args) => { foreach (CategoryChoice choice in categories) choice.Selected = false; refresh(); };
            ok.Click += (sender, args) =>
            {
                if (!categories.Any(choice => choice.Selected))
                {
                    TaskDialog.Show("KStruct - Thống kê", "Chọn ít nhất một category.");
                    return;
                }
                accepted = true;
                window.DialogResult = true;
            };
            refresh();
            window.Content = root;
            return window.ShowDialog() == true && accepted;
        }

        private static string BuildScheduleName(string categoryName, long categoryId)
        {
            char[] invalid = { '\\', '/', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '"' };
            string safe = new string((categoryName ?? "Category").Select(character => invalid.Contains(character) ? '-' : character).ToArray());
            return "KStruct - " + safe + " (" + categoryId.ToString(CultureInfo.InvariantCulture) + ")";
        }
        private static string GetFamilyName(Element type)
        {
            if (type is FamilySymbol symbol) return symbol.FamilyName ?? "";
            return type?.Name ?? "";
        }

        private static double ReadAreaSquareMeters(Element element)
        {
            Parameter area = element.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
            return area == null || !area.HasValue || area.StorageType != StorageType.Double
                ? 0 : UnitUtils.ConvertFromInternalUnits(area.AsDouble(), UnitTypeId.SquareMeters);
        }

        private static double ReadVolumeCubicMeters(Element element)
        {
            Parameter volume = element.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
            return volume == null || !volume.HasValue || volume.StorageType != StorageType.Double
                ? 0 : UnitUtils.ConvertFromInternalUnits(volume.AsDouble(), UnitTypeId.CubicMeters);
        }

        private static string ReadNamedParameter(Element element, string name, Document doc)
        {
            Parameter parameter = element.Parameters.Cast<Parameter>()
                .FirstOrDefault(item => item.Definition != null &&
                    string.Equals(item.Definition.Name, name, StringComparison.OrdinalIgnoreCase));
            return ReadParameter(parameter, doc);
        }

        private static string ReadParameter(Parameter parameter, Document doc)
        {
            if (parameter == null || !parameter.HasValue) return string.Empty;
            switch (parameter.StorageType)
            {
                case StorageType.String: return parameter.AsString() ?? string.Empty;
                case StorageType.Integer: return parameter.AsValueString() ?? parameter.AsInteger().ToString(CultureInfo.InvariantCulture);
                case StorageType.Double: return parameter.AsValueString() ?? parameter.AsDouble().ToString("G17", CultureInfo.InvariantCulture);
                case StorageType.ElementId:
                    ElementId id = parameter.AsElementId();
                    Element referenced = id != ElementId.InvalidElementId ? doc.GetElement(id) : null;
                    return referenced?.Name ?? id.Value.ToString(CultureInfo.InvariantCulture);
                default: return string.Empty;
            }
        }

        private static void WriteSummary(string path, IList<SummaryRow> rows)
        {
            var lines = new List<string> { "Category,Family,Type,TypeId,Count,Area (m2),Volume (m3)" };
            lines.AddRange(rows.Select(row => string.Join(",", new[]
            {
                EscapeCsv(row.Category), EscapeCsv(row.Family), EscapeCsv(row.Type),
                row.TypeId.ToString(CultureInfo.InvariantCulture), row.Count.ToString(CultureInfo.InvariantCulture),
                row.AreaSquareMeters.ToString("0.###", CultureInfo.InvariantCulture),
                row.VolumeCubicMeters.ToString("0.###", CultureInfo.InvariantCulture)
            })));
            File.WriteAllLines(path, lines, new UTF8Encoding(true));
        }

        private static string EscapeCsv(string value)
        {
            value = value ?? string.Empty;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
