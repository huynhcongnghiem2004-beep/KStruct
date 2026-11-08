using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;

namespace SDK_HD.KStruct.NativeDocumentation
{
    internal static class FootingDrawingBuilder
    {
        private sealed class ViewOutput
        {
            internal View View;
            internal string Role;
        }

        internal static ViewSheet CreateForFooting(Document doc, ViewPlan parent, FamilyInstance footing,
            FootingDrawingConfiguration config, HashSet<string> names, HashSet<string> sheetNumbers,
            int batchIndex, IList<string> warnings)
        {
            BoundingBoxXYZ hostBox = footing.get_BoundingBox(null);
            LocationPoint location = footing.Location as LocationPoint;
            if (hostBox == null || location == null) throw new InvalidOperationException("Móng cần có LocationPoint và bounding box.");
            FootingDrawingProfile p = config.Profile;
            var outputs = new List<ViewOutput>();
            bool top = p.OutputLayers == "Top" || p.OutputLayers == "Both";
            bool bot = p.OutputLayers == "Bot" || p.OutputLayers == "Both";
            if (!p.CreateCallouts && (top || bot))
            {
                if (config.PlanType.ViewFamily != ViewFamily.StructuralPlan && config.PlanType.ViewFamily != ViewFamily.FloorPlan)
                    throw new InvalidOperationException("View Type mặt bằng không hợp lệ.");
            }
            if (p.CreateCallouts && parent == null) throw new InvalidOperationException("Callout cần chạy từ mặt bằng Revit.");
            if (!p.CreateCallouts && !p.CreateSections) throw new InvalidOperationException("Cần bật callout hoặc mặt cắt.");

            if (top) outputs.Add(new ViewOutput { View = CreatePlanOutput(doc, parent, footing, hostBox, config, names, true, warnings), Role = "Plan-Top" });
            if (bot) outputs.Add(new ViewOutput { View = CreatePlanOutput(doc, parent, footing, hostBox, config, names, false, warnings), Role = "Plan-Bot" });
            if (p.CreateSections)
            {
                List<ViewSection> sections = CreateSections(doc, footing, config, names, warnings);
                outputs.Add(new ViewOutput { View = sections[0], Role = "Section-X" });
                outputs.Add(new ViewOutput { View = sections[1], Role = "Section-Y" });
            }

            ViewSheet sheet = config.ExistingSheet;
            bool newSheet = p.PlaceOnNewSheet;
            if (newSheet)
            {
                sheet = ViewSheet.Create(doc, config.TitleBlock.Id);
                bool isPile = IsPileCap(footing);
                string defaultPrefix = isPile ? "KSMC-" : "KSMD-";
                string prefix = string.IsNullOrWhiteSpace(p.SheetPrefix) ? defaultPrefix : p.SheetPrefix.Trim();
                sheet.SheetNumber = UniqueNumber(prefix, footing.Id.Value.ToString(), sheetNumbers);
                sheet.Name = FormatName(p.SheetNamePattern, footing);
                FootingDrawingStorage.Mark(sheet, footing.UniqueId, "Sheet", p.CropMarginMm);
                sheetNumbers.Add(sheet.SheetNumber);
            }
            if (sheet == null) throw new InvalidOperationException("Không tìm được sheet đích.");

            int index = 0;
            foreach (ViewOutput output in outputs)
            {
                View view = output.View;
                ApplyAnnotations(doc, footing, view, output.Role, config, hostBox, warnings);
                FootingDrawingStorage.Mark(view, footing.UniqueId, output.Role, p.CropMarginMm);
                if (!names.Contains(view.Name)) names.Add(view.Name);
                index++;
            }
            PlaceViewports(doc, sheet, outputs.Select(x => x.View).ToList(), batchIndex, warnings);
            return sheet;
        }

