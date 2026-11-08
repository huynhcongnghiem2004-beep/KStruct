using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class TagElementAtPickedPointCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                Reference target = ui.Selection.PickObject(ObjectType.Element, "Chọn cấu kiện cần gắn tag");
                XYZ point = ui.Selection.PickPoint("Chọn vị trí đặt tag");
                using (Transaction tx = new Transaction(doc, "KStruct - Gắn tag cấu kiện"))
                {
                    tx.Start();
                    IndependentTag tag = IndependentTag.Create(
                        doc,
                        doc.ActiveView.Id,
                        target,
                        false,
                        TagMode.TM_ADDBY_CATEGORY,
                        TagOrientation.Horizontal,
                        point);
                    if (tag == null)
                        throw new InvalidOperationException("Revit không tạo được tag. Kiểm tra project đã nạp tag cho category của cấu kiện chưa.");
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận tag mới.");
                }
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }
            catch (System.Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Gắn tag", ex.Message);
                return Result.Failed;
            }
        }
    }
}
