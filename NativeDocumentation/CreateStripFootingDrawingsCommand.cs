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
    public sealed class CreateStripFootingDrawingsCommand : IExternalCommand
    {
        private sealed class FootingFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) { return element is WallFoundation; }
            public bool AllowReference(Reference reference, XYZ position) { return false; }
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<WallFoundation> footings = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .OfType<WallFoundation>().Distinct().ToList();
                if (footings.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new FootingFilter(),
                        "Chọn các móng băng cần tạo mặt cắt và sheet");
                    footings = picked.Select(x => doc.GetElement(x)).OfType<WallFoundation>().Distinct().ToList();
                }
                if (footings.Count == 0) return Result.Cancelled;

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
                var sheets = new List<ElementId>();
                var skipped = new List<string>();
                using (Transaction transaction = new Transaction(doc, "KStruct - Bản vẽ móng băng"))
                {
                    transaction.Start();
                    foreach (WallFoundation footing in footings)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                Wall wall = doc.GetElement(footing.WallId) as Wall;
                                ViewSection section = CreateSection(doc, footing, wall, sectionType, viewNames);
                                ViewSheet sheet = ViewSheet.Create(doc, titleBlock.Id);
                                sheet.SheetNumber = UniqueSheetNumber("KSMB-" + footing.Id.Value, sheetNumbers);
                                sheet.Name = "Móng băng - Tường " + footing.WallId.Value;
                                Viewport.Create(doc, sheet.Id, section.Id, new XYZ(1.0, 0.8, 0));
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận sheet hoặc viewport.");
                                viewNames.Add(section.Name);
                                sheetNumbers.Add(sheet.SheetNumber);
                                sheets.Add(sheet.Id);
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Móng " + footing.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (sheets.Count == 0)
                    {
                        transaction.RollBack();
                        TaskDialog.Show("KStruct - Bản vẽ móng băng", "Không tạo được bản vẽ.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các bản vẽ mới.");
                }
                ui.Selection.SetElementIds(sheets);
                string report = "Đã tạo " + sheets.Count + "/" + footings.Count + " sheet mặt cắt móng băng.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " móng:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Bản vẽ móng băng", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Bản vẽ móng băng", ex.Message);
                return Result.Failed;
            }
        }

        private static ViewSection CreateSection(Document doc, WallFoundation footing, Wall wall,
            ViewFamilyType type, HashSet<string> names)
        {
            if (wall == null) throw new InvalidOperationException("Không tìm thấy tường host.");
            LocationCurve location = wall.Location as LocationCurve;
            Line axis = location == null ? null : location.Curve as Line;
            BoundingBoxXYZ box = footing.get_BoundingBox(null);
            if (axis == null || box == null) throw new InvalidOperationException("Chỉ hỗ trợ móng trên tường thẳng có bounding box.");

            XYZ up = XYZ.BasisZ;
            XYZ wallNormal = wall.Orientation;
            XYZ viewDirection = new XYZ(wallNormal.X, wallNormal.Y, 0).Normalize();
            XYZ right = up.CrossProduct(viewDirection).Normalize();
            viewDirection = right.CrossProduct(up).Normalize();
            Transform transform = Transform.Identity;
            transform.Origin = axis.Evaluate(0.5, true);
            transform.BasisX = right;
            transform.BasisY = up;
            transform.BasisZ = viewDirection;

            Transform sourceTransform = box.Transform ?? Transform.Identity;
            List<XYZ> points = new List<XYZ>();
            for (int ix = 0; ix < 2; ix++) for (int iy = 0; iy < 2; iy++) for (int iz = 0; iz < 2; iz++)
            {
                XYZ local = new XYZ(ix == 0 ? box.Min.X : box.Max.X,
                    iy == 0 ? box.Min.Y : box.Max.Y, iz == 0 ? box.Min.Z : box.Max.Z);
                points.Add(sourceTransform.OfPoint(local));
            }
            XYZ origin = transform.Origin;
            double minX = points.Min(p => (p - origin).DotProduct(right));
            double maxX = points.Max(p => (p - origin).DotProduct(right));
            double minY = points.Min(p => (p - origin).DotProduct(up));
            double maxY = points.Max(p => (p - origin).DotProduct(up));
            double minZ = points.Min(p => (p - origin).DotProduct(viewDirection));
            double maxZ = points.Max(p => (p - origin).DotProduct(viewDirection));
            double margin = UnitUtils.ConvertToInternalUnits(300, UnitTypeId.Millimeters);
            var sectionBox = new BoundingBoxXYZ
            {
                Transform = transform,
                Min = new XYZ(minX - margin, minY - margin, minZ - margin),
                Max = new XYZ(maxX + margin, maxY + margin, maxZ + margin)
            };
            ViewSection section = ViewSection.CreateSection(doc, type.Id, sectionBox);
            section.Name = UniqueName("KStruct - MC " + footing.Id.Value + " - T" + footing.WallId.Value, names);
            section.CropBoxActive = true;
            section.CropBoxVisible = false;
            section.Scale = 20;
            return section;
        }

        private static string UniqueName(string baseName, HashSet<string> names)
        {
            if (!names.Contains(baseName)) return baseName;
            for (int i = 2; ; i++) { string candidate = baseName + " (" + i + ")"; if (!names.Contains(candidate)) return candidate; }
        }

        private static string UniqueSheetNumber(string baseNumber, HashSet<string> numbers)
        {
            string candidate = baseNumber;
            for (int i = 2; numbers.Contains(candidate); i++) candidate = baseNumber + "-" + i;
            return candidate;
        }
    }
}
