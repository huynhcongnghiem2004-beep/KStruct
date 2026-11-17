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
    public sealed class CreateSlabRebarDrawingSheetsCommand : IExternalCommand
    {
        private sealed class FloorFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Floor;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Floor> floors = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Floor>().Distinct().ToList();
                if (floors.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new FloorFilter(),
                        "Chọn sàn cần lập hồ sơ cốt thép rồi nhấn Finish");
                    floors = picked.Select(reference => doc.GetElement(reference)).OfType<Floor>().Distinct().ToList();
                }
                if (floors.Count == 0) return Result.Cancelled;

                ViewFamilyType planType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>().FirstOrDefault(type => type.ViewFamily == ViewFamily.FloorPlan);
                ViewFamilyType sectionType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>().FirstOrDefault(type => type.ViewFamily == ViewFamily.Section);
                FamilySymbol titleBlock = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().OrderBy(type => type.FamilyName)
                    .ThenBy(type => type.Name).FirstOrDefault();
                if (planType == null) throw new InvalidOperationException("Project chưa có Floor Plan View Family Type.");
                if (sectionType == null) throw new InvalidOperationException("Project chưa có Section View Family Type.");
                if (titleBlock == null) throw new InvalidOperationException("Project chưa nạp Title Block Family Type.");

                HashSet<string> viewNames = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                    .Select(view => view.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                HashSet<string> sheetNumbers = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                    .Select(sheet => sheet.SheetNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var sheets = new List<ElementId>();
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Hồ sơ cốt thép sàn"))
                {
                    tx.Start();
                    if (!titleBlock.IsActive) titleBlock.Activate();
                    foreach (Floor floor in floors)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                var localViewNames = new HashSet<string>(viewNames, StringComparer.OrdinalIgnoreCase);
                                var localSheetNumbers = new HashSet<string>(sheetNumbers, StringComparer.OrdinalIgnoreCase);
                                var localSheets = new List<ElementId>();
                                List<View> views = CreateViews(doc, floor, planType, sectionType, localViewNames);
                                ViewSheet sheet = ViewSheet.Create(doc, titleBlock.Id);
                                sheet.SheetNumber = UniqueSheetNumber("KSDS-" + floor.Id.Value, localSheetNumbers);
                                sheet.Name = "Hồ sơ cốt thép sàn - " + FloorMark(floor);
                                PlaceViews(doc, sheet, views);
                                localSheets.Add(sheet.Id);
                                localSheetNumbers.Add(sheet.SheetNumber);
                                foreach (View view in views) localViewNames.Add(view.Name);

                                List<ViewSheet> separatedSheets = CreateLayerPlans(doc, floor, planType, titleBlock,
                                    localViewNames, localSheetNumbers);
                                localSheets.AddRange(separatedSheets.Select(item => item.Id));
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận sheet hoặc viewport.");
                                sheets.AddRange(localSheets);
                                foreach (string name in localViewNames) viewNames.Add(name);
                                foreach (string number in localSheetNumbers) sheetNumbers.Add(number);
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Sàn " + floor.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (sheets.Count == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Hồ sơ cốt thép sàn", "Không tạo được sheet nào. Yêu cầu Floor phẳng có Level và Title Block trong project.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các sheet mới.");
                }
                ui.Selection.SetElementIds(sheets);
                string report = "Đã tạo " + sheets.Count + " sheet cho " + floors.Count + " sàn: hồ sơ tổng hợp (mặt bằng + 2 mặt cắt), " +
                    "và các mặt bằng riêng thép dưới, thép trên, thép tăng cường khi có Rebar phù hợp.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " sàn:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Hồ sơ cốt thép sàn", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Hồ sơ cốt thép sàn", ex.Message);
                return Result.Failed;
            }
        }

        private static List<View> CreateViews(Document doc, Floor floor, ViewFamilyType planType,
            ViewFamilyType sectionType, HashSet<string> names)
        {
            ElementId levelId = floor.LevelId;
            if (levelId == ElementId.InvalidElementId || !(doc.GetElement(levelId) is Level level))
                throw new InvalidOperationException("Không xác định được Level của sàn.");
            BoundingBoxXYZ box = floor.get_BoundingBox(null);
            if (box == null) throw new InvalidOperationException("Không đọc được bounding box sàn.");
            List<XYZ> corners = WorldCorners(box);
            IList<Reference> topFaces = HostObjectUtils.GetTopFaces(floor);
            PlanarFace top = topFaces.Select(reference => floor.GetGeometryObjectFromReference(reference) as PlanarFace)
                .Where(face => face != null && face.FaceNormal.Normalize().DotProduct(XYZ.BasisZ) > 0.999)
                .OrderByDescending(face => face.Area).FirstOrDefault();
            if (top == null) throw new InvalidOperationException("Chỉ hỗ trợ sàn phẳng nằm ngang.");
            Curve longest = top.GetEdgesAsCurveLoops().SelectMany(loop => loop)
                .OrderByDescending(curve => curve.Length).FirstOrDefault();
            if (longest == null) throw new InvalidOperationException("Không xác định được hướng mặt bằng sàn.");
            XYZ x = (longest.GetEndPoint(1) - longest.GetEndPoint(0)).Normalize();
            x = new XYZ(x.X, x.Y, 0).Normalize();
            XYZ y = XYZ.BasisZ.CrossProduct(x).Normalize();
            XYZ center = corners.Aggregate(XYZ.Zero, (sum, point) => sum + point) / corners.Count;
            double margin = UnitUtils.ConvertToInternalUnits(300, UnitTypeId.Millimeters);

            ViewPlan plan = ViewPlan.Create(doc, planType.Id, level.Id);
            plan.Name = UniqueName("KStruct - CT sàn " + floor.Id.Value + " - MB", names);
            plan.DetailLevel = ViewDetailLevel.Fine;
            SetWidePlanRange(plan, level.Id);
            SetCrop(plan, corners, margin);
            var sections = new List<ViewSection>
            {
                CreateSection(doc, sectionType, center, x, corners, names, "KStruct - CT sàn " + floor.Id.Value + " - MC1", margin),
                CreateSection(doc, sectionType, center, y, corners, names, "KStruct - CT sàn " + floor.Id.Value + " - MC2", margin)
            };
            return new List<View> { plan, sections[0], sections[1] };
        }

        private static List<ViewSheet> CreateLayerPlans(Document doc, Floor floor, ViewFamilyType planType,
            FamilySymbol titleBlock, ISet<string> viewNames, ISet<string> sheetNumbers)
        {
            IList<Reference> topReferences = HostObjectUtils.GetTopFaces(floor);
            PlanarFace top = topReferences.Select(reference => floor.GetGeometryObjectFromReference(reference) as PlanarFace)
                .Where(face => face != null && face.FaceNormal.Normalize().DotProduct(XYZ.BasisZ) > 0.999)
                .OrderByDescending(face => face.Area).FirstOrDefault();
            FloorType type = doc.GetElement(floor.GetTypeId()) as FloorType;
            double thickness = type?.GetCompoundStructure()?.GetWidth() ?? 0;
            if (top == null || thickness <= doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Không xác định được mặt trên và chiều dày sàn để tách thép trên/dưới.");

            List<Rebar> floorBars = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).WhereElementIsNotElementType()
                .Cast<Rebar>().Where(bar => bar.GetHostId() == floor.Id).ToList();
            if (floorBars.Count == 0) return new List<ViewSheet>();

            BoundingBoxXYZ floorBox = floor.get_BoundingBox(null);
            if (floorBox == null) return new List<ViewSheet>();
            List<XYZ> corners = WorldCorners(floorBox);
            double margin = UnitUtils.ConvertToInternalUnits(300, UnitTypeId.Millimeters);
            double midZ = top.Origin.Z - thickness / 2.0;
            var plans = new[]
            {
                new { Key = "Thép dưới", Bars = floorBars.Where(bar => { double? z = BarCenterZ(bar); return z.HasValue && z.Value <= midZ; }).ToList() },
                new { Key = "Thép trên", Bars = floorBars.Where(bar => { double? z = BarCenterZ(bar); return z.HasValue && z.Value > midZ; }).ToList() },
                new { Key = "Thép tăng cường", Bars = floorBars.Where(bar => global::SDK_HD.KStruct.NativeRebar.ReinforceSlabRegionCommand.IsManagedRegionBar(bar, floor.UniqueId)).ToList() }
            };
            var result = new List<ViewSheet>();
            foreach (var planData in plans)
            {
                if (planData.Bars.Count == 0) continue;
                ViewPlan view = ViewPlan.Create(doc, planType.Id, floor.LevelId);
                view.Name = UniqueName("KStruct - CT sàn " + FloorMark(floor) + " - " + planData.Key, viewNames);
                view.DetailLevel = ViewDetailLevel.Fine;
                SetWidePlanRange(view, floor.LevelId);
                SetCrop(view, corners, margin);

                HashSet<ElementId> visibleIds = planData.Bars.Select(bar => bar.Id).ToHashSet();
                List<ElementId> hideIds = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).WhereElementIsNotElementType()
                    .ToElementIds().Where(id => !visibleIds.Contains(id)).ToList();
                if (hideIds.Count > 0) view.HideElements(hideIds);

                ViewSheet sheet = ViewSheet.Create(doc, titleBlock.Id);
                sheet.SheetNumber = UniqueSheetNumber("KSDS-" + floor.Id.Value + "-" + SheetCode(planData.Key), sheetNumbers);
                sheet.Name = "Cốt thép sàn " + FloorMark(floor) + " - " + planData.Key;
                doc.Regenerate();
                BoundingBoxUV outline = sheet.Outline;
                Viewport.Create(doc, sheet.Id, view.Id, new XYZ((outline.Min.U + outline.Max.U) / 2,
                    (outline.Min.V + outline.Max.V) / 2, 0));
                viewNames.Add(view.Name);
                sheetNumbers.Add(sheet.SheetNumber);
                result.Add(sheet);
            }
            return result;
        }

        private static double? BarCenterZ(Rebar bar)
        {
            BoundingBoxXYZ box = bar.get_BoundingBox(null);
            if (box == null) return null;
            XYZ center = box.Transform.OfPoint((box.Min + box.Max) * 0.5);
            return center.Z;
        }

        private static string SheetCode(string name) => name == "Thép dưới" ? "BOT" : name == "Thép trên" ? "TOP" : "ADD";

        private static void SetWidePlanRange(ViewPlan plan, ElementId levelId)
        {
            PlanViewRange range = plan.GetViewRange();
            double above = UnitUtils.ConvertToInternalUnits(3000, UnitTypeId.Millimeters);
            double below = UnitUtils.ConvertToInternalUnits(-1500, UnitTypeId.Millimeters);
            double viewDepth = UnitUtils.ConvertToInternalUnits(-2000, UnitTypeId.Millimeters);
            range.SetLevelId(PlanViewPlane.TopClipPlane, levelId);
            range.SetOffset(PlanViewPlane.TopClipPlane, above);
            range.SetLevelId(PlanViewPlane.CutPlane, levelId);
            range.SetOffset(PlanViewPlane.CutPlane, UnitUtils.ConvertToInternalUnits(1200, UnitTypeId.Millimeters));
            range.SetLevelId(PlanViewPlane.BottomClipPlane, levelId);
            range.SetOffset(PlanViewPlane.BottomClipPlane, below);
            range.SetLevelId(PlanViewPlane.ViewDepthPlane, levelId);
            range.SetOffset(PlanViewPlane.ViewDepthPlane, viewDepth);
            plan.SetViewRange(range);
        }

        private static void SetCrop(View view, IList<XYZ> points, double margin)
        {
            BoundingBoxXYZ crop = view.CropBox;
            Transform transform = crop.Transform;
            Transform inverse = transform.Inverse;
            List<XYZ> local = points.Select(inverse.OfPoint).ToList();
            crop.Min = new XYZ(local.Min(point => point.X) - margin, local.Min(point => point.Y) - margin, local.Min(point => point.Z) - margin);
            crop.Max = new XYZ(local.Max(point => point.X) + margin, local.Max(point => point.Y) + margin, local.Max(point => point.Z) + margin);
            view.CropBox = crop;
            view.CropBoxActive = true;
            view.CropBoxVisible = false;
        }

        private static ViewSection CreateSection(Document doc, ViewFamilyType type, XYZ center, XYZ right,
            IList<XYZ> points, HashSet<string> names, string baseName, double margin)
        {
            XYZ up = XYZ.BasisZ;
            XYZ x = right.Normalize();
            XYZ z = x.CrossProduct(up).Normalize();
            Transform transform = Transform.Identity;
            transform.Origin = center;
            transform.BasisX = x;
            transform.BasisY = up;
            transform.BasisZ = z;
            double minX = points.Min(point => (point - center).DotProduct(x)) - margin;
            double maxX = points.Max(point => (point - center).DotProduct(x)) + margin;
            double minY = points.Min(point => (point - center).DotProduct(up)) - margin;
            double maxY = points.Max(point => (point - center).DotProduct(up)) + margin;
            double halfDepth = UnitUtils.ConvertToInternalUnits(300, UnitTypeId.Millimeters);
            var sectionBox = new BoundingBoxXYZ
            {
                Transform = transform,
                Min = new XYZ(minX, minY, -halfDepth),
                Max = new XYZ(maxX, maxY, halfDepth)
            };
            ViewSection section = ViewSection.CreateSection(doc, type.Id, sectionBox);
            section.Name = UniqueName(baseName, names);
            section.DetailLevel = ViewDetailLevel.Fine;
            section.CropBoxActive = true;
            section.CropBoxVisible = false;
            return section;
        }

        private static void PlaceViews(Document doc, ViewSheet sheet, IList<View> views)
        {
            doc.Regenerate();
            BoundingBoxUV outline = sheet.Outline;
            UV min = outline.Min, max = outline.Max;
            double width = max.U - min.U, height = max.V - min.V;
            Fit(views[0], width * 0.60);
            Fit(views[1], width * 0.27);
            Fit(views[2], width * 0.27);
            doc.Regenerate();
            Viewport.Create(doc, sheet.Id, views[0].Id, new XYZ(min.U + width * 0.35, min.V + height * 0.60, 0));
            Viewport.Create(doc, sheet.Id, views[1].Id, new XYZ(min.U + width * 0.76, min.V + height * 0.70, 0));
            Viewport.Create(doc, sheet.Id, views[2].Id, new XYZ(min.U + width * 0.76, min.V + height * 0.34, 0));
        }

        private static void Fit(View view, double paperWidth)
        {
            BoundingBoxXYZ crop = view.CropBox;
            double modelWidth = crop.Max.X - crop.Min.X;
            if (modelWidth > 0 && paperWidth > 0) view.Scale = Math.Max(1, (int)Math.Ceiling(modelWidth / paperWidth));
        }

        private static List<XYZ> WorldCorners(BoundingBoxXYZ box)
        {
            Transform transform = box.Transform ?? Transform.Identity;
            var points = new List<XYZ>();
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
                points.Add(transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X,
                    y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z)));
            return points;
        }

        private static string FloorMark(Element floor)
        {
            Parameter mark = floor.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            return mark == null || !mark.HasValue || string.IsNullOrWhiteSpace(mark.AsString())
                ? floor.Id.Value.ToString() : mark.AsString().Trim();
        }

        private static string UniqueName(string baseName, ISet<string> existing)
        {
            if (!existing.Contains(baseName)) return baseName;
            for (int i = 2; ; i++)
            {
                string candidate = baseName + " (" + i + ")";
                if (!existing.Contains(candidate)) return candidate;
            }
        }

        private static string UniqueSheetNumber(string baseNumber, ISet<string> existing)
        {
            string candidate = baseNumber;
            for (int i = 2; existing.Contains(candidate); i++) candidate = baseNumber + "-" + i;
            return candidate;
        }
    }
}
