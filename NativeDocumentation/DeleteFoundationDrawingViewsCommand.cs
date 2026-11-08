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
    public sealed class DeleteFoundationDrawingViewsCommand : IExternalCommand
    {
        private static readonly Regex IsGeneratedSection = new Regex(
            @"^KStruct - (?:MĐ \d+ - MC[12]|MC \d+ - T\d+)(?: \(\d+\))?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<ViewSheet> generatedSheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>().Where(sheet =>
                        (HasPrefix(sheet.SheetNumber, "KSMD-") && HasPrefix(sheet.Name, "Móng đơn -")) ||
                        (HasPrefix(sheet.SheetNumber, "KSMB-") && HasPrefix(sheet.Name, "Móng băng - Tường"))).ToList();
                var ids = new HashSet<ElementId>(generatedSheets.Select(sheet => sheet.Id));
                foreach (ViewSheet sheet in generatedSheets)
                {
                    foreach (ElementId viewportId in sheet.GetAllViewports())
                    {
                        ids.Add(viewportId);
                        Viewport viewport = doc.GetElement(viewportId) as Viewport;
                        if (viewport != null) ids.Add(viewport.ViewId);
                    }
                }

                foreach (View view in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                    .Where(candidate => !candidate.IsTemplate && IsGeneratedSection.IsMatch(candidate.Name)))
                    ids.Add(view.Id);

                if (ids.Count == 0)
                {
                    TaskDialog.Show("KStruct - Xóa hồ sơ móng", "Không tìm thấy sheet/mặt cắt móng đơn hoặc móng băng do KStruct tạo.");
                    return Result.Cancelled;
                }

                int sheetCount = generatedSheets.Count;
                int viewCount = ids.Select(doc.GetElement).OfType<View>().Count(view => !(view is ViewSheet));
                TaskDialog confirm = new TaskDialog("KStruct - Xóa hồ sơ móng")
                {
                    MainInstruction = "Xóa các hồ sơ móng do KStruct tạo?",
                    MainContent = "Tìm thấy " + sheetCount + " sheet và " + viewCount +
                        " view liên quan (móng đơn/móng băng). Các phần tử phụ thuộc như viewport sẽ bị Revit xóa cùng.\n\n" +
                        "Chỉ các sheet KSMD-/KSMB- và mặt cắt có tên định danh KStruct mới được chọn.",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.No
                };
                if (confirm.Show() != TaskDialogResult.Yes) return Result.Cancelled;

                using (Transaction tx = new Transaction(doc, "KStruct - Xóa hồ sơ móng đã tạo"))
                {
                    tx.Start();
                    ICollection<ElementId> deleted = doc.Delete(ids.ToList());
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thao tác xóa hồ sơ móng.");
                    TaskDialog.Show("KStruct - Xóa hồ sơ móng", "Đã xóa " + (deleted == null ? 0 : deleted.Count) + " phần tử liên quan.");
                }
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Xóa hồ sơ móng", ex.Message);
                return Result.Failed;
            }
        }

        private static bool HasPrefix(string value, string prefix)
        {
            return !string.IsNullOrWhiteSpace(value) && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
    }
}
