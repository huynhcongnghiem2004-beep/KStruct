using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeDocumentation
{
    /// <summary>Pan the currently open 3D view by translating its visible model-space rectangle.</summary>
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.ReadOnly)]
    public sealed class Move3DViewCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            View3D view = ui.ActiveView as View3D;
            if (view == null || view.IsTemplate)
            {
                TaskDialog.Show("KStruct - Di chuyển view 3D", "Mở một view 3D không phải template trước khi chạy lệnh.");
                return Result.Cancelled;
            }

            try
            {
                UIView uiView = ui.GetOpenUIViews().FirstOrDefault(openView => openView.ViewId == view.Id);
                if (uiView == null) throw new InvalidOperationException("Không tìm thấy cửa sổ UI của view 3D đang mở.");
                var corners = uiView.GetZoomCorners();
                if (corners == null || corners.Count != 2)
                    throw new InvalidOperationException("Revit không trả về được vùng nhìn hiện tại.");

                XYZ source = ui.Selection.PickPoint("Chọn điểm tham chiếu hiện tại trong view 3D");
                XYZ target = ui.Selection.PickPoint("Chọn vị trí mới của điểm tham chiếu trong view 3D");
                XYZ drag = target - source;
                ViewOrientation3D orientation = view.GetOrientation();
                XYZ forward = orientation.ForwardDirection.Normalize();
                XYZ up = orientation.UpDirection.Normalize();
                XYZ right = forward.CrossProduct(up).Normalize();
                XYZ projected = right * drag.DotProduct(right) + up * drag.DotProduct(up);
                if (projected.GetLength() < ui.Document.Application.ShortCurveTolerance)
                    throw new InvalidOperationException("Hai điểm không tạo được độ dịch chuyển trên mặt phẳng màn hình.");

                // Move the view rectangle opposite to the picked drag so model content follows the cursor.
                XYZ pan = projected.Negate();
                uiView.ZoomAndCenterRectangle(corners[0] + pan, corners[1] + pan);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Di chuyển view 3D", ex.Message);
                return Result.Failed;
            }
        }
    }
}