        private static View CreatePlanOutput(Document doc, ViewPlan parent, FamilyInstance footing, BoundingBoxXYZ box,
            FootingDrawingConfiguration config, HashSet<string> names, bool topLayer, IList<string> warnings)
        {
            FootingDrawingProfile p = config.Profile;
            List<Rebar> bars = GetHostedBars(doc, footing);
            List<Rebar> layerBars = bars.Where(x => IsLayer(x, box, topLayer)).ToList();
            ViewPlan view;
            if (p.CreateCallouts)
            {
                List<XYZ> corners = WorldCorners(box);
                XYZ right = parent.RightDirection.Normalize(), up = parent.UpDirection.Normalize();
                XYZ origin = parent.Origin;
                double margin = UnitUtils.ConvertToInternalUnits(p.CropMarginMm, UnitTypeId.Millimeters);
                double minX = corners.Min(x => (x - origin).DotProduct(right)) - margin;
                double maxX = corners.Max(x => (x - origin).DotProduct(right)) + margin;
                double minY = corners.Min(x => (x - origin).DotProduct(up)) - margin;
                double maxY = corners.Max(x => (x - origin).DotProduct(up)) + margin;
                double z = parent.GenLevel == null ? locationZ(footing) : parent.GenLevel.Elevation;
                XYZ a = origin + right * minX + up * minY;
                XYZ b = origin + right * maxX + up * maxY;
                a = new XYZ(a.X, a.Y, z); b = new XYZ(b.X, b.Y, z);
                View created = ViewSection.CreateCallout(doc, parent.Id, config.PlanType.Id, a, b);
                view = created as ViewPlan;
                if (view == null) throw new InvalidOperationException("Revit không tạo được callout mặt bằng.");
            }
            else
            {
                Level level = doc.GetElement(footing.LevelId) as Level ?? new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(x => Math.Abs(x.Elevation - locationZ(footing))).FirstOrDefault();
                if (level == null) throw new InvalidOperationException("Không tìm được Level để tạo mặt bằng.");
                view = ViewPlan.Create(doc, config.PlanType.Id, level.Id);
                SetPlanCrop(view, box, p.CropMarginMm);
            }
            string layer = topLayer ? "Top" : "Bot";
            view.Name = UniqueName(FormatViewName(p.ViewNamePattern, footing, "Plan-" + layer), names);
            view.Scale = p.PlanScale;
            view.Discipline = ViewDiscipline.Structural;
            view.DetailLevel = ViewDetailLevel.Fine;
            ApplyTemplate(view, config.PlanTemplate, warnings);
            SetLayerRange(view, box, bars, layerBars, topLayer, footing.LevelId);
            List<ElementId> hidden = bars.Where(x => !layerBars.Any(y => y.Id == x.Id)).Select(x => x.Id).ToList();
            if (hidden.Count > 0) try { view.HideElements(hidden); } catch (Exception ex) { warnings.Add("Không lọc được một số Rebar ở " + view.Name + ": " + ex.Message); }
            return view;
        }

        private static void SetLayerRange(ViewPlan view, BoundingBoxXYZ hostBox, List<Rebar> all, List<Rebar> layer,
            bool topLayer, ElementId levelId)
        {
            if (view.IsTemplate) return;
            double elevation = layer.Select(x => x.get_BoundingBox(null)).Where(x => x != null).Select(x => (x.Min.Z + x.Max.Z) / 2.0)
                .DefaultIfEmpty(topLayer ? hostBox.Max.Z - UnitUtils.ConvertToInternalUnits(50, UnitTypeId.Millimeters) : hostBox.Min.Z + UnitUtils.ConvertToInternalUnits(50, UnitTypeId.Millimeters)).Average();
            Level level = view.Document.GetElement(levelId) as Level;
            if (level == null) level = view.GenLevel;
            if (level == null) return;
            double half = UnitUtils.ConvertToInternalUnits(150, UnitTypeId.Millimeters);
            PlanViewRange range = view.GetViewRange();
            foreach (PlanViewPlane plane in new[] { PlanViewPlane.TopClipPlane, PlanViewPlane.CutPlane, PlanViewPlane.BottomClipPlane, PlanViewPlane.ViewDepthPlane })
            {
                range.SetLevelId(plane, level.Id);
                double offset = plane == PlanViewPlane.TopClipPlane ? elevation + half - level.Elevation :
                    plane == PlanViewPlane.CutPlane ? elevation + half / 2 - level.Elevation :
                    plane == PlanViewPlane.BottomClipPlane ? elevation - half - level.Elevation : elevation - 2 * half - level.Elevation;
                range.SetOffset(plane, offset);
            }
            view.SetViewRange(range);
        }

