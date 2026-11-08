using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using WpfPanel = System.Windows.Controls.Panel;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CreateBubbleSlabGeometryCommand : IExternalCommand
    {
        private const string ApplicationId = "KStruct.BubbleSlab";
        private sealed class FloorFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Floor;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
        private sealed class Settings
        {
            public double DiameterMm, PitchXmm, PitchYmm, TopCoverMm, BottomCoverMm, EdgeClearanceMm, SupportClearanceMm;
        }
        private sealed class Bounds2
        {
            public double MinX, MinY, MaxX, MaxY, MinZ, MaxZ;
        }
        private sealed class FloorPlan
        {
            public Floor Floor;
            public double TopZ, BottomZ;
            public double MinX, MinY, MaxX, MaxY;
            public List<List<XYZ>> Loops;
            public List<Bounds2> Supports;
            public List<GeometryObject> Spheres;
            public int ExcludedByEdge, ExcludedBySupport;
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
                    floors = ui.Selection.PickObjects(ObjectType.Element, new FloorFilter(), "Chọn Floor hiện có để tạo hình cầu rỗng Bubble")
                        .Select(reference => doc.GetElement(reference)).OfType<Floor>().Distinct().ToList();
                if (floors.Count == 0) return Result.Cancelled;
                Settings settings = ChooseOptions(data.Application.MainWindowHandle);
                if (settings == null) return Result.Cancelled;

                var plans = new List<FloorPlan>();
                var skipped = new List<string>();
                foreach (Floor floor in floors)
                {
                    try
                    {
                        FloorPlan plan = ReadFloorPlan(doc, floor);
                        plan.Supports = ReadSupports(doc, floor, plan.BottomZ, plan.TopZ);
                        plan.Spheres = CreateBubbleSpheres(doc, plan, settings);
                        if (plan.Spheres.Count == 0) throw new InvalidOperationException("Không có vị trí cầu hợp lệ trong biên sàn.");
                        plans.Add(plan);
                    }
                    catch (Exception ex) { skipped.Add("Sàn " + floor.Id.Value + ": " + ex.Message); }
                }
                if (plans.Count == 0)
                    throw new InvalidOperationException("Không tạo được bố trí Bubble trên sàn nào.\n" + string.Join("\n", skipped.Take(8)));

                int sphereCount = plans.Sum(plan => plan.Spheres.Count);
                string preview = "Preview hình Bubble: " + sphereCount + " khối cầu trên " + plans.Count + "/" + floors.Count +
                    " sàn; Ø" + settings.DiameterMm.ToString("0.#") + " mm; bước " + settings.PitchXmm.ToString("0.#") + " × " +
                    settings.PitchYmm.ToString("0.#") + " mm; cover trên/dưới " + settings.TopCoverMm.ToString("0.#") + "/" +
                    settings.BottomCoverMm.ToString("0.#") + " mm. Khối dương được đặt bên trong sàn; lệnh không cắt void khỏi Floor.";
                if (skipped.Count > 0) preview += "\nBỏ qua " + skipped.Count + " sàn:\n" + string.Join("\n", skipped.Take(5));
                TaskDialogResult response = TaskDialog.Show("KStruct - Bubble slab", preview + "\n\nTạo hoặc cập nhật hình khối?",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No, TaskDialogResult.No);
                if (response != TaskDialogResult.Yes) return Result.Cancelled;

                int created = 0, updated = 0;
                using (Transaction transaction = new Transaction(doc, "KStruct - Tạo hình Bubble slab"))
                {
                    transaction.Start();
                    foreach (FloorPlan plan in plans)
                    {
                        DirectShape shape = FindManagedShape(doc, plan.Floor.Id);
                        if (shape == null)
                        {
                            shape = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                            shape.ApplicationId = ApplicationId;
                            shape.ApplicationDataId = plan.Floor.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            Parameter mark = shape.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                            if (mark != null && !mark.IsReadOnly) mark.Set("KSTR_BUBBLE_" + plan.Floor.Id.Value);
                            created++;
                        }
                        else updated++;
                        shape.SetShape(plan.Spheres);
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận hình Bubble.");
                }
                string report = "Đã tạo/cập nhật " + sphereCount + " khối cầu trên " + plans.Count + " sàn (mới " + created + ", cập nhật " + updated + ").";
                report += "\nLoại khỏi biên: " + plans.Sum(plan => plan.ExcludedByEdge) + "; vùng cột/dầm/tường: " + plans.Sum(plan => plan.ExcludedBySupport) + ".";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " sàn:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Bubble slab", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Bubble slab", ex.Message);
                return Result.Failed;
            }
        }

        private static FloorPlan ReadFloorPlan(Document doc, Floor floor)
        {
            PlanarFace top = HostObjectUtils.GetTopFaces(floor).Select(reference => floor.GetGeometryObjectFromReference(reference) as PlanarFace)
                .Where(face => face != null && face.FaceNormal.Normalize().DotProduct(XYZ.BasisZ) > 0.999)
                .OrderByDescending(face => face.Area).FirstOrDefault();
            if (top == null) throw new InvalidOperationException("Chỉ hỗ trợ sàn có mặt trên phẳng và ngang.");
            List<Solid> solids = new List<Solid>();
            GeometryElement geometry = floor.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = true });
            if (geometry != null) CollectSolids(geometry, solids);
            PlanarFace bottom = solids.SelectMany(solid => solid.Faces.Cast<Face>()).OfType<PlanarFace>()
                .Where(face => face.FaceNormal.Normalize().DotProduct(XYZ.BasisZ) < -0.999 && face.Origin.Z < top.Origin.Z)
                .OrderByDescending(face => face.Area).FirstOrDefault();
            if (bottom == null) throw new InvalidOperationException("Không tìm thấy mặt dưới ngang của Floor.");
            var loops = new List<List<XYZ>>();
            foreach (CurveLoop curveLoop in top.GetEdgesAsCurveLoops())
            {
                var points = new List<XYZ>();
                foreach (Curve curve in curveLoop)
                foreach (XYZ point in curve.Tessellate())
                    if (points.Count == 0 || points[points.Count - 1].DistanceTo(point) > doc.Application.ShortCurveTolerance / 10.0)
                        points.Add(point);
                if (points.Count > 1 && points[0].DistanceTo(points[points.Count - 1]) <= doc.Application.ShortCurveTolerance / 10.0) points.RemoveAt(points.Count - 1);
                if (points.Count >= 3) loops.Add(points);
            }
            if (loops.Count == 0) throw new InvalidOperationException("Không đọc được biên sàn.");
            List<XYZ> all = loops.SelectMany(loop => loop).ToList();
            return new FloorPlan
            {
                Floor = floor, TopZ = top.Origin.Z, BottomZ = bottom.Origin.Z, Loops = loops,
                MinX = all.Min(point => point.X), MaxX = all.Max(point => point.X),
                MinY = all.Min(point => point.Y), MaxY = all.Max(point => point.Y)
            };
        }

        private static List<GeometryObject> CreateBubbleSpheres(Document doc, FloorPlan plan, Settings settings)
        {
            double diameter = UnitUtils.ConvertToInternalUnits(settings.DiameterMm, UnitTypeId.Millimeters);
            double radius = diameter / 2.0;
            double topCover = UnitUtils.ConvertToInternalUnits(settings.TopCoverMm, UnitTypeId.Millimeters);
            double bottomCover = UnitUtils.ConvertToInternalUnits(settings.BottomCoverMm, UnitTypeId.Millimeters);
            double clearance = UnitUtils.ConvertToInternalUnits(settings.EdgeClearanceMm, UnitTypeId.Millimeters) + radius;
            double supportClearance = UnitUtils.ConvertToInternalUnits(settings.SupportClearanceMm, UnitTypeId.Millimeters) + radius;
            double usableBottom = plan.BottomZ + bottomCover + radius;
            double usableTop = plan.TopZ - topCover - radius;
            if (usableTop < usableBottom) throw new InvalidOperationException("Chiều dày sàn không đủ cho đường kính Bubble và cover trên/dưới.");
            double centerZ = (usableBottom + usableTop) / 2.0;
            double pitchX = UnitUtils.ConvertToInternalUnits(settings.PitchXmm, UnitTypeId.Millimeters);
            double pitchY = UnitUtils.ConvertToInternalUnits(settings.PitchYmm, UnitTypeId.Millimeters);
            var points = new List<XYZ>();
            for (double x = plan.MinX + clearance; x <= plan.MaxX - clearance; x += pitchX)
            for (double y = plan.MinY + clearance; y <= plan.MaxY - clearance; y += pitchY)
            {
                if (!PointInside(plan.Loops, x, y) || DistanceToBoundary(plan.Loops, x, y) < clearance)
                {
                    plan.ExcludedByEdge++;
                    continue;
                }
                if (plan.Supports.Any(box => x >= box.MinX - supportClearance && x <= box.MaxX + supportClearance &&
                    y >= box.MinY - supportClearance && y <= box.MaxY + supportClearance))
                {
                    plan.ExcludedBySupport++;
                    continue;
                }
                points.Add(new XYZ(x, y, centerZ));
                if (points.Count > 10000) throw new InvalidOperationException("Lưới tạo quá 10.000 khối cầu trên một sàn; tăng bước bố trí.");
            }
            var result = new List<GeometryObject>(points.Count);
            foreach (XYZ center in points) result.Add(CreateSphere(center, radius));
            return result;
        }

        private static Solid CreateSphere(XYZ center, double radius)
        {
            XYZ plus = center + XYZ.BasisY * radius;
            XYZ minus = center - XYZ.BasisY * radius;
            XYZ arcPoint = center + XYZ.BasisX * radius;
            CurveLoop profile = CurveLoop.Create(new Curve[] { Line.CreateBound(plus, minus), Arc.Create(minus, plus, arcPoint) });
            Autodesk.Revit.DB.Frame frame = new Autodesk.Revit.DB.Frame(center, XYZ.BasisX, -XYZ.BasisZ, XYZ.BasisY);
            if (!Autodesk.Revit.DB.Frame.CanDefineRevitGeometry(frame)) throw new InvalidOperationException("Không tạo được hệ trục hình cầu.");
            return GeometryCreationUtilities.CreateRevolvedGeometry(frame, new List<CurveLoop> { profile }, 0, Math.PI * 2.0);
        }

        private static List<Bounds2> ReadSupports(Document doc, Floor floor, double bottomZ, double topZ)
        {
            double tolerance = doc.Application.ShortCurveTolerance;
            var result = new List<Bounds2>();
            foreach (Element element in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                if (element.Id == floor.Id || element.Category == null) continue;
                long category = element.Category.Id.Value;
                if (category != (long)BuiltInCategory.OST_StructuralColumns &&
                    category != (long)BuiltInCategory.OST_StructuralFraming &&
                    category != (long)BuiltInCategory.OST_Walls) continue;
                BoundingBoxXYZ box = element.get_BoundingBox(null);
                if (box == null || box.Max.Z < bottomZ - tolerance || box.Min.Z > topZ + tolerance) continue;
                Transform transform = box.Transform ?? Transform.Identity;
                XYZ[] corners = new[]
                {
                    new XYZ(box.Min.X, box.Min.Y, box.Min.Z), new XYZ(box.Min.X, box.Min.Y, box.Max.Z),
                    new XYZ(box.Min.X, box.Max.Y, box.Min.Z), new XYZ(box.Min.X, box.Max.Y, box.Max.Z),
                    new XYZ(box.Max.X, box.Min.Y, box.Min.Z), new XYZ(box.Max.X, box.Min.Y, box.Max.Z),
                    new XYZ(box.Max.X, box.Max.Y, box.Min.Z), new XYZ(box.Max.X, box.Max.Y, box.Max.Z)
                }.Select(transform.OfPoint).ToArray();
                result.Add(new Bounds2
                {
                    MinX = corners.Min(point => point.X), MaxX = corners.Max(point => point.X),
                    MinY = corners.Min(point => point.Y), MaxY = corners.Max(point => point.Y),
                    MinZ = corners.Min(point => point.Z), MaxZ = corners.Max(point => point.Z)
                });
            }
            return result;
        }

        private static DirectShape FindManagedShape(Document doc, ElementId floorId)
        {
            string dataId = floorId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return new FilteredElementCollector(doc).OfClass(typeof(DirectShape)).Cast<DirectShape>()
                .FirstOrDefault(shape => string.Equals(shape.ApplicationId, ApplicationId, StringComparison.Ordinal) &&
                    string.Equals(shape.ApplicationDataId, dataId, StringComparison.Ordinal));
        }

        private static bool PointInside(IList<List<XYZ>> loops, double x, double y)
        {
            bool inside = false;
            foreach (List<XYZ> loop in loops)
            {
                bool loopInside = false;
                for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                {
                    XYZ a = loop[i], b = loop[j];
                    if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X) loopInside = !loopInside;
                }
                if (loopInside) inside = !inside;
            }
            return inside;
        }

        private static double DistanceToBoundary(IList<List<XYZ>> loops, double x, double y)
        {
            double best = double.MaxValue;
            foreach (List<XYZ> loop in loops)
            for (int i = 0; i < loop.Count; i++)
            {
                XYZ a = loop[i], b = loop[(i + 1) % loop.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double length2 = dx * dx + dy * dy;
                double t = length2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((x - a.X) * dx + (y - a.Y) * dy) / length2));
                double px = a.X + t * dx, py = a.Y + t * dy;
                best = Math.Min(best, Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py)));
            }
            return best;
        }

        private static void CollectSolids(GeometryElement geometry, IList<Solid> solids)
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
                if (instance != null) CollectSolids(instance.GetInstanceGeometry(), solids);
            }
        }

        private static Settings ChooseOptions(IntPtr owner)
        {
            var window = new Window { Title = "Tạo hình Bubble slab - KStruct", Width = 440, Height = 390,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false };
            var root = new StackPanel { Margin = new Thickness(12) };
            WpfTextBox diameter = AddInput(root, "Đường kính khối cầu (mm):", "180");
            WpfTextBox pitchX = AddInput(root, "Bước lưới X (mm):", "250");
            WpfTextBox pitchY = AddInput(root, "Bước lưới Y (mm):", "250");
            WpfTextBox topCover = AddInput(root, "Bê tông phía trên (mm):", "60");
            WpfTextBox bottomCover = AddInput(root, "Bê tông phía dưới (mm):", "60");
            WpfTextBox edge = AddInput(root, "Lùi biên/lỗ sàn (mm):", "100");
            WpfTextBox support = AddInput(root, "Khoảng tránh cột/dầm/tường (mm):", "200");
            root.Children.Add(new TextBlock { Text = "Hình cầu dương được quản lý riêng; Floor gốc không bị cắt void.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 4) });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
            var ok = new Button { Content = "Preview", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) =>
            {
                double d, x, y, tc, bc, e, s;
                if (!double.TryParse(diameter.Text, out d) || d <= 0 || !double.TryParse(pitchX.Text, out x) || x < d ||
                    !double.TryParse(pitchY.Text, out y) || y < d || !double.TryParse(topCover.Text, out tc) || tc < 0 ||
                    !double.TryParse(bottomCover.Text, out bc) || bc < 0 || !double.TryParse(edge.Text, out e) || e < 0 ||
                    !double.TryParse(support.Text, out s) || s < 0)
                {
                    TaskDialog.Show("KStruct - Bubble slab", "Đường kính phải dương; bước X/Y phải ít nhất bằng đường kính; cover/khoảng tránh không được âm.");
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
            return new Settings
            {
                DiameterMm = double.Parse(diameter.Text), PitchXmm = double.Parse(pitchX.Text), PitchYmm = double.Parse(pitchY.Text),
                TopCoverMm = double.Parse(topCover.Text), BottomCoverMm = double.Parse(bottomCover.Text),
                EdgeClearanceMm = double.Parse(edge.Text), SupportClearanceMm = double.Parse(support.Text)
            };
        }

        private static WpfTextBox AddInput(WpfPanel root, string label, string value)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 2, 0, 1) });
            var box = new WpfTextBox { MinWidth = 400, MinHeight = 20, Text = value };
            root.Children.Add(box);
            return box;
        }
    }
}
