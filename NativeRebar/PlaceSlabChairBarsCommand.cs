using System;
using System.Collections.Generic;
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
    public sealed class PlaceSlabChairBarsCommand : IExternalCommand
    {
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
                double dia = type.BarNominalDiameter;
                Label = type.Name + " | Ø" + UnitUtils.ConvertFromInternalUnits(dia, UnitTypeId.Millimeters).ToString("0.##") + " mm";
            }
            public override string ToString() => Label;
        }

        private sealed class Options
        {
            public RebarBarType Type;
            public double TopCoverMm, BottomCoverMm, TopMatDiameterMm;
            public double TopSpanMm, FootLengthMm, SpacingXmm, SpacingYmm;
            public bool AlongX;
        }

        private sealed class Frame
        {
            public XYZ Origin, X, Y, Normal;
            public double MinX, MaxX, MinY, MaxY, Thickness;
            public List<List<Point2>> Loops;
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
                    floors = ui.Selection.PickObjects(ObjectType.Element, new FloorFilter(), "Chọn sàn cần bố trí thép chân chó")
                        .Select(reference => doc.GetElement(reference)).OfType<Floor>().Distinct().ToList();
                if (floors.Count == 0) return Result.Cancelled;

                List<RebarBarType> types = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType))
                    .Cast<RebarBarType>().OrderBy(type => type.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (types.Count == 0) throw new InvalidOperationException("Project chưa có RebarBarType.");
                Options options = ChooseOptions(data.Application.MainWindowHandle, types);
                if (options == null) return Result.Cancelled;

                int completed = 0, created = 0;
                var skipped = new List<string>();
                using (Transaction transaction = new Transaction(doc, "KStruct - Thép chân chó sàn"))
                {
                    transaction.Start();
                    foreach (Floor floor in floors)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                Frame frame = ReadFrame(doc, floor);
                                int count = PlaceOnFloor(doc, floor, frame, options);
                                if (count == 0) throw new InvalidOperationException("Không có vị trí chân chó nào nằm trọn trong biên sàn và cover.");
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận thép chân chó.");
                                completed++;
                                created += count;
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
                        transaction.RollBack();
                        TaskDialog.Show("KStruct - Thép chân chó", "Không tạo được thanh nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các thanh thép mới.");
                }

                string report = "Đã tạo " + created + " thanh chân chó trên " + completed + "/" + floors.Count + " sàn.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " sàn:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Thép chân chó", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Thép chân chó", ex.Message);
                return Result.Failed;
            }
        }

        private static Frame ReadFrame(Document doc, Floor floor)
        {
            PlanarFace face = HostObjectUtils.GetTopFaces(floor)
                .Select(reference => floor.GetGeometryObjectFromReference(reference) as PlanarFace)
                .Where(candidate => candidate != null && candidate.FaceNormal.Normalize().DotProduct(XYZ.BasisZ) > 0.999)
                .OrderByDescending(candidate => candidate.Area).FirstOrDefault();
            if (face == null) throw new InvalidOperationException("Lệnh hỗ trợ sàn phẳng nằm ngang.");

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
            if (worldLoops.Count == 0) throw new InvalidOperationException("Không đọc được biên sàn.");

            XYZ normal = face.FaceNormal.Normalize();
            XYZ direction = worldLoops.SelectMany(loop => Enumerable.Range(0, loop.Count)
                    .Select(index => loop[(index + 1) % loop.Count] - loop[index]))
                .Where(vector => vector.GetLength() > doc.Application.ShortCurveTolerance)
                .OrderByDescending(vector => vector.GetLength()).FirstOrDefault();
            if (direction == null) throw new InvalidOperationException("Không xác định được phương sàn.");
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
            FloorType floorType = doc.GetElement(floor.GetTypeId()) as FloorType;
            CompoundStructure compound = floorType == null ? null : floorType.GetCompoundStructure();
            double thickness = compound == null ? 0 : compound.GetWidth();
            if (thickness <= doc.Application.ShortCurveTolerance) throw new InvalidOperationException("Không xác định được chiều dày FloorType.");

            return new Frame
            {
                Origin = origin + x * midX + y * midY,
                X = x, Y = y, Normal = normal,
                MinX = minX - midX, MaxX = maxX - midX,
                MinY = minY - midY, MaxY = maxY - midY,
                Thickness = thickness,
                Loops = projected.Select(loop => loop.Select(point => new Point2(point.X - midX, point.Y - midY)).ToList()).ToList()
            };
        }

        private static int PlaceOnFloor(Document doc, Floor floor, Frame frame, Options options)
        {
            double dia = options.Type.BarNominalDiameter;
            double coverTop = UnitUtils.ConvertToInternalUnits(options.TopCoverMm, UnitTypeId.Millimeters);
            double coverBottom = UnitUtils.ConvertToInternalUnits(options.BottomCoverMm, UnitTypeId.Millimeters);
            double topMatDia = UnitUtils.ConvertToInternalUnits(options.TopMatDiameterMm, UnitTypeId.Millimeters);
            double topSpan = UnitUtils.ConvertToInternalUnits(options.TopSpanMm, UnitTypeId.Millimeters);
            double foot = UnitUtils.ConvertToInternalUnits(options.FootLengthMm, UnitTypeId.Millimeters);
            double stepX = UnitUtils.ConvertToInternalUnits(options.SpacingXmm, UnitTypeId.Millimeters);
            double stepY = UnitUtils.ConvertToInternalUnits(options.SpacingYmm, UnitTypeId.Millimeters);
            // Keep the chair's top surface tangent to the underside of the upper mat.
            double topZ = -coverTop - topMatDia - dia / 2.0;
            double bottomZ = -frame.Thickness + coverBottom + dia + dia / 2.0;
            if (topZ - bottomZ <= doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Chiều dày sàn không đủ cho cover và thép đã chọn.");

            double halfSpan = UnitUtils.ConvertToInternalUnits(options.TopSpanMm, UnitTypeId.Millimeters) / 2.0;
            double radius = dia / 2.0;
            double edgeClearance = Math.Max(coverTop, coverBottom) + radius;
            double minX = frame.MinX + edgeClearance + halfSpan + (options.AlongX ? foot : 0);
            double maxX = frame.MaxX - edgeClearance - halfSpan - (options.AlongX ? foot : 0);
            double minY = frame.MinY + edgeClearance + (options.AlongX ? foot : halfSpan + foot);
            double maxY = frame.MaxY - edgeClearance - (options.AlongX ? foot : halfSpan + foot);
            if (maxX < minX || maxY < minY) throw new InvalidOperationException("Kích thước chân chó/cover lớn hơn vùng sàn hữu dụng.");

            int countX = Math.Max(1, (int)Math.Floor((maxX - minX) / stepX) + 1);
            int countY = Math.Max(1, (int)Math.Floor((maxY - minY) / stepY) + 1);
            if ((long)countX * countY > 2000) throw new InvalidOperationException("Bố trí vượt quá 2000 thanh trên một sàn.");
            double shiftX = countX == 1 ? (minX + maxX) / 2.0 : (maxX - minX) / 2.0;
            double shiftY = countY == 1 ? (minY + maxY) / 2.0 : (maxY - minY) / 2.0;
            int created = 0;
            for (int ix = 0; ix < countX; ix++)
            for (int iy = 0; iy < countY; iy++)
            {
                double cx = countX == 1 ? shiftX : minX + ix * (maxX - minX) / (countX - 1.0);
                double cy = countY == 1 ? shiftY : minY + iy * (maxY - minY) / (countY - 1.0);
                XYZ planeAxis = options.AlongX ? frame.X : frame.Y;
                XYZ center = frame.Origin + frame.X * cx + frame.Y * cy;
                XYZ leftFoot = center - planeAxis * (halfSpan + foot) + frame.Normal * bottomZ;
                XYZ leftKnee = center - planeAxis * halfSpan + frame.Normal * bottomZ;
                XYZ leftTop = center - planeAxis * halfSpan + frame.Normal * topZ;
                XYZ rightTop = center + planeAxis * halfSpan + frame.Normal * topZ;
                XYZ rightKnee = center + planeAxis * halfSpan + frame.Normal * bottomZ;
                XYZ rightFoot = center + planeAxis * (halfSpan + foot) + frame.Normal * bottomZ;
                XYZ[] points = { leftFoot, leftKnee, leftTop, rightTop, rightKnee, rightFoot };
                double localAcross = options.AlongX ? cy : cx;
                double localAlong = options.AlongX ? cx : cy;
                if (!FootprintIsInside(frame.Loops, options.AlongX, localAlong, localAcross, halfSpan + foot, radius + Math.Max(coverTop, coverBottom)))
                    continue;

                var curves = new List<Curve>();
                for (int i = 0; i < points.Length - 1; i++)
                {
                    if (points[i].DistanceTo(points[i + 1]) <= doc.Application.ShortCurveTolerance) { curves.Clear(); break; }
                    curves.Add(Line.CreateBound(points[i], points[i + 1]));
                }
                if (curves.Count != points.Length - 1) continue;
                XYZ planeNormal = options.AlongX ? frame.X.CrossProduct(frame.Normal).Normalize() : frame.Y.CrossProduct(frame.Normal).Normalize();
                Rebar rebar = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.Standard, options.Type, null, null, floor,
                    planeNormal, curves, RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                if (rebar == null) continue;
                Parameter partition = rebar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
                if (partition != null && !partition.IsReadOnly) partition.Set("KSTR_CHAIR_" + floor.Id.Value);
                created++;
            }
            return created;
        }

        private static bool FootprintIsInside(IList<List<Point2>> loops, bool alongX, double along, double across, double halfLength, double clearance)
        {
            int samples = Math.Max(12, Math.Min(400, (int)Math.Ceiling(2.0 * halfLength / UnitUtils.ConvertToInternalUnits(25, UnitTypeId.Millimeters))));
            for (int i = 0; i <= samples; i++)
            {
                double d = -halfLength + (2.0 * halfLength * i / samples);
                double x = alongX ? along + d : across;
                double y = alongX ? across : along + d;
                if (!Inside(loops, x, y) || DistanceToBoundary(loops, x, y) < clearance) return false;
            }
            return true;
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

        private static Options ChooseOptions(IntPtr owner, IList<RebarBarType> types)
        {
            var choices = types.Select(type => new BarChoice(type)).ToList();
            var window = new Window
            {
                Title = "Thép chân chó sàn - KStruct", Width = 390, Height = 505,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock { Text = "Loại thép chân chó:", Margin = new Thickness(0, 0, 0, 4) });
            var typeBox = new System.Windows.Controls.ComboBox { ItemsSource = choices, SelectedIndex = 0, MinWidth = 340 };
            root.Children.Add(typeBox);
            var orientation = new System.Windows.Controls.ComboBox { ItemsSource = new[] { "Thanh nằm theo phương X", "Thanh nằm theo phương Y" }, SelectedIndex = 0, Margin = new Thickness(0, 8, 0, 4) };
            root.Children.Add(orientation);
            System.Windows.Controls.TextBox topCover = AddInput(root, "Cover mặt trên (mm):", "25");
            System.Windows.Controls.TextBox bottomCover = AddInput(root, "Cover mặt dưới (mm):", "25");
            System.Windows.Controls.TextBox topDiameter = AddInput(root, "Đường kính lưới thép trên (mm):", "12");
            System.Windows.Controls.TextBox topSpan = AddInput(root, "Chiều dài thanh đỡ phía trên (mm):", "300");
            System.Windows.Controls.TextBox footLength = AddInput(root, "Chiều dài chân mỗi bên (mm):", "100");
            System.Windows.Controls.TextBox spacingX = AddInput(root, "Khoảng cách bố trí X (mm):", "900");
            System.Windows.Controls.TextBox spacingY = AddInput(root, "Khoảng cách bố trí Y (mm):", "900");
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "Tạo thép", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) =>
            {
                double a, b, c, d, e, f, g;
                if (typeBox.SelectedItem == null || orientation.SelectedIndex < 0 ||
                    !double.TryParse(topCover.Text, out a) || !double.TryParse(bottomCover.Text, out b) ||
                    !double.TryParse(topDiameter.Text, out c) || !double.TryParse(topSpan.Text, out d) ||
                    !double.TryParse(footLength.Text, out e) || !double.TryParse(spacingX.Text, out f) || !double.TryParse(spacingY.Text, out g) ||
                    a < 0 || b < 0 || c <= 0 || d <= 0 || e < 0 || f <= 0 || g <= 0)
                {
                    TaskDialog.Show("KStruct - Thép chân chó", "Thông số phải là số hợp lệ; đường kính, chiều dài thanh đỡ và khoảng cách phải lớn hơn 0.");
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
                Type = ((BarChoice)typeBox.SelectedItem).Type,
                AlongX = orientation.SelectedIndex == 0,
                TopCoverMm = double.Parse(topCover.Text), BottomCoverMm = double.Parse(bottomCover.Text),
                TopMatDiameterMm = double.Parse(topDiameter.Text), TopSpanMm = double.Parse(topSpan.Text),
                FootLengthMm = double.Parse(footLength.Text), SpacingXmm = double.Parse(spacingX.Text), SpacingYmm = double.Parse(spacingY.Text)
            };
        }

        private static System.Windows.Controls.TextBox AddInput(System.Windows.Controls.Panel root, string label, string value)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 5, 0, 2) });
            var box = new System.Windows.Controls.TextBox { Text = value, MinWidth = 340, MinHeight = 22 };
            root.Children.Add(box);
            return box;
        }
    }
}
