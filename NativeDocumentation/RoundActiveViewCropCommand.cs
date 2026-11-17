using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class RoundActiveViewCropCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            View view = doc.ActiveView;
            try
            {
                if (view == null || view.IsTemplate) throw new InvalidOperationException("View hiện hành không hỗ trợ crop.");
                ViewCropRegionShapeManager manager = view.GetCropRegionShapeManager();
                if (manager == null || !manager.CanHaveShape)
                    throw new InvalidOperationException("Loại view hiện hành không hỗ trợ crop region dạng hình học.");
                BoundingBoxXYZ box = view.CropBox;
                if (box == null) throw new InvalidOperationException("View chưa có CropBox.");
                double width = box.Max.X - box.Min.X;
                double height = box.Max.Y - box.Min.Y;
                double radius = Math.Min(width, height) / 2.0;
                if (radius < doc.Application.ShortCurveTolerance * 2)
                    throw new InvalidOperationException("CropBox hiện hành quá nhỏ hoặc chưa được cấu hình.");
                Transform transform = box.Transform ?? Transform.Identity;
                XYZ center = transform.OfPoint(new XYZ((box.Min.X + box.Max.X) / 2.0,
                    (box.Min.Y + box.Max.Y) / 2.0, 0));
                XYZ x = view.RightDirection.Normalize();
                XYZ y = view.UpDirection.Normalize();
                CurveLoop loop = new CurveLoop();
                loop.Append(Arc.Create(center, radius, 0, Math.PI, x, y));
                loop.Append(Arc.Create(center, radius, Math.PI, 2 * Math.PI, x, y));
                if (!manager.IsCropRegionShapeValid(loop))
                    throw new InvalidOperationException("Revit không chấp nhận crop tròn cho view này.");

                using (Transaction tx = new Transaction(doc, "KStruct - Crop tròn view"))
                {
                    tx.Start();
                    view.CropBoxActive = true;
                    manager.SetCropShape(loop);
                    view.CropBoxVisible = true;
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận crop region.");
                }
                TaskDialog.Show("KStruct - Crop tròn", "Đã đặt crop tròn cho view: " + view.Name);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Crop tròn", ex.Message);
                return Result.Failed;
            }
        }
    }
}
