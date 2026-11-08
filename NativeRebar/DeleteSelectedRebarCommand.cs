using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class DeleteSelectedRebarCommand : IExternalCommand
    {
        private sealed class RebarFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Rebar;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<ElementId> ids = ui.Selection.GetElementIds()
                    .Select(doc.GetElement).OfType<Rebar>().Select(x => x.Id).Distinct().ToList();
                if (ids.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new RebarFilter(), "Chọn các thanh thép cần xóa");
                    ids = picked.Select(r => doc.GetElement(r)).OfType<Rebar>().Select(x => x.Id).Distinct().ToList();
                }
                if (ids.Count == 0) return Result.Cancelled;

                TaskDialogResult answer = TaskDialog.Show("KStruct - Xóa cốt thép",
                    "Xóa " + ids.Count + " đối tượng Rebar khỏi mô hình? Có thể hoàn tác bằng Undo.",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);
                if (answer != TaskDialogResult.Yes) return Result.Cancelled;

                using (Transaction transaction = new Transaction(doc, "KStruct - Xóa cốt thép"))
                {
                    transaction.Start();
                    ICollection<ElementId> deleted = doc.Delete(ids);
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thao tác xóa.");
                    TaskDialog.Show("KStruct - Xóa cốt thép", "Đã xóa " + deleted.Count + " phần tử. Dùng Undo để khôi phục nếu cần.");
                }
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Xóa cốt thép", ex.Message);
                return Result.Failed;
            }
        }
    }
}