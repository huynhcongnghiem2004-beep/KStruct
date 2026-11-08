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
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class AlignBeamsToSlabCommand : IExternalCommand
    {
        private enum AlignmentMode { BeamTopToSlabBottom, BeamBottomToSlabTop }

        private sealed class MixedFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Floor || IsBeam(element);
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class FaceElevation
        {
            public PlanarFace Face;
            public double Z;
            public double Distance;
        }

        private sealed class Alignment
        {
            public FamilyInstance Beam;
            public double DeltaZ;
            public ElementId FloorId;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Floor> floors = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Floor>().Distinct().ToList();
                List<FamilyInstance> beams = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<FamilyInstance>().Where(IsBeam).Distinct().ToList();
                if (floors.Count == 0 || beams.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new MixedFilter(),
                        "Chọn Floor và dầm Structural Framing cần căn, nhấn Finish khi xong");
                    floors = floors.Concat(picked.Select(reference => doc.GetElement(reference)).OfType<Floor>()).Distinct().ToList();
                    beams = beams.Concat(picked.Select(reference => doc.GetElement(reference)).OfType<FamilyInstance>().Where(IsBeam)).Distinct().ToList();
                }
                if (floors.Count == 0 || beams.Count == 0)
                    throw new InvalidOperationException("Cần chọn ít nhất một Floor và một dầm Structural Framing.");

                Tuple<AlignmentMode, double> settings = ChooseOptions(data.Application.MainWindowHandle);
                if (settings == null) return Result.Cancelled;
                List<Alignment> alignments = new List<Alignment>();
                var skipped = new List<string>();
                foreach (FamilyInstance beam in beams)
                {
                    try { alignments.Add(FindAlignment(doc, beam, floors, settings.Item1, settings.Item2)); }
                    catch (Exception ex) { skipped.Add("Dầm " + beam.Id.Value + ": " + ex.Message); }
                }
                if (alignments.Count == 0)
                    throw new InvalidOperationException("Không tìm được mặt sàn hợp lệ cho dầm nào.\n" + string.Join("\n", skipped.Take(8)));

                double largestMove = alignments.Max(item => Math.Abs(item.DeltaZ));
                string target = settings.Item1 == AlignmentMode.BeamTopToSlabBottom
                    ? "đỉnh dầm trùng mặt dưới sàn" : "đáy dầm trùng mặt trên sàn";
                string preview = "Sẽ căn " + alignments.Count + "/" + beams.Count + " dầm để " + target +
                    ", khe hở " + settings.Item2.ToString("0.##") + " mm; dịch chuyển lớn nhất " +
                    UnitUtils.ConvertFromInternalUnits(largestMove, UnitTypeId.Millimeters).ToString("0.#") + " mm.";
                if (skipped.Count > 0) preview += "\nBỏ qua " + skipped.Count + " dầm không khớp.\n" + string.Join("\n", skipped.Take(5));
                TaskDialogResult confirm = TaskDialog.Show("KStruct - Căn dầm theo sàn", preview + "\n\nÁp dụng dịch chuyển đứng?",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No, TaskDialogResult.No);
                if (confirm != TaskDialogResult.Yes) return Result.Cancelled;

                using (Transaction transaction = new Transaction(doc, "KStruct - Căn dầm theo sàn"))
                {
                    transaction.Start();
                    foreach (Alignment alignment in alignments)
                        ElementTransformUtils.MoveElement(doc, alignment.Beam.Id, XYZ.BasisZ * alignment.DeltaZ);
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận dịch chuyển dầm.");
                }
                TaskDialog.Show("KStruct - Căn dầm theo sàn", "Đã căn " + alignments.Count + "/" + beams.Count + " dầm; khe hở " + settings.Item2.ToString("0.##") + " mm.");
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Căn dầm theo sàn", ex.Message);
                return Result.Failed;
            }
        }

        private static Alignment FindAlignment(Document doc, FamilyInstance beam, IList<Floor> floors,
            AlignmentMode mode, double gapMm)
        {
            Line axis = (beam.Location as LocationCurve)?.Curve as Line;
            if (axis == null || Math.Abs(axis.Direction.Z) > 1e-3)
                throw new InvalidOperationException("Chỉ hỗ trợ dầm thẳng nằm ngang.");
            BoundingBoxXYZ beamBox = beam.get_BoundingBox(null);
            if (beamBox == null) throw new InvalidOperationException("Không đọc được bounding box dầm.");
            XYZ center = axis.Evaluate(0.5, true);
            double edgeZ = mode == AlignmentMode.BeamTopToSlabBottom ? beamBox.Max.Z : beamBox.Min.Z;
            double gap = UnitUtils.ConvertToInternalUnits(gapMm, UnitTypeId.Millimeters);

            var possible = new List<Tuple<Floor, double, double>>();
            foreach (Floor floor in floors)
            {
                BoundingBoxXYZ floorBox = floor.get_BoundingBox(null);
                if (floorBox == null || center.X < floorBox.Min.X - doc.Application.ShortCurveTolerance ||
                    center.X > floorBox.Max.X + doc.Application.ShortCurveTolerance ||
                    center.Y < floorBox.Min.Y - doc.Application.ShortCurveTolerance ||
                    center.Y > floorBox.Max.Y + doc.Application.ShortCurveTolerance) continue;
                FaceElevation face = FindFaceElevation(doc, floor, center.X, center.Y,
                    mode == AlignmentMode.BeamTopToSlabBottom);
                if (face == null) continue;
                double delta = mode == AlignmentMode.BeamTopToSlabBottom
                    ? face.Z - edgeZ - gap
                    : face.Z - edgeZ + gap;
                possible.Add(Tuple.Create(floor, delta, face.Distance));
            }
            Tuple<Floor, double, double> best = possible.OrderBy(item => Math.Abs(item.Item2)).FirstOrDefault();
            if (best == null) throw new InvalidOperationException("Không có mặt sàn hợp lệ nằm trên vùng giữa dầm.");
            return new Alignment { Beam = beam, FloorId = best.Item1.Id, DeltaZ = best.Item2 };
        }

        private static FaceElevation FindFaceElevation(Document doc, Floor floor, double x, double y, bool underside)
        {
            var solids = new List<Solid>();
            GeometryElement geometry = floor.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = true });
            if (geometry != null) CollectSolids(geometry, solids);
            XYZ probe = new XYZ(x, y, floor.get_BoundingBox(null)?.Max.Z ?? 0);
            return solids.SelectMany(solid => solid.Faces.Cast<Face>()).OfType<PlanarFace>()
                .Where(face => underside ? face.FaceNormal.Z < -0.9 : face.FaceNormal.Z > 0.9)
                .Select(face =>
                {
                    IntersectionResult projection;
                    try { projection = face.Project(probe); } catch { return null; }
                    if (projection == null || !face.IsInside(projection.UVPoint)) return null;
                    XYZ normal = face.FaceNormal;
                    if (Math.Abs(normal.Z) < 1e-6) return null;
                    double z = face.Origin.Z - (normal.X * (x - face.Origin.X) + normal.Y * (y - face.Origin.Y)) / normal.Z;
                    return new FaceElevation { Face = face, Z = z, Distance = Math.Abs(probe.Z - z) };
                })
                .Where(result => result != null)
                .OrderBy(result => result.Distance).FirstOrDefault();
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

        private static bool IsBeam(Element element) => element is FamilyInstance beam && beam.Category != null &&
            beam.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;

        private static Tuple<AlignmentMode, double> ChooseOptions(IntPtr owner)
        {
            var window = new Window { Title = "Beam To Slab - KStruct", Width = 420, Height = 210,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock { Text = "Kiểu căn cao độ:", Margin = new Thickness(0, 0, 0, 3) });
            var mode = new WpfComboBox { MinWidth = 390, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 7) };
            mode.Items.Add("Đỉnh dầm → mặt dưới sàn");
            mode.Items.Add("Đáy dầm → mặt trên sàn");
            root.Children.Add(mode);
            root.Children.Add(new TextBlock { Text = "Khe hở đứng giữa dầm và sàn (mm):", Margin = new Thickness(0, 2, 0, 3) });
            var gap = new WpfTextBox { MinWidth = 390, Text = "0" };
            root.Children.Add(gap);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "Tiếp tục", Width = 88, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) =>
            {
                double value;
                if (mode.SelectedIndex < 0 || !double.TryParse(gap.Text, out value) || value < 0)
                {
                    TaskDialog.Show("KStruct - Căn dầm theo sàn", "Chọn kiểu căn và nhập khe hở không âm.");
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
            return Tuple.Create(mode.SelectedIndex == 0 ? AlignmentMode.BeamTopToSlabBottom : AlignmentMode.BeamBottomToSlabTop,
                double.Parse(gap.Text));
        }
    }
}