        private static List<ViewSection> CreateSections(Document doc, FamilyInstance footing, FootingDrawingConfiguration config, HashSet<string> names, IList<string> warnings)
        {
            BoundingBoxXYZ source = footing.get_BoundingBox(null);
            LocationPoint location = footing.Location as LocationPoint;
            if (source == null || location == null) throw new InvalidOperationException("Không đọc được hình học móng để tạo mặt cắt.");
            double angle = location.Rotation;
            XYZ xAxis = new XYZ(Math.Cos(angle), Math.Sin(angle), 0).Normalize();
            XYZ yAxis = new XYZ(-Math.Sin(angle), Math.Cos(angle), 0).Normalize();
            List<XYZ> corners = WorldCorners(source);
            XYZ center = location.Point;
            double margin = UnitUtils.ConvertToInternalUnits(config.Profile.CropMarginMm, UnitTypeId.Millimeters);
            var result = new List<ViewSection>();
            for (int i = 0; i < 2; i++)
            {
                XYZ direction = i == 0 ? xAxis : yAxis;
                XYZ right = XYZ.BasisZ.CrossProduct(direction).Normalize();
                Transform transform = Transform.Identity;
                transform.Origin = center; transform.BasisX = right; transform.BasisY = XYZ.BasisZ;
                transform.BasisZ = right.CrossProduct(XYZ.BasisZ).Normalize();
                XYZ depth = transform.BasisZ;
                var crop = new BoundingBoxXYZ
                {
                    Transform = transform,
                    Min = new XYZ(corners.Min(p => (p - center).DotProduct(right)) - margin,
                        corners.Min(p => p.Z - center.Z) - margin,
                        corners.Min(p => (p - center).DotProduct(depth)) - margin),
                    Max = new XYZ(corners.Max(p => (p - center).DotProduct(right)) + margin,
                        corners.Max(p => p.Z - center.Z) + margin,
                        corners.Max(p => (p - center).DotProduct(depth)) + margin)
                };
                ViewSection view = ViewSection.CreateSection(doc, config.SectionType.Id, crop);
                view.Name = UniqueName(FormatViewName(config.Profile.ViewNamePattern, footing, i == 0 ? "Section-X" : "Section-Y"), names);
                view.Scale = config.Profile.SectionScale; view.CropBoxActive = true; view.CropBoxVisible = false;
                ApplyTemplate(view, config.SectionTemplate, warnings);
                result.Add(view);
            }
            return result;
        }

        private static void ApplyAnnotations(Document doc, FamilyInstance footing, View view, string role,
            FootingDrawingConfiguration config, BoundingBoxXYZ hostBox, IList<string> warnings)
        {
            if (config.Profile.CreateDimensions && config.DimensionType != null)
                CreateHostDimensions(doc, footing, view, config.DimensionType, hostBox, warnings);
            if (!config.Profile.CreateTags) return;
            if (config.FoundationTagType != null)
            {
                TryTag(doc, config.FoundationTagType, view, footing, SafeCenter(hostBox), warnings);
                if (IsPileCap(footing) && (view is ViewPlan))
                {
                    List<FamilyInstance> piles = GetPiles(doc, footing, hostBox);
                    foreach (var pile in piles)
                    {
                        BoundingBoxXYZ pbox = pile.get_BoundingBox(view) ?? pile.get_BoundingBox(null);
                        if (pbox != null)
                        {
                            TryTag(doc, config.FoundationTagType, view, pile, SafeCenter(pbox), warnings);
                        }
                    }
                }
            }
            if (config.RebarTagType != null)
            {
                bool top = role.IndexOf("Top", StringComparison.OrdinalIgnoreCase) >= 0;
                bool bot = role.IndexOf("Bot", StringComparison.OrdinalIgnoreCase) >= 0;
                List<Rebar> bars = GetHostedBars(doc, footing).Where(x => !top && !bot || IsLayer(x, hostBox, top)).OrderBy(x => x.Id.Value).ToList();
                XYZ up = view.UpDirection.Normalize();
                double offset = UnitUtils.ConvertToInternalUnits(80, UnitTypeId.Millimeters);
                for (int i = 0; i < bars.Count; i++)
                {
                    BoundingBoxXYZ bb = bars[i].get_BoundingBox(view) ?? bars[i].get_BoundingBox(null);
                    if (bb == null) continue;
                    XYZ head = SafeCenter(bb) + up * (offset * (1 + (i % 3)));
                    TryTag(doc, config.RebarTagType, view, bars[i], head, warnings);
                }
            }
        }

