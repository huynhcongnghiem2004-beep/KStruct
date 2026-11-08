using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class DeleteFootingDrawingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                var ids = new HashSet<ElementId>();
                List<ViewSheet> sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .Where(sheet => FootingDrawingStorage.TryRead(sheet, out string host, out string role, out double margin) && role == "Sheet")
                    .ToList();
                List<ViewSheet> legacySheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .Where(sheet => sheet.SheetNumber.StartsWith("KSMD-", StringComparison.OrdinalIgnoreCase) &&
                        (sheet.Name.StartsWith("Móng đơn -", StringComparison.OrdinalIgnoreCase) || sheet.Name.StartsWith("Móng -", StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                foreach (ViewSheet legacySheet in legacySheets) if (!sheets.Any(x => x.Id == legacySheet.Id)) sheets.Add(legacySheet);
                var legacyViewName = new Regex(@"^KStruct - (?:MĐ \d+ - MC[12]|Móng \d+ - (?:TOP|BOT))(?: \(\d+\))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                foreach (ViewSheet sheet in sheets)
                {
                    ids.Add(sheet.Id);
                    foreach (ElementId viewportId in sheet.GetAllViewports())
                    {
                        ids.Add(viewportId);
                        Viewport viewport = doc.GetElement(viewportId) as Viewport;
                        View placed = viewport == null ? null : doc.GetElement(viewport.ViewId) as View;
                        if (placed != null && ((FootingDrawingStorage.TryRead(placed, out string host, out string role, out double margin)) || legacyViewName.IsMatch(placed.Name)))
                            ids.Add(placed.Id);
                    }
                }
                foreach (View view in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate))
                    if ((FootingDrawingStorage.TryRead(view, out string host, out string role, out double margin) && role != "Sheet") || legacyViewName.IsMatch(view.Name)) ids.Add(view.Id);
                if (ids.Count == 0)
                {
                    TaskDialog.Show("KStruct - Xóa hồ sơ móng", "Không tìm thấy hồ sơ móng được gắn metadata KStruct.");
                    return Result.Cancelled;
                }
                var confirm = new TaskDialog("KStruct - Xóa hồ sơ móng")
                {
                    MainInstruction = "Xóa hồ sơ móng do KStruct tạo?",
                    MainContent = "Tìm thấy " + sheets.Count + " sheet và " + (ids.Count - sheets.Count) + " view/viewport. Chỉ hồ sơ có metadata KStruct hoặc định danh legacy KSMD-/KStruct mới bị xóa.",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.No
                };
                if (confirm.Show() != TaskDialogResult.Yes) return Result.Cancelled;
                using (var tx = new Transaction(doc, "KStruct - Xóa hồ sơ móng"))
                {
                    tx.Start(); doc.Delete(ids.ToList());
                    if (tx.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit không commit được thao tác xóa.");
                }
                TaskDialog.Show("KStruct - Xóa hồ sơ móng", "Đã xóa hồ sơ KStruct đã chọn.");
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.ToString(); TaskDialog.Show("KStruct - Xóa hồ sơ móng", ex.Message); return Result.Failed; }
        }
    }
}





