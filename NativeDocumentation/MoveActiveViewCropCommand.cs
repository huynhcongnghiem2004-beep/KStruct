using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class MoveActiveViewCropCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            View view = doc.ActiveView;
            try
            {
                if (view == null || view.IsTemplate || view.GetTypeId() == ElementId.InvalidElementId)
                    throw new InvalidOperationException("View hiện hành không hỗ trợ di chuyển crop.");
                BoundingBoxXYZ crop = view.CropBox;
                if (crop == null) throw new InvalidOperationException("View chưa có CropBox.");
                XYZ start = ui.Selection.PickPoint("Chọn điểm gốc để di chuyển crop");
                XYZ end = ui.Selection.PickPoint("Chọn điểm đích cho crop");
                XYZ delta = end - start;
                XYZ normal = view.ViewDirection.Normalize();
                delta = delta - normal * delta.DotProduct(normal);
                if (delta.GetLength() < doc.Application.ShortCurveTolerance)
                {
                    TaskDialog.Show("KStruct - Di chuyển crop", "Điểm gốc và điểm đích không tạo ra độ dịch chuyển trong mặt phẳng view.");
                    return Result.Cancelled;
                }
                Transform transform = crop.Transform ?? Transform.Identity;
                transform.Origin = transform.Origin + delta;
                crop.Transform = transform;
                using (Transaction tx = new Transaction(doc, "KStruct - Di chuyển crop view"))
                {
                    tx.Start();
                    view.CropBoxActive = true;
                    view.CropBox = crop;
                    view.CropBoxVisible = true;
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận vị trí crop mới.");
                }
                TaskDialog.Show("KStruct - Di chuyển crop", "Đã di chuyển crop của view " + view.Name + ".");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Di chuyển crop", ex.Message);
                return Result.Failed;
            }
        }
    }
}
