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
    public sealed class CreateBeamDetailSheetsCommand : IExternalCommand
    {
        private sealed class BeamFilter : ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                FamilyInstance beam = element as FamilyInstance;
                return beam != null && beam.Category != null &&
                    beam.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;
            }
            public bool AllowReference(Reference reference, XYZ position) { return false; }
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<FamilyInstance> beams = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<FamilyInstance>()
                    .Where(IsFraming).Distinct().ToList();
                if (beams.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new BeamFilter(),
                        "Chọn dầm thẳng cần tạo bản vẽ chi tiết");
                    beams = picked.Select(x => doc.GetElement(x)).OfType<FamilyInstance>().Where(IsFraming).Distinct().ToList();
                }
                if (beams.Count == 0) return Result.Cancelled;
                List<ViewFamilyType> sectionTypes = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>().Where(x => x.ViewFamily == ViewFamily.Section)
                    .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                List<FamilySymbol> titleBlocks = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().OrderBy(x => x.FamilyName)
                    .ThenBy(x => x.Name).ToList();
                List<ViewSheet> existingSheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .Where(x => !x.IsPlaceholder).OrderBy(x => x.SheetNumber).ToList();
                BeamDocumentationOptions options = BeamDocumentationOptionsWindow.Show(data.Application.MainWindowHandle, sectionTypes, titleBlocks, existingSheets);
                if (options == null) return Result.Cancelled;
                if (options.ExistingSheetId != null && beams.Count > 1)
                    throw new InvalidOperationException("KStruct hiện chỉ gắn chi tiết vào một bản vẽ hiện có cho mỗi lần chạy. Chọn một dầm rồi chạy lại.");
                ViewFamilyType sectionType = options.SectionType;
                FamilySymbol titleBlock = options.TitleBlock;
                HashSet<string> names = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                    .Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                HashSet<string> numbers = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .Select(x => x.SheetNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var sheets = new List<ElementId>();
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Bản vẽ chi tiết dầm"))
                {
                    tx.Start();
                    if (!titleBlock.IsActive) titleBlock.Activate();
                    foreach (FamilyInstance beam in beams)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                List<ViewSection> views = CreateBeamViews(doc, beam, options, names);
                                if (options.AddDimensions)
                                {
                                    ViewSection crossView = views.FirstOrDefault(v => v.Name.Contains("Ngang"));
                                    if (crossView != null) try { DimensionBeamSectionCommand.CreateDimensions(doc, crossView, beam); } catch { }
                                }
                                ViewSheet sheet = options.ExistingSheetId == null ? ViewSheet.Create(doc, titleBlock.Id) : doc.GetElement(options.ExistingSheetId) as ViewSheet;
                                if (sheet == null) throw new InvalidOperationException("Không tìm thấy bản vẽ được chọn.");
                                if (options.ExistingSheetId == null)
                                {
                                    string requestedNumber = options.SheetNumber;
                                    if (beams.Count > 1) requestedNumber += "-" + (sheets.Count + 1).ToString("00");
                                    sheet.SheetNumber = UniqueSheetNumber(requestedNumber, numbers);
                                    sheet.Name = options.SheetName + " - " + BeamMark(beam);
                                }
                                PlaceViewsOnSheet(doc, sheet, views);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận sheet và viewport.");
                                foreach (ViewSection view in views) names.Add(view.Name);
                                numbers.Add(sheet.SheetNumber);
                                sheets.Add(sheet.Id);
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Dầm " + beam.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (sheets.Count == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Bản vẽ chi tiết dầm", "Không tạo được sheet. Hỗ trợ hiện tại yêu cầu trục thẳng không đứng.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các sheet mới.");
                }
                ui.Selection.SetElementIds(sheets);
                string report = "Đã tạo " + sheets.Count + "/" + beams.Count + " sheet chi tiết dầm; mỗi sheet có mặt cắt dọc và ngang.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " dầm:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Bản vẽ chi tiết dầm", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Bản vẽ chi tiết dầm", ex.Message);
                return Result.Failed;
            }
        }

        private static List<ViewSection> CreateBeamViews(Document doc, FamilyInstance beam, BeamDocumentationOptions options, HashSet<string> names)
        {
            LocationCurve location = beam.Location as LocationCurve;
            Line axis = location == null ? null : location.Curve as Line;
            BoundingBoxXYZ box = beam.get_BoundingBox(null);
            if (axis == null || box == null) throw new InvalidOperationException("Chỉ hỗ trợ dầm thẳng có bounding box.");
            XYZ direction = axis.Direction.Normalize();
            XYZ projected = new XYZ(direction.X, direction.Y, 0);
            if (projected.GetLength() < 1e-6) throw new InvalidOperationException("Không hỗ trợ trục dầm đứng.");
            projected = projected.Normalize();
            XYZ sectionUp = XYZ.BasisZ - direction * direction.DotProduct(XYZ.BasisZ);
            if (sectionUp.GetLength() < 1e-6) throw new InvalidOperationException("Không xác định được mặt cắt dầm.");
            sectionUp = sectionUp.Normalize();
            XYZ sectionRight = sectionUp.CrossProduct(direction).Normalize();
            XYZ center = axis.Evaluate(0.5, true) + XYZ.BasisZ * UnitUtils.ConvertToInternalUnits(options.ElevationOffsetMm, UnitTypeId.Millimeters);
            List<XYZ> corners = WorldCorners(box);
            double margin = UnitUtils.ConvertToInternalUnits(300, UnitTypeId.Millimeters);
            var result = new List<ViewSection>();
            if (options.CreateLongitudinal)
                result.Add(MakeSection(doc, options.SectionType, center, projected, XYZ.BasisZ,
                    corners, names, "KStruct - DD " + beam.Id.Value + " - Doc", margin));
            if (options.CreateCross)
                result.Add(MakeSection(doc, options.SectionType, center, sectionRight, sectionUp,
                    corners, names, "KStruct - DD " + beam.Id.Value + " - Ngang", margin));
            if (result.Count == 0) throw new InvalidOperationException("Chọn ít nhất một mặt cắt dầm.");
            return result;
        }

        private static ViewSection MakeSection(Document doc, ViewFamilyType type, XYZ origin, XYZ right, XYZ up,
            IList<XYZ> points, HashSet<string> names, string baseName, double margin)
        {
            XYZ x = right.Normalize(), y = up.Normalize(), z = x.CrossProduct(y).Normalize();
            Transform transform = Transform.Identity;
            transform.Origin = origin; transform.BasisX = x; transform.BasisY = y; transform.BasisZ = z;
            double minX = points.Min(p => (p - origin).DotProduct(x)) - margin;
            double maxX = points.Max(p => (p - origin).DotProduct(x)) + margin;
            double minY = points.Min(p => (p - origin).DotProduct(y)) - margin;
            double maxY = points.Max(p => (p - origin).DotProduct(y)) + margin;
            double minZ = points.Min(p => (p - origin).DotProduct(z)) - margin;
            double maxZ = points.Max(p => (p - origin).DotProduct(z)) + margin;
            var crop = new BoundingBoxXYZ
            {
                Transform = transform,
                Min = new XYZ(minX, minY, minZ),
                Max = new XYZ(maxX, maxY, maxZ)
            };
            ViewSection view = ViewSection.CreateSection(doc, type.Id, crop);
            view.Name = UniqueName(baseName, names);
            view.CropBoxActive = true;
            view.CropBoxVisible = false;
            return view;
        }

        private static void PlaceViewsOnSheet(Document doc, ViewSheet sheet, IList<ViewSection> views)
        {
            doc.Regenerate();
            BoundingBoxUV outline = sheet.Outline;
            UV min = outline.Min, max = outline.Max;
            double width = max.U - min.U, height = max.V - min.V;
            double targetWidth = width * (views.Count == 1 ? 0.72 : 0.55);
            foreach (ViewSection view in views) FitViewToSheet(view, targetWidth / views.Count);
            doc.Regenerate();
            double spacing = width / (views.Count + 1.0);
            for (int i=0;i<views.Count;i++)
            {
                XYZ center = new XYZ(min.U + spacing * (i + 1), min.V + height * 0.53, 0);
                Viewport.Create(doc, sheet.Id, views[i].Id, center);
            }
        }

        private static void FitViewToSheet(ViewSection view, double targetPaperWidth)
        {
            BoundingBoxXYZ crop = view.CropBox;
            if (crop == null || targetPaperWidth <= 0) return;
            double modelWidth = crop.Max.X - crop.Min.X;
            if (modelWidth <= 0) return;
            view.Scale = Math.Max(1, (int)Math.Ceiling(modelWidth / targetPaperWidth));
        }

        private static List<XYZ> WorldCorners(BoundingBoxXYZ box)
        {
            Transform transform = box.Transform ?? Transform.Identity;
            var points = new List<XYZ>();
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                points.Add(transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X,
                    y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z)));
            return points;
        }

        private static bool IsFraming(FamilyInstance beam) => beam.Category != null &&
            beam.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;

        private static string BeamMark(Element beam)
        {
            Parameter parameter = beam.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            return parameter == null || !parameter.HasValue || string.IsNullOrWhiteSpace(parameter.AsString())
                ? beam.Id.Value.ToString() : parameter.AsString().Trim();
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
