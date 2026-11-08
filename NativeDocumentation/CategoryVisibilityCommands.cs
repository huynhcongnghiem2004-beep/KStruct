using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class HidePickedCategoryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
            CategoryVisibility.Execute(data, ref message, hide: true);
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class ShowPickedCategoryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements) =>
            CategoryVisibility.Execute(data, ref message, hide: false);
    }

    internal static class CategoryVisibility
    {
        public static Result Execute(ExternalCommandData data, ref string message, bool hide)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            View view = doc.ActiveView;
            try
            {
                Element seed = ui.Selection.GetElementIds().Select(doc.GetElement).FirstOrDefault(element => element?.Category != null);
                if (seed == null)
                {
                    Reference reference = ui.Selection.PickObject(ObjectType.Element,
                        hide ? "Chọn một phần tử thuộc category cần ẩn trong view hiện tại" :
                            "Chọn một phần tử thuộc category cần hiện trong view hiện tại");
                    seed = doc.GetElement(reference);
                }
                Category category = seed?.Category;
                if (category == null) return Result.Cancelled;
                if (!view.CanCategoryBeHidden(category.Id))
                {
                    TaskDialog.Show("KStruct - Hiển thị category", "Revit không cho phép ẩn/hiện category '" + category.Name + "' trong view này.");
                    return Result.Cancelled;
                }
                bool current = view.GetCategoryHidden(category.Id);
                if (current == hide)
                {
                    TaskDialog.Show("KStruct - Hiển thị category", "Category '" + category.Name + "' đã ở trạng thái yêu cầu trong view hiện tại.");
                    return Result.Cancelled;
                }
                using (Transaction tx = new Transaction(doc, hide ? "KStruct - Ẩn category" : "KStruct - Hiện category"))
                {
                    tx.Start();
                    view.SetCategoryHidden(category.Id, hide);
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi hiển thị category.");
                }
                ui.RefreshActiveView();
                TaskDialog.Show("KStruct - Hiển thị category",
                    (hide ? "Đã ẩn " : "Đã hiện ") + "category '" + category.Name + "' trong view '" + view.Name + "'.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Hiển thị category", ex.Message);
                return Result.Failed;
            }
        }
    }
}