        private static void CreateHostDimensions(Document doc, FamilyInstance footing, View view, DimensionType type, BoundingBoxXYZ box, IList<string> warnings)
        {
            try
            {
                XYZ x = view.RightDirection.Normalize(), y = view.UpDirection.Normalize();
                List<PlanarFace> faces = GetPlanarFaces(footing);
                TryDimensionAxis(doc, view, type, box, faces, x, y, warnings, "X");
                if (view.ViewType == ViewType.FloorPlan || view.ViewType == ViewType.EngineeringPlan || view.ViewType == ViewType.CeilingPlan)
                    TryDimensionAxis(doc, view, type, box, faces, y, x, warnings, "Y");
                else
                {
                    XYZ vertical = XYZ.BasisZ;
                    TryDimensionAxis(doc, view, type, box, faces, vertical, x, warnings, "Z");
                }
            }
            catch (Exception ex) { warnings.Add("Không tạo được kích thước móng " + footing.Id.Value + ": " + ex.Message); }
        }

        private static void TryDimensionAxis(Document doc, View view, DimensionType type, BoundingBoxXYZ box,
            List<PlanarFace> faces, XYZ axis, XYZ offsetAxis, IList<string> warnings, string label)
        {
            var candidates = faces.Where(f => Math.Abs(Math.Abs(f.FaceNormal.Normalize().DotProduct(axis)) - 1.0) < 0.02 && f.Reference != null)
                .Select(f => new { Face = f, Value = f.Origin.DotProduct(axis) }).OrderBy(x => x.Value).ToList();
            if (candidates.Count < 2) return;
            PlanarFace a = candidates.First().Face, b = candidates.Last().Face;
            if (a.Reference == null || b.Reference == null || a.Reference == b.Reference) return;
            XYZ center = SafeCenter(box);
            double extent = Math.Max(box.Max.X - box.Min.X, Math.Max(box.Max.Y - box.Min.Y, box.Max.Z - box.Min.Z));
            double offset = UnitUtils.ConvertToInternalUnits(180, UnitTypeId.Millimeters) + extent * 0.12;
            XYZ start = center - axis * (extent / 2 + offset) + offsetAxis * (extent / 2 + offset);
            XYZ end = center + axis * (extent / 2 + offset) + offsetAxis * (extent / 2 + offset);
            var refs = new ReferenceArray(); refs.Append(a.Reference); refs.Append(b.Reference);
            try { doc.Create.NewDimension(view, Line.CreateBound(start, end), refs, type); }
            catch (Exception ex) { warnings.Add("Bỏ qua dim " + label + " của " + view.Name + ": " + ex.Message); }
        }

        private static List<PlanarFace> GetPlanarFaces(FamilyInstance instance)
        {
            var result = new List<PlanarFace>();
            var options = new Options { ComputeReferences = true, IncludeNonVisibleObjects = true, DetailLevel = ViewDetailLevel.Fine };
            GeometryElement geometry = instance.get_Geometry(options);
            if (geometry == null) return result;
            CollectPlanarFaces(geometry, result);
            return result;
        }
        private static void CollectPlanarFaces(GeometryElement geometry, List<PlanarFace> result)
        {
            foreach (GeometryObject obj in geometry)
            {
                if (obj is Solid solid)
                {
                    foreach (Face face in solid.Faces)
                        if (face is PlanarFace planar) result.Add(planar);
                }
                else if (obj is GeometryInstance gi)
                    CollectPlanarFaces(gi.GetInstanceGeometry(), result);
            }
        }
        private static void TryTag(Document doc, ElementType tagType, View view, Element target, XYZ head, IList<string> warnings)
        {
            try
            {
                var tag = IndependentTag.Create(doc, tagType.Id, view.Id, new Reference(target), true, TagOrientation.Horizontal, head);
                if (tag == null) warnings.Add("Không tạo được tag cho " + target.Id.Value + " trong " + view.Name);
            }
            catch (Exception ex) { warnings.Add("Bỏ qua tag cho " + target.Id.Value + " trong " + view.Name + ": " + ex.Message); }
        }

