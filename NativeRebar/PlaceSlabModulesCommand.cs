using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class PlaceSlabModulesCommand : IExternalCommand
    {
        private static readonly Guid ManagedModuleSchemaId = new Guid("93E4C7BD-1F59-4712-9215-7E496AA18B92");
        private const string ManagedModuleFloorField = "HostFloorId";
        private sealed class FloorFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Floor;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class SymbolChoice
        {
            public FamilySymbol Symbol { get; }
            public string Label { get; }
            public SymbolChoice(FamilySymbol symbol)
            {
                Symbol = symbol;
                Label = symbol.FamilyName + " : " + symbol.Name + " (" + symbol.Family.FamilyPlacementType + ")";
            }
            public override string ToString() => Label;
        }

        private sealed class Options
        {
            public FamilySymbol Symbol;
            public double SpacingXmm, SpacingYmm, EdgeCoverMm, RotationDegrees, StructureClearanceMm;
            public bool ReplaceExisting;
        }

        private sealed class FloorFrame
        {
            public Reference TopFaceReference;
            public XYZ Center, X, Y, Normal;
            public double MinX, MaxX, MinY, MaxY;
            public List<List<Point2>> Loops;
        }

        private sealed class ModuleFootprint
        {
            public double Radius, HalfX, HalfY;
        }

        private sealed class Obstacle2D
        {
            public string Label;
            public double MinX, MaxX, MinY, MaxY;
        }

        private sealed class Point2
        {
            public double X, Y;
            public Point2(double x, double y) { X = x; Y = y; }
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
                    floors = ui.Selection.PickObjects(ObjectType.Element, new FloorFilter(), "Chọn sàn để bố trí family module")
                        .Select(reference => doc.GetElement(reference)).OfType<Floor>().Distinct().ToList();
                if (floors.Count == 0) return Result.Cancelled;

                List<FamilySymbol> symbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>().Where(IsSupportedPlacement)
                    .OrderBy(symbol => symbol.FamilyName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(symbol => symbol.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (symbols.Count == 0)
                    throw new InvalidOperationException("Project chưa có family type phù hợp. Hãy nạp family module sàn vào project trước.");
                Options options = ChooseOptions(data.Application.MainWindowHandle, symbols);
                if (options == null) return Result.Cancelled;

                int placed = 0, failed = 0;
                var reports = new List<string>();
                using (Transaction transaction = new Transaction(doc, "KStruct - Bố trí family module sàn"))
                {
                    transaction.Start();
                    if (!options.Symbol.IsActive)
                    {
                        options.Symbol.Activate();
                        doc.Regenerate();
                    }
                    foreach (Floor floor in floors)
                    {
                        using (SubTransaction floorTransaction = new SubTransaction(doc))
                        {
                            floorTransaction.Start();
                            try
                            {
                                if (options.ReplaceExisting)
                                    DeleteManagedModulesOnFloor(doc, floor.Id);
                                FloorFrame frame = ReadFrame(doc, floor);
                                ModuleFootprint footprint = MeasureFootprint(options.Symbol, frame, options.RotationDegrees,
                                    doc.Application.ShortCurveTolerance);
                                List<Obstacle2D> obstacles = CollectStructuralObstacles(doc, frame);
                                List<XYZ> points = GenerateGrid(frame, options, footprint, obstacles,
                                    doc.Application.ShortCurveTolerance, out int excludedByStructure);
                                if (excludedByStructure > 0 && reports.Count < 8)
                                    reports.Add("Sàn " + floor.Id.Value + ": loại " + excludedByStructure + " điểm lưới do gần cột/drop panel.");
                                int floorCount = 0;
                                foreach (XYZ point in points)
                                {
                                    try
                                    {
                                        FamilyInstance instance = PlaceInstance(doc, floor, frame, options.Symbol, point);
                                        if (instance == null) { failed++; continue; }
                                        TagManagedModule(instance, floor.Id);
                                        double radians = options.RotationDegrees * Math.PI / 180.0;
                                        if (Math.Abs(radians) > 1e-9)
                                        {
                                            Line axis = Line.CreateBound(point, point + frame.Normal);
                                            ElementTransformUtils.RotateElement(doc, instance.Id, axis, radians);
                                        }
                                        floorCount++;
                                    }
                                    catch (Exception ex)
                                    {
                                        failed++;
                                        if (reports.Count < 8) reports.Add("Sàn " + floor.Id.Value + ", điểm " + (placed + failed) + ": " + ex.Message);
                                    }
                                }
                                if (floorCount == 0)
                                {
                                    if (reports.Count < 8) reports.Add("Sàn " + floor.Id.Value + ": không có điểm đặt hợp lệ trong biên/cover.");
                                    if (options.ReplaceExisting)
                                        throw new InvalidOperationException("Không có điểm đặt hợp lệ; giữ nguyên các module KStruct cũ trên sàn này.");
                                }
                                if (floorTransaction.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận cập nhật module trên sàn.");
                                placed += floorCount;
                            }
                            catch (Exception ex)
                            {
                                if (floorTransaction.GetStatus() == TransactionStatus.Started) floorTransaction.RollBack();
                                failed++;
                                if (reports.Count < 8) reports.Add("Sàn " + floor.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (placed == 0)
                    {
                        transaction.RollBack();
                        TaskDialog.Show("KStruct - Module sàn", "Không đặt được family instance nào.\n" + string.Join("\n", reports));
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các family instance mới.");
                }

                string report = "Đã đặt " + placed + " instance '" + options.Symbol.FamilyName + " : " + options.Symbol.Name + "' trên " + floors.Count + " sàn.";
                if (failed > 0) report += "\nBỏ qua " + failed + " điểm/sàn:\n" + string.Join("\n", reports);
                TaskDialog.Show("KStruct - Module sàn", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Module sàn", ex.Message);
                return Result.Failed;
            }
        }

        private static bool IsSupportedPlacement(FamilySymbol symbol)
        {
            if (symbol == null || symbol.Family == null) return false;
            FamilyPlacementType placement = symbol.Family.FamilyPlacementType;
            return placement == FamilyPlacementType.OneLevelBased ||
                   placement == FamilyPlacementType.OneLevelBasedHosted ||
                   placement == FamilyPlacementType.WorkPlaneBased;
        }

        private static FloorFrame ReadFrame(Document doc, Floor floor)
        {
            Reference reference = HostObjectUtils.GetTopFaces(floor)
                .Select(candidate => new { Ref = candidate, Face = floor.GetGeometryObjectFromReference(candidate) as PlanarFace })
                .Where(candidate => candidate.Face != null && candidate.Face.FaceNormal.Normalize().DotProduct(XYZ.BasisZ) > 0.999)
                .OrderByDescending(candidate => candidate.Face.Area).Select(candidate => candidate.Ref).FirstOrDefault();
            PlanarFace face = reference == null ? null : floor.GetGeometryObjectFromReference(reference) as PlanarFace;
            if (face == null) throw new InvalidOperationException("Chỉ hỗ trợ sàn có mặt trên phẳng nằm ngang.");

            IList<CurveLoop> edgeLoops = face.GetEdgesAsCurveLoops();
            var worldLoops = new List<List<XYZ>>();
            foreach (CurveLoop edgeLoop in edgeLoops)
            {
                var points = new List<XYZ>();
                foreach (Curve curve in edgeLoop)
                    foreach (XYZ point in curve.Tessellate())
                        if (points.Count == 0 || points[points.Count - 1].DistanceTo(point) > doc.Application.ShortCurveTolerance / 10.0)
                            points.Add(point);
                if (points.Count > 1 && points[0].DistanceTo(points[points.Count - 1]) <= doc.Application.ShortCurveTolerance / 10.0)
                    points.RemoveAt(points.Count - 1);
                if (points.Count >= 3) worldLoops.Add(points);
            }
            if (worldLoops.Count == 0) throw new InvalidOperationException("Không đọc được đường bao sàn.");

            XYZ normal = face.FaceNormal.Normalize();
            XYZ direction = worldLoops.SelectMany(loop => Enumerable.Range(0, loop.Count)
                    .Select(index => loop[(index + 1) % loop.Count] - loop[index]))
                .Where(vector => vector.GetLength() > doc.Application.ShortCurveTolerance)
                .OrderByDescending(vector => vector.GetLength()).FirstOrDefault();
            if (direction == null) throw new InvalidOperationException("Không xác định được phương bố trí.");
            XYZ x = (direction - normal * direction.DotProduct(normal)).Normalize();
            XYZ y = normal.CrossProduct(x).Normalize();
            XYZ origin = worldLoops[0][0];
            List<List<Point2>> projected = worldLoops.Select(loop => loop.Select(point =>
            {
                XYZ relative = point - origin;
                return new Point2(relative.DotProduct(x), relative.DotProduct(y));
            }).ToList()).ToList();
            List<Point2> all = projected.SelectMany(loop => loop).ToList();
            double minX = all.Min(point => point.X), maxX = all.Max(point => point.X);
            double minY = all.Min(point => point.Y), maxY = all.Max(point => point.Y);
            double midX = (minX + maxX) / 2.0, midY = (minY + maxY) / 2.0;
            return new FloorFrame
            {
                TopFaceReference = reference,
                Center = origin + x * midX + y * midY,
                X = x, Y = y, Normal = normal,
                MinX = minX - midX, MaxX = maxX - midX,
                MinY = minY - midY, MaxY = maxY - midY,
                Loops = projected.Select(loop => loop.Select(point => new Point2(point.X - midX, point.Y - midY)).ToList()).ToList()
            };
        }

        private static ModuleFootprint MeasureFootprint(FamilySymbol symbol, FloorFrame frame, double rotationDegrees, double tolerance)
        {
            BoundingBoxXYZ box = symbol.get_BoundingBox(null);
            if (box == null) throw new InvalidOperationException("Không đọc được bounding box của family type để bảo đảm module không tràn biên sàn.");
            Transform transform = box.Transform ?? Transform.Identity;
            XYZ origin = transform.Origin;
            double radians = rotationDegrees * Math.PI / 180.0;
            double cosine = Math.Cos(radians), sine = Math.Sin(radians);
            double halfX = 0, halfY = 0, radius = 0;
            for (int ix = 0; ix < 2; ix++)
            for (int iy = 0; iy < 2; iy++)
            for (int iz = 0; iz < 2; iz++)
            {
                XYZ point = transform.OfPoint(new XYZ(
                    ix == 0 ? box.Min.X : box.Max.X,
                    iy == 0 ? box.Min.Y : box.Max.Y,
                    iz == 0 ? box.Min.Z : box.Max.Z));
                XYZ vector = point - origin;
                XYZ rotated = new XYZ(
                    vector.X * cosine - vector.Y * sine,
                    vector.X * sine + vector.Y * cosine,
                    vector.Z);
                double x = Math.Abs(rotated.DotProduct(frame.X));
                double y = Math.Abs(rotated.DotProduct(frame.Y));
                halfX = Math.Max(halfX, x);
                halfY = Math.Max(halfY, y);
                radius = Math.Max(radius, Math.Sqrt(x * x + y * y));
            }
            if (radius <= tolerance) throw new InvalidOperationException("Family type không có footprint mặt bằng hợp lệ.");
            return new ModuleFootprint { Radius = radius, HalfX = halfX, HalfY = halfY };
        }

        private static List<XYZ> GenerateGrid(FloorFrame frame, Options options, ModuleFootprint footprint,
            IList<Obstacle2D> obstacles, double tolerance, out int excludedByStructure)
        {
            excludedByStructure = 0;
            double sx = UnitUtils.ConvertToInternalUnits(options.SpacingXmm, UnitTypeId.Millimeters);
            double sy = UnitUtils.ConvertToInternalUnits(options.SpacingYmm, UnitTypeId.Millimeters);
            if (sx + tolerance < 2 * footprint.HalfX || sy + tolerance < 2 * footprint.HalfY)
                throw new InvalidOperationException("Bước lưới nhỏ hơn footprint family sau khi xoay; tăng bước X/Y để tránh các module chồng lấn.");
            double edge = UnitUtils.ConvertToInternalUnits(options.EdgeCoverMm, UnitTypeId.Millimeters) + footprint.Radius;
            double minX = frame.MinX + edge, maxX = frame.MaxX - edge;
            double minY = frame.MinY + edge, maxY = frame.MaxY - edge;
            if (maxX < minX || maxY < minY) throw new InvalidOperationException("Cover lớn hơn vùng mặt sàn.");
            double spanX = maxX - minX, spanY = maxY - minY;
            int nx = spanX <= sx ? 1 : (int)Math.Ceiling(spanX / sx) + 1;
            int ny = spanY <= sy ? 1 : (int)Math.Ceiling(spanY / sy) + 1;
            if ((long)nx * ny > 5000) throw new InvalidOperationException("Lưới yêu cầu trên 5000 family instance cho một sàn.");
            double dx = nx == 1 ? (minX + maxX) / 2.0 : spanX / (nx - 1.0);
            double dy = ny == 1 ? (minY + maxY) / 2.0 : spanY / (ny - 1.0);
            var result = new List<XYZ>();
            for (int ix = 0; ix < nx; ix++)
            for (int iy = 0; iy < ny; iy++)
            {
                double x = nx == 1 ? dx : minX + ix * dx;
                double y = ny == 1 ? dy : minY + iy * dy;
                if (!Inside(frame.Loops, x, y) || DistanceToBoundary(frame.Loops, x, y) + tolerance < edge) continue;
                double clearance = UnitUtils.ConvertToInternalUnits(options.StructureClearanceMm, UnitTypeId.Millimeters);
                bool nearStructure = obstacles.Any(obstacle =>
                {
                    double gapX = Math.Max(Math.Max(obstacle.MinX - (x + footprint.HalfX), 0),
                        (x - footprint.HalfX) - obstacle.MaxX);
                    double gapY = Math.Max(Math.Max(obstacle.MinY - (y + footprint.HalfY), 0),
                        (y - footprint.HalfY) - obstacle.MaxY);
                    bool overlap = x + footprint.HalfX > obstacle.MinX + tolerance &&
                        x - footprint.HalfX < obstacle.MaxX - tolerance &&
                        y + footprint.HalfY > obstacle.MinY + tolerance &&
                        y - footprint.HalfY < obstacle.MaxY - tolerance;
                    return overlap || gapX * gapX + gapY * gapY < clearance * clearance;
                });
                if (nearStructure) { excludedByStructure++; continue; }
                result.Add(frame.Center + frame.X * x + frame.Y * y);
            }
            return result;
        }

        private static List<Obstacle2D> CollectStructuralObstacles(Document doc, FloorFrame frame)
        {
            var result = new List<Obstacle2D>();
            double zTolerance = UnitUtils.ConvertToInternalUnits(5, UnitTypeId.Millimeters);
            IEnumerable<FamilyInstance> columns = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance))
                .WhereElementIsNotElementType().Cast<FamilyInstance>()
                .Where(instance => instance.Category != null &&
                    (instance.Category.Id.Value == (long)BuiltInCategory.OST_StructuralColumns ||
                     instance.Category.Id.Value == (long)BuiltInCategory.OST_Columns));
            foreach (FamilyInstance column in columns)
            {
                BoundingBoxXYZ box = column.get_BoundingBox(null);
                if (box == null) continue;
                List<XYZ> corners = WorldCorners(box);
                if (corners.Max(point => point.Z) < frame.Center.Z - zTolerance ||
                    corners.Min(point => point.Z) > frame.Center.Z + zTolerance) continue;
                result.Add(ProjectObstacle(box, frame, "cột " + column.Id.Value));
            }

            IEnumerable<Floor> dropPanels = new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>()
                .Where(floor => floor.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString()
                    .StartsWith("KSTR_DP_", StringComparison.OrdinalIgnoreCase) == true);
            foreach (Floor panel in dropPanels)
            {
                BoundingBoxXYZ box = panel.get_BoundingBox(null);
                if (box == null) continue;
                List<XYZ> corners = WorldCorners(box);
                if (Math.Abs(corners.Max(point => point.Z) - frame.Center.Z) > zTolerance) continue;
                result.Add(ProjectObstacle(box, frame, "drop panel " + panel.Id.Value));
            }
            return result;
        }

        private static Obstacle2D ProjectObstacle(BoundingBoxXYZ box, FloorFrame frame, string label)
        {
            List<XYZ> corners = WorldCorners(box);
            var points = corners.Select(point =>
            {
                XYZ relative = point - frame.Center;
                return new Point2(relative.DotProduct(frame.X), relative.DotProduct(frame.Y));
            }).ToList();
            return new Obstacle2D
            {
                Label = label,
                MinX = points.Min(point => point.X), MaxX = points.Max(point => point.X),
                MinY = points.Min(point => point.Y), MaxY = points.Max(point => point.Y)
            };
        }

        private static List<XYZ> WorldCorners(BoundingBoxXYZ box)
        {
            Transform transform = box.Transform ?? Transform.Identity;
            var points = new List<XYZ>();
            for (int ix = 0; ix < 2; ix++)
            for (int iy = 0; iy < 2; iy++)
            for (int iz = 0; iz < 2; iz++)
                points.Add(transform.OfPoint(new XYZ(ix == 0 ? box.Min.X : box.Max.X,
                    iy == 0 ? box.Min.Y : box.Max.Y, iz == 0 ? box.Min.Z : box.Max.Z)));
            return points;
        }

        private static FamilyInstance PlaceInstance(Document doc, Floor floor, FloorFrame frame, FamilySymbol symbol, XYZ point)
        {
            FamilyPlacementType placement = symbol.Family.FamilyPlacementType;
            if (placement == FamilyPlacementType.OneLevelBasedHosted)
                return doc.Create.NewFamilyInstance(point, symbol, floor, StructuralType.NonStructural);
            if (placement == FamilyPlacementType.WorkPlaneBased)
                return doc.Create.NewFamilyInstance(frame.TopFaceReference, point, frame.X, symbol);
            Level level = doc.GetElement(floor.LevelId) as Level;
            if (level == null) throw new InvalidOperationException("Không xác định được Level của sàn để đặt family.");
            return doc.Create.NewFamilyInstance(point, symbol, level, StructuralType.NonStructural);
        }

        internal static Schema GetManagedModuleSchema(bool create)
        {
            Schema schema = Schema.Lookup(ManagedModuleSchemaId);
            if (schema != null || !create) return schema;
            var builder = new SchemaBuilder(ManagedModuleSchemaId);
            builder.SetSchemaName("KStructManagedSlabModuleV1");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField(ManagedModuleFloorField, typeof(long));
            return builder.Finish();
        }

        private static void TagManagedModule(FamilyInstance instance, ElementId floorId)
        {
            Schema schema = GetManagedModuleSchema(true);
            Entity entity = new Entity(schema);
            entity.Set(schema.GetField(ManagedModuleFloorField), floorId.Value);
            instance.SetEntity(entity);
        }

        internal static long? ReadManagedModuleFloorId(FamilyInstance instance)
        {
            Schema schema = GetManagedModuleSchema(false);
            if (schema == null) return null;
            Entity entity = instance.GetEntity(schema);
            if (!entity.IsValid()) return null;
            return entity.Get<long>(schema.GetField(ManagedModuleFloorField));
        }

        internal static int DeleteManagedModulesOnFloor(Document doc, ElementId floorId)
        {
            long floorValue = floorId.Value;
            List<ElementId> ids = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance))
                .WhereElementIsNotElementType().Cast<FamilyInstance>()
                .Where(instance => ReadManagedModuleFloorId(instance) == floorValue)
                .Select(instance => instance.Id).ToList();
            if (ids.Count > 0) doc.Delete(ids);
            return ids.Count;
        }

        private static bool Inside(IList<List<Point2>> loops, double x, double y)
        {
            bool inside = false;
            foreach (List<Point2> loop in loops)
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
            {
                Point2 a = loop[i], b = loop[j];
                if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }
            return inside;
        }

        private static double DistanceToBoundary(IList<List<Point2>> loops, double x, double y)
        {
            double best = double.MaxValue;
            foreach (List<Point2> loop in loops)
            for (int i = 0; i < loop.Count; i++)
            {
                Point2 a = loop[i], b = loop[(i + 1) % loop.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double length2 = dx * dx + dy * dy;
                double t = length2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((x - a.X) * dx + (y - a.Y) * dy) / length2));
                double px = a.X + t * dx, py = a.Y + t * dy;
                best = Math.Min(best, Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py)));
            }
            return best;
        }

        private static Options ChooseOptions(IntPtr owner, IList<FamilySymbol> symbols)
        {
            var choices = symbols.Select(symbol => new SymbolChoice(symbol)).ToList();
            var window = new Window
            {
                Title = "Bố trí family module sàn - KStruct", Width = 450, Height = 360,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock { Text = "Family type đã nạp trong project:", Margin = new Thickness(0, 0, 0, 4) });
            var symbolBox = new System.Windows.Controls.ComboBox { ItemsSource = choices, SelectedIndex = 0, MinWidth = 420 };
            root.Children.Add(symbolBox);
            System.Windows.Controls.TextBox spacingX = AddInput(root, "Bước lưới phương X (mm):", "600");
            System.Windows.Controls.TextBox spacingY = AddInput(root, "Bước lưới phương Y (mm):", "600");
            System.Windows.Controls.TextBox edge = AddInput(root, "Khoảng lùi mép/lỗ mở (mm):", "100");
            System.Windows.Controls.TextBox structureClearance = AddInput(root, "Khoảng hở module với cột/drop panel (mm):", "100");
            System.Windows.Controls.TextBox rotation = AddInput(root, "Góc xoay instance (độ):", "0");
            var replaceExisting = new CheckBox
            {
                Content = "Cập nhật: thay các module KStruct cũ trên sàn đã chọn",
                Margin = new Thickness(0, 9, 0, 0)
            };
            root.Children.Add(replaceExisting);
            root.Children.Add(new TextBlock
            {
                Text = "Vùng loại trừ dùng khoảng cách giữa bounding box trong mặt bằng; kiểm tra là xấp xỉ hình học.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 2)
            });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "Bố trí", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) =>
            {
                double x, y, e, c, a;
                if (symbolBox.SelectedItem == null || !double.TryParse(spacingX.Text, out x) ||
                    !double.TryParse(spacingY.Text, out y) || !double.TryParse(edge.Text, out e) ||
                    !double.TryParse(structureClearance.Text, out c) || !double.TryParse(rotation.Text, out a) ||
                    x <= 0 || y <= 0 || e < 0 || c < 0)
                {
                    TaskDialog.Show("KStruct - Module sàn", "Bước lưới phải lớn hơn 0; khoảng lùi mép và khoảng hở kết cấu không được âm.");
                    return;
                }
                window.DialogResult = true;
            };
            buttons.Children.Add(ok); buttons.Children.Add(cancel); root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            if (window.ShowDialog() != true) return null;
            return new Options
            {
                Symbol = ((SymbolChoice)symbolBox.SelectedItem).Symbol,
                SpacingXmm = double.Parse(spacingX.Text), SpacingYmm = double.Parse(spacingY.Text),
                EdgeCoverMm = double.Parse(edge.Text), StructureClearanceMm = double.Parse(structureClearance.Text),
                RotationDegrees = double.Parse(rotation.Text), ReplaceExisting = replaceExisting.IsChecked == true
            };
        }

        private static System.Windows.Controls.TextBox AddInput(System.Windows.Controls.Panel root, string label, string value)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 5, 0, 2) });
            var box = new System.Windows.Controls.TextBox { Text = value, MinWidth = 420, MinHeight = 22 };
            root.Children.Add(box);
            return box;
        }
    }
}
