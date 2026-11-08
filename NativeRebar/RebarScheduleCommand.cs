using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class RebarScheduleCommand : IExternalCommand
    {
        private static readonly (BuiltInParameter Parameter, string Heading)[] Fields =
        {
            (BuiltInParameter.REBAR_ELEM_SCHEDULE_MARK, "Mã thép"),
            (BuiltInParameter.REBAR_ELEM_HOST_MARK, "Mã cấu kiện"),
            (BuiltInParameter.REBAR_BAR_DIAMETER, "Đường kính"),
            (BuiltInParameter.REBAR_ELEM_LENGTH, "Chiều dài thanh"),
            (BuiltInParameter.REBAR_ELEM_QUANTITY_OF_BARS, "Số thanh"),
            (BuiltInParameter.REBAR_ELEM_TOTAL_LENGTH, "Tổng chiều dài")
        };

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;

            try
            {
                using (Transaction transaction = new Transaction(doc, "KStruct - Tạo bảng thống kê cốt thép"))
                {
                    transaction.Start();
                    ElementId categoryId = new ElementId(BuiltInCategory.OST_Rebar);
                    ViewSchedule schedule = ViewSchedule.CreateSchedule(doc, categoryId);
                    ScheduleDefinition definition = schedule.Definition;
                    definition.IsItemized = false;
                    definition.ShowGrandTotal = true;
                    definition.ShowGrandTotalTitle = true;
                    definition.ShowGrandTotalCount = true;
                    definition.GrandTotalTitle = "Tổng";

                    var schedulable = definition.GetSchedulableFields();
                    var added = new System.Collections.Generic.List<ScheduleField>();
                    foreach (var item in Fields)
                    {
                        ElementId parameterId = new ElementId(item.Parameter);
                        SchedulableField candidate = schedulable.FirstOrDefault(field => field.ParameterId == parameterId);
                        if (candidate == null) continue;
                        ScheduleField scheduleField = definition.AddField(candidate);
                        scheduleField.ColumnHeading = item.Heading;
                        added.Add(scheduleField);
                    }

                    if (added.Count == 0)
                        throw new InvalidOperationException("Revit không cung cấp các trường thống kê cốt thép trong project này.");

                    schedule.Name = UniqueScheduleName(doc, "KStruct - Thống kê cốt thép");
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận bảng thống kê.");
                }

                TaskDialog.Show("KStruct", "Đã tạo bảng thống kê cốt thép. Kiểm tra các trường và quy tắc tổng hợp trước khi phát hành hồ sơ.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Thống kê cốt thép", ex.Message);
                return Result.Failed;
            }
        }

        private static string UniqueScheduleName(Document doc, string baseName)
        {
            var names = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>().Select(view => view.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!names.Contains(baseName)) return baseName;
            int index = 2;
            string candidate;
            do { candidate = baseName + " (" + index++ + ")"; }
            while (names.Contains(candidate));
            return candidate;
        }
    }
}