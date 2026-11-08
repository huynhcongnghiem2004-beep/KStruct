using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ReinforceMushroomDropPanelsCommand : IExternalCommand
    {
        private const string PanelPrefix = "KSTR_DP_";
        private const string SteelPrefix = "KSTR_DROPSTEEL_";

        private sealed class FloorFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Floor;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class BarChoice
        {
            public RebarBarType Type { get; }
            public string Label { get; }
            public BarChoice(RebarBarType type)
            {
                Type = type;
                Label = type.Name + " | Ø" + UnitUtils.ConvertFromInternalUnits(type.BarNominalDiameter, UnitTypeId.Millimeters).ToString("0.##");
            }
            public override string ToString() => Label;
        }

        private sealed class Options
        {
            public RebarBarType TypeX, TypeY;
            public RebarBarType UBarType;
            public double CoverBottomMm, CoverTopMm, CoverSideMm, SpacingXmm, SpacingYmm;
            public double UBarSpacingMm;
            public bool Bottom, Top, ReplaceExisting, CreateUBars;
        }

        private sealed class Point2
        {
            public double X, Y;
            public Point2(double x, double y) { X = x; Y = y; }
        }

        private sealed class PanelFrame
        {
            public XYZ Origin, X, Y;
            public double Top, Thickness, MinX, MaxX, MinY, MaxY;
            public List<List<Point2>> Boundaries;
        }

        private sealed class ColumnFootprint
        {
            public double MinX, MaxX, MinY, MaxY;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Floor> selected = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Floor>().Distinct().ToList();
                if (selected.Count == 0)
                {
                    Reference picked = ui.Selection.PickObject(ObjectType.Element, new FloorFilter(), "Chọn sàn hoặc drop panel Mushroom KStruct");
                    Floor floor = doc.GetElement(picked) as Floor;
                    if (floor != null) selected.Add(floor);
                }
                if (selected.Count == 0) return Result.Cancelled;

                List<Floor> panels = selected.Where(IsDropPanel).ToList();
                Floor baseSlab = selected.FirstOrDefault(floor => !IsDropPanel(floor));
                if (baseSlab != null)
                {
                    panels.AddRange(FindJoinedPanels(doc, baseSlab));
                }
                else
                {
                    foreach (Floor panel in panels.ToList())
                    {
                        Floor joinedBase = GetJoinedBaseSlab(doc, panel);
                        if (joinedBase != null) baseSlab = baseSlab ?? joinedBase;
                    }
                }
                panels = panels.GroupBy(panel => panel.Id).Select(group => group.First()).ToList();
                if (panels.Count == 0)
                    throw new InvalidOperationException("Không tìm thấy Floor drop panel có Mark KSTR_DP_. Hãy tạo drop panel bằng lệnh KStruct trước.");

                List<RebarBarType> types = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
                    .OrderBy(type => type.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (types.Count == 0) throw new InvalidOperationException("Project chưa có RebarBarType.");
                Options options = ChooseOptions(data.Application.MainWindowHandle, types);
                if (options == null) return Result.Cancelled;

                int completed = 0, created = 0, createdUBars = 0, removed = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Thép drop panel Mushroom"))
                {
                    tx.Start();
                    foreach (Floor panel in panels)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                if (options.ReplaceExisting) removed += DeleteOwnedBars(doc, panel);
                                PanelFrame frame = ReadPanelFrame(doc, panel);
                                double maxRadius = Math.Max(options.TypeX.BarNominalDiameter, options.TypeY.BarNominalDiameter) / 2.0;
                                if (options.CreateUBars) maxRadius = Math.Max(maxRadius, options.UBarType.BarNominalDiameter / 2.0);
                                ColumnFootprint column = AddColumnKeepout(doc, panel, frame, options.CoverSideMm, maxRadius);
                                int count = CreateMats(doc, panel, frame, options);
                                int uBarCount = options.CreateUBars ? CreateColumnUBars(doc, panel, frame, column, options) : 0;
                                count += uBarCount;
                                if (count == 0) throw new InvalidOperationException("Không còn đoạn thép hợp lệ sau khi trừ vùng cột/cover.");
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận thép drop panel.");
                                created += count;
                                createdUBars += uBarCount;
                                completed++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Panel " + panel.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (completed == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Thép drop panel", "Không tạo được thép.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận Rebar mới.");
                }
                string report = "Đã tạo " + created + " thanh Rebar trên " + completed + "/" + panels.Count + " drop panel.";
                if (options.CreateUBars) report += " Trong đó có " + createdUBars + " U-bar đứng quanh cột.";
                if (options.ReplaceExisting) report += " Đã thay " + removed + " thanh KStruct được đánh dấu trước đó.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " panel:\n" + string.Join("\n", skipped.Take(8));
                report += "\nLệnh bố trí hình học U-bar không thay thế kiểm toán chọc thủng hoặc kiểm tra thiết kế.";
                TaskDialog.Show("KStruct - Thép drop panel", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Thép drop panel", ex.Message);
                return Result.Failed;
            }
        }

        private static bool IsDropPanel(Floor floor) => ReadString(floor, BuiltInParameter.ALL_MODEL_MARK).StartsWith(PanelPrefix, StringComparison.OrdinalIgnoreCase);

        private static List<Floor> FindJoinedPanels(Document doc, Floor slab)
        {
            return new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>()
                .Where(panel => panel.Id != slab.Id && IsDropPanel(panel))
                .Where(panel => { try { return JoinGeometryUtils.AreElementsJoined(doc, slab, panel); } catch { return false; } }).ToList();
        }

        private static Floor GetJoinedBaseSlab(Document doc, Floor panel)
        {
            try
            {
                return JoinGeometryUtils.GetJoinedElements(doc, panel).Select(doc.GetElement).OfType<Floor>()
                    .FirstOrDefault(floor => floor.Id != panel.Id && !IsDropPanel(floor));
            }
            catch { return null; }
        }

        private static int DeleteOwnedBars(Document doc, Floor panel)
        {
            string prefix = SteelPrefix + panel.Id.Value + "_";
            List<ElementId> ids = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
                .Where(bar => bar.GetHostId() == panel.Id && ReadString(bar, BuiltInParameter.NUMBER_PARTITION_PARAM)
                    .StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(bar => bar.Id).ToList();
            if (ids.Count == 0) return 0;
            ICollection<ElementId> deleted = doc.Delete(ids);
            return deleted == null ? ids.Count : deleted.Count;
        }

        private static PanelFrame ReadPanelFrame(Document doc, Floor panel)
        {
            PlanarFace face = HostObjectUtils.GetTopFaces(panel)
                .Select(reference => panel.GetGeometryObjectFromReference(reference) as PlanarFace)
                .Where(candidate => candidate != null && candidate.FaceNormal.Normalize().DotProduct(XYZ.BasisZ) > 0.999)
                .OrderByDescending(candidate => candidate.Area).FirstOrDefault();
            if (face == null) throw new InvalidOperationException("Drop panel phải có mặt trên nằm ngang.");
            var worldLoops = new List<List<XYZ>>();
            foreach (CurveLoop loop in face.GetEdgesAsCurveLoops())
            {
                var points = new List<XYZ>();
                foreach (Curve curve in loop)
                foreach (XYZ point in curve.Tessellate())
                    if (points.Count == 0 || points[points.Count - 1].DistanceTo(point) > doc.Application.ShortCurveTolerance / 10.0)
                        points.Add(point);
                if (points.Count > 1 && points[0].DistanceTo(points[points.Count - 1]) <= doc.Application.ShortCurveTolerance / 10.0)
                    points.RemoveAt(points.Count - 1);
                if (points.Count >= 3) worldLoops.Add(points);
            }
            if (worldLoops.Count == 0) throw new InvalidOperationException("Không đọc được biên drop panel.");
            XYZ direction = worldLoops.SelectMany(loop => Enumerable.Range(0, loop.Count)
                    .Select(index => loop[(index + 1) % loop.Count] - loop[index]))
                .Where(vector => vector.GetLength() > doc.Application.ShortCurveTolerance)
                .OrderByDescending(vector => vector.GetLength()).FirstOrDefault();
            if (direction == null) throw new InvalidOperationException("Không xác định được phương panel.");
            XYZ x = new XYZ(direction.X, direction.Y, 0).Normalize();
            XYZ y = XYZ.BasisZ.CrossProduct(x).Normalize();
            XYZ origin = worldLoops[0][0];
            List<List<Point2>> projected = worldLoops.Select(loop => loop.Select(point =>
            {
                XYZ relative = point - origin;
                return new Point2(relative.DotProduct(x), relative.DotProduct(y));
            }).ToList()).ToList();
            List<Point2> outer = projected.OrderByDescending(Area).First();
            FloorType type = doc.GetElement(panel.GetTypeId()) as FloorType;
            double thickness = type?.GetCompoundStructure()?.GetWidth() ?? 0;
            if (thickness <= doc.Application.ShortCurveTolerance) throw new InvalidOperationException("Không xác định được chiều dày panel.");
            return new PanelFrame
            {
                Origin = origin, X = x, Y = y, Top = face.Origin.Z, Thickness = thickness,
                MinX = outer.Min(point => point.X), MaxX = outer.Max(point => point.X),
                MinY = outer.Min(point => point.Y), MaxY = outer.Max(point => point.Y),
                Boundaries = projected
            };
        }

        private static ColumnFootprint AddColumnKeepout(Document doc, Floor panel, PanelFrame frame, double sideCoverMm, double barRadius)
        {
            string mark = ReadString(panel, BuiltInParameter.ALL_MODEL_MARK);
            string idText = mark.Substring(PanelPrefix.Length);
            if (!long.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long columnValue))
                throw new InvalidOperationException("Mark panel không chứa ElementId cột.");
            FamilyInstance column = doc.GetElement(new ElementId(columnValue)) as FamilyInstance;
            if (column == null) throw new InvalidOperationException("Không tìm thấy cột liên kết với drop panel.");
            BoundingBoxXYZ box = column.get_BoundingBox(null);
            if (box == null) throw new InvalidOperationException("Không đọc được kích thước cột.");
            Transform transform = box.Transform ?? Transform.Identity;
            var corners = new List<XYZ>();
            for (int ix = 0; ix < 2; ix++) for (int iy = 0; iy < 2; iy++) for (int iz = 0; iz < 2; iz++)
                corners.Add(transform.OfPoint(new XYZ(ix == 0 ? box.Min.X : box.Max.X,
                    iy == 0 ? box.Min.Y : box.Max.Y, iz == 0 ? box.Min.Z : box.Max.Z)));
            List<Point2> local = corners.Select(point =>
            {
                XYZ relative = point - frame.Origin;
                return new Point2(relative.DotProduct(frame.X), relative.DotProduct(frame.Y));
            }).ToList();
            double clearance = UnitUtils.ConvertToInternalUnits(sideCoverMm, UnitTypeId.Millimeters) + barRadius;
            double minX = local.Min(point => point.X) - clearance, maxX = local.Max(point => point.X) + clearance;
            double minY = local.Min(point => point.Y) - clearance, maxY = local.Max(point => point.Y) + clearance;
            if (minX <= frame.MinX || maxX >= frame.MaxX || minY <= frame.MinY || maxY >= frame.MaxY)
                throw new InvalidOperationException("Vùng cột/cover chiếm hết drop panel.");
            frame.Boundaries.Add(new List<Point2>
            {
                new Point2(minX,minY), new Point2(maxX,minY), new Point2(maxX,maxY), new Point2(minX,maxY)
            });
            return new ColumnFootprint { MinX = minX, MaxX = maxX, MinY = minY, MaxY = maxY };
        }

        private static int CreateMats(Document doc, Floor panel, PanelFrame frame, Options options)
        {
            double coverBottom = UnitUtils.ConvertToInternalUnits(options.CoverBottomMm, UnitTypeId.Millimeters);
            double coverTop = UnitUtils.ConvertToInternalUnits(options.CoverTopMm, UnitTypeId.Millimeters);
            double coverSide = UnitUtils.ConvertToInternalUnits(options.CoverSideMm, UnitTypeId.Millimeters);
            double diaX = options.TypeX.BarNominalDiameter, diaY = options.TypeY.BarNominalDiameter;
            double radiusX = diaX / 2.0, radiusY = diaY / 2.0;
            double spacingX = UnitUtils.ConvertToInternalUnits(options.SpacingXmm, UnitTypeId.Millimeters);
            double spacingY = UnitUtils.ConvertToInternalUnits(options.SpacingYmm, UnitTypeId.Millimeters);
            double minThickness = options.Bottom && options.Top
                ? coverBottom + coverTop + 4 * (radiusX + radiusY) + UnitUtils.ConvertToInternalUnits(2, UnitTypeId.Millimeters)
                : options.Bottom ? coverBottom + 2 * (radiusX + radiusY)
                : options.Top ? coverTop + 2 * (radiusX + radiusY) : 0;
            if (minThickness > 0 && frame.Thickness <= minThickness)
                throw new InvalidOperationException("Chiều dày drop panel không đủ cho cover và các lớp thép.");

            double edgeX = coverSide + radiusX, edgeY = coverSide + radiusY;
            int countY = Math.Max(2, (int)Math.Ceiling((frame.MaxY - frame.MinY - 2 * edgeY) / spacingY) + 1);
            int countX = Math.Max(2, (int)Math.Ceiling((frame.MaxX - frame.MinX - 2 * edgeX) / spacingX) + 1);
            if ((long)(countX + countY) * ((options.Bottom ? 1 : 0) + (options.Top ? 1 : 0)) > 2000)
                throw new InvalidOperationException("Khoảng cách yêu cầu tạo trên 2000 hàng thép cho một panel.");
            int created = 0;
            if (options.Bottom)
            {
                double bottomX = frame.Top - frame.Thickness + coverBottom + radiusX;
                double bottomY = frame.Top - frame.Thickness + coverBottom + 2 * radiusX + radiusY;
                created += CreateDirectionBars(doc, panel, frame, options.TypeX, countY, edgeY, true, bottomX, "BOT_X");
                created += CreateDirectionBars(doc, panel, frame, options.TypeY, countX, edgeX, false, bottomY, "BOT_Y");
            }
            if (options.Top)
            {
                double topY = frame.Top - coverTop - radiusY;
                double topX = frame.Top - coverTop - 2 * radiusY - radiusX;
                created += CreateDirectionBars(doc, panel, frame, options.TypeX, countY, edgeY, true, topX, "TOP_X");
                created += CreateDirectionBars(doc, panel, frame, options.TypeY, countX, edgeX, false, topY, "TOP_Y");
            }
            return created;
        }

        private static int CreateColumnUBars(Document doc, Floor panel, PanelFrame frame,
            ColumnFootprint column, Options options)
        {
            double spacing = UnitUtils.ConvertToInternalUnits(options.UBarSpacingMm, UnitTypeId.Millimeters);
            double coverBottom = UnitUtils.ConvertToInternalUnits(options.CoverBottomMm, UnitTypeId.Millimeters);
            double coverTop = UnitUtils.ConvertToInternalUnits(options.CoverTopMm, UnitTypeId.Millimeters);
            double diameter = options.UBarType.BarNominalDiameter;
            double radius = diameter / 2.0;
            double clearance = UnitUtils.ConvertToInternalUnits(2, UnitTypeId.Millimeters);
            double bottom = frame.Top - frame.Thickness;
            double zX = bottom + coverBottom + radius;
            double zY = zX + 2 * radius + clearance;
            double top = frame.Top - coverTop - radius;
            double spanX = column.MaxX - column.MinX;
            double spanY = column.MaxY - column.MinY;
            if (spanX <= doc.Application.ShortCurveTolerance || spanY <= doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Kích thước vùng cột không đủ để tạo U-bar.");
            if (top - zX <= doc.Application.ShortCurveTolerance || top - zY <= doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Chiều dày drop panel không đủ cho U-bar theo cover đã nhập.");

            List<double> yPositions = Distribute(column.MinY + radius + clearance, column.MaxY - radius - clearance, spacing);
            List<double> xPositions = Distribute(column.MinX + radius + clearance, column.MaxX - radius - clearance, spacing);
            if ((long)yPositions.Count + xPositions.Count > 2000)
                throw new InvalidOperationException("U-bar yêu cầu trên 2000 thanh cho một drop panel.");

            int created = 0;
            foreach (double y in yPositions)
            {
                XYZ p0 = ToWorld(frame, column.MinX, y, top - frame.Top);
                XYZ p1 = ToWorld(frame, column.MinX, y, zX - frame.Top);
                XYZ p2 = ToWorld(frame, column.MaxX, y, zX - frame.Top);
                XYZ p3 = ToWorld(frame, column.MaxX, y, top - frame.Top);
                created += CreateUBar(doc, panel, options.UBarType, frame.Y,
                    new[] { p0, p1, p2, p3 }, "UBAR_X");
            }
            foreach (double x in xPositions)
            {
                XYZ p0 = ToWorld(frame, x, column.MinY, top - frame.Top);
                XYZ p1 = ToWorld(frame, x, column.MinY, zY - frame.Top);
                XYZ p2 = ToWorld(frame, x, column.MaxY, zY - frame.Top);
                XYZ p3 = ToWorld(frame, x, column.MaxY, top - frame.Top);
                created += CreateUBar(doc, panel, options.UBarType, frame.X,
                    new[] { p0, p1, p2, p3 }, "UBAR_Y");
            }
            return created;
        }

        private static List<double> Distribute(double minimum, double maximum, double maximumSpacing)
        {
            double span = maximum - minimum;
            int count = span <= maximumSpacing ? 1 : (int)Math.Ceiling(span / maximumSpacing) + 1;
            var points = new List<double>();
            for (int index = 0; index < count; index++)
                points.Add(count == 1 ? (minimum + maximum) / 2.0 : minimum + span * index / (count - 1.0));
            return points;
        }

        private static int CreateUBar(Document doc, Floor panel, RebarBarType type, XYZ normal,
            IList<XYZ> points, string direction)
        {
            var curves = new List<Curve>();
            for (int index = 0; index + 1 < points.Count; index++)
            {
                if (points[index].DistanceTo(points[index + 1]) <= doc.Application.ShortCurveTolerance)
                    throw new InvalidOperationException("Một nhánh U-bar ngắn hơn ShortCurveTolerance.");
                curves.Add(Line.CreateBound(points[index], points[index + 1]));
            }
            Rebar bar = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.Standard, type, null, null, panel,
                normal, curves, RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
            if (bar == null) throw new InvalidOperationException("Revit không tạo được U-bar phương " + direction + ".");
            Parameter partition = bar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
            if (partition != null && !partition.IsReadOnly)
                partition.Set(SteelPrefix + panel.Id.Value + "_" + direction);
            return 1;
        }

        private static XYZ ToWorld(PanelFrame frame, double x, double y, double verticalOffset)
        {
            return frame.Origin + frame.X * x + frame.Y * y + XYZ.BasisZ * verticalOffset;
        }

        private static int CreateDirectionBars(Document doc, Floor panel, PanelFrame frame, RebarBarType type,
            int rows, double edge, bool alongX, double z, string mark)
        {
            double minDistribution = (alongX ? frame.MinY : frame.MinX) + edge;
            double maxDistribution = (alongX ? frame.MaxY : frame.MaxX) - edge;
            double span = maxDistribution - minDistribution;
            if (span <= doc.Application.ShortCurveTolerance) return 0;
            int count = Math.Max(2, rows);
            int created = 0;
            for (int row = 0; row < count; row++)
            {
                double coordinate = minDistribution + span * row / (count - 1.0);
                foreach (Tuple<double, double> interval in FindIntervals(frame.Boundaries, alongX, coordinate, edge))
                {
                    XYZ start = alongX
                        ? frame.Origin + frame.X * interval.Item1 + frame.Y * coordinate + XYZ.BasisZ * z
                        : frame.Origin + frame.X * coordinate + frame.Y * interval.Item1 + XYZ.BasisZ * z;
                    XYZ end = alongX
                        ? frame.Origin + frame.X * interval.Item2 + frame.Y * coordinate + XYZ.BasisZ * z
                        : frame.Origin + frame.X * coordinate + frame.Y * interval.Item2 + XYZ.BasisZ * z;
                    if (start.DistanceTo(end) <= doc.Application.ShortCurveTolerance) continue;
                    Rebar bar = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.Standard, type, null, null, panel, XYZ.BasisZ,
                        new List<Curve> { Line.CreateBound(start, end) }, RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                    if (bar == null) throw new InvalidOperationException("Revit không tạo được thanh " + mark + ".");
                    Parameter partition = bar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
                    if (partition != null && !partition.IsReadOnly)
                        partition.Set(SteelPrefix + panel.Id.Value + "_" + mark);
                    created++;
                }
            }
            return created;
        }

        private static List<Tuple<double, double>> FindIntervals(IList<List<Point2>> boundaries, bool alongX, double coordinate, double endCover)
        {
            var intersections = new List<double>();
            foreach (List<Point2> loop in boundaries)
            for (int index = 0; index < loop.Count; index++)
            {
                Point2 a = loop[index], b = loop[(index + 1) % loop.Count];
                double da = alongX ? a.Y : a.X, db = alongX ? b.Y : b.X;
                if ((da <= coordinate && db > coordinate) || (db <= coordinate && da > coordinate))
                {
                    double t = (coordinate - da) / (db - da);
                    double av = alongX ? a.X : a.Y, bv = alongX ? b.X : b.Y;
                    intersections.Add(av + (bv - av) * t);
                }
            }
            intersections.Sort();
            var intervals = new List<Tuple<double, double>>();
            for (int index = 0; index + 1 < intersections.Count; index += 2)
            {
                double start = intersections[index] + endCover;
                double end = intersections[index + 1] - endCover;
                if (end - start > 0.05) intervals.Add(Tuple.Create(start, end));
            }
            return intervals;
        }

        private static double Area(IList<Point2> points)
        {
            double area = 0;
            for (int i = 0; i < points.Count; i++)
            {
                Point2 a = points[i], b = points[(i + 1) % points.Count];
                area += a.X * b.Y - b.X * a.Y;
            }
            return Math.Abs(area) / 2.0;
        }

        private static string ReadString(Element element, BuiltInParameter parameter)
        {
            Parameter value = element?.get_Parameter(parameter);
            return value != null && value.HasValue ? value.AsString() ?? string.Empty : string.Empty;
        }

        private static Options ChooseOptions(IntPtr owner, IList<RebarBarType> types)
        {
            var choices = types.Select(type => new BarChoice(type)).ToList();
            var window = new Window
            {
                Title = "Cốt thép drop panel Mushroom - KStruct", Width = 470, Height = 560,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock { Text = "Thép lưới hai phương cho các drop panel KStruct đã join với sàn.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            root.Children.Add(new TextBlock { Text = "Loại thép X:" });
            var typeX = new System.Windows.Controls.ComboBox { ItemsSource = choices, SelectedIndex = 0, DisplayMemberPath = "Label", MinWidth = 430 };
            root.Children.Add(typeX);
            root.Children.Add(new TextBlock { Text = "Loại thép Y:", Margin = new Thickness(0, 6, 0, 0) });
            var typeY = new System.Windows.Controls.ComboBox { ItemsSource = choices, SelectedIndex = 0, DisplayMemberPath = "Label", MinWidth = 430 };
            root.Children.Add(typeY);
            System.Windows.Controls.TextBox coverBottom = AddInput(root, "Cover đáy (mm):", "50");
            System.Windows.Controls.TextBox coverTop = AddInput(root, "Cover trên (mm):", "40");
            System.Windows.Controls.TextBox coverSide = AddInput(root, "Cover cạnh cột/panel (mm):", "40");
            System.Windows.Controls.TextBox spacingX = AddInput(root, "Khoảng cách X (mm):", "150");
            System.Windows.Controls.TextBox spacingY = AddInput(root, "Khoảng cách Y (mm):", "150");
            root.Children.Add(new TextBlock { Text = "Loại thép U-bar:", Margin = new Thickness(0, 6, 0, 0) });
            var typeU = new System.Windows.Controls.ComboBox { ItemsSource = choices, SelectedIndex = 0, DisplayMemberPath = "Label", MinWidth = 430 };
            root.Children.Add(typeU);
            System.Windows.Controls.TextBox uBarSpacing = AddInput(root, "Bước U-bar dọc hai mặt cột (mm):", "150");
            var uBars = new CheckBox { Content = "Tạo U-bar đứng hai phương quanh cột", IsChecked = false, Margin = new Thickness(0, 6, 0, 2) };
            root.Children.Add(uBars);
            root.Children.Add(new TextBlock { Text = "U-bar là hình học Rebar dạng chữ U trong drop panel; đây không phải kiểm toán chọc thủng.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            var bottom = new CheckBox { Content = "Tạo lớp đáy hai phương", IsChecked = true, Margin = new Thickness(0, 7, 0, 2) };
            var top = new CheckBox { Content = "Tạo lớp trên hai phương", IsChecked = true, Margin = new Thickness(0, 0, 0, 2) };
            var replace = new CheckBox { Content = "Thay thép KStruct đã tạo trên panel", IsChecked = true, Margin = new Thickness(0, 0, 0, 6) };
            root.Children.Add(bottom); root.Children.Add(top); root.Children.Add(replace);
            root.Children.Add(new TextBlock { Text = "Vùng cột được trừ khỏi lưới; chưa tạo thép chọc thủng hoặc U-bar.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Tạo thép", Width = 88, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            buttons.Children.Add(ok); buttons.Children.Add(cancel); root.Children.Add(buttons);
            window.Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            new WindowInteropHelper(window).Owner = owner;
            ok.Click += (sender, args) =>
            {
                double cb, ct, cs, sx, sy, su;
                if (typeX.SelectedItem == null || typeY.SelectedItem == null ||
                    !double.TryParse(coverBottom.Text, out cb) || cb <= 0 ||
                    !double.TryParse(coverTop.Text, out ct) || ct <= 0 ||
                    !double.TryParse(coverSide.Text, out cs) || cs <= 0 ||
                    !double.TryParse(spacingX.Text, out sx) || sx <= 0 ||
                    !double.TryParse(spacingY.Text, out sy) || sy <= 0 ||
                    (uBars.IsChecked == true && (typeU.SelectedItem == null || !double.TryParse(uBarSpacing.Text, out su) || su <= 0)) ||
                    (bottom.IsChecked != true && top.IsChecked != true && uBars.IsChecked != true))
                {
                    TaskDialog.Show("KStruct - Thép drop panel", "Chọn loại thép, nhập cover/khoảng cách lớn hơn 0 và chọn lưới đáy/trên hoặc U-bar.");
                    return;
                }
                window.DialogResult = true;
            };
            if (window.ShowDialog() != true) return null;
            return new Options
            {
                TypeX = ((BarChoice)typeX.SelectedItem).Type, TypeY = ((BarChoice)typeY.SelectedItem).Type,
                UBarType = ((BarChoice)typeU.SelectedItem).Type,
                CoverBottomMm = double.Parse(coverBottom.Text), CoverTopMm = double.Parse(coverTop.Text), CoverSideMm = double.Parse(coverSide.Text),
                SpacingXmm = double.Parse(spacingX.Text), SpacingYmm = double.Parse(spacingY.Text),
                UBarSpacingMm = uBars.IsChecked == true ? double.Parse(uBarSpacing.Text) : 150,
                CreateUBars = uBars.IsChecked == true,
                Bottom = bottom.IsChecked == true, Top = top.IsChecked == true, ReplaceExisting = replace.IsChecked == true
            };
        }

        private static System.Windows.Controls.TextBox AddInput(System.Windows.Controls.Panel root, string label, string value)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 5, 0, 2) });
            var box = new System.Windows.Controls.TextBox { Text = value, MinWidth = 430, MinHeight = 22 };
            root.Children.Add(box);
            return box;
        }
    }
}