        private static List<Rebar> GetHostedBars(Document doc, FamilyInstance footing) => new FilteredElementCollector(doc).OfClass(typeof(Rebar)).WhereElementIsNotElementType()
            .Cast<Rebar>().Where(x => x.GetHostId() == footing.Id).ToList();
        private static bool IsLayer(Rebar rebar, BoundingBoxXYZ host, bool top)
        {
            BoundingBoxXYZ bar = rebar.get_BoundingBox(null);
            if (bar == null) return true;
            double center = (bar.Min.Z + bar.Max.Z) / 2;
            double mid = (host.Min.Z + host.Max.Z) / 2;
            return top ? center >= mid : center < mid;
        }
        private static void ApplyTemplate(View view, View template, IList<string> warnings)
        {
            if (view == null || template == null) return;
            try { view.ViewTemplateId = template.Id; }
            catch (Exception ex) { warnings.Add("Không áp được template " + template.Name + " cho " + view.Name + ": " + ex.Message); }
        }
        private static void SetPlanCrop(ViewPlan view, BoundingBoxXYZ source, double marginMm)
        {
            List<XYZ> corners = WorldCorners(source);
            double m = UnitUtils.ConvertToInternalUnits(marginMm, UnitTypeId.Millimeters);
            view.CropBox = new BoundingBoxXYZ { Transform = Transform.Identity,
                Min = new XYZ(corners.Min(x => x.X) - m, corners.Min(x => x.Y) - m, corners.Min(x => x.Z) - m),
                Max = new XYZ(corners.Max(x => x.X) + m, corners.Max(x => x.Y) + m, corners.Max(x => x.Z) + m) };
            view.CropBoxActive = true; view.CropBoxVisible = false;
        }
        internal static void UpdateCrop(View view, FamilyInstance footing, double marginMm, string role)
        {
            BoundingBoxXYZ source = footing.get_BoundingBox(null), prior = view.CropBox;
            if (source == null || prior == null) throw new InvalidOperationException("Không đọc được móng hoặc vùng crop.");
            Transform transform = prior.Transform ?? Transform.Identity;
            if (role != null && role.StartsWith("Section-", StringComparison.OrdinalIgnoreCase))
            {
                LocationPoint location = footing.Location as LocationPoint;
                if (location != null)
                {
                    double angle = location.Rotation;
                    XYZ direction = role.EndsWith("X", StringComparison.OrdinalIgnoreCase)
                        ? new XYZ(Math.Cos(angle), Math.Sin(angle), 0).Normalize()
                        : new XYZ(-Math.Sin(angle), Math.Cos(angle), 0).Normalize();
                    XYZ right = XYZ.BasisZ.CrossProduct(direction).Normalize();
                    transform = Transform.Identity;
                    transform.Origin = location.Point;
                    transform.BasisX = right;
                    transform.BasisY = XYZ.BasisZ;
                    transform.BasisZ = right.CrossProduct(XYZ.BasisZ).Normalize();
                }
            }
            List<XYZ> corners = WorldCorners(source);
            XYZ x = transform.BasisX.Normalize(), y = transform.BasisY.Normalize(), z = transform.BasisZ.Normalize(), origin = transform.Origin;
            double margin = UnitUtils.ConvertToInternalUnits(marginMm, UnitTypeId.Millimeters);
            view.CropBox = new BoundingBoxXYZ
            {
                Transform = transform,
                Min = new XYZ(corners.Min(p => (p-origin).DotProduct(x))-margin,
                    corners.Min(p => (p-origin).DotProduct(y))-margin,
                    corners.Min(p => (p-origin).DotProduct(z))-margin),
                Max = new XYZ(corners.Max(p => (p-origin).DotProduct(x))+margin,
                    corners.Max(p => (p-origin).DotProduct(y))+margin,
                    corners.Max(p => (p-origin).DotProduct(z))+margin)
            };
            view.CropBoxActive = true;
            if (view is ViewPlan plan && role != null &&
                (role.EndsWith("Top", StringComparison.OrdinalIgnoreCase) || role.EndsWith("Bot", StringComparison.OrdinalIgnoreCase)))
            {
                List<Rebar> bars = GetHostedBars(view.Document, footing);
                bool top = role.EndsWith("Top", StringComparison.OrdinalIgnoreCase);
                List<Rebar> layer = bars.Where(x => IsLayer(x, source, top)).ToList();
                try { SetLayerRange(plan, source, bars, layer, top, footing.LevelId); } catch { }
                List<ElementId> hidden = bars.Where(x => !layer.Any(y => y.Id == x.Id)).Select(x => x.Id).ToList();
                if (hidden.Count > 0) try { plan.HideElements(hidden); } catch { }
            }
        }
        private static void PlaceViewports(Document doc, ViewSheet sheet, IList<View> views, int batchIndex, IList<string> warnings)
        {
            BoundingBoxUV outline = sheet.Outline;
            double minX = outline.Min.U, maxX = outline.Max.U, minY = outline.Min.V, maxY = outline.Max.V;
            double width = maxX - minX, height = maxY - minY;
            if (width <= 0 || height <= 0) throw new InvalidOperationException("Khổ sheet không hợp lệ.");

            int columns = 2, rows = (views.Count + columns - 1) / columns;
            double cellWidth = width * 0.42;
            double usableTop = maxY - height * 0.10, usableBottom = minY + height * 0.10;
            double cellHeight = (usableTop - usableBottom) / Math.Max(1, rows);
            double targetWidth = cellWidth * 0.92;
            double targetHeight = cellHeight * 0.76;
            double leftCenter = minX + width * 0.28;
            double rightCenter = maxX - width * 0.28;

            for (int i = 0; i < views.Count; i++)
            {
                int row = i / columns, column = i % columns;
                XYZ center = new XYZ(column == 0 ? leftCenter : rightCenter,
                    usableTop - (row + 0.5) * cellHeight, 0);
                Viewport viewport = Viewport.Create(doc, sheet.Id, views[i].Id, center);
                doc.Regenerate();

                FitViewportToCell(doc, views[i], viewport, targetWidth, targetHeight, warnings);
                viewport.SetBoxCenter(center);
                doc.Regenerate();
                CenterViewportTitleRef(viewport, warnings);
                doc.Regenerate();
            }
        }

