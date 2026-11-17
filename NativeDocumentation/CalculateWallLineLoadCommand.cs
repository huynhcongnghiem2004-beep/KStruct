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

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CalculateWallLineLoadCommand : IExternalCommand
    {
        private sealed class BeamFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element?.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class WallFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Wall;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class LoadOptions
        {
            public LoadCase Case;
            public LineLoadType Type;
            public double AdditionalKnPerSquareMeter;
        }

        private sealed class WallData
        {
            public Wall Wall;
            public Line Location;
            public double HeightMeters;
            public double SelfWeightKnPerSquareMeter;
            public double BaseZ;
            public double TopZ;
            public XYZ PlanAxis;
            public List<OpeningData> Openings = new List<OpeningData>();
        }

        private sealed class OpeningData
        {
            public double Start;
            public double End;
            public double BottomZ;
            public double TopZ;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<FamilyInstance> beams = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<FamilyInstance>()
                    .Where(e => e.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralFraming).Distinct().ToList();
                List<Wall> walls = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<Wall>().Distinct().ToList();
                if (beams.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new BeamFilter(),
                        "Chọn dầm đỡ để gán tải đường do tường truyền xuống");
                    beams = picked.Select(r => doc.GetElement(r)).OfType<FamilyInstance>().Distinct().ToList();
                }
                if (walls.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new WallFilter(),
                        "Chọn các tường đặt trên dầm để tính tải đường");
                    walls = picked.Select(r => doc.GetElement(r)).OfType<Wall>().Distinct().ToList();
                }
                if (beams.Count == 0 || walls.Count == 0) return Result.Cancelled;

                var wallData = new List<WallData>();
                var skipped = new List<string>();
                foreach (Wall wall in walls)
                {
                    try { wallData.Add(CalculateWallData(doc, wall)); }
                    catch (Exception ex) { skipped.Add("Tường " + wall.Id.Value + ": " + ex.Message); }
                }
                if (wallData.Count == 0)
                {
                    TaskDialog.Show("KStruct - Tải đường tường", "Không tính được tải từ các tường đã chọn.\n" + string.Join("\n", skipped.Take(8)));
                    return Result.Cancelled;
                }

                List<LoadCase> cases = new FilteredElementCollector(doc).OfClass(typeof(LoadCase)).Cast<LoadCase>()
                    .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                List<LineLoadType> types = new FilteredElementCollector(doc).OfClass(typeof(LineLoadType)).Cast<LineLoadType>()
                    .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (cases.Count == 0) throw new InvalidOperationException("Project chưa có Structural Load Case.");
                if (types.Count == 0) throw new InvalidOperationException("Project chưa có Line Load Type.");
                LoadOptions options = ChooseOptions(data.Application.MainWindowHandle, wallData, cases, types);
                if (options == null) return Result.Cancelled;

                AnalyticalToPhysicalAssociationManager association = AnalyticalToPhysicalAssociationManager.GetAnalyticalToPhysicalAssociationManager(doc);
                int created = 0;
                using (Transaction tx = new Transaction(doc, "KStruct - Tải đường do tường"))
                {
                    tx.Start();
                    foreach (FamilyInstance beam in beams)
                    {
                        try
                        {
                            ElementId analyticalId = association.GetAssociatedElementIds(beam.Id)
                                .FirstOrDefault(id => doc.GetElement(id) is AnalyticalMember);
                            AnalyticalMember member = analyticalId == null ? null : doc.GetElement(analyticalId) as AnalyticalMember;
                            if (member == null) throw new InvalidOperationException("Dầm chưa liên kết Analytical Member; hãy tạo analytical trước.");
                            if (!LineLoad.IsValidHostId(doc, member.Id))
                                throw new InvalidOperationException("Analytical Member này không hợp lệ làm host LineLoad.");
                            Line beamLine = (member.Location as LocationCurve)?.Curve as Line;
                            if (beamLine == null) throw new InvalidOperationException("Chỉ hỗ trợ Analytical Member dạng dầm thẳng.");
                            XYZ a = beamLine.GetEndPoint(0), b = beamLine.GetEndPoint(1);
                            XYZ plan = new XYZ(b.X - a.X, b.Y - a.Y, 0);
                            double beamLength = plan.GetLength();
                            if (beamLength <= doc.Application.ShortCurveTolerance || Math.Abs(b.Z - a.Z) > UnitUtils.ConvertToInternalUnits(20, UnitTypeId.Millimeters))
                                throw new InvalidOperationException("Chỉ hỗ trợ dầm analytical gần ngang.");
                            XYZ axis = plan.Normalize();
                            foreach (WallData wall in wallData)
                            {
                                double alignmentTolerance = UnitUtils.ConvertToInternalUnits(50, UnitTypeId.Millimeters);
                                if (!IsAlignedWithBeam(wall.Location, a, axis, alignmentTolerance))
                                {
                                    skipped.Add("Tường " + wall.Wall.Id.Value + " không song song/trùng trục với dầm " + beam.Id.Value + ".");
                                    continue;
                                }
                                XYZ w0 = wall.Location.GetEndPoint(0), w1 = wall.Location.GetEndPoint(1);
                                double s0 = new XYZ(w0.X - a.X, w0.Y - a.Y, 0).DotProduct(axis);
                                double s1 = new XYZ(w1.X - a.X, w1.Y - a.Y, 0).DotProduct(axis);
                                double start = Math.Max(0, Math.Min(s0, s1));
                                double end = Math.Min(beamLength, Math.Max(s0, s1));
                                if (end - start <= doc.Application.ShortCurveTolerance)
                                {
                                    skipped.Add("Tường " + wall.Wall.Id.Value + " không chồng lấn chiều dài dầm " + beam.Id.Value + ".");
                                    continue;
                                }
                                XYZ beamVector = b - a;
                                var stations = new List<double> { start, end };
                                double wallStartOnBeam = new XYZ(w0.X - a.X, w0.Y - a.Y, 0).DotProduct(axis);
                                double direction = (w1 - w0).DotProduct(new XYZ(axis.X, axis.Y, 0)) >= 0 ? 1.0 : -1.0;
                                foreach (OpeningData opening in wall.Openings)
                                {
                                    double openingStart = wallStartOnBeam + direction * opening.Start;
                                    double openingEnd = wallStartOnBeam + direction * opening.End;
                                    if (openingEnd > start && openingStart < end)
                                    {
                                        stations.Add(Math.Max(start, openingStart));
                                        stations.Add(Math.Min(end, openingEnd));
                                    }
                                }
                                stations = stations.Distinct().OrderBy(x => x).ToList();
                                for (int i = 0; i < stations.Count - 1; i++)
                                {
                                    double segmentStart = stations[i], segmentEnd = stations[i + 1];
                                    if (segmentEnd - segmentStart <= doc.Application.ShortCurveTolerance) continue;
                                    double station = direction * ((segmentStart + segmentEnd) * 0.5 - wallStartOnBeam);
                                    double openingHeight = GetOpeningHeightAt(wall, station);
                                    double solidHeight = Math.Max(0, wall.HeightMeters - UnitUtils.ConvertFromInternalUnits(openingHeight, UnitTypeId.Meters));
                                    double lineKnPerMeter = (wall.SelfWeightKnPerSquareMeter + options.AdditionalKnPerSquareMeter) * solidHeight;
                                    if (lineKnPerMeter <= 1e-9) continue;
                                    double magnitude = UnitUtils.ConvertToInternalUnits(lineKnPerMeter, UnitTypeId.KilonewtonsPerMeter);
                                    XYZ startPoint = a + beamVector.Multiply(segmentStart / beamLength);
                                    XYZ endPoint = a + beamVector.Multiply(segmentEnd / beamLength);
                                    using (SubTransaction sub = new SubTransaction(doc))
                                    {
                                        sub.Start();
                                        try
                                        {
                                            LineLoad load = LineLoad.Create(doc, member.Id, Line.CreateBound(startPoint, endPoint),
                                                new XYZ(0, 0, -magnitude), XYZ.Zero, options.Type);
                                            if (load == null) throw new InvalidOperationException("LineLoad.Create trả về null.");
                                            load.LoadCaseId = options.Case.Id;
                                            if (sub.Commit() != TransactionStatus.Committed)
                                                throw new InvalidOperationException("Revit không chấp nhận LineLoad.");
                                            created++;
                                        }
                                        catch (Exception ex)
                                        {
                                            if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                            skipped.Add("Dầm " + beam.Id.Value + " / tường " + wall.Wall.Id.Value + ": " + ex.Message);
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex) { skipped.Add("Dầm " + beam.Id.Value + ": " + ex.Message); }
                    }
                    if (created == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Tải đường tường", "Không gán được LineLoad nào.\n" + string.Join("\n", skipped.Take(10)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận tải đường.");
                }
                string report = "Đã gán " + created + " tải đường theo phương -Z.\nLoad Case: " + options.Case.Name +
                    "\nTải hoàn thiện bổ sung: " + options.AdditionalKnPerSquareMeter.ToString("0.###") + " kN/m²";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " trường hợp:\n" + string.Join("\n", skipped.Take(10));
                TaskDialog.Show("KStruct - Tải đường tường", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Tải đường tường", ex.Message);
                return Result.Failed;
            }
        }

        private static WallData CalculateWallData(Document doc, Wall wall)
        {
            Line location = (wall.Location as LocationCurve)?.Curve as Line;
            if (location == null) throw new InvalidOperationException("Chỉ hỗ trợ tường thẳng.");
            WallType type = doc.GetElement(wall.GetTypeId()) as WallType;
            CompoundStructure structure = type?.GetCompoundStructure();
            if (structure == null) throw new InvalidOperationException("WallType không có CompoundStructure.");
            double kgPerSquareMeter = 0;
            foreach (CompoundStructureLayer layer in structure.GetLayers())
            {
                if (layer.Width <= 1e-9) continue;
                Material material = doc.GetElement(layer.MaterialId) as Material;
                double density = material == null ? 0 : CalculateFloorDeadLoadCommand.GetDensityKgPerCubicFoot(doc, material);
                if (density <= 0) throw new InvalidOperationException("Material '" + (material?.Name ?? "?") + "' chưa khai báo density.");
                double thicknessMeters = UnitUtils.ConvertFromInternalUnits(layer.Width, UnitTypeId.Meters);
                kgPerSquareMeter += density / 0.028316846592 * thicknessMeters;
            }
            if (kgPerSquareMeter <= 0) throw new InvalidOperationException("Không tính được khối lượng lớp tường.");
            BoundingBoxXYZ box = wall.get_BoundingBox(null);
            if (box == null) throw new InvalidOperationException("Không đọc được chiều cao tường.");
            double minZ = GetWorldCorners(box).Min(p => p.Z);
            double maxZ = GetWorldCorners(box).Max(p => p.Z);
            double heightMeters = UnitUtils.ConvertFromInternalUnits(maxZ - minZ, UnitTypeId.Meters);
            if (heightMeters <= 0) throw new InvalidOperationException("Chiều cao tường không hợp lệ.");
            XYZ axisVector = location.GetEndPoint(1) - location.GetEndPoint(0);
            XYZ planAxis = new XYZ(axisVector.X, axisVector.Y, 0).Normalize();
            var result = new WallData
            {
                Wall = wall,
                Location = location,
                HeightMeters = heightMeters,
                SelfWeightKnPerSquareMeter = kgPerSquareMeter * 9.80665 / 1000.0,
                BaseZ = minZ,
                TopZ = maxZ,
                PlanAxis = planAxis
            };
            foreach (ElementId insertId in wall.FindInserts(true, true, true, true))
            {
                Element insert = doc.GetElement(insertId);
                BoundingBoxXYZ insertBox = insert?.get_BoundingBox(null);
                if (insertBox == null) continue;
                List<XYZ> points = GetWorldCorners(insertBox);
                double alongStart = points.Min(p => (p - location.GetEndPoint(0)).DotProduct(planAxis));
                double alongEnd = points.Max(p => (p - location.GetEndPoint(0)).DotProduct(planAxis));
                double bottom = Math.Max(minZ, points.Min(p => p.Z));
                double top = Math.Min(maxZ, points.Max(p => p.Z));
                if (alongEnd > alongStart && top > bottom)
                    result.Openings.Add(new OpeningData { Start = alongStart, End = alongEnd, BottomZ = bottom, TopZ = top });
            }
            return result;
        }

        private static double GetOpeningHeightAt(WallData wall, double station)
        {
            var intervals = wall.Openings.Where(o => station >= o.Start && station <= o.End)
                .Select(o => new { Bottom = o.BottomZ, Top = o.TopZ }).OrderBy(o => o.Bottom).ToList();
            if (intervals.Count == 0) return 0;
            double total = 0, start = intervals[0].Bottom, end = intervals[0].Top;
            for (int i = 1; i < intervals.Count; i++)
            {
                if (intervals[i].Bottom <= end) end = Math.Max(end, intervals[i].Top);
                else { total += end - start; start = intervals[i].Bottom; end = intervals[i].Top; }
            }
            return total + end - start;
        }

        private static bool IsAlignedWithBeam(Line wall, XYZ beamStart, XYZ beamAxis, double tolerance)
        {
            XYZ wallVector = wall.GetEndPoint(1) - wall.GetEndPoint(0);
            XYZ wallPlan = new XYZ(wallVector.X, wallVector.Y, 0);
            if (wallPlan.GetLength() <= 1e-8 || Math.Abs(wallPlan.Normalize().DotProduct(beamAxis)) < 0.99) return false;
            foreach (XYZ point in new[] { wall.GetEndPoint(0), wall.GetEndPoint(1) })
            {
                XYZ delta = new XYZ(point.X - beamStart.X, point.Y - beamStart.Y, 0);
                if (Math.Abs(delta.X * beamAxis.Y - delta.Y * beamAxis.X) > tolerance) return false;
            }
            return true;
        }

        private static List<XYZ> GetWorldCorners(BoundingBoxXYZ box)
        {
            var corners = new List<XYZ>();
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                        corners.Add(box.Transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X,
                            y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z)));
            return corners;
        }

        private static LoadOptions ChooseOptions(IntPtr owner, IList<WallData> walls, IList<LoadCase> cases, IList<LineLoadType> types)
        {
            var window = new Window
            {
                Title = "Tính tải đường từ tường - KStruct",
                Width = 460,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock
            {
                Text = "Tải tự trọng tính từ các lớp vật liệu và chiều cao tường; phần lỗ mở được khấu trừ theo hộp bao phần tử. Chỉ gán lên dầm analytical thẳng song song và gần trùng trục với tường.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            root.Children.Add(new TextBlock { Text = "Tường được tính:" });
            root.Children.Add(new TextBlock
            {
                Text = string.Join("\n", walls.Take(8).Select(w => "Wall " + w.Wall.Id.Value + ": " + w.SelfWeightKnPerSquareMeter.ToString("0.###") + " kN/m² × " + w.HeightMeters.ToString("0.###") + " m")),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 8)
            });
            root.Children.Add(new TextBlock { Text = "Load Case:" });
            var caseCombo = new System.Windows.Controls.ComboBox { ItemsSource = cases, DisplayMemberPath = "Name", SelectedIndex = 0, MinWidth = 420 };
            root.Children.Add(caseCombo);
            root.Children.Add(new TextBlock { Text = "Line Load Type:", Margin = new Thickness(0, 7, 0, 2) });
            var typeCombo = new System.Windows.Controls.ComboBox { ItemsSource = types, DisplayMemberPath = "Name", SelectedIndex = 0, MinWidth = 420 };
            root.Children.Add(typeCombo);
            root.Children.Add(new TextBlock { Text = "Tải hoàn thiện bổ sung trên diện tích tường (kN/m²):", Margin = new Thickness(0, 7, 0, 2) });
            var additionalBox = new System.Windows.Controls.TextBox { Text = "0", MinWidth = 420 };
            root.Children.Add(additionalBox);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "Gán tải", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (s, e) =>
            {
                if (caseCombo.SelectedItem == null || typeCombo.SelectedItem == null ||
                    !double.TryParse(additionalBox.Text, out double value) || value < 0)
                {
                    TaskDialog.Show("KStruct - Tải đường tường", "Chọn Load Case/Line Load Type và nhập tải hoàn thiện không âm.");
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
            return new LoadOptions
            {
                Case = (LoadCase)caseCombo.SelectedItem,
                Type = (LineLoadType)typeCombo.SelectedItem,
                AdditionalKnPerSquareMeter = double.Parse(additionalBox.Text)
            };
        }
    }
}
