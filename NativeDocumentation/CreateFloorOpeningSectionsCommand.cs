using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CreateFloorOpeningSectionsCommand : IExternalCommand
    {
        private sealed class OpeningFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Opening;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Opening> openings = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Opening>().Distinct().ToList();
                if (openings.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new OpeningFilter(), "Chọn lỗ mở chữ nhật trên sàn để tạo hai mặt cắt");
                    openings = picked.Select(r => doc.GetElement(r)).OfType<Opening>().Distinct().ToList();
                }
                if (openings.Count == 0) return Result.Cancelled;
                ViewFamilyType sectionType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>().FirstOrDefault(t => t.ViewFamily == ViewFamily.Section);
                if (sectionType == null) throw new InvalidOperationException("Project chưa có Section View Family Type.");
                HashSet<string> names = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                    .Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                int created = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Mặt cắt lỗ mở sàn"))
                {
                    tx.Start();
                    foreach (Opening opening in openings)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                List<ViewSection> views = CreateSections(doc, opening, sectionType, names);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận các mặt cắt.");
                                foreach (ViewSection view in views) names.Add(view.Name);
                                created += views.Count;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Lỗ mở " + opening.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (created == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Mặt cắt lỗ mở", "Không tạo được mặt cắt. Hỗ trợ hiện tại: lỗ mở chữ nhật nằm ngang trong sàn.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các view mới.");
                }
                string report = "Đã tạo " + created + " mặt cắt cho " + openings.Count + " lỗ mở sàn.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " lỗ mở:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Mặt cắt lỗ mở", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Mặt cắt lỗ mở", ex.Message);
                return Result.Failed;
            }
        }

        private static List<ViewSection> CreateSections(Document doc, Opening opening, ViewFamilyType type, HashSet<string> names)
        {
            if (!opening.IsRectBoundary) throw new InvalidOperationException("Chỉ hỗ trợ lỗ mở chữ nhật.");
            Floor floor = opening.Host as Floor;
            if (floor == null) throw new InvalidOperationException("Host lỗ mở phải là sàn (Floor).");
            List<XYZ> corners = opening.BoundaryRect.Where(p => p != null).Distinct(new PointComparer()).ToList();
            if (corners.Count != 4) throw new InvalidOperationException("BoundaryRect không có đúng bốn góc.");
            XYZ center = corners.Aggregate(XYZ.Zero, (sum, p) => sum + p) / 4.0;
            corners = corners.OrderBy(p => Math.Atan2(p.Y - center.Y, p.X - center.X)).ToList();
            if (corners.Any(p => Math.Abs(p.Z - corners[0].Z) > doc.Application.ShortCurveTolerance))
                throw new InvalidOperationException("Lỗ mở không nằm trên mặt phẳng ngang.");

            List<XYZ> directions = new List<XYZ>();
            for (int i = 0; i < 4; i++)
            {
                XYZ edge = corners[(i + 1) % 4] - corners[i];
                if (edge.GetLength() < doc.Application.ShortCurveTolerance) throw new InvalidOperationException("Lỗ mở có cạnh quá ngắn.");
                XYZ direction = new XYZ(edge.X, edge.Y, 0).Normalize();
                if (!directions.Any(d => Math.Abs(d.DotProduct(direction)) > 0.999)) directions.Add(direction);
            }
            if (directions.Count != 2 || Math.Abs(directions[0].DotProduct(directions[1])) > 0.02)
                throw new InvalidOperationException("Lỗ mở không phải hình chữ nhật hợp lệ.");

            BoundingBoxXYZ hostBox = floor.get_BoundingBox(null);
            if (hostBox == null) throw new InvalidOperationException("Không đọc được hình học sàn.");
            List<XYZ> hostCorners = GetWorldCorners(hostBox);
            double minZ = hostCorners.Min(p => p.Z), maxZ = hostCorners.Max(p => p.Z);
            double originZ = (minZ + maxZ) / 2.0;
            XYZ origin = new XYZ(center.X, center.Y, originZ);
            var result = new List<ViewSection>();
            const double margin = 1.0;
            for (int i = 0; i < 2; i++)
            {
                XYZ normal = directions[i];
                XYZ basisY = XYZ.BasisZ;
                XYZ basisX = basisY.CrossProduct(normal).Normalize();
                basisY = normal.CrossProduct(basisX).Normalize();
                double halfWidth = corners.Max(p => Math.Abs((p - center).DotProduct(basisX))) + margin;
                double halfDepth = corners.Max(p => Math.Abs((p - center).DotProduct(normal))) + margin;
                Transform transform = Transform.Identity;
                transform.Origin = origin;
                transform.BasisX = basisX;
                transform.BasisY = basisY;
                transform.BasisZ = normal;
                var box = new BoundingBoxXYZ
                {
                    Transform = transform,
                    Min = new XYZ(-halfWidth, minZ - originZ - margin, -halfDepth),
                    Max = new XYZ(halfWidth, maxZ - originZ + margin, halfDepth)
                };
                ViewSection view = ViewSection.CreateSection(doc, type.Id, box);
                string mark = floor.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
                string label = string.IsNullOrWhiteSpace(mark) ? floor.Id.Value.ToString() : mark.Trim();
                view.Name = UniqueName("KStruct - MC lỗ mở " + opening.Id.Value + " - " + label + " - " + (i + 1), names);
                view.CropBoxActive = true;
                view.CropBoxVisible = false;
                result.Add(view);
            }
            return result;
        }

        private static List<XYZ> GetWorldCorners(BoundingBoxXYZ box)
        {
            Transform transform = box.Transform ?? Transform.Identity;
            var points = new List<XYZ>();
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                points.Add(transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X,
                    y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z)));
            return points;
        }

        private sealed class PointComparer : IEqualityComparer<XYZ>
        {
            public bool Equals(XYZ a, XYZ b) => a != null && b != null && a.DistanceTo(b) < 1e-7;
            public int GetHashCode(XYZ point) => 0;
        }

        private static string UniqueName(string baseName, HashSet<string> names)
        {
            if (!names.Contains(baseName)) return baseName;
            for (int i = 2; ; i++)
            {
                string candidate = baseName + " (" + i + ")";
                if (!names.Contains(candidate)) return candidate;
            }
        }
    }
}
