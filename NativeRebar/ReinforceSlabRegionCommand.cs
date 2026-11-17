using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ReinforceSlabRegionCommand : IExternalCommand
    {
        private static readonly Guid SchemaId = new Guid("72D15F25-B5A3-4C9A-9D65-0286B9AB20D4");
        private const string ManualAdditionalRegionName = "__KStruct_ManualAdditional__";
        private enum Direction { AlongX, AlongY }
        private enum Layer { Top, Bottom }

        private sealed class FloorFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Floor;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class TypeChoice
        {
            public RebarBarType Type { get; }
            public string Label { get; }
            public TypeChoice(RebarBarType type)
            {
                Type = type;
                double diameter = UnitUtils.ConvertFromInternalUnits(type.BarNominalDiameter, UnitTypeId.Millimeters);
                Label = type.Name + " | Ø" + diameter.ToString("0.##", CultureInfo.CurrentCulture) + " mm";
            }
            public override string ToString() => Label;
        }

        private sealed class Choice<T>
        {
            public T Value { get; }
            public string Name { get; }
            public Choice(T value, string name) { Value = value; Name = name; }
            public override string ToString() => Name;
        }

        private sealed class Options
        {
            public string RegionName;
            public RebarBarType BarType;
            public Direction Direction;
            public Layer Layer;
            public double SpacingMm;
            public double CoverMm;
        }

        private sealed class SlabFrame
        {
            public XYZ Origin, Normal, X, Y;
            public double TopZ, Thickness;
            public List<List<Point2>> Boundaries;
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
                Floor floor = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Floor>().FirstOrDefault();
                if (floor == null)
                {
                    Reference reference = ui.Selection.PickObject(ObjectType.Element, new FloorFilter(),
                        "Chọn sàn ngang để bố trí thép theo vùng");
                    floor = doc.GetElement(reference) as Floor;
                }
                if (floor == null) return Result.Cancelled;
                SlabFrame frame = ReadFrame(doc, floor);
                List<RebarBarType> types = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
                    .OrderBy(type => type.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (types.Count == 0) throw new InvalidOperationException("Project chưa có RebarBarType.");
                Options options = ChooseOptions(data.Application.MainWindowHandle, types);
                if (options == null) return Result.Cancelled;

                List<Point2> region = PickRegion(ui, frame, doc.Application.ShortCurveTolerance);
                ValidatePolygon(region, doc.Application.ShortCurveTolerance);
                double minX = region.Min(point => point.X), maxX = region.Max(point => point.X);
                double minY = region.Min(point => point.Y), maxY = region.Max(point => point.Y);
                double tolerance = doc.Application.ShortCurveTolerance * 2;
                if (maxX - minX < tolerance || maxY - minY < tolerance)
                    throw new InvalidOperationException("Vùng thép phải có chiều dài và chiều rộng lớn hơn 0.");

                double cover = UnitUtils.ConvertToInternalUnits(options.CoverMm, UnitTypeId.Millimeters) + options.BarType.BarNominalDiameter / 2.0;
                if (maxX - minX <= 2 * cover || maxY - minY <= 2 * cover)
                    throw new InvalidOperationException("Vùng thép quá nhỏ so với cover và đường kính thanh.");
                List<Tuple<double, List<Tuple<double, double>>>> rows = BuildRows(frame, options, region, minX, maxX, minY, maxY, cover);
                if (rows.Count == 0 || rows.All(row => row.Item2.Count == 0))
                    throw new InvalidOperationException("Vùng chọn không giao với diện tích sàn đặc.");
                if (rows.Sum(row => row.Item2.Count) > 1500)
                    throw new InvalidOperationException("Vùng yêu cầu trên 1500 thanh Rebar; hãy tăng spacing hoặc giảm vùng.");

                Schema schema = GetSchema();
                List<ElementId> oldBars = FindOldRegionBars(doc, schema, floor.UniqueId, options.RegionName);
                int created = 0;
                using (Transaction transaction = new Transaction(doc, "KStruct - Thép sàn theo vùng"))
                {
                    transaction.Start();
                    using (SubTransaction sub = new SubTransaction(doc))
                    {
                        sub.Start();
                        try
                        {
                            foreach (Tuple<double, List<Tuple<double, double>>> row in rows)
                                foreach (Tuple<double, double> interval in row.Item2)
                                {
                                    XYZ a, b;
                                    if (options.Direction == Direction.AlongX)
                                    {
                                        a = FromLocal(frame, interval.Item1, row.Item1, LayerElevation(frame, options, cover));
                                        b = FromLocal(frame, interval.Item2, row.Item1, LayerElevation(frame, options, cover));
                                    }
                                    else
                                    {
                                        a = FromLocal(frame, row.Item1, interval.Item1, LayerElevation(frame, options, cover));
                                        b = FromLocal(frame, row.Item1, interval.Item2, LayerElevation(frame, options, cover));
                                    }
                                    Rebar bar = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.Standard, options.BarType,
                                        null, null, floor, frame.Normal, new List<Curve> { Line.CreateBound(a, b) },
                                        RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                                    if (bar == null) throw new InvalidOperationException("Revit không tạo được thanh trong vùng '" + options.RegionName + "'.");
                                    SetRegionTag(bar, schema, floor.UniqueId, options.RegionName);
                                    created++;
                                }
                            if (created == 0) throw new InvalidOperationException("Không tạo được thanh Rebar nào trong vùng.");
                            if (oldBars.Count > 0) doc.Delete(oldBars);
                            if (sub.Commit() != TransactionStatus.Committed)
                                throw new InvalidOperationException("Revit không chấp nhận bộ thép của vùng.");
                        }
                        catch
                        {
                            if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                            throw;
                        }
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các thanh thép vùng sàn.");
                }

                TaskDialog.Show("KStruct - Thép sàn theo vùng",
                    "Vùng: " + options.RegionName + "\nĐã tạo/cập nhật " + created + " thanh Rebar.\n" +
                    "Lớp: " + (options.Layer == Layer.Top ? "trên" : "dưới") +
                    " | Phương: " + (options.Direction == Direction.AlongX ? "X" : "Y") +
                    " | Ø" + UnitUtils.ConvertFromInternalUnits(options.BarType.BarNominalDiameter, UnitTypeId.Millimeters).ToString("0.##") +
                    " @ " + options.SpacingMm.ToString("0.##") + " mm.\n" +
                    (oldBars.Count > 0 ? "Đã thay bộ thép KStruct cũ cùng tên vùng." : ""));
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Thép sàn theo vùng", ex.Message);
                return Result.Failed;
            }
        }

        private static SlabFrame ReadFrame(Document doc, Floor floor)
        {
            PlanarFace top = HostObjectUtils.GetTopFaces(floor).Select(reference => floor.GetGeometryObjectFromReference(reference) as PlanarFace)
                .Where(face => face != null && face.FaceNormal.Normalize().DotProduct(XYZ.BasisZ) > 0.999)
                .OrderByDescending(face => face.Area).FirstOrDefault();
            if (top == null) throw new InvalidOperationException("Chỉ hỗ trợ sàn có mặt trên phẳng, nằm ngang.");
            IList<CurveLoop> loops = top.GetEdgesAsCurveLoops();
            if (loops == null || loops.Count == 0) throw new InvalidOperationException("Không đọc được biên sàn.");
            XYZ edge = loops.SelectMany(loop => loop.SelectMany(curve => curve.Tessellate().Zip(curve.Tessellate().Skip(1), (a, b) => b - a)))
                .Where(vector => vector.GetLength() > doc.Application.ShortCurveTolerance)
                .OrderByDescending(vector => vector.GetLength()).FirstOrDefault();
            if (edge == null) throw new InvalidOperationException("Không xác định được phương local của sàn.");
            XYZ normal = top.FaceNormal.Normalize();
            XYZ x = (edge - normal * edge.DotProduct(normal)).Normalize();
            XYZ y = normal.CrossProduct(x).Normalize();
            var boundaries = new List<List<Point2>>();
            foreach (CurveLoop loop in loops)
            {
                var points = new List<Point2>();
                foreach (Curve curve in loop)
                    foreach (XYZ point in curve.Tessellate())
                    {
                        Point2 local = ToLocal(new SlabFrame { Origin = top.Origin, X = x, Y = y }, point);
                        if (points.Count == 0 || Distance(points[points.Count - 1], local) > doc.Application.ShortCurveTolerance / 10.0)
                            points.Add(local);
                    }
                if (points.Count > 1 && Distance(points[0], points[points.Count - 1]) <= doc.Application.ShortCurveTolerance / 10.0)
                    points.RemoveAt(points.Count - 1);
                if (points.Count >= 3) boundaries.Add(points);
            }
            if (boundaries.Count == 0) throw new InvalidOperationException("Không tạo được biên vùng sàn hợp lệ.");
            FloorType type = doc.GetElement(floor.GetTypeId()) as FloorType;
            double thickness = type?.GetCompoundStructure()?.GetWidth() ?? 0;
            if (thickness <= doc.Application.ShortCurveTolerance) throw new InvalidOperationException("Không xác định được chiều dày sàn.");
            return new SlabFrame { Origin = top.Origin, Normal = normal, X = x, Y = y, TopZ = top.Origin.Z, Thickness = thickness, Boundaries = boundaries };
        }

        private static List<Point2> PickRegion(UIDocument ui, SlabFrame frame, double shortCurveTolerance)
        {
            var points = new List<Point2>();
            while (true)
            {
                XYZ picked = ui.Selection.PickPoint(points.Count < 3
                    ? "Chọn các đỉnh vùng thép (ít nhất 3 điểm)"
                    : "Chọn các đỉnh tiếp theo; chọn lại đỉnh đầu để khép vùng");
                Point2 local = ToLocal(frame, picked);
                if (points.Count >= 3 && Distance(points[0], local) <= Math.Max(
                    UnitUtils.ConvertToInternalUnits(5, UnitTypeId.Millimeters), shortCurveTolerance * 2))
                    break;
                if (points.Count > 0 && Distance(points[points.Count - 1], local) <= shortCurveTolerance)
                    continue;
                points.Add(local);
                if (points.Count > 100) throw new InvalidOperationException("Vùng thép không được vượt quá 100 đỉnh.");
            }
            return points;
        }

        private static void ValidatePolygon(IList<Point2> polygon, double tolerance)
        {
            if (polygon == null || polygon.Count < 3)
                throw new InvalidOperationException("Vùng thép cần ít nhất ba đỉnh.");
            double twiceArea = 0;
            for (int i = 0; i < polygon.Count; i++)
            {
                Point2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                if (Distance(a, b) <= tolerance)
                    throw new InvalidOperationException("Hai đỉnh liền kề của vùng thép quá gần nhau.");
                twiceArea += a.X * b.Y - b.X * a.Y;
            }
            if (Math.Abs(twiceArea) <= tolerance * tolerance)
                throw new InvalidOperationException("Vùng thép có diện tích gần bằng 0.");
            for (int i = 0; i < polygon.Count; i++)
            {
                Point2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                for (int j = i + 1; j < polygon.Count; j++)
                {
                    if (j == i || j == (i + 1) % polygon.Count || (j + 1) % polygon.Count == i) continue;
                    if (SegmentsIntersect(a, b, polygon[j], polygon[(j + 1) % polygon.Count], tolerance))
                        throw new InvalidOperationException("Biên vùng thép tự giao nhau; hãy chọn các đỉnh theo thứ tự quanh chu vi.");
                }
            }
        }

        private static bool SegmentsIntersect(Point2 a, Point2 b, Point2 c, Point2 d, double tolerance)
        {
            double scale = Math.Max(Distance(a, b), Distance(c, d));
            double areaTolerance = tolerance * Math.Max(scale, tolerance);
            double abC = Cross(a, b, c), abD = Cross(a, b, d);
            double cdA = Cross(c, d, a), cdB = Cross(c, d, b);
            int o1 = Sign(abC, areaTolerance), o2 = Sign(abD, areaTolerance);
            int o3 = Sign(cdA, areaTolerance), o4 = Sign(cdB, areaTolerance);
            if (o1 * o2 < 0 && o3 * o4 < 0) return true;
            return o1 == 0 && OnSegment(a, b, c, tolerance) ||
                   o2 == 0 && OnSegment(a, b, d, tolerance) ||
                   o3 == 0 && OnSegment(c, d, a, tolerance) ||
                   o4 == 0 && OnSegment(c, d, b, tolerance);
        }

        private static int Sign(double value, double tolerance) => value > tolerance ? 1 : value < -tolerance ? -1 : 0;

        private static bool OnSegment(Point2 a, Point2 b, Point2 p, double tolerance) =>
            p.X >= Math.Min(a.X, b.X) - tolerance && p.X <= Math.Max(a.X, b.X) + tolerance &&
            p.Y >= Math.Min(a.Y, b.Y) - tolerance && p.Y <= Math.Max(a.Y, b.Y) + tolerance;

        private static double Cross(Point2 a, Point2 b, Point2 c) =>
            (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        private static List<Tuple<double, List<Tuple<double, double>>>> BuildRows(SlabFrame frame, Options options,
            IList<Point2> region, double minX, double maxX, double minY, double maxY, double edge)
        {
            double spacing = UnitUtils.ConvertToInternalUnits(options.SpacingMm, UnitTypeId.Millimeters);
            bool alongX = options.Direction == Direction.AlongX;
            double perpendicularMin = (alongX ? minY : minX) + edge;
            double perpendicularMax = (alongX ? maxY : maxX) - edge;
            double parallelMin = (alongX ? minX : minY);
            double parallelMax = (alongX ? maxX : maxY);
            double span = perpendicularMax - perpendicularMin;
            if (span <= 0 || parallelMax <= parallelMin) return new List<Tuple<double, List<Tuple<double, double>>>>();
            int count = Math.Max(1, (int)Math.Floor(span / spacing) + 1);
            var rows = new List<Tuple<double, List<Tuple<double, double>>>>();
            for (int i = 0; i < count; i++)
            {
                double coordinate = perpendicularMin + i * spacing;
                List<Tuple<double, double>> regionIntervals = ScanlineIntervals(
                    new List<List<Point2>> { region.ToList() }, coordinate, alongX);
                List<Tuple<double, double>> floorIntervals = ScanlineIntervals(frame.Boundaries, coordinate, alongX);
                List<Tuple<double, double>> intervals = IntersectIntervals(regionIntervals, floorIntervals, parallelMin, parallelMax);
                intervals = intervals.Select(interval => Tuple.Create(interval.Item1 + edge, interval.Item2 - edge))
                    .Where(interval => interval.Item2 - interval.Item1 > 1e-6).ToList();
                rows.Add(Tuple.Create(coordinate, intervals));
            }
            return rows;
        }

        private static List<Tuple<double, double>> ScanlineIntervals(IList<List<Point2>> loops, double coordinate, bool alongX)
        {
            var intersections = new List<double>();
            foreach (List<Point2> polygon in loops)
                for (int i = 0; i < polygon.Count; i++)
                {
                    Point2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                    double av = alongX ? a.Y : a.X, bv = alongX ? b.Y : b.X;
                    double au = alongX ? a.X : a.Y, bu = alongX ? b.X : b.Y;
                    if ((av <= coordinate && bv > coordinate) || (bv <= coordinate && av > coordinate))
                    {
                        double t = (coordinate - av) / (bv - av);
                        intersections.Add(au + (bu - au) * t);
                    }
                }
            intersections.Sort();
            var result = new List<Tuple<double, double>>();
            for (int i = 0; i + 1 < intersections.Count; i += 2)
                if (intersections[i + 1] - intersections[i] > 1e-8)
                    result.Add(Tuple.Create(intersections[i], intersections[i + 1]));
            return result;
        }

        private static List<Tuple<double, double>> IntersectIntervals(IList<Tuple<double, double>> first,
            IList<Tuple<double, double>> second, double min, double max)
        {
            var result = new List<Tuple<double, double>>();
            foreach (Tuple<double, double> a in first)
                foreach (Tuple<double, double> b in second)
                {
                    double start = Math.Max(Math.Max(a.Item1, b.Item1), min);
                    double end = Math.Min(Math.Min(a.Item2, b.Item2), max);
                    if (end > start) result.Add(Tuple.Create(start, end));
                }
            return result.OrderBy(interval => interval.Item1).ToList();
        }

        private static double LayerElevation(SlabFrame frame, Options options, double coverAndRadius) =>
            options.Layer == Layer.Top ? frame.TopZ - coverAndRadius : frame.TopZ - frame.Thickness + coverAndRadius;

        private static Point2 ToLocal(SlabFrame frame, XYZ point)
        {
            XYZ relative = point - frame.Origin;
            return new Point2(relative.DotProduct(frame.X), relative.DotProduct(frame.Y));
        }

        private static XYZ FromLocal(SlabFrame frame, double x, double y, double z)
        {
            XYZ point = frame.Origin + frame.X * x + frame.Y * y;
            return new XYZ(point.X, point.Y, z);
        }

        private static double Distance(Point2 a, Point2 b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static Schema GetSchema()
        {
            Schema schema = Schema.Lookup(SchemaId);
            if (schema != null) return schema;
            var builder = new SchemaBuilder(SchemaId);
            builder.SetSchemaName("KStructSlabRegionRebar");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.AddSimpleField("FloorUniqueId", typeof(string));
            builder.AddSimpleField("RegionName", typeof(string));
            return builder.Finish();
        }

        internal static bool IsManagedRegionBar(Rebar bar, string floorUniqueId)
        {
            if (bar == null) return false;
            Schema schema = Schema.Lookup(SchemaId);
            if (schema == null) return false;
            try
            {
                Entity entity = bar.GetEntity(schema);
                return entity.IsValid() && string.Equals(entity.Get<string>(schema.GetField("FloorUniqueId")),
                    floorUniqueId, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        internal static void MarkAsManualAdditionalBar(Rebar bar)
        {
            if (bar == null || !(bar.Document.GetElement(bar.GetHostId()) is Floor floor))
                throw new InvalidOperationException("Chỉ có thể đánh dấu Rebar được host bởi Floor.");
            SetRegionTag(bar, GetSchema(), floor.UniqueId, ManualAdditionalRegionName);
        }

        private static void SetRegionTag(Element element, Schema schema, string floorUniqueId, string regionName)
        {
            var entity = new Entity(schema);
            entity.Set(schema.GetField("FloorUniqueId"), floorUniqueId ?? "");
            entity.Set(schema.GetField("RegionName"), regionName ?? "");
            element.SetEntity(entity);
        }

        private static List<ElementId> FindOldRegionBars(Document doc, Schema schema, string floorUniqueId, string regionName) =>
            new FilteredElementCollector(doc).OfClass(typeof(Rebar)).WhereElementIsNotElementType()
                .Where(bar =>
                {
                    Entity entity = bar.GetEntity(schema);
                    return entity.IsValid() && string.Equals(entity.Get<string>(schema.GetField("FloorUniqueId")), floorUniqueId, StringComparison.Ordinal) &&
                        string.Equals(entity.Get<string>(schema.GetField("RegionName")), regionName, StringComparison.OrdinalIgnoreCase);
                }).Select(bar => bar.Id).ToList();

        private static Options ChooseOptions(IntPtr owner, IList<RebarBarType> types)
        {
            var window = new Window
            {
                Title = "Cốt thép sàn theo vùng - KStruct",
                Width = 470,
                Height = 390,
                MinWidth = 430,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            new WindowInteropHelper(window).Owner = owner;
            var root = new StackPanel { Margin = new Thickness(14) };
            var nameBox = AddInput(root, "Tên vùng (dùng để cập nhật khi chạy lại):", "Vùng 1");
            var typeBox = AddCombo(root, "Loại thép:", types.Select(type => new TypeChoice(type)).ToList());
            var directionBox = AddCombo(root, "Phương thanh:", new[]
            {
                new Choice<Direction>(Direction.AlongX, "Theo trục X local sàn"),
                new Choice<Direction>(Direction.AlongY, "Theo trục Y local sàn")
            });
            var layerBox = AddCombo(root, "Lớp thép:", new[]
            {
                new Choice<Layer>(Layer.Bottom, "Thép dưới"),
                new Choice<Layer>(Layer.Top, "Thép trên")
            });
            var spacingBox = AddInput(root, "Spacing tối đa (mm):", "200");
            var coverBox = AddInput(root, "Cover (mm):", "25");
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "Chọn vùng / tạo", MinWidth = 120, IsDefault = true, Margin = new Thickness(0, 0, 7, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 70, IsCancel = true };
            Options result = null;
            ok.Click += (sender, args) =>
            {
                TypeChoice type = typeBox.SelectedItem as TypeChoice;
                Choice<Direction> direction = directionBox.SelectedItem as Choice<Direction>;
                Choice<Layer> layer = layerBox.SelectedItem as Choice<Layer>;
                double spacing, cover;
                if (string.IsNullOrWhiteSpace(nameBox.Text) || string.Equals(nameBox.Text.Trim(), ManualAdditionalRegionName, StringComparison.OrdinalIgnoreCase) ||
                    type == null || direction == null || layer == null ||
                    !double.TryParse(spacingBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out spacing) || spacing <= 0 ||
                    !double.TryParse(coverBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out cover) || cover <= 0)
                {
                    MessageBox.Show(window, "Nhập tên vùng khác tên dành riêng, loại thép, spacing và cover hợp lệ.", "KStruct - Thép sàn theo vùng",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                result = new Options { RegionName = nameBox.Text.Trim(), BarType = type.Type, Direction = direction.Value,
                    Layer = layer.Value, SpacingMm = spacing, CoverMm = cover };
                window.DialogResult = true;
            };
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            return window.ShowDialog() == true ? result : null;
        }

        private static WpfComboBox AddCombo<TChoice>(System.Windows.Controls.Panel root, string label, IList<TChoice> choices)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 4, 0, 2) });
            var combo = new WpfComboBox { ItemsSource = choices, SelectedIndex = 0, MinHeight = 25 };
            root.Children.Add(combo);
            return combo;
        }

        private static WpfTextBox AddInput(System.Windows.Controls.Panel root, string label, string value)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 4, 0, 2) });
            var input = new WpfTextBox { Text = value, MinHeight = 24 };
            root.Children.Add(input);
            return input;
        }
    }
}
