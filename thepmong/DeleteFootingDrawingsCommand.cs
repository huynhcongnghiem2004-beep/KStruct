using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SDK_HD.KStruct.NativeDocumentation
{
    // Safe replacement: scoped deletion only. Never delete every KStruct sheet in a project.
    [Transaction(TransactionMode.Manual)]
    public sealed class DeleteFootingDrawingsCommand : IExternalCommand
    {
        private static void AddHost(Element element, Document doc, ISet<string> hostIds)
        {
            if (element == null) return;
            if (element is FamilyInstance foundation &&
                foundation.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation)
            {
                hostIds.Add(foundation.UniqueId);
                return;
            }
            if (element is Viewport viewport) element = doc.GetElement(viewport.ViewId);
            if (FootingDrawingStorage.TryRead(element, out string host, out string role, out double margin))
                hostIds.Add(host);
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                var hostIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (ElementId id in ui.Selection.GetElementIds())
                    AddHost(doc.GetElement(id), doc, hostIds);
                if (hostIds.Count == 0)
                    AddHost(ui.ActiveView, doc, hostIds);
                if (hostIds.Count == 0)
                {
                    TaskDialog.Show("KStruct - Xóa hồ sơ móng",
                        "Hãy chọn móng, View/Viewport hoặc mở Sheet/View có metadata KStruct cần xóa. " +
                        "Lệnh không tự chọn và xóa toàn bộ hồ sơ trong project.");
                    return Result.Cancelled;
                }

                var views = new FilteredElementCollector(doc).OfClass(typeof(View))
                    .Cast<View>().Where(v => !v.IsTemplate &&
                        FootingDrawingStorage.TryRead(v, out string host, out string role, out double margin) &&
                        hostIds.Contains(host) && role != "Sheet" &&
                        (FootingDrawingStorage.Version(v) < 3 ||
                            (doc.GetElement(host) is FamilyInstance footing &&
                             FootingDrawingDetailState.OwnsView(v, footing))))
                    .ToList();
                if (views.Count == 0)
                {
                    TaskDialog.Show("KStruct - Xóa hồ sơ móng",
                        "Không tìm thấy View do KStruct quản lý trong phạm vi đã chọn. " +
                        "Không xóa các View chỉ có tên giống KStruct nhưng thiếu metadata.");
                    return Result.Cancelled;
                }

                var sheetIds = new HashSet<long>();
                foreach (View view in views)
                    foreach (Viewport port in new FilteredElementCollector(doc)
                        .OfClass(typeof(Viewport)).Cast<Viewport>()
                        .Where(x => x.ViewId.Value == view.Id.Value))
                        sheetIds.Add(port.SheetId.Value);

                var confirm = new TaskDialog("KStruct - Xóa hồ sơ móng")
                {
                    MainInstruction = "Xóa " + views.Count + " View của " + hostIds.Count + " móng được chọn?",
                    MainContent = "Các View được nhận diện bằng metadata sở hữu KStruct. " +
                        "Xóa View cũng xóa các Dimension/Tag/Detail Item thủ công đặt trong View đó. " +
                        "Giữ nguyên " + sheetIds.Count + " Sheet liên quan và mọi nội dung thủ công trên Sheet. " +
                        "Không xóa View hoặc Sheet của móng khác.",
                    CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                    DefaultButton = TaskDialogResult.No
                };
                if (confirm.Show() != TaskDialogResult.Yes) return Result.Cancelled;

                using (var tx = new Transaction(doc, "KStruct - Xóa View hồ sơ móng trong phạm vi chọn"))
                {
                    tx.Start();
                    doc.Delete(views.Select(v => v.Id).ToList());
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không commit được thao tác xóa View.");
                }
                TaskDialog.Show("KStruct - Xóa hồ sơ móng",
                    "Đã xóa " + views.Count + " View KStruct. " +
                    "Các Sheet vẫn được giữ lại để không ảnh hưởng nội dung thủ công.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Xóa hồ sơ móng", ex.Message);
                return Result.Failed;
            }
        }
    }
}
