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
    public sealed class ReinforceFloorOpeningCommand : IExternalCommand
    {
        private sealed class OpeningFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Opening;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class BarChoice
        {
            public RebarBarType Type { get; private set; }
            public string Name { get; private set; }
            public BarChoice(RebarBarType type)
            {
                Type = type;
                Parameter diameter = type.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER);
                double mm = diameter != null && diameter.HasValue ? UnitUtils.ConvertFromInternalUnits(diameter.AsDouble(), UnitTypeId.Millimeters) : 0;
                Name = type.Name + " | Ø" + mm.ToString("0.##") + " mm";
            }
        }

        private sealed class Options
        {
            public RebarBarType Type;
            public double CoverMm;
            public double ExtensionMm;
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
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new OpeningFilter(), "Chọn lỗ mở đa giác hoặc chữ nhật trên sàn/tường cần gia cường");
                    openings = picked.Select(r => doc.GetElement(r)).OfType<Opening>().Distinct().ToList();
                }
                if (openings.Count == 0) return Result.Cancelled;
                List<RebarBarType> types = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType))
                    .Cast<RebarBarType>().OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (types.Count == 0) throw new InvalidOperationException("Project chưa có RebarBarType.");
                Options options = ChooseOptions(data.Application.MainWindowHandle, types);
                if (options == null) return Result.Cancelled;

                int created = 0, createdBars = 0, replacedBars = 0;
                var skipped = new List<string>();
                using (Transaction transaction = new Transaction(doc, "KStruct - Gia cường lỗ mở sàn"))
                {
                    transaction.Start();
                    foreach (Opening opening in openings)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                List<ElementId> previousManagedBars = FindManagedOpeningBars(doc, opening);
                                int bars = CreateOpeningTrimmers(doc, opening, options);
                                if (bars <= 0) throw new InvalidOperationException("Không tạo được thanh mới; bộ KStruct cũ được giữ nguyên.");
                                if (previousManagedBars.Count > 0) doc.Delete(previousManagedBars);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận thép gia cường.");
                                created++;
                                createdBars += bars;
                                replacedBars += previousManagedBars.Count;
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
                        transaction.RollBack();
                        TaskDialog.Show("KStruct - Gia cường lỗ mở", "Không gia cường được lỗ mở nào. Hỗ trợ hiện tại: lỗ đa giác trên sàn ngang và lỗ chữ nhật/đa giác trên tường đứng thẳng.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các thanh thép mới.");
                }
                string report = "Đã gia cường " + created + "/" + openings.Count + " lỗ mở, tạo " + createdBars + " thanh viền.";
                if (replacedBars > 0) report += " Đã thay " + replacedBars + " thanh KStruct cũ.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " lỗ mở:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Gia cường lỗ mở", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Gia cường lỗ mở", ex.Message);
                return Result.Failed;
            }
        }

        private static List<ElementId> FindManagedOpeningBars(Document doc, Opening opening)
        {
            Element host = opening.Host;
            if (host == null) return new List<ElementId>();
            string prefix = "KSTR_OPENING_" + opening.Id.Value + "_";
            return new FilteredElementCollector(doc).OfClass(typeof(Rebar)).WhereElementIsNotElementType()
                .Cast<Rebar>().Where(bar => bar.GetHostId() == host.Id)
                .Where(bar =>
                {
                    Parameter partition = bar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
                    return partition != null && partition.HasValue && partition.AsString() != null &&
                        partition.AsString().StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                }).Select(bar => bar.Id).ToList();
        }

        private static void SetPartition(Rebar bar, ElementId openingId, string suffix)
        {
            Parameter partition = bar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
            if (partition != null && !partition.IsReadOnly)
                partition.Set("KSTR_OPENING_" + openingId.Value + "_" + suffix);
        }

        private static int CreateOpeningTrimmers(Document doc, Opening opening, Options options)
        {
            Floor floor = opening.Host as Floor;
            if (floor == null)
            {
                Wall wall = opening.Host as Wall;
                if (wall != null) return CreateWallOpeningTrimmers(doc, opening, wall, options);
                throw new InvalidOperationException("Host lỗ mở phải là Floor hoặc Wall.");
            }
            if (floor.Category == null || floor.Category.Id.Value != (long)BuiltInCategory.OST_Floors)
                throw new InvalidOperationException("Lỗ mở sàn phải có host là Floor.");
            if (!opening.IsRectBoundary)
                return CreatePolygonFloorTrimmers(doc, opening, floor, options);
            IList<XYZ> points = opening.BoundaryRect;
            if (points == null) throw new InvalidOperationException("Revit không trả về BoundaryRect.");
            List<XYZ> corners = points.Where(p => p != null).Distinct(new PointComparer()).ToList();
            if (corners.Count != 4) throw new InvalidOperationException("Lỗ mở không có đúng bốn góc phân biệt.");
            XYZ center = corners.Aggregate(XYZ.Zero, (sum, point) => sum + point) / corners.Count;
            corners = corners.OrderBy(p => Math.Atan2(p.Y - center.Y, p.X - center.X)).ToList();
            if (corners.Any(p => Math.Abs(p.Z - corners[0].Z) > doc.Application.ShortCurveTolerance))
                throw new InvalidOperationException("Lỗ mở không nằm trên mặt phẳng ngang.");
            for (int i = 0; i < 4; i++)
            {
                XYZ edge = corners[(i + 1) % 4] - corners[i];
                XYZ next = corners[(i + 2) % 4] - corners[(i + 1) % 4];
                if (edge.GetLength() < doc.Application.ShortCurveTolerance || Math.Abs(edge.Normalize().DotProduct(next.Normalize())) > 0.02)
                    throw new InvalidOperationException("BoundaryRect không tạo thành hình chữ nhật hợp lệ.");
            }

            BoundingBoxXYZ hostBox = floor.get_BoundingBox(null);
            if (hostBox == null) throw new InvalidOperationException("Không đọc được hình học sàn.");
            double cover = UnitUtils.ConvertToInternalUnits(options.CoverMm, UnitTypeId.Millimeters);
            double extension = UnitUtils.ConvertToInternalUnits(options.ExtensionMm, UnitTypeId.Millimeters);
            double radius = options.Type.BarNominalDiameter / 2.0;
            double z = hostBox.Max.Z - cover - radius;
            if (z <= hostBox.Min.Z + radius) throw new InvalidOperationException("Chiều dày sàn không đủ với cover/thép đã chọn.");

            for (int i = 0; i < 4; i++)
            {
                XYZ a = corners[i], b = corners[(i + 1) % 4];
                XYZ direction = (b - a).Normalize();
                XYZ midpoint = (a + b) / 2.0;
                XYZ inward = center - midpoint;
                inward = new XYZ(inward.X, inward.Y, 0).Normalize();
                XYZ outward = inward.Negate();
                XYZ offset = outward * (cover + radius);
                XYZ start = new XYZ(a.X + offset.X - direction.X * extension, a.Y + offset.Y - direction.Y * extension, z);
                XYZ end = new XYZ(b.X + offset.X + direction.X * extension, b.Y + offset.Y + direction.Y * extension, z);
                Rebar bar = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.Standard, options.Type, null, null, floor,
                    XYZ.BasisZ, new List<Curve> { Line.CreateBound(start, end) },
                    RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                if (bar == null) throw new InvalidOperationException("Revit không tạo được thanh viền số " + (i + 1) + ".");
                SetPartition(bar, opening.Id, "FLOOR_" + (i + 1));
            }
            return 4;
        }

        private static int CreatePolygonFloorTrimmers(Document doc, Opening opening, Floor floor, Options options)
        {
            List<XYZ> points = ReadBoundaryPoints(opening, doc.Application.ShortCurveTolerance);
            if (points.Count < 3) throw new InvalidOperationException("Biên lỗ mở sàn cần ít nhất ba đỉnh.");
            if (points.Any(point => Math.Abs(point.Z - points[0].Z) > doc.Application.ShortCurveTolerance * 2))
                throw new InvalidOperationException("Lỗ mở đa giác không nằm trên một mặt phẳng ngang.");
            double area = SignedArea(points);
            if (Math.Abs(area) < doc.Application.ShortCurveTolerance * doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Diện tích biên lỗ mở gần bằng 0.");

            BoundingBoxXYZ hostBox = floor.get_BoundingBox(null);
            if (hostBox == null) throw new InvalidOperationException("Không đọc được hình học sàn.");
            double cover = UnitUtils.ConvertToInternalUnits(options.CoverMm, UnitTypeId.Millimeters);
            double extension = UnitUtils.ConvertToInternalUnits(options.ExtensionMm, UnitTypeId.Millimeters);
            double radius = options.Type.BarNominalDiameter / 2.0;
            double offsetDistance = cover + radius;
            double z = hostBox.Max.Z - offsetDistance;
            if (z <= hostBox.Min.Z + radius) throw new InvalidOperationException("Chiều dày sàn không đủ với cover/thép đã chọn.");

            int created = 0;
            for (int i = 0; i < points.Count; i++)
            {
                XYZ a = points[i], b = points[(i + 1) % points.Count];
                XYZ edge = b - a;
                XYZ direction = new XYZ(edge.X, edge.Y, 0);
                if (direction.GetLength() <= doc.Application.ShortCurveTolerance)
                    throw new InvalidOperationException("Biên lỗ mở có cạnh quá ngắn.");
                direction = direction.Normalize();
                XYZ outward = area > 0
                    ? new XYZ(direction.Y, -direction.X, 0)
                    : new XYZ(-direction.Y, direction.X, 0);
                XYZ shift = outward.Multiply(offsetDistance);
                XYZ start = new XYZ(a.X + shift.X - direction.X * extension, a.Y + shift.Y - direction.Y * extension, z);
                XYZ end = new XYZ(b.X + shift.X + direction.X * extension, b.Y + shift.Y + direction.Y * extension, z);
                Rebar bar = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.Standard, options.Type, null, null,
                    floor, XYZ.BasisZ, new List<Curve> { Line.CreateBound(start, end) },
                    RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                if (bar == null) throw new InvalidOperationException("Revit không tạo được thanh viền đa giác số " + (i + 1) + ".");
                SetPartition(bar, opening.Id, "FLOOR_POLY_" + (i + 1));
                created++;
            }
            return created;
        }

        private static int CreateWallOpeningTrimmers(Document doc, Opening opening, Wall wall, Options options)
        {
            Line wallAxis = (wall.Location as LocationCurve)?.Curve as Line;
            BoundingBoxXYZ wallBox = wall.get_BoundingBox(null);
            if (wallAxis == null || wallBox == null || Math.Abs(wallAxis.Direction.Z) > 1e-3)
                throw new InvalidOperationException("Lỗ mở tường chỉ hỗ trợ trên tường thẳng đứng có trục ngang.");
            if (!opening.IsRectBoundary)
                return CreatePolygonWallTrimmers(doc, opening, wall, wallAxis, wallBox, options);
            IList<XYZ> boundary = opening.BoundaryRect;
            List<XYZ> corners = boundary?.Where(point => point != null).Distinct(new PointComparer()).ToList();
            if (corners == null || corners.Count != 4)
                throw new InvalidOperationException("Revit không trả về bốn góc riêng biệt cho lỗ mở tường.");

            XYZ axis = new XYZ(wallAxis.Direction.X, wallAxis.Direction.Y, 0).Normalize();
            XYZ normal = wall.Orientation.Normalize();
            if (Math.Abs(normal.Z) > 1e-3 || Math.Abs(normal.DotProduct(axis)) > 1e-3)
                throw new InvalidOperationException("Không xác định được hai mặt tường.");
            XYZ midpoint = wallAxis.Evaluate(0.5, true);
            double[] along = corners.Select(point => (point - midpoint).DotProduct(axis)).ToArray();
            double z0 = corners.Min(point => point.Z), z1 = corners.Max(point => point.Z);
            double s0 = along.Min(), s1 = along.Max();
            double tolerance = UnitUtils.ConvertToInternalUnits(2, UnitTypeId.Millimeters);
            if (s1 - s0 < tolerance || z1 - z0 < tolerance)
                throw new InvalidOperationException("Kích thước lỗ mở tường quá nhỏ hoặc không đọc được.");
            if (corners.Any(point => Math.Abs((point - midpoint).DotProduct(normal)) > wall.Width / 2.0 + tolerance))
                throw new InvalidOperationException("Biên lỗ mở không nằm trong bề dày của tường host.");

            double cover = UnitUtils.ConvertToInternalUnits(options.CoverMm, UnitTypeId.Millimeters);
            double extension = UnitUtils.ConvertToInternalUnits(options.ExtensionMm, UnitTypeId.Millimeters);
            double radius = options.Type.BarNominalDiameter / 2.0;
            double faceOffset = wall.Width / 2.0 - cover - radius;
            if (faceOffset <= 0) throw new InvalidOperationException("Cover và đường kính thép lớn hơn nửa bề dày tường.");
            double wallHalfLength = wallAxis.Length / 2.0;
            double minAlong = Math.Max(-wallHalfLength, s0 - extension);
            double maxAlong = Math.Min(wallHalfLength, s1 + extension);
            double minZ = Math.Max(wallBox.Min.Z, z0 - extension);
            double maxZ = Math.Min(wallBox.Max.Z, z1 + extension);
            if (maxAlong - minAlong < doc.Application.ShortCurveTolerance || maxZ - minZ < doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Đoạn nối dài không còn chiều dài hữu dụng trong phạm vi tường.");

            int created = 0;
            for (int side = -1; side <= 1; side += 2)
            {
                XYZ faceOffsetVector = normal * (side * faceOffset);
                XYZ PointAt(double alongValue, double z) => midpoint + axis * alongValue + XYZ.BasisZ * (z - midpoint.Z) + faceOffsetVector;
                var edges = new[]
                {
                    Tuple.Create(PointAt(minAlong, z0), PointAt(maxAlong, z0)),
                    Tuple.Create(PointAt(minAlong, z1), PointAt(maxAlong, z1)),
                    Tuple.Create(PointAt(s0, minZ), PointAt(s0, maxZ)),
                    Tuple.Create(PointAt(s1, minZ), PointAt(s1, maxZ))
                };
                for (int edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
                {
                    Tuple<XYZ, XYZ> edge = edges[edgeIndex];
                    Rebar bar = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.Standard, options.Type,
                        null, null, wall, normal, new List<Curve> { Line.CreateBound(edge.Item1, edge.Item2) },
                        RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                    if (bar == null) throw new InvalidOperationException("Revit không tạo được thanh viền lỗ mở tường.");
                    SetPartition(bar, opening.Id, "WALL_" + side + "_" + edgeIndex);
                    created++;
                }
            }
            return created;
        }

        private static int CreatePolygonWallTrimmers(Document doc, Opening opening, Wall wall, Line wallAxis,
            BoundingBoxXYZ wallBox, Options options)
        {
            List<XYZ> points = ReadBoundaryPoints(opening, doc.Application.ShortCurveTolerance);
            if (points.Count < 3) throw new InvalidOperationException("Biên lỗ mở tường cần ít nhất ba đỉnh.");
            XYZ axis = new XYZ(wallAxis.Direction.X, wallAxis.Direction.Y, 0).Normalize();
            XYZ normal = wall.Orientation.Normalize();
            XYZ midpoint = wallAxis.Evaluate(0.5, true);
            double tolerance = UnitUtils.ConvertToInternalUnits(2, UnitTypeId.Millimeters);
            if (points.Any(point => Math.Abs((point - midpoint).DotProduct(normal)) > wall.Width / 2.0 + tolerance))
                throw new InvalidOperationException("Biên lỗ mở không nằm trong bề dày tường host.");

            var local = points.Select(point => new Point2((point - midpoint).DotProduct(axis), point.Z)).ToList();
            double area = SignedArea2D(local);
            if (Math.Abs(area) < tolerance * tolerance)
                throw new InvalidOperationException("Diện tích biên lỗ mở tường gần bằng 0.");
            double cover = UnitUtils.ConvertToInternalUnits(options.CoverMm, UnitTypeId.Millimeters);
            double extension = UnitUtils.ConvertToInternalUnits(options.ExtensionMm, UnitTypeId.Millimeters);
            double radius = options.Type.BarNominalDiameter / 2.0;
            double faceOffset = wall.Width / 2.0 - cover - radius;
            if (faceOffset <= 0) throw new InvalidOperationException("Cover và đường kính thép lớn hơn nửa bề dày tường.");
            double halfLength = wallAxis.Length / 2.0;
            int created = 0;
            for (int side = -1; side <= 1; side += 2)
            {
                XYZ faceOffsetVector = normal.Multiply(side * faceOffset);
                for (int i = 0; i < local.Count; i++)
                {
                    Point2 a = local[i], b = local[(i + 1) % local.Count];
                    double dx = b.X - a.X, dz = b.Y - a.Y;
                    double length = Math.Sqrt(dx * dx + dz * dz);
                    if (length <= doc.Application.ShortCurveTolerance) throw new InvalidOperationException("Biên lỗ mở có cạnh quá ngắn.");
                    double startAlong = Math.Max(-halfLength, a.X - dx / length * extension);
                    double endAlong = Math.Min(halfLength, b.X + dx / length * extension);
                    double startZ = Math.Max(wallBox.Min.Z, a.Y - dz / length * extension);
                    double endZ = Math.Min(wallBox.Max.Z, b.Y + dz / length * extension);
                    XYZ start = midpoint + axis * startAlong + XYZ.BasisZ * (startZ - midpoint.Z) + faceOffsetVector;
                    XYZ end = midpoint + axis * endAlong + XYZ.BasisZ * (endZ - midpoint.Z) + faceOffsetVector;
                    if (start.DistanceTo(end) < doc.Application.ShortCurveTolerance)
                        throw new InvalidOperationException("Đoạn nối dài của thanh viền quá ngắn trong phạm vi tường.");
                    Rebar bar = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.Standard, options.Type,
                        null, null, wall, normal, new List<Curve> { Line.CreateBound(start, end) },
                        RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                    if (bar == null) throw new InvalidOperationException("Revit không tạo được thanh viền tường đa giác.");
                    SetPartition(bar, opening.Id, "WALL_POLY_" + side + "_" + (i + 1));
                    created++;
                }
            }
            return created;
        }

        private static List<XYZ> ReadBoundaryPoints(Opening opening, double tolerance)
        {
            CurveArray boundaryCurves = opening.BoundaryCurves;
            var curves = new List<Curve>();
            if (boundaryCurves != null)
                foreach (Curve curve in boundaryCurves) curves.Add(curve);
            var pieces = curves.Where(curve => curve != null && curve.IsBound)
                .Select(curve => curve.Tessellate().Where(point => point != null).ToList())
                .Where(points => points.Count >= 2).ToList();
            if (pieces.Count == 0) throw new InvalidOperationException("Revit không trả về BoundaryCurves cho lỗ mở đa giác.");
            var result = new List<XYZ>(pieces[0]);
            pieces.RemoveAt(0);
            while (pieces.Count > 0)
            {
                XYZ end = result[result.Count - 1];
                int best = -1;
                bool reverse = false;
                double distance = double.MaxValue;
                for (int i = 0; i < pieces.Count; i++)
                {
                    double first = end.DistanceTo(pieces[i][0]);
                    double last = end.DistanceTo(pieces[i][pieces[i].Count - 1]);
                    if (first < distance) { distance = first; best = i; reverse = false; }
                    if (last < distance) { distance = last; best = i; reverse = true; }
                }
                if (best < 0 || distance > tolerance * 5)
                    throw new InvalidOperationException("BoundaryCurves của lỗ mở không tạo thành một vòng khép kín liên tục.");
                List<XYZ> next = pieces[best];
                if (reverse) next.Reverse();
                result.AddRange(next.Skip(1));
                pieces.RemoveAt(best);
            }
            if (result.Count > 1 && result[0].DistanceTo(result[result.Count - 1]) <= tolerance * 5)
                result.RemoveAt(result.Count - 1);
            var clean = new List<XYZ>();
            foreach (XYZ point in result)
                if (clean.Count == 0 || clean[clean.Count - 1].DistanceTo(point) > tolerance / 10.0)
                    clean.Add(point);
            if (clean.Count > 1 && clean[0].DistanceTo(clean[clean.Count - 1]) <= tolerance / 10.0)
                clean.RemoveAt(clean.Count - 1);
            return clean;
        }

        private static double SignedArea(IList<XYZ> points)
        {
            double area = 0;
            for (int i = 0; i < points.Count; i++)
            {
                XYZ a = points[i], b = points[(i + 1) % points.Count];
                area += a.X * b.Y - b.X * a.Y;
            }
            return area / 2.0;
        }

        private static double SignedArea2D(IList<Point2> points)
        {
            double area = 0;
            for (int i = 0; i < points.Count; i++)
            {
                Point2 a = points[i], b = points[(i + 1) % points.Count];
                area += a.X * b.Y - b.X * a.Y;
            }
            return area / 2.0;
        }

        private sealed class PointComparer : IEqualityComparer<XYZ>
        {
            public bool Equals(XYZ a, XYZ b) => a != null && b != null && a.DistanceTo(b) < 1e-7;
            public int GetHashCode(XYZ point) => 0;
        }

        private sealed class Point2
        {
            public double X, Y;
            public Point2(double x, double y) { X = x; Y = y; }
        }

        private static Options ChooseOptions(IntPtr owner, IList<RebarBarType> types)
        {
            List<BarChoice> choices = types.Select(t => new BarChoice(t)).ToList();
            var window = new Window
            {
                Title = "Gia cường lỗ mở sàn / tường",
                Width = 410,
                Height = 225,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock { Text = "Loại thép viền lỗ mở:", Margin = new Thickness(0, 0, 0, 4) });
            var typeCombo = new System.Windows.Controls.ComboBox { MinWidth = 370, MinHeight = 25, DisplayMemberPath = "Name", ItemsSource = choices, SelectedIndex = 0 };
            root.Children.Add(typeCombo);
            var coverBox = AddInput(root, "Cover từ mặt cấu kiện (mm):", "30");
            var extensionBox = AddInput(root, "Chiều dài nối quá góc (mm):", "300");
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "Tạo thép", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) =>
            {
                double cover, extension;
                if (typeCombo.SelectedItem == null || !double.TryParse(coverBox.Text, out cover) || cover <= 0 ||
                    !double.TryParse(extensionBox.Text, out extension) || extension < 0)
                {
                    TaskDialog.Show("KStruct - Gia cường lỗ mở", "Cover phải lớn hơn 0; chiều dài nối không được âm.");
                    return;
                }
                window.DialogResult = true;
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            if (window.ShowDialog() != true) return null;
            return new Options
            {
                Type = ((BarChoice)typeCombo.SelectedItem).Type,
                CoverMm = double.Parse(coverBox.Text),
                ExtensionMm = double.Parse(extensionBox.Text)
            };
        }

        private static System.Windows.Controls.TextBox AddInput(System.Windows.Controls.Panel root, string label, string value)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 7, 0, 3) });
            var input = new System.Windows.Controls.TextBox { MinWidth = 370, MinHeight = 23, Text = value };
            root.Children.Add(input);
            return input;
        }
    }
}
