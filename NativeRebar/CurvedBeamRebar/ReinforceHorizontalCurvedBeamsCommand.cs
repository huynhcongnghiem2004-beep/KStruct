using System;
using System.Collections.Generic;
using System.Linq;
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
    public sealed class ReinforceHorizontalCurvedBeamsCommand : IExternalCommand
    {
        private sealed class CurvedBeamFilter : ISelectionFilter
        {
            public bool AllowElement(Element element)
            {
                FamilyInstance beam = element as FamilyInstance;
                LocationCurve location = beam == null ? null : beam.Location as LocationCurve;
                return IsFraming(beam) && location != null && location.Curve is Arc;
            }
            public bool AllowReference(Reference reference, XYZ position) { return false; }
        }

        private sealed class SpacingOptions
        {
            public double EndSpacingMm, MidSpacingMm, EndZoneMm;
            public RebarHookType StartHook, EndHook;
        }

        private sealed class CurvedOptions
        {
            public RebarBarType TopType, BottomType, TopLayer2Type, BottomLayer2Type, TieType;
            public int TopCount, BottomCount, TopLayer2Count, BottomLayer2Count;
            public double CoverMm, TieSpacingMm, LayerSpacingMm, HookXTopMm, HookYTopMm, HookXBottomMm, HookYBottomMm;
            public bool Unobscured, SolidIn3D;
        }

        private sealed class BarChoice
        {
            public RebarBarType Type { get; private set; }
            public string Display { get; private set; }
            public BarChoice(RebarBarType type)
            {
                Type = type;
                double diameter = type.BarNominalDiameter;
                Display = type.Name + " | Ø" + UnitUtils.ConvertFromInternalUnits(diameter, UnitTypeId.Millimeters).ToString("0.##") + " mm";
            }
        }
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<FamilyInstance> beams = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<FamilyInstance>()
                    .Where(IsHorizontalCircularBeam).Distinct().ToList();
                if (beams.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new CurvedBeamFilter(),
                        "Chọn dầm Structural Framing cong tròn nằm ngang");
                    beams = picked.Select(x => doc.GetElement(x)).OfType<FamilyInstance>().Where(IsHorizontalCircularBeam).Distinct().ToList();
                }
                if (beams.Count == 0) return Result.Cancelled;
                List<RebarBarType> types = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType))
                    .Cast<RebarBarType>().OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (types.Count == 0) throw new InvalidOperationException("Project chưa có RebarBarType.");
                CurvedOptions options = ChooseBarOptions(data.Application.MainWindowHandle, types);
                if (options == null) return Result.Cancelled;
                List<RebarHookType> hookTypes = new FilteredElementCollector(doc).OfClass(typeof(RebarHookType)).Cast<RebarHookType>().OrderBy(h => h.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                SpacingOptions spacingOptions = ChooseSpacingOptions(data.Application.MainWindowHandle, options.TieSpacingMm, hookTypes);
                if (spacingOptions == null) return Result.Cancelled;

                int completed = 0, mainBars = 0, ties = 0, replacedBars = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Cốt thép dầm cong"))
                {
                    tx.Start();
                    foreach (FamilyInstance beam in beams)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                List<ElementId> previousManagedBars = FindManagedBars(doc, beam.Id);
                                int tieCount = ReinforceCurvedBeam(doc, beam, options, spacingOptions);
                                if (tieCount <= 0) throw new InvalidOperationException("Không tạo được bộ thép mới; bộ KStruct cũ được giữ nguyên.");
                                if (previousManagedBars.Count > 0) doc.Delete(previousManagedBars);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận cốt thép dầm cong.");
                                completed++;
                                replacedBars += previousManagedBars.Count;
                                mainBars += options.TopCount + options.BottomCount + options.TopLayer2Count + options.BottomLayer2Count;
                                ties += tieCount;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Dầm " + beam.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (completed == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Cốt thép dầm cong", "Không gia cường được dầm nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi.");
                }
                string report = "Đã gia cường " + completed + "/" + beams.Count + " dầm cung ngang. Thép dọc: " + mainBars + "; đai: " + ties + ".";
                if (replacedBars > 0) report += " Đã thay " + replacedBars + " thanh KStruct cũ sau khi tạo bộ mới thành công.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " dầm:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Cốt thép dầm cong", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Cốt thép dầm cong", ex.Message);
                return Result.Failed;
            }
        }

        private static int ReinforceCurvedBeam(Document doc, FamilyInstance beam,
            CurvedOptions options, SpacingOptions spacingOptions)
        {
            LocationCurve location = beam.Location as LocationCurve;
            Arc path = location == null ? null : location.Curve as Arc;
            if (path == null) throw new InvalidOperationException("LocationCurve không phải Arc.");
            if (Math.Abs(Math.Abs(path.Normal.Normalize().DotProduct(XYZ.BasisZ)) - 1.0) > 1e-4)
                throw new InvalidOperationException("Chỉ hỗ trợ cung nằm ngang.");
            BoundingBoxXYZ box = beam.get_BoundingBox(null);
            if (box == null) throw new InvalidOperationException("Không đọc được bounding box dầm.");
            double width = ParameterValue(beam, BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH);
            double height = ParameterValue(beam, BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT);
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("Không đọc được bề rộng/chiều cao tiết diện chữ nhật từ tham số kết cấu.");

            double cover = UnitUtils.ConvertToInternalUnits(options.CoverMm, UnitTypeId.Millimeters);
            double mainDia = Math.Max(options.TopType.BarNominalDiameter, options.BottomType.BarNominalDiameter);
            if (options.TopLayer2Count > 0) mainDia = Math.Max(mainDia, options.TopLayer2Type.BarNominalDiameter);
            if (options.BottomLayer2Count > 0) mainDia = Math.Max(mainDia, options.BottomLayer2Type.BarNominalDiameter);
            double tieDia = options.TieType.BarNominalDiameter;
            double innerHalfW = width / 2.0 - cover - tieDia / 2.0;
            double innerHalfH = height / 2.0 - cover - tieDia / 2.0;
            if (innerHalfW <= mainDia / 2.0 || innerHalfH <= mainDia / 2.0)
                throw new InvalidOperationException("Cover và đường kính thép không vừa tiết diện.");

            double centerZ = (box.Min.Z + box.Max.Z) / 2.0;
            double centerOffsetZ = centerZ - path.Center.Z;
            double layerSpacing = UnitUtils.ConvertToInternalUnits(options.LayerSpacingMm, UnitTypeId.Millimeters);
            if (options.TopLayer2Count > 0 && layerSpacing < (options.TopType.BarNominalDiameter + options.TopLayer2Type.BarNominalDiameter) / 2.0)
                throw new InvalidOperationException("Khoảng cách tâm hai lớp thép phía trên nhỏ hơn tổng bán kính hai thanh.");
            if (options.BottomLayer2Count > 0 && layerSpacing < (options.BottomType.BarNominalDiameter + options.BottomLayer2Type.BarNominalDiameter) / 2.0)
                throw new InvalidOperationException("Khoảng cách tâm hai lớp thép phía dưới nhỏ hơn tổng bán kính hai thanh.");
            CreateLongitudinalRow(doc, beam, path, options.BottomType, options.BottomCount, width, height,
                centerOffsetZ, cover, tieDia, false, 0, spacingOptions.StartHook, spacingOptions.EndHook,
                options.Unobscured, options.SolidIn3D, options.HookXBottomMm, options.HookYBottomMm);
            CreateLongitudinalRow(doc, beam, path, options.TopType, options.TopCount, width, height,
                centerOffsetZ, cover, tieDia, true, 0, spacingOptions.StartHook, spacingOptions.EndHook,
                options.Unobscured, options.SolidIn3D, options.HookXTopMm, options.HookYTopMm);
            if (options.BottomLayer2Count > 0)
                CreateLongitudinalRow(doc, beam, path, options.BottomLayer2Type, options.BottomLayer2Count, width, height,
                    centerOffsetZ, cover, tieDia, false, layerSpacing, spacingOptions.StartHook, spacingOptions.EndHook,
                    options.Unobscured, options.SolidIn3D, options.HookXBottomMm, options.HookYBottomMm);
            if (options.TopLayer2Count > 0)
                CreateLongitudinalRow(doc, beam, path, options.TopLayer2Type, options.TopLayer2Count, width, height,
                    centerOffsetZ, cover, tieDia, true, layerSpacing, spacingOptions.StartHook, spacingOptions.EndHook,
                    options.Unobscured, options.SolidIn3D, options.HookXTopMm, options.HookYTopMm);

            double startParam = path.GetEndParameter(0), endParam = path.GetEndParameter(1);
            double sweep = endParam - startParam;
            if (sweep <= 0) throw new InvalidOperationException("Tham số cung không hợp lệ.");
            double length = path.Radius * sweep;
            double tieInset = cover + tieDia / 2.0;
            if (length <= tieInset * 2.0) throw new InvalidOperationException("Cung dầm quá ngắn để đặt đai.");
            List<double> stations = BuildTieStations(tieInset, length - tieInset,
                UnitUtils.ConvertToInternalUnits(spacingOptions.EndZoneMm, UnitTypeId.Millimeters),
                UnitUtils.ConvertToInternalUnits(spacingOptions.EndSpacingMm, UnitTypeId.Millimeters),
                UnitUtils.ConvertToInternalUnits(spacingOptions.MidSpacingMm, UnitTypeId.Millimeters));
            int tieCount = stations.Count;
            if (tieCount < 2) throw new InvalidOperationException("Không đủ chiều dài để bố trí đai.");
            if (tieCount > 1000) throw new InvalidOperationException("Bước đai tạo ra hơn 1000 đai cho một dầm.");
            for (int i = 0; i < tieCount; i++)
            {
                double t = stations[i] / length;
                XYZ point = path.Evaluate(t, true);
                XYZ tangent = path.ComputeDerivatives(t, true).BasisX.Normalize();
                XYZ lateral = XYZ.BasisZ.CrossProduct(tangent).Normalize();
                XYZ center = new XYZ(point.X, point.Y, centerZ);
                XYZ p0 = center + lateral * -innerHalfW + XYZ.BasisZ * -innerHalfH;
                XYZ p1 = center + lateral * innerHalfW + XYZ.BasisZ * -innerHalfH;
                XYZ p2 = center + lateral * innerHalfW + XYZ.BasisZ * innerHalfH;
                XYZ p3 = center + lateral * -innerHalfW + XYZ.BasisZ * innerHalfH;
                var curves = new List<Curve>
                {
                    Line.CreateBound(p0, p1), Line.CreateBound(p1, p2),
                    Line.CreateBound(p2, p3), Line.CreateBound(p3, p0)
                };
                Rebar tie = ExistingProjectRebar.Create(doc, RebarStyle.StirrupTie, options.TieType,
                    null, null, beam, tangent, curves, RebarHookOrientation.Left, RebarHookOrientation.Right, "Đai dầm cong " + beam.Id.Value);
                if (tie == null) throw new InvalidOperationException("Revit không tạo được đai số " + (i + 1) + ".");
                ApplyRebarDisplay(tie, doc.ActiveView, options.Unobscured, options.SolidIn3D);
                SetPartition(tie, beam.Id);
            }
            return tieCount;
        }

        private static List<double> BuildTieStations(double start, double end, double endZone, double endSpacing, double midSpacing)
        {
            if (end <= start || endSpacing <= 0 || midSpacing <= 0 || endZone < 0)
                throw new InvalidOperationException("Khoảng cách đai theo vùng không hợp lệ.");
            endZone = Math.Min(endZone, (end - start) / 2.0);
            var stations = new List<double>();
            AddStations(stations, start, Math.Min(end, start + endZone), endSpacing);
            double middleStart = Math.Min(end, start + endZone);
            double middleEnd = Math.Max(middleStart, end - endZone);
            AddStations(stations, middleStart, middleEnd, midSpacing);
            AddStations(stations, middleEnd, end, endSpacing);
            return stations;
        }

        private static void CreateLongitudinalRow(Document doc, FamilyInstance beam, Arc path, RebarBarType type,
            int count, double width, double height, double centerOffsetZ, double cover, double tieDia,
            bool top, double layerSpacing, RebarHookType startHook, RebarHookType endHook, bool unobscured,
            bool solidIn3D, double hookXmm, double hookYmm)
        {
            if (count <= 0) return;
            double dia = type.BarNominalDiameter;
            double inset = cover + tieDia + dia / 2.0;
            double halfWidth = width / 2.0 - inset;
            if (halfWidth <= doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Không còn bề rộng để đặt thép dọc loại " + type.Name + ".");
            double elevation = top
                ? centerOffsetZ + height / 2.0 - inset - layerSpacing
                : centerOffsetZ - height / 2.0 + inset + layerSpacing;
            double allowableCenter = height / 2.0 - cover - tieDia / 2.0 - dia / 2.0;
            if (Math.Abs(elevation) > allowableCenter + doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Chiều cao tiết diện không đủ chỗ cho lớp thép " + type.Name + ".");
            CreateLongitudinalBars(doc, beam, path, type, count, -halfWidth, halfWidth,
                elevation, inset, startHook, endHook, unobscured, solidIn3D, doc.ActiveView, hookXmm, hookYmm);
        }

        private static void AddStations(List<double> stations, double start, double end, double spacing)
        {
            if (end < start) return;
            if (stations.Count == 0 || Math.Abs(stations[stations.Count - 1] - start) > 1e-7) stations.Add(start);
            double current = start;
            while (current + spacing < end - 1e-7)
            {
                current += spacing;
                if (Math.Abs(stations[stations.Count - 1] - current) > 1e-7) stations.Add(current);
            }
            if (Math.Abs(stations[stations.Count - 1] - end) > 1e-7) stations.Add(end);
        }

        private sealed class HookChoice
        {
            public RebarHookType Type { get; private set; }
            public string Display { get; private set; }
            public HookChoice(RebarHookType type) { Type = type; Display = type == null ? "Không dùng móc" : type.Name; }
        }

        private static CurvedOptions ChooseBarOptions(IntPtr owner, IList<RebarBarType> types)
        {
            List<BarChoice> choices = types.Select(t => new BarChoice(t)).ToList();
            var window = new Window { Title = "Cốt thép dầm cong (cung ngang) - KStruct", Width = 500, Height = 760,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false };
            var root = new System.Windows.Controls.StackPanel { Margin = new Thickness(12) };
            var topType = AddBarType(root, "Thép lớp chính phía trên:", choices, 0);
            var topCount = AddInput(root, "Số thanh lớp chính phía trên:", "2");
            var topLayer2Type = AddBarType(root, "Thép lớp 2 phía trên:", choices, 0);
            var topLayer2Count = AddInput(root, "Số thanh lớp 2 phía trên (0 = bỏ):", "0");
            var bottomType = AddBarType(root, "Thép lớp chính phía dưới:", choices, 0);
            var bottomCount = AddInput(root, "Số thanh lớp chính phía dưới:", "2");
            var bottomLayer2Type = AddBarType(root, "Thép lớp 2 phía dưới:", choices, 0);
            var bottomLayer2Count = AddInput(root, "Số thanh lớp 2 phía dưới (0 = bỏ):", "0");
            var tieType = AddBarType(root, "Loại thép đai:", choices, Math.Min(1, choices.Count - 1));
            var cover = AddInput(root, "Cover (mm):", "30");
            var tieSpacing = AddInput(root, "Bước đai giữa mặc định (mm):", "150");
            var layerSpacing = AddInput(root, "Khoảng cách tâm lớp chính đến lớp 2 (mm):", "50");
            var hookXTop = AddInput(root, "Móc tùy chỉnh lớp trên - X theo tiếp tuyến (mm, 0 = không dùng):", "0");
            var hookYTop = AddInput(root, "Móc tùy chỉnh lớp trên - Y hướng tâm (mm, 0 = không dùng):", "0");
            var hookXBottom = AddInput(root, "Móc tùy chỉnh lớp dưới - X theo tiếp tuyến (mm, 0 = không dùng):", "0");
            var hookYBottom = AddInput(root, "Móc tùy chỉnh lớp dưới - Y hướng tâm (mm, 0 = không dùng):", "0");
            var buttons = new System.Windows.Controls.StackPanel { Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var unobscured = new CheckBox { Content = "Hiển thị thép không bị che khuất trong view hiện hành", IsChecked = true,
                Margin = new Thickness(0, 8, 0, 0) };
            root.Children.Add(unobscured);
            var solidIn3D = new CheckBox { Content = "Hiển thị thép đặc trong 3D (Revit 2024: chuyển toàn view sang Fine)", IsChecked = true,
                Margin = new Thickness(0, 4, 0, 0) };
            root.Children.Add(solidIn3D);
            var ok = new Button { Content = "Tiếp tục", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (s, e) =>
            {
                int topN, bottomN, top2N, bottom2N;
                double coverMm, tieMm, layerMm, hookXTopMm, hookYTopMm, hookXBottomMm, hookYBottomMm;
                bool validCounts = int.TryParse(topCount.Text, out topN) && topN >= 1 && topN <= 20 &&
                    int.TryParse(bottomCount.Text, out bottomN) && bottomN >= 1 && bottomN <= 20 &&
                    int.TryParse(topLayer2Count.Text, out top2N) && top2N >= 0 && top2N <= 20 &&
                    int.TryParse(bottomLayer2Count.Text, out bottom2N) && bottom2N >= 0 && bottom2N <= 20;
                bool validNumbers = double.TryParse(cover.Text, out coverMm) && coverMm > 0 &&
                    double.TryParse(tieSpacing.Text, out tieMm) && tieMm > 0 &&
                    double.TryParse(layerSpacing.Text, out layerMm) && layerMm > 0 &&
                    double.TryParse(hookXTop.Text, out hookXTopMm) && hookXTopMm >= 0 &&
                    double.TryParse(hookYTop.Text, out hookYTopMm) && hookYTopMm >= 0 &&
                    double.TryParse(hookXBottom.Text, out hookXBottomMm) && hookXBottomMm >= 0 &&
                    double.TryParse(hookYBottom.Text, out hookYBottomMm) && hookYBottomMm >= 0;
                if (!validCounts || !validNumbers || topType.SelectedItem == null || topLayer2Type.SelectedItem == null ||
                    bottomType.SelectedItem == null || bottomLayer2Type.SelectedItem == null || tieType.SelectedItem == null)
                {
                    TaskDialog.Show("KStruct - Dầm cong", "Số thanh lớp chính phải từ 1 đến 20; số thanh lớp 2 có thể bằng 0; cover, bước đai và khoảng cách lớp phải lớn hơn 0; kích thước móc phải không âm.");
                    return;
                }
                window.DialogResult = true;
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            new WindowInteropHelper(window).Owner = owner;
            if (window.ShowDialog() != true) return null;
            return new CurvedOptions
            {
                TopType = ((BarChoice)topType.SelectedItem).Type,
                TopCount = int.Parse(topCount.Text),
                TopLayer2Type = ((BarChoice)topLayer2Type.SelectedItem).Type,
                TopLayer2Count = int.Parse(topLayer2Count.Text),
                BottomType = ((BarChoice)bottomType.SelectedItem).Type,
                BottomCount = int.Parse(bottomCount.Text),
                BottomLayer2Type = ((BarChoice)bottomLayer2Type.SelectedItem).Type,
                BottomLayer2Count = int.Parse(bottomLayer2Count.Text),
                TieType = ((BarChoice)tieType.SelectedItem).Type,
                CoverMm = double.Parse(cover.Text),
                TieSpacingMm = double.Parse(tieSpacing.Text),
                LayerSpacingMm = double.Parse(layerSpacing.Text),
                HookXTopMm = double.Parse(hookXTop.Text),
                HookYTopMm = double.Parse(hookYTop.Text),
                HookXBottomMm = double.Parse(hookXBottom.Text),
                HookYBottomMm = double.Parse(hookYBottom.Text),
                Unobscured = unobscured.IsChecked == true,
                SolidIn3D = solidIn3D.IsChecked == true
            };
        }

        private static System.Windows.Controls.ComboBox AddBarType(System.Windows.Controls.Panel root, string label,
            IList<BarChoice> choices, int selectedIndex)
        {
            root.Children.Add(new System.Windows.Controls.TextBlock { Text = label, Margin = new Thickness(0, 4, 0, 2) });
            var combo = new System.Windows.Controls.ComboBox { MinWidth = 420, MinHeight = 22,
                DisplayMemberPath = "Display", ItemsSource = choices, SelectedIndex = selectedIndex };
            root.Children.Add(combo);
            return combo;
        }

        private static SpacingOptions ChooseSpacingOptions(IntPtr owner, double defaultMidSpacing, IList<RebarHookType> hookTypes)
        {
            var window = new System.Windows.Window { Title = "Đai dầm cong theo vùng - KStruct", Width = 390, Height = 260,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false };
            var root = new System.Windows.Controls.StackPanel { Margin = new Thickness(12) };
            System.Windows.Controls.TextBox endSpacing = AddInput(root, "Khoảng cách đai vùng đầu/cuối (mm):", "100");
            System.Windows.Controls.TextBox midSpacing = AddInput(root, "Khoảng cách đai vùng giữa (mm):", defaultMidSpacing.ToString(System.Globalization.CultureInfo.InvariantCulture));
            System.Windows.Controls.TextBox zone = AddInput(root, "Chiều dài từng vùng đầu/cuối (mm, 0 = bỏ vùng):", "600");
            var choices = new List<HookChoice> { new HookChoice(null) };
            choices.AddRange(hookTypes.Select(h => new HookChoice(h)));
            System.Windows.Controls.ComboBox startHook = AddHookChoice(root, "Móc đầu thanh dọc:", choices);
            System.Windows.Controls.ComboBox endHook = AddHookChoice(root, "Móc cuối thanh dọc:", choices);
            var buttons = new System.Windows.Controls.StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var ok = new System.Windows.Controls.Button { Content = "Tiếp tục", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new System.Windows.Controls.Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (s, e) =>
            {
                double endValue, midValue, zoneValue;
                if (!double.TryParse(endSpacing.Text, out endValue) || endValue <= 0 ||
                    !double.TryParse(midSpacing.Text, out midValue) || midValue <= 0 ||
                    !double.TryParse(zone.Text, out zoneValue) || zoneValue < 0)
                { TaskDialog.Show("KStruct - Dầm cong", "Khoảng cách phải lớn hơn 0; chiều dài vùng phải không âm."); return; }
                window.DialogResult = true;
            };
            buttons.Children.Add(ok); buttons.Children.Add(cancel); root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            if (window.ShowDialog() != true) return null;
            return new SpacingOptions { EndSpacingMm = double.Parse(endSpacing.Text), MidSpacingMm = double.Parse(midSpacing.Text), EndZoneMm = double.Parse(zone.Text), StartHook = ((HookChoice)startHook.SelectedItem).Type, EndHook = ((HookChoice)endHook.SelectedItem).Type };
        }

        private static System.Windows.Controls.ComboBox AddHookChoice(System.Windows.Controls.Panel root, string label, IList<HookChoice> choices)
        {
            root.Children.Add(new System.Windows.Controls.TextBlock { Text = label, Margin = new Thickness(0, 4, 0, 2) });
            var combo = new System.Windows.Controls.ComboBox { MinWidth = 340, MinHeight = 22, DisplayMemberPath = "Display", ItemsSource = choices, SelectedIndex = 0 };
            root.Children.Add(combo);
            return combo;
        }
        private static System.Windows.Controls.TextBox AddInput(System.Windows.Controls.Panel root, string label, string value)
        {
            root.Children.Add(new System.Windows.Controls.TextBlock { Text = label, Margin = new Thickness(0, 4, 0, 2) });
            var box = new System.Windows.Controls.TextBox { MinWidth = 340, MinHeight = 22, Text = value };
            root.Children.Add(box);
            return box;
        }
        private static void CreateLongitudinalBars(Document doc, FamilyInstance beam, Arc path, RebarBarType type,
            int count, double minW, double maxW, double elevationOffset, double inset, RebarHookType startHook, RebarHookType endHook,
            bool unobscured, bool solidIn3D, View view, double hookXmm, double hookYmm)
        {
            double start = path.GetEndParameter(0) + inset / path.Radius;
            double end = path.GetEndParameter(1) - inset / path.Radius;
            if (end <= start) throw new InvalidOperationException("Cung không đủ dài sau khi trừ cover và neo.");
            double radialSign = Math.Sign((path.Evaluate(0.5, true) - path.Center).Normalize()
                .DotProduct(XYZ.BasisZ.CrossProduct(path.ComputeDerivatives(0.5, true).BasisX.Normalize())));
            if (radialSign == 0) throw new InvalidOperationException("Không xác định được phương bán kính của cung.");
            for (int i = 0; i < count; i++)
            {
                double offset = count == 1 ? (minW + maxW) / 2.0 : minW + (maxW - minW) * i / (count - 1.0);
                double radius = path.Radius + radialSign * offset;
                if (radius <= 0) throw new InvalidOperationException("Offset thép dọc làm bán kính cung không hợp lệ.");
                XYZ center = new XYZ(path.Center.X, path.Center.Y, path.Center.Z + elevationOffset);
                Arc barCurve = Arc.Create(center, radius, start, end, path.XDirection, path.YDirection);
                bool customHook = hookXmm > 0 || hookYmm > 0;
                var curves = new List<Curve>();
                RebarHookType actualStartHook = customHook ? null : startHook;
                RebarHookType actualEndHook = customHook ? null : endHook;
                if (customHook)
                {
                    double hookX = UnitUtils.ConvertToInternalUnits(hookXmm, UnitTypeId.Millimeters);
                    double hookY = UnitUtils.ConvertToInternalUnits(hookYmm, UnitTypeId.Millimeters);
                    double startParameter = barCurve.GetEndParameter(0);
                    double endParameter = barCurve.GetEndParameter(1);
                    XYZ startPoint = barCurve.GetEndPoint(0);
                    XYZ endPoint = barCurve.GetEndPoint(1);
                    XYZ startTangent = path.ComputeDerivatives(startParameter, false).BasisX.Normalize();
                    XYZ endTangent = path.ComputeDerivatives(endParameter, false).BasisX.Normalize();
                    XYZ startRadial = (startPoint - center).Normalize();
                    XYZ endRadial = (endPoint - center).Normalize();
                    XYZ startCorner = startPoint - startTangent * hookX;
                    XYZ endCorner = endPoint + endTangent * hookX;
                    XYZ startTip = startCorner - startRadial * hookY;
                    XYZ endTip = endCorner - endRadial * hookY;
                    if (hookY > doc.Application.ShortCurveTolerance) curves.Add(Line.CreateBound(startTip, startCorner));
                    if (hookX > doc.Application.ShortCurveTolerance) curves.Add(Line.CreateBound(startCorner, startPoint));
                    curves.Add(barCurve);
                    if (hookX > doc.Application.ShortCurveTolerance) curves.Add(Line.CreateBound(endPoint, endCorner));
                    if (hookY > doc.Application.ShortCurveTolerance) curves.Add(Line.CreateBound(endCorner, endTip));
                }
                else curves.Add(barCurve);
                Rebar bar = ExistingProjectRebar.Create(doc, RebarStyle.Standard, type, actualStartHook, actualEndHook, beam,
                    path.Normal, curves, RebarHookOrientation.Left,
                    RebarHookOrientation.Right, "Thép dọc dầm cong " + beam.Id.Value);
                if (bar == null) throw new InvalidOperationException("Revit không tạo được thanh dọc.");
                ApplyRebarDisplay(bar, view, unobscured, solidIn3D);
                SetPartition(bar, beam.Id);
            }
        }

        private static void ApplyRebarDisplay(Rebar rebar, View view, bool unobscured, bool solidIn3D)
        {
            if (unobscured) rebar.SetUnobscuredInView(view, true);
            if (!solidIn3D) return;
            View3D view3D = view as View3D;
            if (view3D == null) return;

            System.Reflection.MethodInfo setSolid = typeof(Rebar).GetMethod("SetSolidInView",
                new[] { typeof(View3D), typeof(bool) });
            if (setSolid != null)
            {
                setSolid.Invoke(rebar, new object[] { view3D, true });
                return;
            }

            // Revit 2024 does not expose SetSolidInView. Fine detail is its supported
            // view-level control for displaying individual reinforcement geometry.
            if (view3D.DetailLevel != ViewDetailLevel.Fine) view3D.DetailLevel = ViewDetailLevel.Fine;
        }

        private static double ParameterValue(Element element, BuiltInParameter id)
        {
            Parameter parameter = element.get_Parameter(id);
            if ((parameter == null || !parameter.HasValue) && element is FamilyInstance instance && instance.Symbol != null)
                parameter = instance.Symbol.get_Parameter(id);
            if (parameter == null || !parameter.HasValue || parameter.StorageType != StorageType.Double) return 0;
            return parameter.AsDouble();
        }

        private static List<ElementId> FindManagedBars(Document doc, ElementId beamId)
        {
            string partitionName = "KSTR_CURVED_BEAM_" + beamId.Value;
            return new FilteredElementCollector(doc).OfClass(typeof(Rebar)).WhereElementIsNotElementType()
                .Cast<Rebar>().Where(bar => bar.GetHostId() == beamId)
                .Where(bar =>
                {
                    Parameter partition = bar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
                    return partition != null && partition.HasValue &&
                        string.Equals(partition.AsString(), partitionName, StringComparison.OrdinalIgnoreCase);
                }).Select(bar => bar.Id).ToList();
        }

        private static void SetPartition(Rebar rebar, ElementId beamId)
        {
            Parameter parameter = rebar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
            if (parameter != null && !parameter.IsReadOnly) parameter.Set("KSTR_CURVED_BEAM_" + beamId.Value);
        }

        private static bool IsFraming(FamilyInstance instance) => instance != null && instance.Category != null &&
            instance.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;

        private static bool IsHorizontalCircularBeam(FamilyInstance beam)
        {
            LocationCurve location = beam == null ? null : beam.Location as LocationCurve;
            Arc arc = location == null ? null : location.Curve as Arc;
            return IsFraming(beam) && arc != null &&
                Math.Abs(Math.Abs(arc.Normal.Normalize().DotProduct(XYZ.BasisZ)) - 1.0) <= 1e-4;
        }
    }
}



