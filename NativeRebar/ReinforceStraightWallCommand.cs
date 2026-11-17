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
using MediaBrushes = System.Windows.Media.Brushes;
using MediaDrawingContext = System.Windows.Media.DrawingContext;
using MediaFormattedText = System.Windows.Media.FormattedText;
using MediaPen = System.Windows.Media.Pen;
using MediaStreamGeometry = System.Windows.Media.StreamGeometry;
using MediaStreamGeometryContext = System.Windows.Media.StreamGeometryContext;
using MediaTypeface = System.Windows.Media.Typeface;
using MediaVisualTreeHelper = System.Windows.Media.VisualTreeHelper;
using WpfPoint = System.Windows.Point;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ReinforceStraightWallCommand : IExternalCommand
    {
        private sealed class WallFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Wall;
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

        private sealed class HookChoice
        {
            public RebarHookType Type { get; private set; }
            public string Display { get; private set; }
            public HookChoice(RebarHookType type) { Type = type; Display = type == null ? "(không có)" : type.Name; }
            public override string ToString() => Display;
        }

        private sealed class ShapeChoice
        {
            public RebarShape Shape { get; private set; }
            public string Display { get; private set; }
            public ShapeChoice(RebarShape shape) { Shape = shape; Display = shape.Name; }
            public override string ToString() => Display;
        }

        private sealed class Options
        {
            public RebarBarType HorizontalType, VerticalType, LeftEdgeType, RightEdgeType, TieType, ShapeTieType;
            public RebarHookType TieHook;
            public bool UseLeftEdge, UseRightEdge, DrawTransverseTies;
            public bool UseTieO, UseTieC, UseTieU;
            public bool UseSavedWallProfiles;
            public RebarShape TieOShape, TieCShape, TieUShape;
            public double LeftEdgeLengthMm, RightEdgeLengthMm;
            public bool LapAdjacentWalls;
            public double CoverMm, HorizontalSpacingMm, VerticalSpacingMm, VerticalLapMm, TieSpacingMm;
            public double ShapeTieZoneLengthMm, ShapeTieSpacingMm;
        }

        private sealed class WallExtension
        {
            public double Bottom, Top;
        }

        private sealed class OpeningRegion
        {
            public double MinAlong, MaxAlong, MinZ, MaxZ;
        }

        private sealed class PreviewPath
        {
            public List<WpfPoint> Points = new List<WpfPoint>();
        }

        private sealed class WallPreview
        {
            public string Title;
            public double Length, MinZ, MaxZ;
            public List<OpeningRegion> Openings = new List<OpeningRegion>();
            public List<PreviewPath> Bars = new List<PreviewPath>();
            public int BarPositions;
        }

        private sealed class WallPreviewDrawing : FrameworkElement
        {
            private readonly WallPreview _preview;
            public WallPreviewDrawing(WallPreview preview) { _preview = preview; Width = 760; Height = 355; }

            protected override void OnRender(MediaDrawingContext dc)
            {
                base.OnRender(dc);
                const double margin = 28;
                double minS = Math.Min(0, _preview.Bars.SelectMany(path => path.Points).Select(point => point.X).DefaultIfEmpty(0).Min());
                double maxS = Math.Max(_preview.Length, _preview.Bars.SelectMany(path => path.Points).Select(point => point.X).DefaultIfEmpty(_preview.Length).Max());
                double minZ = Math.Min(_preview.MinZ, _preview.Bars.SelectMany(path => path.Points).Select(point => point.Y).DefaultIfEmpty(_preview.MinZ).Min());
                double maxZ = Math.Max(_preview.MaxZ, _preview.Bars.SelectMany(path => path.Points).Select(point => point.Y).DefaultIfEmpty(_preview.MaxZ).Max());
                double spanS = Math.Max(1e-6, maxS - minS), spanZ = Math.Max(1e-6, maxZ - minZ);
                double scale = Math.Min((ActualWidth - margin * 2) / spanS, (ActualHeight - margin * 2) / spanZ);
                double drawWidth = spanS * scale, drawHeight = spanZ * scale;
                double left = (ActualWidth - drawWidth) / 2.0, bottom = (ActualHeight + drawHeight) / 2.0;
                WpfPoint Map(double s, double z) => new WpfPoint(left + (s - minS) * scale, bottom - (z - minZ) * scale);

                dc.DrawRectangle(MediaBrushes.WhiteSmoke, new MediaPen(MediaBrushes.DimGray, 1), new Rect(left, bottom - (maxZ - minZ) * scale, drawWidth, drawHeight));
                foreach (OpeningRegion opening in _preview.Openings)
                {
                    WpfPoint a = Map(opening.MinAlong, opening.MaxZ), b = Map(opening.MaxAlong, opening.MinZ);
                    dc.DrawRectangle(MediaBrushes.White, new MediaPen(MediaBrushes.Gray, 1), new Rect(a, b));
                }
                MediaPen barPen = new MediaPen(MediaBrushes.DarkCyan, 1.35);
                foreach (PreviewPath bar in _preview.Bars)
                {
                    if (bar.Points.Count == 0) continue;
                    if (bar.Points.Count == 1)
                    {
                        WpfPoint p = Map(bar.Points[0].X, bar.Points[0].Y);
                        dc.DrawEllipse(MediaBrushes.DarkCyan, null, p, 2.2, 2.2);
                        continue;
                    }
                    var geometry = new MediaStreamGeometry();
                    using (MediaStreamGeometryContext context = geometry.Open())
                    {
                        context.BeginFigure(Map(bar.Points[0].X, bar.Points[0].Y), false, false);
                        context.PolyLineTo(bar.Points.Skip(1).Select(point => Map(point.X, point.Y)).ToList(), true, false);
                    }
                    geometry.Freeze();
                    dc.DrawGeometry(null, barPen, geometry);
                }
                dc.DrawText(new MediaFormattedText(
                    _preview.Title + "   |   " + _preview.BarPositions + " vị trí thanh  |   mặt đứng khai triển, đã chiếu 2 mặt lên cùng hình",
                    System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new MediaTypeface("Segoe UI"), 13, MediaBrushes.Black, MediaVisualTreeHelper.GetDpi(this).PixelsPerDip), new WpfPoint(8, 5));
            }
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Wall> walls = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Wall>().Distinct().ToList();
                if (walls.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new WallFilter(), "Chọn tường đứng cần bố trí thép; thanh sẽ tránh các phần tử chèn có bounding box");
                    walls = picked.Select(r => doc.GetElement(r)).OfType<Wall>().Distinct().ToList();
                }
                if (walls.Count == 0) return Result.Cancelled;
                List<RebarBarType> types = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType))
                    .Cast<RebarBarType>().OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (types.Count == 0) throw new InvalidOperationException("Project chưa có RebarBarType.");
                List<RebarHookType> tieHooks = new FilteredElementCollector(doc).OfClass(typeof(RebarHookType))
                    .Cast<RebarHookType>().Where(h => h.Style == RebarStyle.StirrupTie && Math.Abs(h.HookAngle - Math.PI) < 1e-6)
                    .OrderBy(h => h.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                List<RebarShape> tieShapes = new FilteredElementCollector(doc).OfClass(typeof(RebarShape))
                    .Cast<RebarShape>().OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                Dictionary<ElementId, StraightWallRebarSettingsStore.Values> wallProfiles = walls
                    .ToDictionary(w => w.Id, w => StraightWallRebarSettingsStore.Load(w));
                StraightWallRebarSettingsStore.Values defaults = wallProfiles[walls[0].Id];
                bool hasSavedProfiles = wallProfiles.Values.Any(v => v != null);
                Options options = ChooseOptions(data.Application.MainWindowHandle, types, tieHooks, tieShapes,
                    defaults, walls.Count, hasSavedProfiles);
                if (options == null) return Result.Cancelled;
                int linkedJoints;
                Dictionary<ElementId, WallExtension> extensions = BuildWallLapExtensions(walls, options, out linkedJoints);

                List<WallPreview> previews = BuildPreview(doc, walls, options, extensions, wallProfiles, types, tieHooks, tieShapes);
                if (previews.Count == 0)
                {
                    TaskDialog.Show("KStruct - Preview thép tường", "Không tạo được hình học preview cho tường đã chọn. Mô hình chưa bị thay đổi.");
                    return Result.Cancelled;
                }
                if (!ShowPreview(data.Application.MainWindowHandle, previews)) return Result.Cancelled;

                int completed = 0, barCount = 0, replacedBars = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Cốt thép tường thẳng"))
                {
                    tx.Start();
                    foreach (Wall wall in walls)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                WallExtension extension = extensions[wall.Id];
                                Options wallOptions = options.UseSavedWallProfiles && wallProfiles[wall.Id] != null
                                    ? ApplySavedProfile(options, wallProfiles[wall.Id], types, tieHooks, tieShapes)
                                    : CloneOptions(options);
                                // Lap pairing is computed for the selected batch, so keep this batch option common.
                                wallOptions.LapAdjacentWalls = options.LapAdjacentWalls;
                                wallOptions.VerticalLapMm = options.VerticalLapMm;
                                List<ElementId> previousManagedBars = FindManagedWallBars(doc, wall.Id);
                                int created = ReinforceWall(doc, wall, wallOptions, extension.Bottom, extension.Top);
                                if (created <= 0) throw new InvalidOperationException("Không tạo được bộ thép mới; bộ KStruct cũ được giữ nguyên.");
                                if (previousManagedBars.Count > 0) doc.Delete(previousManagedBars);
                                StraightWallRebarSettingsStore.Save(wall, new StraightWallRebarSettingsStore.Values
                                {
                                    HorizontalTypeName = wallOptions.HorizontalType.Name,
                                    VerticalTypeName = wallOptions.VerticalType.Name,
                                    CoverMm = wallOptions.CoverMm,
                                    HorizontalSpacingMm = wallOptions.HorizontalSpacingMm,
                                    VerticalSpacingMm = wallOptions.VerticalSpacingMm,
                                    UseLeftEdge = wallOptions.UseLeftEdge,
                                    LeftTypeName = wallOptions.LeftEdgeType == null ? "" : wallOptions.LeftEdgeType.Name,
                                    LeftLengthMm = wallOptions.LeftEdgeLengthMm,
                                    UseRightEdge = wallOptions.UseRightEdge,
                                    RightTypeName = wallOptions.RightEdgeType == null ? "" : wallOptions.RightEdgeType.Name,
                                    RightLengthMm = wallOptions.RightEdgeLengthMm,
                                    LapAdjacentWalls = wallOptions.LapAdjacentWalls,
                                    VerticalLapMm = wallOptions.VerticalLapMm,
                                    DrawTransverseTies = wallOptions.DrawTransverseTies,
                                    TieTypeName = wallOptions.TieType == null ? "" : wallOptions.TieType.Name,
                                    TieHookName = wallOptions.TieHook == null ? "" : wallOptions.TieHook.Name,
                                    TieSpacingMm = wallOptions.TieSpacingMm,
                                    UseTieO = wallOptions.UseTieO,
                                    UseTieC = wallOptions.UseTieC,
                                    UseTieU = wallOptions.UseTieU,
                                    TieOShapeName = wallOptions.TieOShape == null ? "" : wallOptions.TieOShape.Name,
                                    TieCShapeName = wallOptions.TieCShape == null ? "" : wallOptions.TieCShape.Name,
                                    TieUShapeName = wallOptions.TieUShape == null ? "" : wallOptions.TieUShape.Name,
                                    ShapeTieTypeName = wallOptions.ShapeTieType == null ? "" : wallOptions.ShapeTieType.Name,
                                    ShapeTieZoneLengthMm = wallOptions.ShapeTieZoneLengthMm,
                                    ShapeTieSpacingMm = wallOptions.ShapeTieSpacingMm
                                });
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận cốt thép tường.");
                                completed++;
                                barCount += created;
                                replacedBars += previousManagedBars.Count;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Tường " + wall.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (completed == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Cốt thép tường", "Không tạo được thép cho tường nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các thanh mới.");
                }
                string report = "Đã bố trí " + barCount + " thanh ngang/dọc cho " + completed + "/" + walls.Count + " tường (hai mặt). Vùng cửa/lỗ chèn được xấp xỉ bằng bounding box và có thép viền.";
                if (replacedBars > 0) report += "\nĐã thay " + replacedBars + " thanh KStruct cũ của các tường này sau khi tạo bộ mới thành công.";
                if (options.LapAdjacentWalls)
                    report += linkedJoints > 0 ? "\nĐã nối chồng thép dọc tại " + linkedJoints + " mối nối tầng (" + options.VerticalLapMm.ToString("0.#") + " mm)." :
                        "\nKhông tìm thấy cặp tường kề tầng cùng trục để nối chồng.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " tường:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Cốt thép tường", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Cốt thép tường", ex.Message);
                return Result.Failed;
            }
        }

        private static List<WallPreview> BuildPreview(Document doc, IList<Wall> walls, Options options,
            IDictionary<ElementId, WallExtension> extensions,
            IDictionary<ElementId, StraightWallRebarSettingsStore.Values> wallProfiles,
            IList<RebarBarType> types, IList<RebarHookType> tieHooks, IList<RebarShape> tieShapes)
        {
            var before = new HashSet<ElementId>(new FilteredElementCollector(doc).OfClass(typeof(Rebar))
                .Cast<Rebar>().Select(rebar => rebar.Id));
            var previews = new List<WallPreview>();
            var skipped = new List<string>();
            using (Transaction previewTransaction = new Transaction(doc, "KStruct - Tạo preview thép tường"))
            {
                previewTransaction.Start();
                foreach (Wall wall in walls)
                {
                    using (SubTransaction sub = new SubTransaction(doc))
                    {
                        sub.Start();
                        try
                        {
                            Options wallOptions = options.UseSavedWallProfiles && wallProfiles[wall.Id] != null
                                ? ApplySavedProfile(options, wallProfiles[wall.Id], types, tieHooks, tieShapes)
                                : CloneOptions(options);
                            wallOptions.LapAdjacentWalls = options.LapAdjacentWalls;
                            wallOptions.VerticalLapMm = options.VerticalLapMm;
                            ReinforceWall(doc, wall, wallOptions, extensions[wall.Id].Bottom, extensions[wall.Id].Top);
                            if (sub.Commit() != TransactionStatus.Committed)
                                throw new InvalidOperationException("Revit không tạo được hình học preview.");
                        }
                        catch (Exception ex)
                        {
                            if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                            skipped.Add("Tường " + wall.Id.Value + ": " + ex.Message);
                        }
                    }
                }

                doc.Regenerate();
                List<Rebar> generated = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
                    .Where(rebar => !before.Contains(rebar.Id)).ToList();
                foreach (Wall wall in walls)
                {
                    List<Rebar> wallBars = generated.Where(rebar => rebar.GetHostId() == wall.Id).ToList();
                    if (wallBars.Count == 0) continue;
                    Line axisLine = (wall.Location as LocationCurve)?.Curve as Line;
                    BoundingBoxXYZ box = wall.get_BoundingBox(null);
                    if (axisLine == null || box == null) continue;
                    XYZ axis = axisLine.Direction.Normalize();
                    XYZ origin = axisLine.GetEndPoint(0);
                    var preview = new WallPreview
                    {
                        Title = wall.Name + " (Id " + wall.Id.Value + ")",
                        Length = axisLine.Length,
                        MinZ = box.Min.Z,
                        MaxZ = box.Max.Z,
                        Openings = ReadOpeningRegions(doc, wall, axisLine)
                    };
                    foreach (Rebar rebar in wallBars)
                    {
                        int positions = Math.Max(1, rebar.NumberOfBarPositions);
                        for (int position = 0; position < positions; position++)
                        {
                            if (positions > 1 && !rebar.DoesBarExistAtPosition(position)) continue;
                            IList<Curve> curves = rebar.GetCenterlineCurves(false, false, false,
                                MultiplanarOption.IncludeAllMultiplanarCurves, position);
                            if (curves == null || curves.Count == 0) continue;
                            var path = new PreviewPath();
                            foreach (Curve curve in curves)
                            {
                                IList<XYZ> points = curve is Line
                                    ? new List<XYZ> { curve.GetEndPoint(0), curve.GetEndPoint(1) }
                                    : curve.Tessellate();
                                foreach (XYZ point in points)
                                {
                                    WpfPoint projected = new WpfPoint((point - origin).DotProduct(axis), point.Z);
                                    if (path.Points.Count == 0 || (path.Points[path.Points.Count - 1] - projected).Length > 1e-7)
                                        path.Points.Add(projected);
                                }
                            }
                            if (path.Points.Count > 0)
                            {
                                preview.Bars.Add(path);
                                preview.BarPositions++;
                            }
                        }
                    }
                    if (preview.Bars.Count > 0) previews.Add(preview);
                }
                previewTransaction.RollBack();
            }
            if (skipped.Count > 0)
                TaskDialog.Show("KStruct - Preview thép tường", "Không dựng được preview cho " + skipped.Count + " tường:\n" + string.Join("\n", skipped.Take(6)));
            return previews;
        }

        private static bool ShowPreview(IntPtr owner, IList<WallPreview> previews)
        {
            var window = new Window
            {
                Title = "Preview cốt thép tường - KStruct",
                Width = 870,
                Height = 760,
                MinWidth = 700,
                MinHeight = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new DockPanel { Margin = new Thickness(10) };
            var note = new TextBlock
            {
                Text = "Preview được chiếu từ các đường tim Rebar sẽ tạo; vùng trắng là cửa/lỗ chèn. Kiểm tra bố trí rồi chọn Tạo Rebar.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            DockPanel.SetDock(note, Dock.Top);
            root.Children.Add(note);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var create = new Button { Content = "Tạo Rebar", Width = 100, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 76, IsCancel = true };
            create.Click += (sender, args) => window.DialogResult = true;
            buttons.Children.Add(create);
            buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);
            var stack = new StackPanel();
            foreach (WallPreview preview in previews)
            {
                stack.Children.Add(new TextBlock { Text = preview.Title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 8, 2, 2) });
                stack.Children.Add(new WallPreviewDrawing(preview));
            }
            root.Children.Add(new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto });
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            return window.ShowDialog() == true;
        }

        private static Dictionary<ElementId, WallExtension> BuildWallLapExtensions(
            IList<Wall> walls, Options options, out int linkedJoints)
        {
            var result = walls.ToDictionary(w => w.Id, w => new WallExtension());
            linkedJoints = 0;
            if (!options.LapAdjacentWalls || options.VerticalLapMm <= 0 || walls.Count < 2) return result;

            double maxGap = UnitUtils.ConvertToInternalUnits(500, UnitTypeId.Millimeters);
            double tolerance = UnitUtils.ConvertToInternalUnits(30, UnitTypeId.Millimeters);
            double lap = UnitUtils.ConvertToInternalUnits(options.VerticalLapMm, UnitTypeId.Millimeters);
            for (int i = 0; i < walls.Count; i++)
            {
                for (int j = i + 1; j < walls.Count; j++)
                {
                    Wall first = walls[i], second = walls[j];
                    BoundingBoxXYZ firstBox = first.get_BoundingBox(null);
                    BoundingBoxXYZ secondBox = second.get_BoundingBox(null);
                    if (firstBox == null || secondBox == null || !SameWallPlan(first, second, tolerance)) continue;

                    Wall lower;
                    Wall upper;
                    BoundingBoxXYZ lowerBox;
                    BoundingBoxXYZ upperBox;
                    if (firstBox.Max.Z <= secondBox.Min.Z + tolerance)
                    {
                        lower = first; lowerBox = firstBox; upper = second; upperBox = secondBox;
                    }
                    else if (secondBox.Max.Z <= firstBox.Min.Z + tolerance)
                    {
                        lower = second; lowerBox = secondBox; upper = first; upperBox = firstBox;
                    }
                    else continue;

                    double gap = upperBox.Min.Z - lowerBox.Max.Z;
                    if (gap < -tolerance || gap > maxGap) continue;
                    double extension = Math.Max(0, (lap + gap) * 0.5);
                    if (extension <= 0) continue;
                    result[lower.Id].Top = Math.Max(result[lower.Id].Top, extension);
                    result[upper.Id].Bottom = Math.Max(result[upper.Id].Bottom, extension);
                    linkedJoints++;
                }
            }
            return result;
        }

        private static bool SameWallPlan(Wall first, Wall second, double tolerance)
        {
            if (Math.Abs(first.Width - second.Width) > tolerance) return false;
            Line a = (first.Location as LocationCurve)?.Curve as Line;
            Line b = (second.Location as LocationCurve)?.Curve as Line;
            if (a == null || b == null) return false;
            XYZ directionA = a.Direction.Normalize();
            XYZ directionB = b.Direction.Normalize();
            if (Math.Abs(Math.Abs(directionA.DotProduct(directionB)) - 1.0) > 1e-4) return false;
            XYZ normalA = first.Orientation.Normalize();
            XYZ normalB = second.Orientation.Normalize();
            if (Math.Abs(Math.Abs(normalA.DotProduct(normalB)) - 1.0) > 1e-3) return false;
            if (Math.Abs(a.Length - b.Length) > tolerance) return false;

            XYZ a0 = a.GetEndPoint(0), a1 = a.GetEndPoint(1);
            XYZ b0 = b.GetEndPoint(0), b1 = b.GetEndPoint(1);
            double direct = Math.Max(DistanceXY(a0, b0), DistanceXY(a1, b1));
            double reverse = Math.Max(DistanceXY(a0, b1), DistanceXY(a1, b0));
            return Math.Min(direct, reverse) <= tolerance;
        }

        private static double DistanceXY(XYZ a, XYZ b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static List<ElementId> FindManagedWallBars(Document doc, ElementId wallId)
        {
            string prefix = "KSTR_WALL_" + wallId.Value + "_";
            return new FilteredElementCollector(doc).OfClass(typeof(Rebar)).WhereElementIsNotElementType()
                .Cast<Rebar>().Where(bar => bar.GetHostId() == wallId)
                .Where(bar =>
                {
                    Parameter partition = bar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
                    return partition != null && partition.HasValue && partition.AsString() != null &&
                        partition.AsString().StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                }).Select(bar => bar.Id).ToList();
        }

        private static int ReinforceWall(Document doc, Wall wall, Options options, double verticalExtensionBottom, double verticalExtensionTop)
        {
            LocationCurve location = wall.Location as LocationCurve;
            Line axisLine = location == null ? null : location.Curve as Line;
            BoundingBoxXYZ box = wall.get_BoundingBox(null);
            if (axisLine == null || box == null) throw new InvalidOperationException("Chỉ hỗ trợ tường thẳng có bounding box.");
            XYZ axis = axisLine.Direction.Normalize();
            if (Math.Abs(axis.Z) > 1e-3) throw new InvalidOperationException("Chỉ hỗ trợ tường đứng.");
            XYZ faceNormal = wall.Orientation.Normalize();
            if (Math.Abs(faceNormal.Z) > 1e-3 || Math.Abs(axis.DotProduct(faceNormal)) > 1e-3)
                throw new InvalidOperationException("Không xác định được phương hai mặt tường.");
            double height = box.Max.Z - box.Min.Z;
            double thickness = wall.Width;
            double length = axisLine.Length;
            if (height <= 0 || thickness <= 0 || length <= 0) throw new InvalidOperationException("Kích thước tường không hợp lệ.");

            double cover = UnitUtils.ConvertToInternalUnits(options.CoverMm, UnitTypeId.Millimeters);
            double horizontalSpacing = UnitUtils.ConvertToInternalUnits(options.HorizontalSpacingMm, UnitTypeId.Millimeters);
            double verticalSpacing = UnitUtils.ConvertToInternalUnits(options.VerticalSpacingMm, UnitTypeId.Millimeters);
            double horizontalDia = options.HorizontalType.BarNominalDiameter;
            double verticalDia = options.VerticalType.BarNominalDiameter;
            double leftDia = options.UseLeftEdge && options.LeftEdgeType != null ? options.LeftEdgeType.BarNominalDiameter : verticalDia;
            double rightDia = options.UseRightEdge && options.RightEdgeType != null ? options.RightEdgeType.BarNominalDiameter : verticalDia;
            double maxVerticalDia = Math.Max(verticalDia, Math.Max(leftDia, rightDia));
            double faceOffset = thickness / 2.0 - cover - horizontalDia / 2.0;
            double innerOffset = thickness / 2.0 - cover - horizontalDia - UnitUtils.ConvertToInternalUnits(2, UnitTypeId.Millimeters) - maxVerticalDia / 2.0;
            double clear = cover + Math.Max(horizontalDia, maxVerticalDia) / 2.0;
            if (faceOffset <= 0 || innerOffset <= 0 || length <= 2 * clear || height <= 2 * clear)
                throw new InvalidOperationException("Cover/thép không vừa kích thước tường.");

            XYZ boxCenter = new XYZ((box.Min.X + box.Max.X) / 2.0, (box.Min.Y + box.Max.Y) / 2.0, 0);
            XYZ lineMid = axisLine.Evaluate(0.5, true);
            XYZ normalShift = faceNormal * (boxCenter - lineMid).DotProduct(faceNormal);
            XYZ originXY = lineMid + normalShift;
            double minAlong = -length / 2.0, maxAlong = length / 2.0;
            double spanAlong = maxAlong - minAlong - 2 * clear;
            double leftZone = options.UseLeftEdge ? UnitUtils.ConvertToInternalUnits(options.LeftEdgeLengthMm, UnitTypeId.Millimeters) : 0;
            double rightZone = options.UseRightEdge ? UnitUtils.ConvertToInternalUnits(options.RightEdgeLengthMm, UnitTypeId.Millimeters) : 0;
            if (leftZone < 0 || rightZone < 0 || leftZone + rightZone >= spanAlong)
                throw new InvalidOperationException("Tổng chiều dài vùng gia cường đầu tường phải nhỏ hơn chiều dài hữu dụng.");
            double zBottom = box.Min.Z + clear;
            double zTop = box.Max.Z - clear;
            double spanHeight = zTop - zBottom;
            if (spanAlong <= 0 || spanHeight <= 0) throw new InvalidOperationException("Không xác định được vùng đặt thép.");
            int horizontalCount = CountBars(spanHeight, horizontalSpacing);
            int verticalCount = CountBars(spanAlong, verticalSpacing);
            if (horizontalCount + verticalCount > 1200) throw new InvalidOperationException("Mật độ thép yêu cầu vượt quá 1200 thanh trên một tường.");

            List<OpeningRegion> openings = ReadOpeningRegions(doc, wall, axisLine);

            int created = 0;
            foreach (int side in new[] { -1, 1 })
            {
                double horizontalOffset = side * faceOffset;
                double verticalOffset = side * innerOffset;
                for (int i = 0; i < horizontalCount; i++)
                {
                    double z = horizontalCount == 1 ? (zBottom + zTop) / 2.0 : zBottom + spanHeight * i / (horizontalCount - 1.0);
                    List<Tuple<double, double>> intervals = VisibleIntervals(minAlong + clear, maxAlong - clear,
                        openings.Where(o => z >= o.MinZ - clear && z <= o.MaxZ + clear).Select(o => Tuple.Create(o.MinAlong - clear, o.MaxAlong + clear)));
                    foreach (Tuple<double, double> interval in intervals)
                    {
                        XYZ a = new XYZ(originXY.X, originXY.Y, z) + axis * interval.Item1 + faceNormal * horizontalOffset;
                        XYZ b = new XYZ(originXY.X, originXY.Y, z) + axis * interval.Item2 + faceNormal * horizontalOffset;
                        CreateBar(doc, wall, options.HorizontalType, new Line[] { Line.CreateBound(a, b) }, faceNormal, "H");
                        created++;
                    }
                }
                for (int i = 0; i < verticalCount; i++)
                {
                    double s = verticalCount == 1 ? (minAlong + maxAlong) / 2.0 : minAlong + clear + spanAlong * i / (verticalCount - 1.0);
                    List<Tuple<double, double>> intervals = VisibleIntervals(zBottom - verticalExtensionBottom, zTop + verticalExtensionTop,
                        openings.Where(o => s >= o.MinAlong - clear && s <= o.MaxAlong + clear).Select(o => Tuple.Create(o.MinZ - clear, o.MaxZ + clear)));
                    foreach (Tuple<double, double> interval in intervals)
                    {
                        RebarBarType barType = options.VerticalType;
                        if (options.UseLeftEdge && s <= minAlong + clear + leftZone) barType = options.LeftEdgeType;
                        else if (options.UseRightEdge && s >= maxAlong - clear - rightZone) barType = options.RightEdgeType;
                        double barOffset = side * (thickness / 2.0 - cover - horizontalDia -
                            UnitUtils.ConvertToInternalUnits(2, UnitTypeId.Millimeters) - barType.BarNominalDiameter / 2.0);
                        XYZ a = new XYZ(originXY.X, originXY.Y, interval.Item1) + axis * s + faceNormal * barOffset;
                        XYZ b = new XYZ(originXY.X, originXY.Y, interval.Item2) + axis * s + faceNormal * barOffset;
                        CreateBar(doc, wall, barType, new Line[] { Line.CreateBound(a, b) }, axis, "V");
                        created++;
                    }
                }

                foreach (OpeningRegion opening in openings)
                    created += CreateOpeningEdgeBars(doc, wall, options.HorizontalType, options.VerticalType, originXY, axis, faceNormal,
                        horizontalOffset, verticalOffset, opening, openings, minAlong + clear, maxAlong - clear, zBottom, zTop, clear);
            }
            created += CreateTransverseTies(doc, wall, axis, faceNormal, originXY, length, thickness, cover, clear,
                horizontalDia, zBottom, zTop, horizontalCount, openings, options);
            created += CreateRebarShapeWallTies(doc, wall, axis, faceNormal, originXY, length, thickness, box,
                cover, openings, options);
            return created;
        }

        private static int CreateRebarShapeWallTies(Document doc, Wall wall, XYZ axis, XYZ normal, XYZ origin,
            double length, double thickness, BoundingBoxXYZ box, double cover,
            IList<OpeningRegion> openings, Options options)
        {
            var ties = new[]
            {
                Tuple.Create(options.UseTieO, "O", options.TieOShape),
                Tuple.Create(options.UseTieC, "C", options.TieCShape),
                Tuple.Create(options.UseTieU, "U", options.TieUShape)
            }.Where(x => x.Item1).ToList();
            if (ties.Count == 0) return 0;
            if (options.ShapeTieType == null) throw new InvalidOperationException("Chọn loại thép cho các đai RebarShape.");

            double diameter = options.ShapeTieType.BarNominalDiameter;
            double bend = Math.Max(options.ShapeTieType.StirrupTieBendDiameter, diameter);
            double clearance = cover + diameter / 2.0 + bend / 2.0;
            double zoneLength = UnitUtils.ConvertToInternalUnits(options.ShapeTieZoneLengthMm, UnitTypeId.Millimeters);
            double depth = thickness - 2.0 * clearance;
            double zStart = box.Min.Z + clearance;
            double zEnd = box.Max.Z - clearance;
            double height = zEnd - zStart;
            double spacing = UnitUtils.ConvertToInternalUnits(options.ShapeTieSpacingMm, UnitTypeId.Millimeters);
            if (zoneLength <= doc.Application.ShortCurveTolerance || zoneLength * 2.0 >= length - 2.0 * clearance ||
                depth <= doc.Application.ShortCurveTolerance || height <= doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Kích thước vùng đai O/C/U không vừa tường hoặc cover.");

            long estimatedBars = (long)Math.Ceiling(height / spacing) + 1;
            if (estimatedBars * ties.Count * 2 > 2400)
                throw new InvalidOperationException("Mật độ đai O/C/U vượt quá 2400 thanh trên một tường.");

            XYZ planeNormal = axis.CrossProduct(normal).Z >= 0 ? normal : -normal;
            XYZ xVector = axis * zoneLength;
            XYZ yVector = planeNormal * depth;
            int created = 0;
            foreach (Tuple<bool, string, RebarShape> tie in ties)
            {
                if (tie.Item3 == null) throw new InvalidOperationException("Chưa chọn RebarShape cho đai " + tie.Item2 + ".");
                foreach (double alongStart in new[] { -length / 2.0 + clearance, length / 2.0 - clearance - zoneLength })
                {
                    double alongEnd = alongStart + zoneLength;
                    IEnumerable<Tuple<double, double>> blocked = (openings ?? new List<OpeningRegion>())
                        .Where(o => o.MaxAlong >= alongStart - clearance && o.MinAlong <= alongEnd + clearance)
                        .Select(o => Tuple.Create(o.MinZ - clearance, o.MaxZ + clearance));
                    foreach (Tuple<double, double> span in VisibleIntervals(zStart, zEnd, blocked))
                    {
                        if (span.Item2 - span.Item1 <= doc.Application.ShortCurveTolerance) continue;
                        XYZ basePoint = new XYZ(origin.X, origin.Y, span.Item1) + axis * alongStart - planeNormal * (depth / 2.0);
                        Rebar rebar = Rebar.CreateFromRebarShape(doc, tie.Item3, options.ShapeTieType, wall,
                            basePoint, xVector, yVector);
                        if (rebar == null) throw new InvalidOperationException("Revit không tạo được đai " + tie.Item2 + ".");
                        RebarShapeDrivenAccessor accessor = rebar.GetShapeDrivenAccessor();
                        accessor.ScaleToBox(basePoint, xVector, yVector);
                        accessor.SetLayoutAsMaximumSpacing(spacing, span.Item2 - span.Item1, true, true, true);
                        Parameter partition = rebar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
                        if (partition != null && !partition.IsReadOnly)
                            partition.Set("KSTR_WALL_" + wall.Id.Value + "_TIE_" + tie.Item2 + "_" + created);
                        created += Math.Max(1, rebar.NumberOfBarPositions);
                    }
                }
            }
            return created;
        }

        private static int CreateTransverseTies(Document doc, Wall wall, XYZ axis, XYZ normal, XYZ origin,
            double length, double thickness, double cover, double longitudinalClear, double horizontalDiameter,
            double zBottom, double zTop, int horizontalCount, IList<OpeningRegion> openings, Options options)
        {
            if (!options.DrawTransverseTies) return 0;
            RebarBarType type = options.TieType;
            RebarHookType hook = options.TieHook;
            if (type == null || hook == null) throw new InvalidOperationException("Chọn loại thép đai và móc 180° cho thanh nối hai mặt.");
            double diameter = type.BarNominalDiameter;
            double bend = type.StirrupTieBendDiameter;
            double faceOffset = thickness / 2.0 - cover - bend / 2.0 - diameter / 2.0;
            double arrayLength = length - 2.0 * longitudinalClear - diameter;
            double maxSpacing = UnitUtils.ConvertToInternalUnits(options.TieSpacingMm, UnitTypeId.Millimeters);
            if (faceOffset <= doc.Application.ShortCurveTolerance || arrayLength <= doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Không đủ bề rộng/chiều dài tường cho thanh đai nối hai mặt.");
            int alongCount = CountBars(arrayLength, maxSpacing);
            if ((long)alongCount * ((horizontalCount + 1) / 2) > 2400)
                throw new InvalidOperationException("Mật độ đai nối hai mặt vượt quá 2400 thanh trên một tường.");

            double zOffset = horizontalDiameter / 2.0 + bend / 2.0 + diameter / 2.0;
            double topLimit = zTop + longitudinalClear - cover - bend / 2.0 - diameter / 2.0;
            double bottomLimit = zBottom - longitudinalClear + cover + bend / 2.0 + diameter / 2.0;
            int created = 0;
            for (int i = 0; i < horizontalCount; i += 2)
            {
                double z = (horizontalCount == 1 ? (zBottom + zTop) / 2.0 :
                    zBottom + (zTop - zBottom) * i / (horizontalCount - 1.0)) + zOffset;
                if (z < bottomLimit || z > topLimit) continue;
                for (int j = 0; j < alongCount; j++)
                {
                    double localAlong = longitudinalClear + diameter / 2.0 +
                        (alongCount == 1 ? arrayLength / 2.0 : arrayLength * j / (alongCount - 1.0));
                    double s = localAlong - length / 2.0;
                    if (openings.Any(o => s >= o.MinAlong - longitudinalClear && s <= o.MaxAlong + longitudinalClear &&
                        z >= o.MinZ - longitudinalClear && z <= o.MaxZ + longitudinalClear)) continue;
                    XYZ center = new XYZ(origin.X, origin.Y, z) + axis * s;
                    XYZ a = center - normal * faceOffset;
                    XYZ b = center + normal * faceOffset;
                    Rebar tie = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.StirrupTie, type, hook, hook, wall, axis,
                        new List<Curve> { Line.CreateBound(a, b) }, RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                    if (tie == null) throw new InvalidOperationException("Revit không tạo được đai nối hai mặt tại thanh ngang " + (i + 1) + ".");
                    Parameter partition = tie.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
                    if (partition != null && !partition.IsReadOnly)
                        partition.Set("KSTR_WALL_" + wall.Id.Value + "_TIE_" + (i + 1) + "_" + (j + 1));
                    created++;
                }
            }
            return created;
        }

        private static List<OpeningRegion> ReadOpeningRegions(Document doc, Wall wall, Line axisLine)
        {
            IList<ElementId> insertIds = wall.FindInserts(true, true, true, true);
            var result = new List<OpeningRegion>();
            if (insertIds == null) return result;
            XYZ origin = axisLine.Evaluate(0.5, true);
            XYZ axis = axisLine.Direction.Normalize();
            foreach (ElementId id in insertIds.Distinct())
            {
                Element insert = doc.GetElement(id);
                BoundingBoxXYZ box = insert == null ? null : insert.get_BoundingBox(null);
                if (box == null) throw new InvalidOperationException("Không đọc được bounding box phần tử chèn " + id.Value + "; đã dừng để tránh thép xuyên qua lỗ.");
                Transform transform = box.Transform ?? Transform.Identity;
                var points = new List<XYZ>();
                for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                    points.Add(transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X,
                        y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z)));
                double minAlong = points.Min(p => (p - origin).DotProduct(axis));
                double maxAlong = points.Max(p => (p - origin).DotProduct(axis));
                double minZ = points.Min(p => p.Z);
                double maxZ = points.Max(p => p.Z);
                result.Add(new OpeningRegion { MinAlong = minAlong, MaxAlong = maxAlong, MinZ = minZ, MaxZ = maxZ });
            }
            return result;
        }

        private static List<Tuple<double, double>> VisibleIntervals(double start, double end, IEnumerable<Tuple<double, double>> blocked)
        {
            var result = new List<Tuple<double, double>>();
            if (end <= start) return result;
            double tolerance = 1e-5;
            double cursor = start;
            foreach (Tuple<double, double> interval in blocked.Select(x => Tuple.Create(Math.Max(start, x.Item1), Math.Min(end, x.Item2)))
                .Where(x => x.Item2 > x.Item1).OrderBy(x => x.Item1))
            {
                if (interval.Item1 > cursor + tolerance) result.Add(Tuple.Create(cursor, interval.Item1));
                cursor = Math.Max(cursor, interval.Item2);
                if (cursor >= end - tolerance) break;
            }
            if (end > cursor + tolerance) result.Add(Tuple.Create(cursor, end));
            return result;
        }

        private static int CreateOpeningEdgeBars(Document doc, Wall wall, RebarBarType horizontalType, RebarBarType verticalType,
            XYZ origin, XYZ axis, XYZ faceNormal, double horizontalOffset, double verticalOffset,
            OpeningRegion opening, IList<OpeningRegion> allOpenings,
            double minAlong, double maxAlong, double zBottom, double zTop, double clearance)
        {
            double s0 = Math.Max(minAlong, opening.MinAlong - clearance);
            double s1 = Math.Min(maxAlong, opening.MaxAlong + clearance);
            double z0 = Math.Max(zBottom, opening.MinZ - clearance);
            double z1 = Math.Min(zTop, opening.MaxZ + clearance);
            if (s1 <= s0 || z1 <= z0) return 0;
            int created = 0;
            foreach (double z in new[] { z0, z1 })
            {
                var blocked = allOpenings.Where(other => !ReferenceEquals(other, opening) &&
                    z >= other.MinZ - clearance && z <= other.MaxZ + clearance)
                    .Select(other => Tuple.Create(other.MinAlong - clearance, other.MaxAlong + clearance));
                foreach (Tuple<double, double> span in VisibleIntervals(s0, s1, blocked))
                {
                    XYZ a = new XYZ(origin.X, origin.Y, z) + axis * span.Item1 + faceNormal * horizontalOffset;
                    XYZ b = new XYZ(origin.X, origin.Y, z) + axis * span.Item2 + faceNormal * horizontalOffset;
                    CreateBar(doc, wall, horizontalType, new[] { Line.CreateBound(a, b) }, faceNormal, "OPEN_H");
                    created++;
                }
            }
            foreach (double s in new[] { s0, s1 })
            {
                var blocked = allOpenings.Where(other => !ReferenceEquals(other, opening) &&
                    s >= other.MinAlong - clearance && s <= other.MaxAlong + clearance)
                    .Select(other => Tuple.Create(other.MinZ - clearance, other.MaxZ + clearance));
                foreach (Tuple<double, double> span in VisibleIntervals(z0, z1, blocked))
                {
                    XYZ a = new XYZ(origin.X, origin.Y, span.Item1) + axis * s + faceNormal * verticalOffset;
                    XYZ b = new XYZ(origin.X, origin.Y, span.Item2) + axis * s + faceNormal * verticalOffset;
                    CreateBar(doc, wall, verticalType, new[] { Line.CreateBound(a, b) }, axis, "OPEN_V");
                    created++;
                }
            }
            return created;
        }

        private static int CountBars(double span, double maxSpacing) => Math.Max(2, (int)Math.Ceiling(span / maxSpacing) + 1);

        private static void CreateBar(Document doc, Wall wall, RebarBarType type, IList<Line> lines, XYZ normal, string partition)
        {
            Rebar rebar = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.Standard, type, null, null, wall, normal,
                lines.Cast<Curve>().ToList(), RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
            if (rebar == null) throw new InvalidOperationException("Revit không tạo được thanh " + partition + ".");
            Parameter p = rebar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
            if (p != null && !p.IsReadOnly) p.Set("KSTR_WALL_" + wall.Id.Value + "_" + partition);
        }

        private static Options ChooseOptions(IntPtr owner, IList<RebarBarType> types,
            IList<RebarHookType> tieHooks, IList<RebarShape> tieShapes,
            StraightWallRebarSettingsStore.Values defaults, int wallCount, bool hasSavedProfiles)
        {
            List<BarChoice> choices = types.Select(t => new BarChoice(t)).ToList();
            List<HookChoice> hookChoices = tieHooks.Select(h => new HookChoice(h)).ToList();
            List<ShapeChoice> closedTieShapes = tieShapes.Where(s => s.RebarStyle == RebarStyle.StirrupTie)
                .Select(s => new ShapeChoice(s)).ToList();
            List<ShapeChoice> standardTieShapes = tieShapes.Where(s => s.RebarStyle == RebarStyle.Standard)
                .Select(s => new ShapeChoice(s)).ToList();
            Func<string, BarChoice> byName = name => choices.FirstOrDefault(c =>
                string.Equals(c.Type.Name, name, StringComparison.OrdinalIgnoreCase)) ?? choices.FirstOrDefault();
            Func<string, HookChoice> hookByName = name => hookChoices.FirstOrDefault(c =>
                string.Equals(c.Type.Name, name, StringComparison.OrdinalIgnoreCase)) ?? hookChoices.FirstOrDefault();
            Func<IList<ShapeChoice>, string, ShapeChoice> shapeByName = (list, name) => list.FirstOrDefault(c =>
                string.Equals(c.Shape.Name, name, StringComparison.OrdinalIgnoreCase)) ?? list.FirstOrDefault();
            var window = new Window
            {
                Title = "Cốt thép tường thẳng - KStruct",
                Width = 475,
                MaxHeight = 850,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            var perWallProfilesCheck = new CheckBox
            {
                Content = "Dùng cấu hình đã lưu riêng cho từng tường được chọn",
                IsChecked = wallCount > 1 && hasSavedProfiles,
                Visibility = wallCount > 1 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed,
                Margin = new Thickness(0, 0, 0, 6)
            };
            root.Children.Add(perWallProfilesCheck);
            root.Children.Add(new TextBlock
            {
                Text = defaults == null
                    ? "Thiết lập sẽ được lưu riêng trên từng tường sau khi tạo thành công. Tường chưa có cấu hình sẽ dùng các giá trị bên dưới."
                    : "Các giá trị bên dưới lấy từ tường đầu tiên. Khi bật tùy chọn trên, tường đã có cấu hình sẽ dùng cấu hình riêng; tường chưa có sẽ dùng các giá trị bên dưới.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });

            root.Children.Add(new TextBlock { Text = "Loại thép ngang:" });
            var horizontalCombo = CreateTypeCombo(choices, byName(defaults == null ? null : defaults.HorizontalTypeName));
            root.Children.Add(horizontalCombo);
            root.Children.Add(new TextBlock { Text = "Loại thép dọc vùng giữa:", Margin = new Thickness(0, 6, 0, 0) });
            var verticalCombo = CreateTypeCombo(choices, byName(defaults == null ? null : defaults.VerticalTypeName));
            root.Children.Add(verticalCombo);

            var leftCheck = new CheckBox
            {
                Content = "Gia cường vùng đầu trái",
                IsChecked = defaults != null && defaults.UseLeftEdge,
                Margin = new Thickness(0, 8, 0, 2)
            };
            root.Children.Add(leftCheck);
            var leftCombo = CreateTypeCombo(choices, byName(defaults == null ? null : defaults.LeftTypeName));
            root.Children.Add(leftCombo);
            var leftLengthBox = AddInput(root, "Chiều dài vùng trái (mm):",
                (defaults == null || defaults.LeftLengthMm <= 0 ? 400.0 : defaults.LeftLengthMm).ToString(System.Globalization.CultureInfo.CurrentCulture));
            leftCheck.Checked += (s, e) => { leftCombo.IsEnabled = true; leftLengthBox.IsEnabled = true; };
            leftCheck.Unchecked += (s, e) => { leftCombo.IsEnabled = false; leftLengthBox.IsEnabled = false; };
            leftCombo.IsEnabled = leftCheck.IsChecked.GetValueOrDefault();
            leftLengthBox.IsEnabled = leftCheck.IsChecked.GetValueOrDefault();

            var rightCheck = new CheckBox
            {
                Content = "Gia cường vùng đầu phải",
                IsChecked = defaults != null && defaults.UseRightEdge,
                Margin = new Thickness(0, 6, 0, 2)
            };
            root.Children.Add(rightCheck);
            var rightCombo = CreateTypeCombo(choices, byName(defaults == null ? null : defaults.RightTypeName));
            root.Children.Add(rightCombo);
            var rightLengthBox = AddInput(root, "Chiều dài vùng phải (mm):",
                (defaults == null || defaults.RightLengthMm <= 0 ? 400.0 : defaults.RightLengthMm).ToString(System.Globalization.CultureInfo.CurrentCulture));
            rightCheck.Checked += (s, e) => { rightCombo.IsEnabled = true; rightLengthBox.IsEnabled = true; };
            rightCheck.Unchecked += (s, e) => { rightCombo.IsEnabled = false; rightLengthBox.IsEnabled = false; };
            rightCombo.IsEnabled = rightCheck.IsChecked.GetValueOrDefault();
            rightLengthBox.IsEnabled = rightCheck.IsChecked.GetValueOrDefault();

            var lapCheck = new CheckBox
            {
                Content = "Nối chồng thép dọc giữa các tường tầng kề trùng trục",
                IsChecked = defaults != null && defaults.LapAdjacentWalls,
                Margin = new Thickness(0, 8, 0, 2)
            };
            root.Children.Add(lapCheck);
            var lapBox = AddInput(root, "Chiều dài chồng thanh tại mỗi mối nối (mm):",
                (defaults == null || defaults.VerticalLapMm <= 0 ? 600.0 : defaults.VerticalLapMm).ToString(System.Globalization.CultureInfo.CurrentCulture));
            lapBox.IsEnabled = lapCheck.IsChecked.GetValueOrDefault();
            lapCheck.Checked += (s, e) => lapBox.IsEnabled = true;
            lapCheck.Unchecked += (s, e) => lapBox.IsEnabled = false;

            var tieCheck = new CheckBox
            {
                Content = "Tạo đai nối hai mặt tường tại các lớp ngang xen kẽ",
                IsChecked = defaults != null && defaults.DrawTransverseTies && hookChoices.Count > 0,
                IsEnabled = hookChoices.Count > 0,
                Margin = new Thickness(0, 8, 0, 2)
            };
            root.Children.Add(tieCheck);
            if (hookChoices.Count == 0)
                root.Children.Add(new TextBlock { Text = "Project chưa có móc đai 180°; cần nạp RebarHookType phù hợp để bật đai.", TextWrapping = TextWrapping.Wrap });
            root.Children.Add(new TextBlock { Text = "Loại thép đai nối hai mặt:" });
            var tieTypeCombo = CreateTypeCombo(choices, byName(defaults == null ? null : defaults.TieTypeName));
            root.Children.Add(tieTypeCombo);
            root.Children.Add(new TextBlock { Text = "Móc 180°:" });
            var tieHookCombo = new System.Windows.Controls.ComboBox
            {
                MinWidth = 425,
                MinHeight = 24,
                DisplayMemberPath = "Display",
                ItemsSource = hookChoices,
                SelectedItem = hookByName(defaults == null ? null : defaults.TieHookName)
            };
            root.Children.Add(tieHookCombo);
            var tieSpacingBox = AddInput(root, "Khoảng cách đai dọc theo tường tối đa (mm):",
                (defaults == null || defaults.TieSpacingMm <= 0 ? 300.0 : defaults.TieSpacingMm).ToString(System.Globalization.CultureInfo.CurrentCulture));
            Action setTieEnabled = () =>
            {
                bool enabled = tieCheck.IsChecked.GetValueOrDefault() && tieCheck.IsEnabled;
                tieTypeCombo.IsEnabled = enabled;
                tieHookCombo.IsEnabled = enabled;
                tieSpacingBox.IsEnabled = enabled;
            };
            tieCheck.Checked += (s, e) => setTieEnabled();
            tieCheck.Unchecked += (s, e) => setTieEnabled();
            setTieEnabled();

            var tieOCheck = new CheckBox { Content = "Đai O kín theo RebarShape", IsChecked = defaults != null && defaults.UseTieO,
                IsEnabled = closedTieShapes.Count > 0, Margin = new Thickness(0, 8, 0, 2) };
            root.Children.Add(tieOCheck);
            var tieOCombo = CreateShapeCombo(closedTieShapes, shapeByName(closedTieShapes, defaults == null ? null : defaults.TieOShapeName));
            root.Children.Add(tieOCombo);
            var tieCCheck = new CheckBox { Content = "Đai C theo RebarShape", IsChecked = defaults != null && defaults.UseTieC,
                IsEnabled = closedTieShapes.Count > 0, Margin = new Thickness(0, 5, 0, 2) };
            root.Children.Add(tieCCheck);
            var tieCCombo = CreateShapeCombo(closedTieShapes, shapeByName(closedTieShapes, defaults == null ? null : defaults.TieCShapeName));
            root.Children.Add(tieCCombo);
            var tieUCheck = new CheckBox { Content = "Đai U/móc theo RebarShape", IsChecked = defaults != null && defaults.UseTieU,
                IsEnabled = standardTieShapes.Count > 0, Margin = new Thickness(0, 5, 0, 2) };
            root.Children.Add(tieUCheck);
            var tieUCombo = CreateShapeCombo(standardTieShapes, shapeByName(standardTieShapes, defaults == null ? null : defaults.TieUShapeName));
            root.Children.Add(tieUCombo);
            root.Children.Add(new TextBlock { Text = "Loại thép đai O/C/U:" });
            var shapeTieTypeCombo = CreateTypeCombo(choices, byName(defaults == null ? null : defaults.ShapeTieTypeName));
            root.Children.Add(shapeTieTypeCombo);
            var shapeTieZoneBox = AddInput(root, "Chiều dài vùng đai ở mỗi đầu tường (mm):",
                (defaults == null || defaults.ShapeTieZoneLengthMm <= 0 ? 350.0 : defaults.ShapeTieZoneLengthMm).ToString(System.Globalization.CultureInfo.CurrentCulture));
            var shapeTieSpacingBox = AddInput(root, "Khoảng cách đứng tối đa giữa các đai O/C/U (mm):",
                (defaults == null || defaults.ShapeTieSpacingMm <= 0 ? 150.0 : defaults.ShapeTieSpacingMm).ToString(System.Globalization.CultureInfo.CurrentCulture));
            Action setShapeTiesEnabled = () =>
            {
                tieOCombo.IsEnabled = tieOCheck.IsEnabled && tieOCheck.IsChecked.GetValueOrDefault();
                tieCCombo.IsEnabled = tieCCheck.IsEnabled && tieCCheck.IsChecked.GetValueOrDefault();
                tieUCombo.IsEnabled = tieUCheck.IsEnabled && tieUCheck.IsChecked.GetValueOrDefault();
                bool enabled = tieOCheck.IsChecked.GetValueOrDefault() || tieCCheck.IsChecked.GetValueOrDefault() || tieUCheck.IsChecked.GetValueOrDefault();
                shapeTieTypeCombo.IsEnabled = enabled;
                shapeTieZoneBox.IsEnabled = enabled;
                shapeTieSpacingBox.IsEnabled = enabled;
            };
            tieOCheck.Checked += (s, e) => setShapeTiesEnabled();
            tieOCheck.Unchecked += (s, e) => setShapeTiesEnabled();
            tieCCheck.Checked += (s, e) => setShapeTiesEnabled();
            tieCCheck.Unchecked += (s, e) => setShapeTiesEnabled();
            tieUCheck.Checked += (s, e) => setShapeTiesEnabled();
            tieUCheck.Unchecked += (s, e) => setShapeTiesEnabled();
            setShapeTiesEnabled();

            var coverBox = AddInput(root, "Cover hai mặt (mm):",
                (defaults == null ? 25.0 : defaults.CoverMm).ToString(System.Globalization.CultureInfo.CurrentCulture));
            var horizontalBox = AddInput(root, "Khoảng cách thép ngang tối đa (mm):",
                (defaults == null ? 200.0 : defaults.HorizontalSpacingMm).ToString(System.Globalization.CultureInfo.CurrentCulture));
            var verticalBox = AddInput(root, "Khoảng cách thép dọc tối đa (mm):",
                (defaults == null ? 200.0 : defaults.VerticalSpacingMm).ToString(System.Globalization.CultureInfo.CurrentCulture));

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0)
            };
            var ok = new Button { Content = "Tạo thép", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) =>
            {
                double cover, horizontal, vertical, leftLength, rightLength, lapLength, tieSpacing, shapeTieZone, shapeTieSpacing;
                bool valid = horizontalCombo.SelectedItem != null && verticalCombo.SelectedItem != null &&
                    double.TryParse(coverBox.Text, out cover) && cover > 0 &&
                    double.TryParse(horizontalBox.Text, out horizontal) && horizontal > 0 &&
                    double.TryParse(verticalBox.Text, out vertical) && vertical > 0;
                if (leftCheck.IsChecked.GetValueOrDefault())
                    valid = valid && leftCombo.SelectedItem != null &&
                        double.TryParse(leftLengthBox.Text, out leftLength) && leftLength > 0;
                else leftLength = 0;
                if (rightCheck.IsChecked.GetValueOrDefault())
                    valid = valid && rightCombo.SelectedItem != null &&
                        double.TryParse(rightLengthBox.Text, out rightLength) && rightLength > 0;
                else rightLength = 0;
                if (lapCheck.IsChecked.GetValueOrDefault())
                    valid = valid && double.TryParse(lapBox.Text, out lapLength) && lapLength > 0;
                else lapLength = 0;
                if (tieCheck.IsChecked.GetValueOrDefault())
                    valid = valid && tieTypeCombo.SelectedItem != null && tieHookCombo.SelectedItem != null &&
                        double.TryParse(tieSpacingBox.Text, out tieSpacing) && tieSpacing > 0;
                else tieSpacing = 0;
                bool anyShapeTie = tieOCheck.IsChecked.GetValueOrDefault() || tieCCheck.IsChecked.GetValueOrDefault() || tieUCheck.IsChecked.GetValueOrDefault();
                if (anyShapeTie)
                    valid = valid && shapeTieTypeCombo.SelectedItem != null &&
                        (!tieOCheck.IsChecked.GetValueOrDefault() || tieOCombo.SelectedItem != null) &&
                        (!tieCCheck.IsChecked.GetValueOrDefault() || tieCCombo.SelectedItem != null) &&
                        (!tieUCheck.IsChecked.GetValueOrDefault() || tieUCombo.SelectedItem != null) &&
                        double.TryParse(shapeTieZoneBox.Text, out shapeTieZone) && shapeTieZone > 0 &&
                        double.TryParse(shapeTieSpacingBox.Text, out shapeTieSpacing) && shapeTieSpacing > 0;
                else { shapeTieZone = 0; shapeTieSpacing = 0; }
                if (!valid)
                {
                    TaskDialog.Show("KStruct - Cốt thép tường", "Kiểm tra RebarShape, loại thép, móc, cover, khoảng cách và chiều dài vùng biên.");
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
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 820
            };
            new WindowInteropHelper(window).Owner = owner;
            if (window.ShowDialog() != true) return null;

            double coverValue, horizontalValue, verticalValue, leftValue = 0, rightValue = 0, lapValue = 0, tieSpacingValue = 0,
                shapeTieZoneValue = 0, shapeTieSpacingValue = 0;
            double.TryParse(coverBox.Text, out coverValue);
            double.TryParse(horizontalBox.Text, out horizontalValue);
            double.TryParse(verticalBox.Text, out verticalValue);
            if (leftCheck.IsChecked.GetValueOrDefault()) double.TryParse(leftLengthBox.Text, out leftValue);
            if (rightCheck.IsChecked.GetValueOrDefault()) double.TryParse(rightLengthBox.Text, out rightValue);
            if (lapCheck.IsChecked.GetValueOrDefault()) double.TryParse(lapBox.Text, out lapValue);
            if (tieCheck.IsChecked.GetValueOrDefault()) double.TryParse(tieSpacingBox.Text, out tieSpacingValue);
            if (tieOCheck.IsChecked.GetValueOrDefault() || tieCCheck.IsChecked.GetValueOrDefault() || tieUCheck.IsChecked.GetValueOrDefault())
            {
                double.TryParse(shapeTieZoneBox.Text, out shapeTieZoneValue);
                double.TryParse(shapeTieSpacingBox.Text, out shapeTieSpacingValue);
            }
            return new Options
            {
                HorizontalType = ((BarChoice)horizontalCombo.SelectedItem).Type,
                VerticalType = ((BarChoice)verticalCombo.SelectedItem).Type,
                UseLeftEdge = leftCheck.IsChecked.GetValueOrDefault(),
                LeftEdgeType = leftCheck.IsChecked.GetValueOrDefault() ? ((BarChoice)leftCombo.SelectedItem).Type : null,
                LeftEdgeLengthMm = leftValue,
                UseRightEdge = rightCheck.IsChecked.GetValueOrDefault(),
                RightEdgeType = rightCheck.IsChecked.GetValueOrDefault() ? ((BarChoice)rightCombo.SelectedItem).Type : null,
                RightEdgeLengthMm = rightValue,
                CoverMm = coverValue,
                HorizontalSpacingMm = horizontalValue,
                VerticalSpacingMm = verticalValue,
                LapAdjacentWalls = lapCheck.IsChecked.GetValueOrDefault(),
                VerticalLapMm = lapValue,
                DrawTransverseTies = tieCheck.IsChecked.GetValueOrDefault(),
                TieType = tieCheck.IsChecked.GetValueOrDefault() ? ((BarChoice)tieTypeCombo.SelectedItem).Type : null,
                TieHook = tieCheck.IsChecked.GetValueOrDefault() ? ((HookChoice)tieHookCombo.SelectedItem).Type : null,
                TieSpacingMm = tieSpacingValue,
                UseTieO = tieOCheck.IsChecked.GetValueOrDefault(),
                UseTieC = tieCCheck.IsChecked.GetValueOrDefault(),
                UseTieU = tieUCheck.IsChecked.GetValueOrDefault(),
                TieOShape = tieOCheck.IsChecked.GetValueOrDefault() ? ((ShapeChoice)tieOCombo.SelectedItem).Shape : null,
                TieCShape = tieCCheck.IsChecked.GetValueOrDefault() ? ((ShapeChoice)tieCCombo.SelectedItem).Shape : null,
                TieUShape = tieUCheck.IsChecked.GetValueOrDefault() ? ((ShapeChoice)tieUCombo.SelectedItem).Shape : null,
                ShapeTieType = (tieOCheck.IsChecked.GetValueOrDefault() || tieCCheck.IsChecked.GetValueOrDefault() || tieUCheck.IsChecked.GetValueOrDefault())
                    ? ((BarChoice)shapeTieTypeCombo.SelectedItem).Type : null,
                ShapeTieZoneLengthMm = shapeTieZoneValue,
                ShapeTieSpacingMm = shapeTieSpacingValue,
                UseSavedWallProfiles = perWallProfilesCheck.IsChecked.GetValueOrDefault()
            };
        }

        private static Options CloneOptions(Options source)
        {
            return new Options
            {
                HorizontalType = source.HorizontalType,
                VerticalType = source.VerticalType,
                LeftEdgeType = source.LeftEdgeType,
                RightEdgeType = source.RightEdgeType,
                TieType = source.TieType,
                ShapeTieType = source.ShapeTieType,
                TieHook = source.TieHook,
                UseLeftEdge = source.UseLeftEdge,
                UseRightEdge = source.UseRightEdge,
                DrawTransverseTies = source.DrawTransverseTies,
                UseTieO = source.UseTieO,
                UseTieC = source.UseTieC,
                UseTieU = source.UseTieU,
                TieOShape = source.TieOShape,
                TieCShape = source.TieCShape,
                TieUShape = source.TieUShape,
                LeftEdgeLengthMm = source.LeftEdgeLengthMm,
                RightEdgeLengthMm = source.RightEdgeLengthMm,
                LapAdjacentWalls = source.LapAdjacentWalls,
                CoverMm = source.CoverMm,
                HorizontalSpacingMm = source.HorizontalSpacingMm,
                VerticalSpacingMm = source.VerticalSpacingMm,
                VerticalLapMm = source.VerticalLapMm,
                TieSpacingMm = source.TieSpacingMm,
                ShapeTieZoneLengthMm = source.ShapeTieZoneLengthMm,
                ShapeTieSpacingMm = source.ShapeTieSpacingMm,
                UseSavedWallProfiles = source.UseSavedWallProfiles
            };
        }

        private static Options ApplySavedProfile(Options defaults,
            StraightWallRebarSettingsStore.Values saved, IList<RebarBarType> types,
            IList<RebarHookType> hooks, IList<RebarShape> shapes)
        {
            Options result = CloneOptions(defaults);
            Func<string, RebarBarType, RebarBarType> bar = (name, fallback) =>
                string.IsNullOrWhiteSpace(name) ? fallback :
                    types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? fallback;
            Func<string, RebarHookType, RebarHookType> hook = (name, fallback) =>
                string.IsNullOrWhiteSpace(name) ? fallback :
                    hooks.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? fallback;
            Func<string, RebarShape, RebarShape> shape = (name, fallback) =>
                string.IsNullOrWhiteSpace(name) ? fallback :
                    shapes.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? fallback;

            result.HorizontalType = bar(saved.HorizontalTypeName, result.HorizontalType);
            result.VerticalType = bar(saved.VerticalTypeName, result.VerticalType);
            if (saved.CoverMm > 0) result.CoverMm = saved.CoverMm;
            if (saved.HorizontalSpacingMm > 0) result.HorizontalSpacingMm = saved.HorizontalSpacingMm;
            if (saved.VerticalSpacingMm > 0) result.VerticalSpacingMm = saved.VerticalSpacingMm;
            result.UseLeftEdge = saved.UseLeftEdge;
            result.LeftEdgeType = result.UseLeftEdge ? bar(saved.LeftTypeName, result.LeftEdgeType) : null;
            if (saved.LeftLengthMm > 0) result.LeftEdgeLengthMm = saved.LeftLengthMm;
            result.UseRightEdge = saved.UseRightEdge;
            result.RightEdgeType = result.UseRightEdge ? bar(saved.RightTypeName, result.RightEdgeType) : null;
            if (saved.RightLengthMm > 0) result.RightEdgeLengthMm = saved.RightLengthMm;
            result.DrawTransverseTies = saved.DrawTransverseTies;
            result.TieType = result.DrawTransverseTies ? bar(saved.TieTypeName, result.TieType) : null;
            result.TieHook = result.DrawTransverseTies ? hook(saved.TieHookName, result.TieHook) : null;
            if (saved.TieSpacingMm > 0) result.TieSpacingMm = saved.TieSpacingMm;
            result.UseTieO = saved.UseTieO;
            result.UseTieC = saved.UseTieC;
            result.UseTieU = saved.UseTieU;
            result.TieOShape = result.UseTieO ? shape(saved.TieOShapeName, result.TieOShape) : null;
            result.TieCShape = result.UseTieC ? shape(saved.TieCShapeName, result.TieCShape) : null;
            result.TieUShape = result.UseTieU ? shape(saved.TieUShapeName, result.TieUShape) : null;
            bool anyShapeTie = result.UseTieO || result.UseTieC || result.UseTieU;
            result.ShapeTieType = anyShapeTie ? bar(saved.ShapeTieTypeName, result.ShapeTieType) : null;
            if (saved.ShapeTieZoneLengthMm > 0) result.ShapeTieZoneLengthMm = saved.ShapeTieZoneLengthMm;
            if (saved.ShapeTieSpacingMm > 0) result.ShapeTieSpacingMm = saved.ShapeTieSpacingMm;
            return result;
        }

        private static System.Windows.Controls.ComboBox CreateTypeCombo(IList<BarChoice> choices, BarChoice selected)
        {
            return new System.Windows.Controls.ComboBox
            {
                MinWidth = 425,
                MinHeight = 24,
                DisplayMemberPath = "Display",
                ItemsSource = choices,
                SelectedItem = selected
            };
        }
        private static System.Windows.Controls.ComboBox CreateShapeCombo(IList<ShapeChoice> choices, ShapeChoice selected)
        {
            return new System.Windows.Controls.ComboBox
            {
                MinWidth = 425,
                MinHeight = 24,
                DisplayMemberPath = "Display",
                ItemsSource = choices,
                SelectedItem = selected,
                IsEnabled = choices.Count > 0
            };
        }
        private static System.Windows.Controls.TextBox AddInput(System.Windows.Controls.Panel root, string label, string value)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 5, 0, 2) });
            var box = new System.Windows.Controls.TextBox { MinWidth = 370, MinHeight = 22, Text = value };
            root.Children.Add(box);
            return box;
        }
    }
}
