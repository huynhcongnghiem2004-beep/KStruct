using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class SplitStraightWallCommand : IExternalCommand
    {
        private sealed class WallFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Wall;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                Wall wall = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Wall>().FirstOrDefault();
                if (wall == null)
                {
                    Reference picked = ui.Selection.PickObject(ObjectType.Element, new WallFilter(), "Chọn tường thẳng cần chia");
                    wall = doc.GetElement(picked) as Wall;
                }
                if (wall == null) return Result.Cancelled;
                WallType type = doc.GetElement(wall.GetTypeId()) as WallType;
                if (type == null || type.Kind != WallKind.Basic)
                    throw new InvalidOperationException("Chỉ hỗ trợ Basic Wall; curtain wall và stacked wall chưa được chia bằng lệnh này.");
                Line axis = (wall.Location as LocationCurve)?.Curve as Line;
                if (axis == null) throw new InvalidOperationException("Chỉ hỗ trợ tường có trục thẳng.");
                if (wall.FindInserts(true, true, true, true).Count > 0)
                    throw new InvalidOperationException("Tường có cửa, cửa sổ hoặc insert. Hãy xử lý các insert trước khi chia để tránh đổi host ngoài ý muốn.");

                XYZ start = axis.GetEndPoint(0), end = axis.GetEndPoint(1);
                XYZ vector = end - start;
                double length = vector.GetLength();
                if (length <= doc.Application.ShortCurveTolerance * 2)
                    throw new InvalidOperationException("Tường quá ngắn để chia.");
                XYZ direction = vector.Normalize();
                XYZ pickedPoint = ui.Selection.PickPoint("Chọn điểm chia trên trục tường");
                XYZ delta = pickedPoint - start;
                double station = delta.DotProduct(direction);
                XYZ split = start + direction.Multiply(station);
                double offAxis = new XYZ(pickedPoint.X - split.X, pickedPoint.Y - split.Y, 0).GetLength();
                double pickTolerance = UnitUtils.ConvertToInternalUnits(200, UnitTypeId.Millimeters);
                if (offAxis > pickTolerance)
                    throw new InvalidOperationException("Điểm chọn lệch trục tường quá 200 mm.");
                double endTolerance = Math.Max(doc.Application.ShortCurveTolerance * 2,
                    UnitUtils.ConvertToInternalUnits(100, UnitTypeId.Millimeters));
                if (station <= endTolerance || length - station <= endTolerance)
                    throw new InvalidOperationException("Điểm chia phải cách hai đầu tường ít nhất 100 mm.");

                bool wasPinned = wall.Pinned;
                Wall newWall = null;
                using (Transaction tx = new Transaction(doc, "KStruct - Chia tường thẳng"))
                {
                    tx.Start();
                    if (wasPinned) wall.Pinned = false;
                    ICollection<ElementId> copiedIds = ElementTransformUtils.CopyElement(doc, wall.Id, XYZ.Zero);
                    newWall = copiedIds.Select(doc.GetElement).OfType<Wall>()
                        .FirstOrDefault(candidate => candidate.Id != wall.Id);
                    if (newWall == null) throw new InvalidOperationException("Không tạo được bản sao tường.");
                    if (newWall.Pinned) newWall.Pinned = false;

                    WallUtils.DisallowWallJoinAtEnd(wall, 1);
                    WallUtils.DisallowWallJoinAtEnd(newWall, 0);
                    (wall.Location as LocationCurve).Curve = Line.CreateBound(start, split);
                    (newWall.Location as LocationCurve).Curve = Line.CreateBound(split, end);
                    if (wasPinned)
                    {
                        wall.Pinned = true;
                        newWall.Pinned = true;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận chia tường.");
                }
                TaskDialog.Show("KStruct - Chia tường",
                    "Đã chia Wall " + wall.Id.Value + " thành hai đoạn. Tường mới: " + newWall.Id.Value + ".");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Chia tường", ex.Message);
                return Result.Failed;
            }
        }
    }
}
