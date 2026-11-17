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
    public sealed class AlignWallsByPickedPointsCommand : IExternalCommand
    {
        private sealed class WallFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Wall;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class OneWallFilter : ISelectionFilter
        {
            private readonly Document _doc;
            private readonly ElementId _wallId;
            public OneWallFilter(Document doc, ElementId wallId) { _doc = doc; _wallId = wallId; }
            public bool AllowElement(Element element) => element != null && element.Id == _wallId;
            public bool AllowReference(Reference reference, XYZ position) =>
                reference != null && reference.ElementId == _wallId;
        }

        private sealed class AnyPointFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element != null;
            public bool AllowReference(Reference reference, XYZ position) => reference != null;
        }

        private sealed class Move
        {
            public Wall Wall;
            public XYZ Delta;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Wall> walls = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Wall>().Distinct().ToList();
                if (walls.Count == 0)
                {
                    IList<Reference> pickedWalls = ui.Selection.PickObjects(ObjectType.Element, new WallFilter(),
                        "Chọn các tường cần căn theo điểm tham chiếu");
                    walls = pickedWalls.Select(r => doc.GetElement(r)).OfType<Wall>().Distinct().ToList();
                }
                if (walls.Count == 0) return Result.Cancelled;

                var moves = new List<Move>();
                foreach (Wall wall in walls)
                {
                    Reference wallAnchor = ui.Selection.PickObject(ObjectType.PointOnElement,
                        new OneWallFilter(doc, wall.Id), "Chọn điểm neo trên tường " + wall.Id.Value);
                    Reference targetAnchor = ui.Selection.PickObject(ObjectType.PointOnElement,
                        new AnyPointFilter(), "Chọn điểm đích trên grid hoặc cấu kiện cho tường " + wall.Id.Value);
                    if (wallAnchor.GlobalPoint == null || targetAnchor.GlobalPoint == null)
                        throw new InvalidOperationException("Revit không trả về tọa độ điểm neo.");
                    XYZ delta = new XYZ(targetAnchor.GlobalPoint.X - wallAnchor.GlobalPoint.X,
                        targetAnchor.GlobalPoint.Y - wallAnchor.GlobalPoint.Y, 0);
                    if (delta.GetLength() <= doc.Application.ShortCurveTolerance)
                        throw new InvalidOperationException("Điểm neo và điểm đích đã trùng nhau hoặc quá gần.");
                    moves.Add(new Move { Wall = wall, Delta = delta });
                }

                int moved = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Căn tường theo điểm"))
                {
                    tx.Start();
                    foreach (Move move in moves)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                ElementTransformUtils.MoveElement(doc, move.Wall.Id, move.Delta);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không hoàn tất dịch tường.");
                                moved++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Tường " + move.Wall.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (moved == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Căn tường", "Không dịch được tường nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi vị trí tường.");
                }
                string report = "Đã căn " + moved + "/" + moves.Count + " tường theo điểm tham chiếu trong mặt bằng; cao độ không đổi.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " tường:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Căn tường", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Căn tường", ex.Message);
                return Result.Failed;
            }
        }
    }
}
