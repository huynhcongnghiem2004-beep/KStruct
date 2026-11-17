using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class DimensionBeamSectionCommand : IExternalCommand
    {
        private sealed class FaceReference
        {
            public Reference Reference;
            public XYZ Point;
            public XYZ Normal;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            View view = ui.ActiveView;
            if (!(view is ViewSection) || view.IsTemplate)
            {
                TaskDialog.Show("KStruct - Dim mặt cắt dầm", "Mở mặt cắt ngang dầm trước khi chạy lệnh.");
                return Result.Cancelled;
            }

            Document doc = ui.Document;
            try
            {
                var beams = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<FamilyInstance>()
                    .Where(IsStructuralFraming).Distinct().ToList();
                if (beams.Count == 0)
                    beams = new FilteredElementCollector(doc, view.Id).OfClass(typeof(FamilyInstance))
                        .WhereElementIsNotElementType().Cast<FamilyInstance>().Where(IsStructuralFraming).ToList();
                if (beams.Count == 0)
                {
                    TaskDialog.Show("KStruct - Dim mặt cắt dầm", "Không tìm thấy dầm kết cấu trong mặt cắt hiện tại.");
                    return Result.Cancelled;
                }

                int created = 0;
                var skipped = new List<string>();
                using (var tx = new Transaction(doc, "KStruct - Dim mặt cắt dầm"))
                {
                    tx.Start();
                    foreach (FamilyInstance beam in beams)
                    {
                        using (var sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                int count = CreateDimensions(doc, view, beam);
                                if (count == 0) throw new InvalidOperationException("Không tìm thấy đủ mặt phẳng tham chiếu ngang/dọc.");
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận kích thước.");
                                created += count;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Dầm " + beam.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (created == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Dim mặt cắt dầm", "Không tạo được kích thước nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các kích thước mới.");
                }

                string report = "Đã tạo " + created + " kích thước cho " + beams.Count + " dầm trong mặt cắt.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " dầm:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Dim mặt cắt dầm", report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Dim mặt cắt dầm", ex.Message);
                return Result.Failed;
            }
        }

        internal static int CreateDimensions(Document doc, View view, FamilyInstance beam)
        {
            List<FaceReference> faces = GetFaces(beam, view);
            XYZ right = view.RightDirection.Normalize();
            XYZ up = view.UpDirection.Normalize();
            XYZ normal = view.ViewDirection.Normalize();
            XYZ origin = view.Origin;
            double scale = Math.Max(1, view.Scale);
            double offset = UnitUtils.ConvertToInternalUnits(12.0 * scale, UnitTypeId.Millimeters);

            var projected = faces.Select(f => new FaceReference
            {
                Reference = f.Reference,
                Point = ProjectToView(f.Point, origin, normal),
                Normal = f.Normal
            }).Where(f => f.Reference != null).ToList();

            List<FaceReference> rightFaces = projected.Where(f => Math.Abs(f.Normal.DotProduct(right)) > 0.95).ToList();
            List<FaceReference> upFaces = projected.Where(f => Math.Abs(f.Normal.DotProduct(up)) > 0.95).ToList();
            int created = 0;

            if (rightFaces.Count >= 2)
            {
                FaceReference left = rightFaces.OrderBy(f => (f.Point - origin).DotProduct(right)).First();
                FaceReference rightFace = rightFaces.OrderByDescending(f => (f.Point - origin).DotProduct(right)).First();
                double a = (left.Point - origin).DotProduct(right);
                double b = (rightFace.Point - origin).DotProduct(right);
                double low = projected.Min(f => (f.Point - origin).DotProduct(up)) - offset;
                XYZ p0 = origin + right * a + up * low;
                XYZ p1 = origin + right * b + up * low;
                if (a < b - doc.Application.ShortCurveTolerance)
                    created += AddDimension(doc, view, p0, p1, left.Reference, rightFace.Reference);
            }

            if (upFaces.Count >= 2)
            {
                FaceReference bottom = upFaces.OrderBy(f => (f.Point - origin).DotProduct(up)).First();
                FaceReference top = upFaces.OrderByDescending(f => (f.Point - origin).DotProduct(up)).First();
                double a = (bottom.Point - origin).DotProduct(up);
                double b = (top.Point - origin).DotProduct(up);
                double left = projected.Min(f => (f.Point - origin).DotProduct(right)) - offset;
                XYZ p0 = origin + right * left + up * a;
                XYZ p1 = origin + right * left + up * b;
                if (a < b - doc.Application.ShortCurveTolerance)
                    created += AddDimension(doc, view, p0, p1, bottom.Reference, top.Reference);
            }
            return created;
        }

        private static int AddDimension(Document doc, View view, XYZ start, XYZ end, Reference first, Reference second)
        {
            var references = new ReferenceArray();
            references.Append(first);
            references.Append(second);
            doc.Create.NewDimension(view, Line.CreateBound(start, end), references);
            return 1;
        }

        private static XYZ ProjectToView(XYZ point, XYZ origin, XYZ normal)
        {
            return point - normal * (point - origin).DotProduct(normal);
        }

        private static bool IsStructuralFraming(FamilyInstance instance)
        {
            return instance.Category != null && instance.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;
        }

        private static List<FaceReference> GetFaces(FamilyInstance beam, View view)
        {
            var result = new List<FaceReference>();
            var options = new Options { ComputeReferences = true, IncludeNonVisibleObjects = true, DetailLevel = ViewDetailLevel.Fine, View = view };
            GeometryElement geometry = beam.get_Geometry(options);
            if (geometry != null) CollectFaces(geometry, Transform.Identity, result);
            XYZ right = view.RightDirection.Normalize();
            XYZ up = view.UpDirection.Normalize();
            return result.Where(f => Math.Abs(f.Normal.DotProduct(right)) > 0.95 || Math.Abs(f.Normal.DotProduct(up)) > 0.95)
                .GroupBy(f => Math.Round(f.Point.X, 4) + ":" + Math.Round(f.Point.Y, 4) + ":" + Math.Round(f.Point.Z, 4) + ":" +
                    Math.Round(f.Normal.X, 3) + ":" + Math.Round(f.Normal.Y, 3) + ":" + Math.Round(f.Normal.Z, 3))
                .Select(g => g.First()).ToList();
        }

        private static void CollectFaces(GeometryElement geometry, Transform transform, List<FaceReference> result)
        {
            foreach (GeometryObject obj in geometry)
            {
                GeometryInstance instance = obj as GeometryInstance;
                if (instance != null)
                {
                    GeometryElement symbol = instance.GetSymbolGeometry();
                    if (symbol != null) CollectFaces(symbol, transform.Multiply(instance.Transform), result);
                    continue;
                }

                Solid solid = obj as Solid;
                if (solid == null || solid.Faces.IsEmpty) continue;
                foreach (Face face in solid.Faces)
                {
                    PlanarFace planar = face as PlanarFace;
                    if (planar == null || planar.Reference == null) continue;
                    result.Add(new FaceReference
                    {
                        Reference = planar.Reference,
                        Point = transform.OfPoint(planar.Origin),
                        Normal = transform.OfVector(planar.FaceNormal).Normalize()
                    });
                }
            }
        }
    }
}
