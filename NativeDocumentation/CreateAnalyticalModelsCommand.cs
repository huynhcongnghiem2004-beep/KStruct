using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CreateAnalyticalModelsCommand : IExternalCommand
    {
        private sealed class PhysicalStructureFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element != null && element.Category != null;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Element> physical = ui.Selection.GetElementIds().Select(doc.GetElement).Where(IsSupportedPhysical).ToList();
                if (physical.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new PhysicalStructureFilter(),
                        "Chọn dầm, cột, sàn hoặc tường kết cấu để tạo Analytical Member/Panel");
                    physical = picked.Select(r => doc.GetElement(r)).Where(IsSupportedPhysical).ToList();
                }
                if (physical.Count == 0) return Result.Cancelled;

                AnalyticalToPhysicalAssociationManager manager = AnalyticalToPhysicalAssociationManager.GetAnalyticalToPhysicalAssociationManager(doc);
                if (manager == null) throw new InvalidOperationException("Revit không khởi tạo được AnalyticalToPhysicalAssociationManager.");
                int members = 0, panels = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Tạo mô hình phân tích kết cấu"))
                {
                    tx.Start();
                    foreach (Element element in physical.GroupBy(e => e.Id).Select(g => g.First()))
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                if (manager.HasAssociation(element.Id))
                                    throw new InvalidOperationException("Cấu kiện đã có liên kết analytical; bỏ qua để tránh tạo trùng.");
                                Element analytical = CreateAnalyticalElement(doc, element);
                                if (analytical == null) throw new InvalidOperationException("Không tạo được analytical element từ hình học cấu kiện.");
                                if (analytical is AnalyticalMember member)
                                {
                                    FamilyInstance instance = element as FamilyInstance;
                                    if (instance?.Symbol != null && IsMemberCategory(element))
                                        member.SectionTypeId = instance.Symbol.Id;
                                }
                                manager.AddAssociation(analytical.Id, element.Id);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận analytical element hoặc association.");
                                if (analytical is AnalyticalMember) members++;
                                else panels++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add((element.Category?.Name ?? "Cấu kiện") + " " + element.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (members + panels == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Analytical", "Không tạo được analytical element nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các analytical element.");
                }
                string report = "Đã tạo " + members + " Analytical Member và " + panels + " Analytical Panel, có liên kết với cấu kiện vật lý.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " cấu kiện:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Analytical", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Analytical", ex.Message);
                return Result.Failed;
            }
        }

        private static Element CreateAnalyticalElement(Document doc, Element physical)
        {
            if (IsMemberCategory(physical))
            {
                Curve curve = (physical.Location as LocationCurve)?.Curve;
                if (curve == null && physical.Category.Id.Value == (long)BuiltInCategory.OST_StructuralColumns)
                {
                    LocationPoint location = physical.Location as LocationPoint;
                    BoundingBoxXYZ box = physical.get_BoundingBox(null);
                    if (location != null && box != null && box.Max.Z - box.Min.Z > doc.Application.ShortCurveTolerance)
                        curve = Line.CreateBound(new XYZ(location.Point.X, location.Point.Y, box.Min.Z),
                            new XYZ(location.Point.X, location.Point.Y, box.Max.Z));
                }
                if (curve == null || !curve.IsBound || !AnalyticalMember.IsValidCurve(curve))
                    throw new InvalidOperationException("Cấu kiện không có LocationCurve hợp lệ hoặc không phải cột đứng.");
                return AnalyticalMember.Create(doc, curve);
            }

            PlanarFace face = FindPanelFace(doc, physical);
            if (face == null) throw new InvalidOperationException("Không tìm được mặt phẳng chính của tường/sàn.");
            IList<CurveLoop> loops = face.GetEdgesAsCurveLoops();
            if (loops == null || loops.Count == 0) throw new InvalidOperationException("Không đọc được biên mặt phẳng.");
            List<CurveLoop> orderedLoops = loops.OrderByDescending(LoopAreaApproximation).ToList();
            CurveLoop boundary = orderedLoops.FirstOrDefault();
            if (boundary == null) throw new InvalidOperationException("Không đọc được biên mặt phẳng.");
            double thickness = GetHostThickness(doc, physical);
            if (thickness <= doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Không xác định được chiều dày của tường/sàn/móng.");
            XYZ shift = -face.FaceNormal.Normalize() * (thickness / 2.0);
            CurveLoop middleBoundary = TransformLoop(boundary, shift);
            AnalyticalPanel panel = AnalyticalPanel.Create(doc, middleBoundary);
            foreach (CurveLoop openingLoop in orderedLoops.Skip(1))
                AnalyticalOpening.Create(doc, TransformLoop(openingLoop, shift), panel.Id);
            return panel;
        }

        private static PlanarFace FindPanelFace(Document doc, Element element)
        {
            if (element is Floor floor)
            {
                return HostObjectUtils.GetTopFaces(floor)
                    .Select(r => floor.GetGeometryObjectFromReference(r) as PlanarFace)
                    .Where(f => f != null && Math.Abs(f.FaceNormal.Normalize().DotProduct(XYZ.BasisZ)) > 0.999)
                    .OrderByDescending(f => f.Area).FirstOrDefault();
            }
            if (element is Wall wall)
            {
                return HostObjectUtils.GetSideFaces(wall, ShellLayerType.Exterior)
                    .Select(r => wall.GetGeometryObjectFromReference(r) as PlanarFace)
                    .Where(f => f != null && Math.Abs(f.FaceNormal.Normalize().DotProduct(XYZ.BasisZ)) < 0.01)
                    .OrderByDescending(f => f.Area).FirstOrDefault();
            }
            Options options = new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine };
            return GetSolids(element.get_Geometry(options))
                .SelectMany(solid => solid.Faces.Cast<Face>())
                .OfType<PlanarFace>()
                .Where(f => f.FaceNormal.Normalize().DotProduct(XYZ.BasisZ) > 0.999)
                .OrderByDescending(f => f.Area).FirstOrDefault();
        }

        private static IEnumerable<Solid> GetSolids(GeometryElement geometry)
        {
            if (geometry == null) yield break;
            foreach (GeometryObject item in geometry)
            {
                if (item is Solid solid && solid.Volume > 1e-9) yield return solid;
                else if (item is GeometryInstance instance)
                    foreach (Solid nested in GetSolids(instance.GetInstanceGeometry())) yield return nested;
            }
        }

        private static CurveLoop TransformLoop(CurveLoop source, XYZ shift)
        {
            var result = new CurveLoop();
            Transform transform = Transform.CreateTranslation(shift);
            foreach (Curve curve in source) result.Append(curve.CreateTransformed(transform));
            return result;
        }

        private static double GetHostThickness(Document doc, Element element)
        {
            if (element is Wall wall) return wall.Width;
            if (element is Floor floor)
            {
                FloorType type = doc.GetElement(floor.GetTypeId()) as FloorType;
                CompoundStructure structure = type?.GetCompoundStructure();
                return structure == null ? 0 : structure.GetWidth();
            }
            BoundingBoxXYZ box = element.get_BoundingBox(null);
            if (box == null) return 0;
            List<XYZ> corners = new List<XYZ>();
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                        corners.Add(box.Transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X,
                            y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z)));
            return corners.Max(p => p.Z) - corners.Min(p => p.Z);
        }

        private static double LoopAreaApproximation(CurveLoop loop)
        {
            List<XYZ> points = loop.SelectMany(c => c.Tessellate()).ToList();
            if (points.Count < 3) return 0;
            XYZ normal = XYZ.Zero;
            for (int i = 0; i < points.Count; i++)
                normal += points[i].CrossProduct(points[(i + 1) % points.Count]);
            return normal.GetLength() / 2.0;
        }

        private static bool IsMemberCategory(Element element)
        {
            long id = element?.Category?.Id.Value ?? -1;
            return id == (long)BuiltInCategory.OST_StructuralColumns ||
                   id == (long)BuiltInCategory.OST_StructuralFraming;
        }

        internal static bool IsSupportedPhysical(Element element)
        {
            if (element?.Category == null) return false;
            long id = element.Category.Id.Value;
            return id == (long)BuiltInCategory.OST_StructuralColumns ||
                   id == (long)BuiltInCategory.OST_StructuralFraming ||
                   id == (long)BuiltInCategory.OST_StructuralFoundation ||
                   id == (long)BuiltInCategory.OST_Walls ||
                   id == (long)BuiltInCategory.OST_Floors;
        }
    }
}
