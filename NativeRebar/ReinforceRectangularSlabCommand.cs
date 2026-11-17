using System;
using System.Linq;
using System.Collections.Generic;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ReinforceRectangularSlabCommand : IExternalCommand
    {
        private sealed class FloorFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Floor;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class BarChoice
        {
            public RebarBarType Type { get; private set; }
            public string Display { get; private set; }
            public BarChoice(RebarBarType type)
            {
                Type = type;
                Parameter p = type.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER);
                double mm = p != null && p.HasValue ? UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Millimeters) : 0;
                Display = type.Name + " | Ø" + mm.ToString("0.##") + " mm";
            }
        }

        private sealed class Options
        {
            public RebarBarType TypeX, TypeY;
            public double CoverTopMm, CoverBottomMm, CoverSideMm;
            public double SpacingXmm, SpacingYmm;
            public bool Top, Bottom;
            public bool ExcludeSelectedModules;
        }

        private sealed class SlabFrame
        {
            public XYZ Center, Normal, X, Y;
            public double MinX, MaxX, MinY, MaxY, Thickness;
            public List<List<PlanPoint>> Boundaries;
            public List<ModuleZone> ModuleZones = new List<ModuleZone>();
        }

        private sealed class ModuleZone
        {
            public List<PlanPoint> Boundary;
            public double MinZ, MaxZ;
        }

        private sealed class PlanPoint
        {
            public double X, Y;
            public PlanPoint(double x, double y) { X = x; Y = y; }
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<FamilyInstance> selectedModules = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .OfType<FamilyInstance>().Distinct().ToList();
                List<Floor> floors = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Floor>().Distinct().ToList();
                if (floors.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new FloorFilter(), "Chọn sàn phẳng cần bố trí lưới thép");
                    floors = picked.Select(r => doc.GetElement(r)).OfType<Floor>().Distinct().ToList();
                }
                if (floors.Count == 0) return Result.Cancelled;
                List<RebarBarType> types = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType))
                    .Cast<RebarBarType>().OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (types.Count == 0) throw new InvalidOperationException("Project chưa có RebarBarType.");
                Options options = ChooseOptions(data.Application.MainWindowHandle, types, selectedModules.Count);
                if (options == null) return Result.Cancelled;

                int completed = 0, bars = 0, modulesExcluded = 0, bubbleZones = 0, replacedBars = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Cốt thép sàn hai phương"))
                {
                    tx.Start();
                    foreach (Floor floor in floors)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                List<ElementId> previousManagedBars = FindManagedSlabBars(doc, floor.Id);
                                SlabFrame frame = ReadSlabFrame(doc, floor);
                                int bubbleCount = AddManagedBubbleZones(doc, floor, frame,
                                    Math.Max(options.TypeX.BarNominalDiameter, options.TypeY.BarNominalDiameter) / 2.0);
                                int moduleZones = options.ExcludeSelectedModules
                                    ? AddSelectedModuleZones(doc, floor, frame, selectedModules, Math.Max(options.TypeX.BarNominalDiameter, options.TypeY.BarNominalDiameter) / 2.0)
                                    : 0;
                                int created = CreateMats(doc, floor, frame, options);
                                if (created <= 0) throw new InvalidOperationException("Không tạo được bộ thép mới; bộ KStruct cũ được giữ nguyên.");
                                if (previousManagedBars.Count > 0) doc.Delete(previousManagedBars);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận thép sàn.");
                                completed++;
                                bars += created;
                                replacedBars += previousManagedBars.Count;
                                modulesExcluded += moduleZones;
                                bubbleZones += bubbleCount;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Sàn " + floor.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (completed == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Cốt thép sàn", "Không tạo được thép cho sàn nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các thanh sàn mới.");
                }
                string report = "Đã tạo " + bars + " thanh Rebar cho " + completed + "/" + floors.Count + " sàn.";
                if (replacedBars > 0) report += " Đã thay " + replacedBars + " thanh KStruct cũ.";
                if (options.ExcludeSelectedModules) report += " Đã chừa " + modulesExcluded + " vùng module được chọn theo bounding box tại cao độ thanh giao với module.";
                if (bubbleZones > 0) report += " Đã tự chừa " + bubbleZones + " vùng cầu Bubble KStruct tại các lớp thép giao nhau.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " sàn:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Cốt thép sàn", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Cốt thép sàn", ex.Message);
                return Result.Failed;
            }
        }

        private static List<ElementId> FindManagedSlabBars(Document doc, ElementId floorId)
        {
            string prefix = "KSTR_SLAB_" + floorId.Value + "_";
            return new FilteredElementCollector(doc).OfClass(typeof(Rebar)).WhereElementIsNotElementType()
                .Cast<Rebar>().Where(bar => bar.GetHostId() == floorId)
                .Where(bar => (bar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM)?.AsString() ?? "")
                    .StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(bar => bar.Id).ToList();
        }

        private static SlabFrame ReadSlabFrame(Document doc, Floor floor)
        {
            IList<Reference> topFaces = HostObjectUtils.GetTopFaces(floor);
            PlanarFace face = topFaces.Select(r => floor.GetGeometryObjectFromReference(r) as PlanarFace)
                .Where(f => f != null && f.FaceNormal.Normalize().DotProduct(XYZ.BasisZ) > 0.999)
                .OrderByDescending(f => f.Area).FirstOrDefault();
            if (face == null) throw new InvalidOperationException("Không tìm được mặt trên nằm ngang.");
            XYZ normal = face.FaceNormal.Normalize();
            IList<CurveLoop> loops = face.GetEdgesAsCurveLoops();
            if (loops == null || loops.Count == 0) throw new InvalidOperationException("Không đọc được biên sàn.");
            var worldLoops = new List<List<XYZ>>();
            foreach (CurveLoop loop in loops)
            {
                var points = new List<XYZ>();
                foreach (Curve curve in loop)
                    foreach (XYZ point in curve.Tessellate())
                        if (points.Count == 0 || points[points.Count - 1].DistanceTo(point) > doc.Application.ShortCurveTolerance / 10.0)
                            points.Add(point);
                if (points.Count > 1 && points[0].DistanceTo(points[points.Count - 1]) <= doc.Application.ShortCurveTolerance / 10.0)
                    points.RemoveAt(points.Count - 1);
                if (points.Count < 3) throw new InvalidOperationException("Biên sàn phải có ít nhất ba điểm.");
                worldLoops.Add(points);
            }
            XYZ edgeDirection = worldLoops.SelectMany(loop => Enumerable.Range(0, loop.Count)
                    .Select(i => loop[(i + 1) % loop.Count] - loop[i]))
                .Where(vector => vector.GetLength() > doc.Application.ShortCurveTolerance)
                .OrderByDescending(vector => vector.GetLength()).FirstOrDefault();
            if (edgeDirection == null) throw new InvalidOperationException("Không xác định được phương lưới thép từ biên sàn.");
            XYZ x = (edgeDirection - normal * edgeDirection.DotProduct(normal)).Normalize();
            XYZ y = normal.CrossProduct(x).Normalize();
            if (Math.Abs(x.DotProduct(y)) > 0.01) throw new InvalidOperationException("Không xác định được hai phương vuông góc của sàn.");
            XYZ origin = worldLoops[0][0];
            var projected = worldLoops.Select(loop => loop.Select(point =>
            {
                XYZ relative = point - origin;
                return new PlanPoint(relative.DotProduct(x), relative.DotProduct(y));
            }).ToList()).ToList();
            List<PlanPoint> all = projected.SelectMany(loop => loop).ToList();
            double minX = all.Min(point => point.X), maxX = all.Max(point => point.X);
            double minY = all.Min(point => point.Y), maxY = all.Max(point => point.Y);
            XYZ center = origin + x * ((minX + maxX) / 2.0) + y * ((minY + maxY) / 2.0);
            var centered = projected.Select(loop => loop.Select(point =>
                new PlanPoint(point.X - (minX + maxX) / 2.0, point.Y - (minY + maxY) / 2.0)).ToList()).ToList();
            FloorType floorType = doc.GetElement(floor.GetTypeId()) as FloorType;
            CompoundStructure compound = floorType == null ? null : floorType.GetCompoundStructure();
            double thickness = compound == null ? 0 : compound.GetWidth();
            if (thickness <= doc.Application.ShortCurveTolerance) throw new InvalidOperationException("Không xác định được chiều dày FloorType.");
            return new SlabFrame
            {
                Center = center, Normal = normal, X = x, Y = y,
                MinX = minX - (minX + maxX) / 2.0, MaxX = maxX - (minX + maxX) / 2.0,
                MinY = minY - (minY + maxY) / 2.0, MaxY = maxY - (minY + maxY) / 2.0,
                Thickness = thickness, Boundaries = centered
            };
        }

        private static int AddSelectedModuleZones(Document doc, Floor floor, SlabFrame frame,
            IList<FamilyInstance> selectedModules, double barRadius)
        {
            double topZ = frame.Center.Z;
            double bottomZ = topZ - frame.Thickness;
            double tolerance = doc.Application.ShortCurveTolerance;
            int added = 0;
            foreach (FamilyInstance module in selectedModules)
            {
                BoundingBoxXYZ box = module.get_BoundingBox(null);
                if (box == null) continue;
                Transform transform = box.Transform ?? Transform.Identity;
                var corners = new List<XYZ>();
                for (int ix = 0; ix < 2; ix++)
                for (int iy = 0; iy < 2; iy++)
                for (int iz = 0; iz < 2; iz++)
                    corners.Add(transform.OfPoint(new XYZ(
                        ix == 0 ? box.Min.X : box.Max.X,
                        iy == 0 ? box.Min.Y : box.Max.Y,
                        iz == 0 ? box.Min.Z : box.Max.Z)));
                double minZ = corners.Min(point => point.Z), maxZ = corners.Max(point => point.Z);
                if (maxZ <= bottomZ + tolerance || minZ >= topZ - tolerance) continue;
                List<PlanPoint> local = corners.Select(point =>
                {
                    XYZ relative = point - frame.Center;
                    return new PlanPoint(relative.DotProduct(frame.X), relative.DotProduct(frame.Y));
                }).ToList();
                double minX = local.Min(point => point.X), maxX = local.Max(point => point.X);
                double minY = local.Min(point => point.Y), maxY = local.Max(point => point.Y);
                double centerX = (minX + maxX) / 2.0, centerY = (minY + maxY) / 2.0;
                if (!Inside(frame.Boundaries, centerX, centerY)) continue;
                minX = Math.Max(frame.MinX, minX - barRadius);
                maxX = Math.Min(frame.MaxX, maxX + barRadius);
                minY = Math.Max(frame.MinY, minY - barRadius);
                maxY = Math.Min(frame.MaxY, maxY + barRadius);
                if (maxX - minX <= tolerance || maxY - minY <= tolerance) continue;
                frame.ModuleZones.Add(new ModuleZone
                {
                    MinZ = minZ,
                    MaxZ = maxZ,
                    Boundary = new List<PlanPoint>
                    {
                        new PlanPoint(minX, minY), new PlanPoint(maxX, minY),
                        new PlanPoint(maxX, maxY), new PlanPoint(minX, maxY)
                    }
                });
                added++;
            }
            return added;
        }

        private static int AddManagedBubbleZones(Document doc, Floor floor, SlabFrame frame, double barRadius)
        {
            string dataId = floor.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            DirectShape bubble = new FilteredElementCollector(doc).OfClass(typeof(DirectShape)).Cast<DirectShape>()
                .FirstOrDefault(shape => string.Equals(shape.ApplicationId, "KStruct.BubbleSlab", StringComparison.Ordinal) &&
                    string.Equals(shape.ApplicationDataId, dataId, StringComparison.Ordinal));
            if (bubble == null) return 0;
            GeometryElement geometry = bubble.get_Geometry(new Autodesk.Revit.DB.Options
            {
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            });
            if (geometry == null) return 0;
            var solids = new List<Solid>();
            CollectBubbleSolids(geometry, solids);
            double bottom = frame.Center.Z - frame.Thickness;
            double top = frame.Center.Z;
            double tolerance = doc.Application.ShortCurveTolerance;
            int added = 0;
            foreach (Solid solid in solids)
            {
                if (solid.Faces.Size == 0 || solid.Volume <= 1e-9) continue;
                BoundingBoxXYZ box = solid.GetBoundingBox();
                if (box == null) continue;
                Transform transform = box.Transform ?? Transform.Identity;
                XYZ[] corners = new[]
                {
                    new XYZ(box.Min.X, box.Min.Y, box.Min.Z), new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
                    new XYZ(box.Min.X, box.Max.Y, box.Min.Z), new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
                    new XYZ(box.Max.X, box.Min.Y, box.Min.Z), new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
                    new XYZ(box.Max.X, box.Max.Y, box.Min.Z), new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
                }.Select(transform.OfPoint).ToArray();
                double minZ = corners.Min(point => point.Z), maxZ = corners.Max(point => point.Z);
                if (maxZ <= bottom + tolerance || minZ >= top - tolerance) continue;
                List<PlanPoint> local = corners.Select(point =>
                {
                    XYZ relative = point - frame.Center;
                    return new PlanPoint(relative.DotProduct(frame.X), relative.DotProduct(frame.Y));
                }).ToList();
                double minX = Math.Max(frame.MinX, local.Min(point => point.X) - barRadius);
                double maxX = Math.Min(frame.MaxX, local.Max(point => point.X) + barRadius);
                double minY = Math.Max(frame.MinY, local.Min(point => point.Y) - barRadius);
                double maxY = Math.Min(frame.MaxY, local.Max(point => point.Y) + barRadius);
                if (maxX - minX <= tolerance || maxY - minY <= tolerance) continue;
                frame.ModuleZones.Add(new ModuleZone
                {
                    MinZ = minZ, MaxZ = maxZ,
                    Boundary = new List<PlanPoint>
                    {
                        new PlanPoint(minX, minY), new PlanPoint(maxX, minY),
                        new PlanPoint(maxX, maxY), new PlanPoint(minX, maxY)
                    }
                });
                added++;
            }
            return added;
        }

        private static void CollectBubbleSolids(GeometryElement geometry, IList<Solid> solids)
        {
            foreach (GeometryObject item in geometry)
            {
                Solid solid = item as Solid;
                if (solid != null)
                {
                    if (solid.Faces.Size > 0 && solid.Volume > 1e-9) solids.Add(solid);
                    continue;
                }
                GeometryInstance instance = item as GeometryInstance;
                if (instance != null) CollectBubbleSolids(instance.GetInstanceGeometry(), solids);
            }
        }

        private static int CreateMats(Document doc, Floor floor, SlabFrame f, Options options)
        {
            double coverTop = UnitUtils.ConvertToInternalUnits(options.CoverTopMm, UnitTypeId.Millimeters);
            double coverBottom = UnitUtils.ConvertToInternalUnits(options.CoverBottomMm, UnitTypeId.Millimeters);
            double coverSide = UnitUtils.ConvertToInternalUnits(options.CoverSideMm, UnitTypeId.Millimeters);
            double spacingX = UnitUtils.ConvertToInternalUnits(options.SpacingXmm, UnitTypeId.Millimeters);
            double spacingY = UnitUtils.ConvertToInternalUnits(options.SpacingYmm, UnitTypeId.Millimeters);
            double diaX = options.TypeX.BarNominalDiameter;
            double diaY = options.TypeY.BarNominalDiameter;
            double edge = coverSide + Math.Max(diaX, diaY) / 2.0;
            double spanX = f.MaxX - f.MinX - 2 * edge;
            double spanY = f.MaxY - f.MinY - 2 * edge;
            if (spanX <= doc.Application.ShortCurveTolerance || spanY <= doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Cover/đường kính thép lớn hơn kích thước mặt sàn hữu dụng.");
            double clearance = UnitUtils.ConvertToInternalUnits(2, UnitTypeId.Millimeters);
            double layerGap = (diaX + diaY) / 2.0 + clearance;
            if (options.Bottom && f.Thickness <= coverBottom + diaX + clearance)
                throw new InvalidOperationException("Chiều dày sàn không đủ cho hai phương thép lớp dưới theo cover đã nhập.");
            if (options.Top && f.Thickness <= coverTop + diaX + clearance)
                throw new InvalidOperationException("Chiều dày sàn không đủ cho hai phương thép lớp trên theo cover đã nhập.");
            if (options.Top && options.Bottom &&
                f.Thickness <= coverTop + coverBottom + 2 * diaX + 2 * diaY + 2 * clearance)
                throw new InvalidOperationException("Chiều dày sàn không đủ cho hai lớp thép theo cover đã nhập.");
            int created = 0;
            if (options.Bottom)
            {
                double bottomSurfaceX = -f.Thickness + coverBottom + diaX / 2.0;
                created += CreateDirectionBars(doc, floor, f, options.TypeX, spacingY, edge, true, bottomSurfaceX, "BOT_X");
                created += CreateDirectionBars(doc, floor, f, options.TypeY, spacingX, edge, false, bottomSurfaceX + layerGap, "BOT_Y");
            }
            if (options.Top)
            {
                double topSurfaceX = -coverTop - diaX / 2.0;
                created += CreateDirectionBars(doc, floor, f, options.TypeX, spacingY, edge, true, topSurfaceX, "TOP_X");
                created += CreateDirectionBars(doc, floor, f, options.TypeY, spacingX, edge, false, topSurfaceX - layerGap, "TOP_Y");
            }
            if (created == 0) throw new InvalidOperationException("Cover và hình dạng sàn không còn đoạn thanh hữu dụng để tạo.");
            return created;
        }

        private static int CreateDirectionBars(Document doc, Floor floor, SlabFrame f, RebarBarType type,
            double maxSpacing, double edge, bool alongX, double normalOffset, string partition)
        {
            double minDistribution = (alongX ? f.MinY : f.MinX) + edge;
            double maxDistribution = (alongX ? f.MaxY : f.MaxX) - edge;
            double span = maxDistribution - minDistribution;
            if (span <= doc.Application.ShortCurveTolerance) return 0;
            double barZ = f.Center.Z + normalOffset;
            double barRadius = type.BarNominalDiameter / 2.0;
            List<List<PlanPoint>> boundaries = f.Boundaries.ToList();
            foreach (ModuleZone zone in f.ModuleZones)
                if (barZ + barRadius > zone.MinZ && barZ - barRadius < zone.MaxZ)
                    boundaries.Add(zone.Boundary);
            int rows = Math.Max(1, (int)Math.Ceiling(span / maxSpacing) + 1);
            if (rows > 2000) throw new InvalidOperationException("Khoảng cách yêu cầu tạo trên 2000 hàng thép theo phương " + partition + ".");
            int created = 0;
            for (int row = 0; row < rows; row++)
            {
                double distribution = rows == 1 ? (minDistribution + maxDistribution) / 2.0 :
                    minDistribution + span * row / (rows - 1.0);
                foreach (Tuple<double, double> interval in FindIntervals(boundaries, alongX, distribution, edge))
                {
                    XYZ a = alongX
                        ? f.Center + f.X * interval.Item1 + f.Y * distribution + f.Normal * normalOffset
                        : f.Center + f.X * distribution + f.Y * interval.Item1 + f.Normal * normalOffset;
                    XYZ b = alongX
                        ? f.Center + f.X * interval.Item2 + f.Y * distribution + f.Normal * normalOffset
                        : f.Center + f.X * distribution + f.Y * interval.Item2 + f.Normal * normalOffset;
                    if (a.DistanceTo(b) <= doc.Application.ShortCurveTolerance) continue;
                    Rebar rebar = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.Standard, type, null, null, floor,
                        f.Normal, new List<Curve> { Line.CreateBound(a, b) },
                        RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                    if (rebar == null) throw new InvalidOperationException("Revit không tạo được Rebar theo phương " + partition + ".");
                    Parameter p = rebar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
                    if (p != null && !p.IsReadOnly) p.Set("KSTR_SLAB_" + floor.Id.Value + "_" + partition);
                    created++;
                }
            }
            return created;
        }

        private static List<Tuple<double, double>> FindIntervals(IList<List<PlanPoint>> boundaries,
            bool alongX, double distribution, double endCover)
        {
            var intersections = new List<double>();
            foreach (List<PlanPoint> loop in boundaries)
            {
                for (int i = 0; i < loop.Count; i++)
                {
                    PlanPoint a = loop[i];
                    PlanPoint b = loop[(i + 1) % loop.Count];
                    double da = alongX ? a.Y : a.X;
                    double db = alongX ? b.Y : b.X;
                    if ((da <= distribution && db > distribution) || (db <= distribution && da > distribution))
                    {
                        double t = (distribution - da) / (db - da);
                        double valueA = alongX ? a.X : a.Y;
                        double valueB = alongX ? b.X : b.Y;
                        intersections.Add(valueA + (valueB - valueA) * t);
                    }
                }
            }
            intersections.Sort();
            var result = new List<Tuple<double, double>>();
            for (int i = 0; i + 1 < intersections.Count; i += 2)
            {
                double start = intersections[i] + endCover;
                double end = intersections[i + 1] - endCover;
                if (end > start) result.Add(Tuple.Create(start, end));
            }
            return result;
        }

        private static bool Inside(IList<List<PlanPoint>> boundaries, double x, double y)
        {
            bool inside = false;
            foreach (List<PlanPoint> loop in boundaries)
            {
                for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                {
                    PlanPoint a = loop[i], b = loop[j];
                    if ((a.Y > y) != (b.Y > y) &&
                        x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X)
                        inside = !inside;
                }
            }
            return inside;
        }

        private static Options ChooseOptions(IntPtr owner, IList<RebarBarType> types, int selectedModuleCount)
        {
            var choices = types.Select(t => new BarChoice(t)).ToList();
            var window = new Window
            {
                Title = "Cốt thép sàn hai phương - KStruct",
                Width = 430,
                Height = 590,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock { Text = "Loại thép phương X:", Margin = new Thickness(0, 0, 0, 4) });
            var comboX = new System.Windows.Controls.ComboBox { MinWidth = 390, MinHeight = 25, DisplayMemberPath = "Display", ItemsSource = choices, SelectedIndex = 0 };
            root.Children.Add(comboX);
            root.Children.Add(new TextBlock { Text = "Loại thép phương Y:", Margin = new Thickness(0, 6, 0, 4) });
            var comboY = new System.Windows.Controls.ComboBox { MinWidth = 390, MinHeight = 25, DisplayMemberPath = "Display", ItemsSource = choices, SelectedIndex = 0 };
            root.Children.Add(comboY);
            var coverBottomBox = AddInput(root, "Cover đáy (mm):", "25");
            var coverTopBox = AddInput(root, "Cover mặt trên (mm):", "25");
            var coverSideBox = AddInput(root, "Cover cạnh (mm):", "25");
            var spacingXBox = AddInput(root, "Khoảng cách tối đa phương X (mm):", "150");
            var spacingYBox = AddInput(root, "Khoảng cách tối đa phương Y (mm):", "150");
            var topCheck = new CheckBox { Content = "Tạo lớp thép trên", IsChecked = true, Margin = new Thickness(0, 8, 0, 2) };
            var bottomCheck = new CheckBox { Content = "Tạo lớp thép dưới", IsChecked = true };
            root.Children.Add(topCheck);
            root.Children.Add(bottomCheck);
            var excludeModules = new CheckBox
            {
                Content = selectedModuleCount > 0
                    ? "Chừa vùng family module đang chọn theo bounding box (" + selectedModuleCount + ")"
                    : "Chừa family module đã chọn trước khi chạy lệnh",
                IsEnabled = selectedModuleCount > 0,
                IsChecked = selectedModuleCount > 0,
                Margin = new Thickness(0, 8, 0, 2),
                ToolTip = "Chỉ cắt thanh tại cao độ giao với bounding box của FamilyInstance đã chọn."
            };
            root.Children.Add(excludeModules);
            root.Children.Add(new TextBlock
            {
                Text = "Chọn sàn và family module cần chừa. Vùng chừa dùng bounding box; Bubble KStruct được tự nhận diện. Đây là bố trí cấu tạo, không thay thế kiểm tra thiết kế kết cấu.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "Tạo thép", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) =>
            {
                double coverBottom, coverTop, coverSide, spacingX, spacingY;
                if (comboX.SelectedItem == null || comboY.SelectedItem == null ||
                    !double.TryParse(coverBottomBox.Text, out coverBottom) || coverBottom <= 0 ||
                    !double.TryParse(coverTopBox.Text, out coverTop) || coverTop <= 0 ||
                    !double.TryParse(coverSideBox.Text, out coverSide) || coverSide <= 0 ||
                    !double.TryParse(spacingXBox.Text, out spacingX) || spacingX <= 0 ||
                    !double.TryParse(spacingYBox.Text, out spacingY) || spacingY <= 0 ||
                    (topCheck.IsChecked != true && bottomCheck.IsChecked != true))
                {
                    TaskDialog.Show("KStruct - Cốt thép sàn", "Chọn thép theo hai phương, nhập cover/khoảng cách hợp lệ và chọn ít nhất một lớp.");
                    return;
                }
                window.DialogResult = true;
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = new ScrollViewer
            {
                Content = root,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            new WindowInteropHelper(window).Owner = owner;
            if (window.ShowDialog() != true) return null;
            double.TryParse(coverBottomBox.Text, out double coverBottomMm);
            double.TryParse(coverTopBox.Text, out double coverTopMm);
            double.TryParse(coverSideBox.Text, out double coverSideMm);
            double.TryParse(spacingXBox.Text, out double spacingXmm);
            double.TryParse(spacingYBox.Text, out double spacingYmm);
            return new Options
            {
                TypeX = ((BarChoice)comboX.SelectedItem).Type,
                TypeY = ((BarChoice)comboY.SelectedItem).Type,
                CoverBottomMm = coverBottomMm, CoverTopMm = coverTopMm, CoverSideMm = coverSideMm,
                SpacingXmm = spacingXmm, SpacingYmm = spacingYmm,
                Top = topCheck.IsChecked == true, Bottom = bottomCheck.IsChecked == true,
                ExcludeSelectedModules = excludeModules.IsChecked == true
            };
        }

        private static System.Windows.Controls.TextBox AddInput(System.Windows.Controls.Panel root, string label, string value)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 3) });
            var box = new System.Windows.Controls.TextBox { MinWidth = 370, MinHeight = 23, Text = value };
            root.Children.Add(box);
            return box;
        }
    }
}
