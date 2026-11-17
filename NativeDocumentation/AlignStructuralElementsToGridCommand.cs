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
    public sealed class AlignStructuralElementsToGridCommand : IExternalCommand
    {
        private sealed class GridFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Grid;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class StructuralElementFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => GetKind(element) != ElementKind.Unsupported;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private enum ElementKind { Unsupported, PointColumn, CurveMember }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Element> selected = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .Where(element => GetKind(element) != ElementKind.Unsupported).Distinct().ToList();
                if (selected.Count == 0)
                {
                    IList<Reference> pickedElements = ui.Selection.PickObjects(ObjectType.Element,
                        new StructuralElementFilter(), "Chọn cột, dầm hoặc tường cần căn theo Grid");
                    selected = pickedElements.Select(reference => doc.GetElement(reference))
                        .Where(element => element != null && GetKind(element) != ElementKind.Unsupported).Distinct().ToList();
                }
                if (selected.Count == 0) return Result.Cancelled;

                Reference gridReference = ui.Selection.PickObject(ObjectType.Element, new GridFilter(),
                    "Chọn Grid thẳng làm trục căn cấu kiện");
                Grid grid = doc.GetElement(gridReference) as Grid;
                Line gridLine = grid?.Curve as Line;
                if (gridLine == null) throw new InvalidOperationException("Chỉ hỗ trợ Grid thẳng.");
                XYZ axis = new XYZ(gridLine.Direction.X, gridLine.Direction.Y, 0);
                if (axis.GetLength() < 1e-9) throw new InvalidOperationException("Grid không có phương nằm ngang hợp lệ.");
                axis = axis.Normalize();
                XYZ origin = new XYZ(gridLine.GetEndPoint(0).X, gridLine.GetEndPoint(0).Y, 0);

                int moved = 0;
                var skipped = new List<string>();
                using (Transaction transaction = new Transaction(doc, "KStruct - Căn cấu kiện theo Grid"))
                {
                    transaction.Start();
                    foreach (Element element in selected)
                    {
                        try
                        {
                            XYZ anchor = GetAnchor(element);
                            XYZ planar = new XYZ(anchor.X, anchor.Y, 0);
                            XYZ projected = origin + axis * (planar - origin).DotProduct(axis);
                            XYZ translation = new XYZ(projected.X - anchor.X, projected.Y - anchor.Y, 0);
                            if (translation.GetLength() <= doc.Application.ShortCurveTolerance * 0.1) continue;
                            ElementTransformUtils.MoveElement(doc, element.Id, translation);
                            moved++;
                        }
                        catch (Exception ex) { skipped.Add(element.Id.Value + ": " + ex.Message); }
                    }
                    if (moved == 0)
                    {
                        transaction.RollBack();
                        TaskDialog.Show("KStruct - Căn theo Grid", "Không có cấu kiện nào cần dịch chuyển hoặc Revit từ chối thao tác.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi vị trí cấu kiện.");
                }

                string report = "Đã căn " + moved + "/" + selected.Count + " cấu kiện lên Grid " + grid.Name +
                    ". Cột căn theo LocationPoint; dầm/tường căn theo trung điểm. Chiều dài và hướng cấu kiện được giữ nguyên.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " cấu kiện:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Căn theo Grid", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Căn theo Grid", ex.Message);
                return Result.Failed;
            }
        }

        private static ElementKind GetKind(Element element)
        {
            if (element == null || element.Category == null) return ElementKind.Unsupported;
            long category = element.Category.Id.Value;
            if (category == (long)BuiltInCategory.OST_StructuralColumns && element is FamilyInstance)
                return ElementKind.PointColumn;
            if (category == (long)BuiltInCategory.OST_StructuralFraming ||
                category == (long)BuiltInCategory.OST_Walls && element is Wall)
                return ElementKind.CurveMember;
            return ElementKind.Unsupported;
        }

        private static XYZ GetAnchor(Element element)
        {
            if (GetKind(element) == ElementKind.PointColumn && element.Location is LocationPoint point)
                return point.Point;
            LocationCurve location = element.Location as LocationCurve;
            if (location?.Curve is Line line) return line.Evaluate(0.5, true);
            throw new InvalidOperationException("Cấu kiện không có vị trí dạng điểm hoặc trục thẳng.");
        }
    }
}
