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
    public sealed class DimensionSelectedGridsCommand : IExternalCommand
    {
        private sealed class GridFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Grid;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class GridLine
        {
            public Grid Grid;
            public Line VisibleLine;
            public Reference Reference;
            public double Offset;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            View view = ui.ActiveView;
            try
            {
                if (!(view is ViewPlan) || view.ViewType == ViewType.EngineeringPlan)
                    throw new InvalidOperationException("Mở mặt bằng kết cấu trước khi tạo dim Grid.");

                List<Grid> grids = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Grid>().Distinct().ToList();
                if (grids.Count < 2)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new GridFilter(),
                        "Chọn ít nhất hai Grid thẳng, song song để tạo chuỗi kích thước");
                    grids = picked.Select(reference => doc.GetElement(reference)).OfType<Grid>().Distinct().ToList();
                }
                if (grids.Count < 2) throw new InvalidOperationException("Cần chọn ít nhất hai Grid.");

                List<GridLine> lines = grids.Select(grid => ResolveGridLine(grid, view)).ToList();
                XYZ tangent = lines[0].VisibleLine.Direction.Normalize();
                XYZ viewNormal = view.ViewDirection.Normalize();
                XYZ dimensionDirection = viewNormal.CrossProduct(tangent).Normalize();
                foreach (GridLine gridLine in lines)
                {
                    XYZ direction = gridLine.VisibleLine.Direction.Normalize();
                    if (Math.Abs(direction.DotProduct(tangent)) < 0.9999)
                        throw new InvalidOperationException("Các Grid phải thẳng và song song. Nhóm Grid vuông góc hoặc cong cần tạo dim riêng.");
                    gridLine.Offset = gridLine.VisibleLine.Evaluate(0.5, true).DotProduct(dimensionDirection);
                }
                lines = lines.OrderBy(item => item.Offset).ToList();
                if (lines.Select(item => item.Offset).Distinct().Count() < 2)
                    throw new InvalidOperationException("Các Grid được chọn trùng vị trí trong mặt bằng.");

                XYZ pickedPoint = ui.Selection.PickPoint("Chọn vị trí đặt đường kích thước cho chuỗi Grid");
                double along = pickedPoint.DotProduct(tangent);
                double elevation = pickedPoint.DotProduct(viewNormal);
                double extension = UnitUtils.ConvertToInternalUnits(600, UnitTypeId.Millimeters);
                XYZ start = tangent * along + dimensionDirection * (lines.First().Offset - extension) + viewNormal * elevation;
                XYZ end = tangent * along + dimensionDirection * (lines.Last().Offset + extension) + viewNormal * elevation;
                var references = new ReferenceArray();
                foreach (GridLine gridLine in lines) references.Append(gridLine.Reference);

                using (Transaction transaction = new Transaction(doc, "KStruct - Dimension Grid"))
                {
                    transaction.Start();
                    Dimension dimension = doc.Create.NewDimension(view, Line.CreateBound(start, end), references);
                    if (dimension == null)
                    {
                        transaction.RollBack();
                        throw new InvalidOperationException("Revit không tạo được chuỗi kích thước cho các Grid đã chọn.");
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận kích thước Grid.");
                }
                TaskDialog.Show("KStruct - Dimension Grid", "Đã tạo chuỗi kích thước cho " + lines.Count + " Grid song song.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Dimension Grid", ex.Message);
                return Result.Failed;
            }
        }

        private static GridLine ResolveGridLine(Grid grid, View view)
        {
            if (grid.IsCurved) throw new InvalidOperationException("Grid cong chưa được hỗ trợ: " + grid.Name);
            Curve visible = grid.GetCurvesInView(DatumExtentType.ViewSpecific, view).FirstOrDefault();
            if (visible == null) visible = grid.GetCurvesInView(DatumExtentType.Model, view).FirstOrDefault();
            Line line = visible as Line;
            if (line == null) throw new InvalidOperationException("Không đọc được đường Grid thẳng trong view: " + grid.Name);
            Reference reference = line.Reference ?? grid.Curve?.Reference;
            if (reference == null) throw new InvalidOperationException("Grid không cung cấp reference để dimension: " + grid.Name);
            return new GridLine { Grid = grid, VisibleLine = line, Reference = reference };
        }
    }
}