        private static void FitViewportToCell(Document doc, View view, Viewport viewport,
            double targetWidth, double targetHeight, IList<string> warnings)
        {
            try
            {
                Outline bounds = viewport.GetBoxOutline();
                double viewWidth = bounds.MaximumPoint.X - bounds.MinimumPoint.X;
                double viewHeight = bounds.MaximumPoint.Y - bounds.MinimumPoint.Y;
                if (viewWidth <= 0 || viewHeight <= 0) return;

                double factor = Math.Max(viewWidth / targetWidth, viewHeight / targetHeight);
                int currentScale = view.Scale;
                int fittedScale = Math.Max(1, Math.Min(200, (int)Math.Ceiling(currentScale * factor)));
                if (fittedScale != currentScale && (factor > 1.03 || factor < 0.97))
                {
                    view.Scale = fittedScale;
                    doc.Regenerate();
                    bounds = viewport.GetBoxOutline();
                    viewWidth = bounds.MaximumPoint.X - bounds.MinimumPoint.X;
                    viewHeight = bounds.MaximumPoint.Y - bounds.MinimumPoint.Y;
                }

                if (viewWidth > targetWidth * 1.02 || viewHeight > targetHeight * 1.02)
                    warnings.Add("View " + view.Name + " vượt vùng fit sheet ở tỷ lệ 1:" + view.Scale + ".");
            }
            catch (Exception ex)
            {
                warnings.Add("Không thể fit crop view " + view.Name + ": " + ex.Message);
            }
        }

