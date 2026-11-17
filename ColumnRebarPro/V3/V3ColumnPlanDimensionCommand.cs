using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace ColumnRebarPro.V3
{
    [Transaction(TransactionMode.Manual)]
    public sealed class V3StructuralPlanDimensionCommand : IExternalCommand
    {
        private sealed class ColumnFilter : ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                return IsStructuralPlanElement(element);
            }

            public bool AllowReference(Reference reference, XYZ position) { return false; }
        }

        private sealed class FaceRef
        {
            public Reference Reference;
            public XYZ Point;
            public XYZ Normal;
        }

        private sealed class GridRef
        {
            public Grid Grid;
            public Line Line;
            public Reference Reference;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || !(ui.ActiveView is ViewPlan) || ui.ActiveView.IsTemplate)
            {
                TaskDialog.Show("KStruct - Dim cấu kiện", "Hãy mở mặt bằng (Plan) trước khi chạy lệnh.");
                return Result.Cancelled;
            }

            Document doc = ui.Document;
            try
            {
                var elementsToDimension = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .Where(IsStructuralPlanElement).Distinct().ToList();
                if (elementsToDimension.Count == 0)
                {
                    var picked = ui.Selection.PickObjects(ObjectType.Element, new ColumnFilter(),
                        "Chọn cột, dầm, móng hoặc tường cần dim đến lưới");
                    elementsToDimension = picked.Select(r => doc.GetElement(r))
                        .Where(IsStructuralPlanElement).Distinct().ToList();
                }
                if (elementsToDimension.Count == 0) return Result.Cancelled;

                var grids = new FilteredElementCollector(doc, ui.ActiveView.Id).OfClass(typeof(Grid)).Cast<Grid>()
                    .Select(g => MakeGridRef(g)).Where(g => g != null).ToList();
                if (grids.Count == 0)
                {
                    TaskDialog.Show("KStruct - Dim cấu kiện", "Không tìm thấy lưới thẳng trong view hiện tại.");
                    return Result.Cancelled;
                }

                int made = 0, skipped = 0;
                using (var tx = new Transaction(doc, "KStruct - Dim cấu kiện đến lưới"))
                {
                    tx.Start();
                    foreach (Element column in elementsToDimension)
                    {
                        foreach (FaceRef face in GetVerticalFaces(column))
                        {
                            GridRef grid = grids
                                .Where(g => Math.Abs(g.Line.Direction.DotProduct(face.Normal)) < 0.02)
                                .OrderBy(g => DistanceToLineXY(g.Line, face.Point))
                                .FirstOrDefault();
                            if (grid == null) { skipped++; continue; }

                            XYZ tangent = grid.Line.Direction.Normalize();
                            XYZ onGrid = grid.Line.Origin + tangent * (face.Point - grid.Line.Origin).DotProduct(tangent);
                            XYZ a = new XYZ(onGrid.X, onGrid.Y, ViewElevation(ui.ActiveView));
                            XYZ b = new XYZ(face.Point.X, face.Point.Y, ViewElevation(ui.ActiveView));
                            if (a.DistanceTo(b) < 1e-4) { skipped++; continue; }

                            try
                            {
                                var refs = new ReferenceArray();
                                refs.Append(grid.Reference);
                                refs.Append(face.Reference);
                                doc.Create.NewDimension(ui.ActiveView, Line.CreateBound(a, b), refs);
                                made++;
                            }
                            catch { skipped++; }
                        }
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit no commit giao dịch dim.");
                }

                TaskDialog.Show("KStruct - Dim cấu kiện", "Đã tạo " + made + " kích thước đến lưới cho " + elementsToDimension.Count + " cấu kiện." +
                    (skipped > 0 ? "\nBỏ qua " + skipped + " mặt không phù hợp." : ""));
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("KStruct - Dim cấu kiện", ex.Message);
                return Result.Failed;
            }
        }

        private static bool IsStructuralPlanElement(Element instance)
        {
            if (instance == null || instance.Category == null) return false;
            long category = instance.Category.Id.Value;
            return category == (long)BuiltInCategory.OST_StructuralColumns ||
                   category == (long)BuiltInCategory.OST_StructuralFraming ||
                   category == (long)BuiltInCategory.OST_StructuralFoundation ||
                   category == (long)BuiltInCategory.OST_Walls;
        }

        private static GridRef MakeGridRef(Grid grid)
        {
            Line line = grid.Curve as Line;
            if (line == null || grid.Curve.Reference == null) return null;
            XYZ direction = line.Direction;
            if (Math.Abs(direction.Z) > 0.02) return null;
            return new GridRef { Grid = grid, Line = line, Reference = grid.Curve.Reference };
        }

        private static double DistanceToLineXY(Line line, XYZ point)
        {
            XYZ delta = point - line.Origin;
            XYZ planarDirection = new XYZ(line.Direction.X, line.Direction.Y, 0).Normalize();
            XYZ planarDelta = new XYZ(delta.X, delta.Y, 0);
            return (planarDelta - planarDirection * planarDelta.DotProduct(planarDirection)).GetLength();
        }

        private static double ViewElevation(View view)
        {
            ViewPlan plan = view as ViewPlan;
            if (plan != null && plan.GenLevel != null) return plan.GenLevel.Elevation;
            return view.Origin.Z;
        }

        private static List<FaceRef> GetVerticalFaces(Element instance)
        {
            var result = new List<FaceRef>();
            var options = new Options { ComputeReferences = true, IncludeNonVisibleObjects = true, DetailLevel = ViewDetailLevel.Fine };
            GeometryElement geometry = instance.get_Geometry(options);
            if (geometry != null) CollectFaces(geometry, Transform.Identity, result);
            return result.Where(f => f.Reference != null && Math.Abs(f.Normal.Z) < 0.02)
                .GroupBy(f => Math.Round(f.Point.X, 4) + ":" + Math.Round(f.Point.Y, 4) + ":" +
                    Math.Round(f.Normal.X, 3) + ":" + Math.Round(f.Normal.Y, 3))
                .Select(g => g.First()).ToList();
        }

        private static void CollectFaces(GeometryElement geometry, Transform transform, List<FaceRef> result)
        {
            foreach (GeometryObject obj in geometry)
            {
                GeometryInstance nested = obj as GeometryInstance;
                if (nested != null)
                {
                    Transform combined = transform.Multiply(nested.Transform);
                    GeometryElement symbol = nested.GetSymbolGeometry();
                    if (symbol != null) CollectFaces(symbol, combined, result);
                    continue;
                }

                Solid solid = obj as Solid;
                if (solid == null || solid.Faces.IsEmpty) continue;
                foreach (Face face in solid.Faces)
                {
                    PlanarFace planar = face as PlanarFace;
                    if (planar == null) continue;
                    XYZ normal = transform.OfVector(planar.FaceNormal).Normalize();
                    if (Math.Abs(normal.Z) > 0.02) continue;
                    XYZ point = transform.OfPoint(planar.Origin);
                    result.Add(new FaceRef { Reference = planar.Reference, Point = point, Normal = normal });
                }
            }
        }
    }
}
