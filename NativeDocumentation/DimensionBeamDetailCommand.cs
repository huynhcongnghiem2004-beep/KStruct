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
    public sealed class DimensionBeamDetailCommand : IExternalCommand
    {
        private sealed class BeamFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is FamilyInstance fi &&
                fi.Category != null && fi.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class FaceEnd
        {
            public Reference Reference;
            public XYZ Point;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            View view = ui.ActiveView;
            try
            {
                List<FamilyInstance> beams = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .OfType<FamilyInstance>().Where(IsFraming).Distinct().ToList();
                if (beams.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new BeamFilter(),
                        "Chọn dầm cần ghi kích thước chiều dài trong view dọc");
                    beams = picked.Select(doc.GetElement).OfType<FamilyInstance>().Where(IsFraming).Distinct().ToList();
                }
                if (beams.Count == 0) return Result.Cancelled;
                if (view.IsTemplate || Math.Abs(view.ViewDirection.DotProduct(view.RightDirection)) > 0.01)
                    throw new InvalidOperationException("View hiện tại không hỗ trợ đặt kích thước dọc dầm.");

                int created = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Dim chi tiết dầm"))
                {
                    tx.Start();
                    foreach (FamilyInstance beam in beams)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                int count = CreateLengthDimension(doc, view, beam);
                                if (count == 0) throw new InvalidOperationException("Không tìm được hai mặt đầu dầm phù hợp với view.");
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
                        TaskDialog.Show("KStruct - Dim chi tiết dầm", "Không tạo được kích thước nào. Mở view đứng dọc theo dầm rồi chạy lại.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các kích thước mới.");
                }

                string report = "Đã tạo " + created + " kích thước chiều dài cho " + beams.Count + " dầm.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " dầm:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Dim chi tiết dầm", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Dim chi tiết dầm", ex.Message);
                return Result.Failed;
            }
        }

        private static int CreateLengthDimension(Document doc, View view, FamilyInstance beam)
        {
            LocationCurve location = beam.Location as LocationCurve;
            Line axis = location?.Curve as Line;
            if (axis == null) throw new InvalidOperationException("Chỉ hỗ trợ dầm có trục thẳng.");
            XYZ beamAxis = axis.Direction.Normalize();
            XYZ right = view.RightDirection.Normalize();
            if (Math.Abs(beamAxis.DotProduct(right)) < 0.95)
                throw new InvalidOperationException("Hướng nhìn chưa song song với trục dầm.");

            var ends = new List<FaceEnd>();
            var options = new Options { ComputeReferences = true, IncludeNonVisibleObjects = true, DetailLevel = ViewDetailLevel.Fine, View = view };
            GeometryElement geometry = beam.get_Geometry(options);
            if (geometry != null) CollectEndFaces(geometry, Transform.Identity, beamAxis, ends);
            if (ends.Count < 2) return 0;

            XYZ origin = view.Origin;
            XYZ up = view.UpDirection.Normalize();
            double min = ends.Min(x => (x.Point - origin).DotProduct(right));
            double max = ends.Max(x => (x.Point - origin).DotProduct(right));
            FaceEnd first = ends.OrderBy(x => (x.Point - origin).DotProduct(right)).First();
            FaceEnd last = ends.OrderByDescending(x => (x.Point - origin).DotProduct(right)).First();
            if (max - min < doc.Application.ShortCurveTolerance) return 0;
            double low = ends.Min(x => (x.Point - origin).DotProduct(up));
            double offset = UnitUtils.ConvertToInternalUnits(12.0 * Math.Max(1, view.Scale), UnitTypeId.Millimeters);
            XYZ start = origin + right * min + up * (low - offset);
            XYZ end = origin + right * max + up * (low - offset);
            var references = new ReferenceArray();
            references.Append(first.Reference);
            references.Append(last.Reference);
            doc.Create.NewDimension(view, Line.CreateBound(start, end), references);
            return 1;
        }

        private static void CollectEndFaces(GeometryElement geometry, Transform transform, XYZ axis, List<FaceEnd> ends)
        {
            foreach (GeometryObject obj in geometry)
            {
                GeometryInstance instance = obj as GeometryInstance;
                if (instance != null)
                {
                    GeometryElement symbol = instance.GetSymbolGeometry();
                    if (symbol != null) CollectEndFaces(symbol, transform.Multiply(instance.Transform), axis, ends);
                    continue;
                }
                Solid solid = obj as Solid;
                if (solid == null || solid.Faces.IsEmpty) continue;
                foreach (Face face in solid.Faces)
                {
                    PlanarFace planar = face as PlanarFace;
                    if (planar == null || planar.Reference == null) continue;
                    XYZ normal = transform.OfVector(planar.FaceNormal).Normalize();
                    if (Math.Abs(normal.DotProduct(axis)) < 0.95) continue;
                    ends.Add(new FaceEnd { Reference = planar.Reference, Point = transform.OfPoint(planar.Origin) });
                }
            }
        }

        private static bool IsFraming(FamilyInstance instance) => instance.Category != null &&
            instance.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;
    }
}
