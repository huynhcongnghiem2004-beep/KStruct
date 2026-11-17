using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CreateRetainingWallDrawingSheetsCommand : IExternalCommand
    {
        private sealed class WallFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => IsSupportedWall(element as Wall);
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Wall> walls = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Wall>()
                    .Where(IsSupportedWall).Distinct().ToList();
                if (walls.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new WallFilter(),
                        "Chọn tường thẳng đứng cần lập hồ sơ tường chắn");
                    walls = picked.Select(x => doc.GetElement(x)).OfType<Wall>().Where(IsSupportedWall).Distinct().ToList();
                }
                if (walls.Count == 0) return Result.Cancelled;

                ViewFamilyType sectionType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>().FirstOrDefault(x => x.ViewFamily == ViewFamily.Section);
                if (sectionType == null) throw new InvalidOperationException("Project chưa có Section View Family Type.");
                FamilySymbol titleBlock = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().OrderBy(x => x.FamilyName)
                    .ThenBy(x => x.Name).FirstOrDefault();
                if (titleBlock == null) throw new InvalidOperationException("Project chưa nạp Title Block Family Type.");

                HashSet<string> viewNames = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                    .Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                HashSet<string> sheetNumbers = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .Select(x => x.SheetNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var createdSheets = new List<ElementId>();
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Hồ sơ tường chắn"))
                {
                    tx.Start();
                    foreach (Wall wall in walls)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                List<ViewSection> sections = CreateSections(doc, wall, sectionType, viewNames);
                                ViewSheet sheet = ViewSheet.Create(doc, titleBlock.Id);
                                sheet.SheetNumber = UniqueSheetNumber("KSRW-" + wall.Id.Value, sheetNumbers);
                                sheet.Name = "Tường chắn - " + ElementMark(wall);
                                Viewport.Create(doc, sheet.Id, sections[0].Id, new XYZ(0.85, 1.05, 0));
                                Viewport.Create(doc, sheet.Id, sections[1].Id, new XYZ(1.85, 1.05, 0));
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận view, sheet hoặc viewport.");
                                foreach (ViewSection section in sections) viewNames.Add(section.Name);
                                sheetNumbers.Add(sheet.SheetNumber);
                                createdSheets.Add(sheet.Id);
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Tường " + wall.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (createdSheets.Count == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Hồ sơ tường chắn", "Không tạo được sheet.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các hồ sơ mới.");
                }
                ui.Selection.SetElementIds(createdSheets);
                string report = "Đã tạo " + createdSheets.Count + "/" + walls.Count + " sheet tường chắn, mỗi sheet gồm mặt đứng dọc và mặt cắt ngang.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " tường:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Hồ sơ tường chắn", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Hồ sơ tường chắn", ex.Message);
                return Result.Failed;
            }
        }

        private static List<ViewSection> CreateSections(Document doc, Wall wall, ViewFamilyType type, HashSet<string> names)
        {
            LocationCurve location = wall.Location as LocationCurve;
            Line axisLine = location == null ? null : location.Curve as Line;
            if (axisLine == null || Math.Abs(axisLine.Direction.Normalize().DotProduct(XYZ.BasisZ)) > 1e-6)
                throw new InvalidOperationException("Chỉ hỗ trợ tường có đường tâm thẳng, nằm ngang.");

            var bounds = new List<BoundingBoxXYZ>();
            BoundingBoxXYZ wallBounds = wall.get_BoundingBox(null);
            if (wallBounds == null) throw new InvalidOperationException("Không đọc được bounding box tường.");
            bounds.Add(wallBounds);
            foreach (WallFoundation footing in new FilteredElementCollector(doc).OfClass(typeof(WallFoundation))
                .Cast<WallFoundation>().Where(x => x.WallId == wall.Id))
            {
                BoundingBoxXYZ footingBounds = footing.get_BoundingBox(null);
                if (footingBounds != null) bounds.Add(footingBounds);
            }
            List<XYZ> corners = bounds.SelectMany(WorldCorners).ToList();
            XYZ up = XYZ.BasisZ;
            XYZ wallNormal = new XYZ(wall.Orientation.X, wall.Orientation.Y, 0).Normalize();
            XYZ wallAxis = new XYZ(axisLine.Direction.X, axisLine.Direction.Y, 0).Normalize();
            XYZ center = axisLine.Evaluate(0.5, true);
            double margin = UnitUtils.ConvertToInternalUnits(300, UnitTypeId.Millimeters);
            var sections = new List<ViewSection>();

            // Long elevation: view into the wall face, with the wall axis horizontal on screen.
            XYZ elevationView = wallNormal;
            XYZ elevationRight = up.CrossProduct(elevationView).Normalize();
            sections.Add(CreateSection(doc, type, names, wall, corners, center, elevationRight, up, elevationView, margin, "Dọc"));

            // Cross section: view along the wall, with thickness horizontal on screen.
            XYZ crossView = wallAxis;
            XYZ crossRight = up.CrossProduct(crossView).Normalize();
            sections.Add(CreateSection(doc, type, names, wall, corners, center, crossRight, up, crossView, margin, "Ngang"));
            return sections;
        }

        private static ViewSection CreateSection(Document doc, ViewFamilyType type, HashSet<string> names, Wall wall,
            IList<XYZ> points, XYZ origin, XYZ right, XYZ up, XYZ viewDirection, double margin, string label)
        {
            Transform transform = Transform.Identity;
            transform.Origin = origin;
            transform.BasisX = right;
            transform.BasisY = up;
            transform.BasisZ = viewDirection;
            double minX = points.Min(p => (p - origin).DotProduct(right)) - margin;
            double maxX = points.Max(p => (p - origin).DotProduct(right)) + margin;
            double minY = points.Min(p => (p - origin).DotProduct(up)) - margin;
            double maxY = points.Max(p => (p - origin).DotProduct(up)) + margin;
            double minZ = points.Min(p => (p - origin).DotProduct(viewDirection)) - margin;
            double maxZ = points.Max(p => (p - origin).DotProduct(viewDirection)) + margin;
            BoundingBoxXYZ crop = new BoundingBoxXYZ
            {
                Transform = transform,
                Min = new XYZ(minX, minY, minZ),
                Max = new XYZ(maxX, maxY, maxZ)
            };
            ViewSection section = ViewSection.CreateSection(doc, type.Id, crop);
            section.Name = UniqueName("KStruct - TC " + wall.Id.Value + " - " + label, names);
            section.CropBoxActive = true;
            section.CropBoxVisible = false;
            section.DetailLevel = ViewDetailLevel.Fine;
            section.Scale = 20;
            return section;
        }

        private static IEnumerable<XYZ> WorldCorners(BoundingBoxXYZ box)
        {
            Transform transform = box.Transform ?? Transform.Identity;
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                        yield return transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X,
                            y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z));
        }

        private static bool IsSupportedWall(Wall wall)
        {
            if (wall == null || wall.IsStackedWall || wall.IsStackedWallMember) return false;
            LocationCurve location = wall.Location as LocationCurve;
            Line line = location == null ? null : location.Curve as Line;
            return line != null && Math.Abs(line.Direction.Normalize().DotProduct(XYZ.BasisZ)) <= 1e-6;
        }

        private static string ElementMark(Element element)
        {
            Parameter mark = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            return mark == null || !mark.HasValue || string.IsNullOrWhiteSpace(mark.AsString())
                ? element.Id.Value.ToString() : mark.AsString().Trim();
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

        private static string UniqueSheetNumber(string baseNumber, HashSet<string> numbers)
        {
            string candidate = baseNumber;
            for (int i = 2; numbers.Contains(candidate); i++) candidate = baseNumber + "-" + i;
            return candidate;
        }
    }
}