        private static void CenterViewportTitleRef(Viewport viewport, IList<string> warnings)
        {
            try
            {
                Outline box = viewport.GetBoxOutline();
                Outline label = viewport.GetLabelOutline();
                if (box == null || label == null) return;

                double boxCenterX = (box.MinimumPoint.X + box.MaximumPoint.X) / 2.0;
                double labelCenterX = (label.MinimumPoint.X + label.MaximumPoint.X) / 2.0;
                double deltaX = boxCenterX - labelCenterX;
                if (Math.Abs(deltaX) <= 0.001) return;

                XYZ offset = viewport.LabelOffset;
                viewport.LabelOffset = new XYZ(offset.X + deltaX, offset.Y, offset.Z);
            }
            catch (Exception ex)
            {
                warnings.Add("Không thể căn giữa Title Ref theo viewport: " + ex.Message);
            }
        }
        private static double locationZ(FamilyInstance fi) => (fi.Location as LocationPoint)?.Point.Z ?? 0;
        private static XYZ SafeCenter(BoundingBoxXYZ box) => new XYZ((box.Min.X+box.Max.X)/2, (box.Min.Y+box.Max.Y)/2, (box.Min.Z+box.Max.Z)/2);
        private static List<XYZ> WorldCorners(BoundingBoxXYZ box)
        {
            Transform t = box.Transform ?? Transform.Identity; var result = new List<XYZ>();
            for (int x=0;x<2;x++) for(int y=0;y<2;y++) for(int z=0;z<2;z++) result.Add(t.OfPoint(new XYZ(x==0?box.Min.X:box.Max.X,y==0?box.Min.Y:box.Max.Y,z==0?box.Min.Z:box.Max.Z)));
            return result;
        }
        private static bool IsPileCap(FamilyInstance footing)
        {
            if (footing == null) return false;
            string fam = footing.Symbol?.FamilyName ?? "";
            string typ = footing.Name ?? "";
            return fam.IndexOf("cọc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fam.IndexOf("coc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fam.IndexOf("pile", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fam.IndexOf("cap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fam.IndexOf("đài", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   fam.IndexOf("dai", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typ.IndexOf("cọc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typ.IndexOf("pile", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<FamilyInstance> GetPiles(Document doc, FamilyInstance footing, BoundingBoxXYZ box)
        {
            var piles = new List<FamilyInstance>();
            if (box == null) return piles;
            try
            {
                var collector = new FilteredElementCollector(doc, doc.ActiveView?.Id ?? footing.LevelId)
                    .OfCategory(BuiltInCategory.OST_StructuralFoundation)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(fi => fi.Id != footing.Id);

                foreach (var fi in collector)
                {
                    BoundingBoxXYZ bb = fi.get_BoundingBox(null);
                    if (bb == null) continue;
                    string fam = fi.Symbol?.FamilyName ?? "";
                    string typ = fi.Name ?? "";
                    bool isPile = fam.IndexOf("cọc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 fam.IndexOf("coc", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 fam.IndexOf("pile", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isPile || bb.Max.Z <= box.Min.Z + 0.5)
                    {
                        if (bb.Min.X >= box.Min.X - 1.5 && bb.Max.X <= box.Max.X + 1.5 &&
                            bb.Min.Y >= box.Min.Y - 1.5 && bb.Max.Y <= box.Max.Y + 1.5)
                        {
                            piles.Add(fi);
                        }
                    }
                }
            }
            catch { }
            return piles;
        }

        private static string FormatName(string pattern, FamilyInstance footing)
        {
            string mark = footing.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString();
            string type = footing.Symbol == null ? "Móng" : footing.Symbol.Name;
            string level = footing.LevelId == ElementId.InvalidElementId ? "" : footing.Document.GetElement(footing.LevelId)?.Name ?? "";
            bool isPile = IsPileCap(footing);
            string defaultPattern = isPile ? "Đài cọc - {Mark}" : "Móng đơn - {Mark}";
            string actualPattern = string.IsNullOrWhiteSpace(pattern) ? defaultPattern : pattern;
            return actualPattern.Replace("{Mark}", string.IsNullOrWhiteSpace(mark) ? footing.Id.Value.ToString() : mark.Trim())
                .Replace("{Id}", footing.Id.Value.ToString()).Replace("{Type}", type).Replace("{Level}", level);
        }
        private static string FormatViewName(string pattern, FamilyInstance footing, string role)
        {
            bool isPile = IsPileCap(footing);
            string defaultPattern = isPile ? "KStruct - ĐC {Id} - {Role}" : "KStruct - MĐ {Id} - {Role}";
            string actualPattern = string.IsNullOrWhiteSpace(pattern) ? defaultPattern : pattern;
            return actualPattern
                .Replace("{Mark}", (footing.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "").Trim())
                .Replace("{Id}", footing.Id.Value.ToString()).Replace("{Role}", role);
        }
        private static string UniqueName(string value, HashSet<string> names)
        {
            if (!names.Contains(value)) return value;
            for (int i=2;;i++) { string test=value+" ("+i+")"; if (!names.Contains(test)) return test; }
        }
        private static string UniqueNumber(string prefix, string id, HashSet<string> numbers)
        {
            string value=prefix+id; for(int i=2;numbers.Contains(value);i++) value=prefix+id+"-"+i; return value;
        }
    }
}











