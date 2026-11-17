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

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class AlignFloorToBeamCommand : IExternalCommand
    {
        private sealed class FloorBeamFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => IsFloor(element) || IsBeam(element);
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class AlignOption
        {
            public string Label { get; private set; }
            public bool FloorTop { get; private set; }
            public bool BeamTop { get; private set; }
            public AlignOption(string label, bool floorTop, bool beamTop)
            { Label = label; FloorTop = floorTop; BeamTop = beamTop; }
        }

        private sealed class Settings
        {
            public AlignOption Option;
            public double OffsetMm;
            public bool UsePickedAnchors;
        }

        private sealed class ElementReferenceFilter : ISelectionFilter
        {
            private readonly HashSet<ElementId> _allowed;
            public ElementReferenceFilter(IEnumerable<ElementId> allowed) { _allowed = new HashSet<ElementId>(allowed); }
            public bool AllowElement(Element element) => element != null && _allowed.Contains(element.Id);
            public bool AllowReference(Reference reference, XYZ position) => reference != null && _allowed.Contains(reference.ElementId);
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Element> picked = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .Where(e => e != null && (IsFloor(e) || IsBeam(e))).Distinct().ToList();
                if (!picked.Any(IsFloor) || !picked.Any(IsBeam))
                {
                    IList<Reference> references = ui.Selection.PickObjects(ObjectType.Element, new FloorBeamFilter(),
                        "Chọn sàn và dầm kết cấu cần căn cao độ");
                    picked = references.Select(r => doc.GetElement(r)).Where(e => e != null &&
                        (IsFloor(e) || IsBeam(e))).Distinct().ToList();
                }
                List<Floor> floors = picked.OfType<Floor>().ToList();
                List<FamilyInstance> beams = picked.OfType<FamilyInstance>().Where(IsBeam).ToList();
                if (floors.Count == 0 || beams.Count == 0)
                {
                    TaskDialog.Show("KStruct - Căn sàn theo dầm", "Cần chọn ít nhất một Floor và một dầm Structural Framing.");
                    return Result.Cancelled;
                }

                Settings settings = ShowSettings(data.Application.MainWindowHandle);
                if (settings == null) return Result.Cancelled;
                var anchors = new Dictionary<ElementId, Tuple<double, double>>();
                if (settings.UsePickedAnchors)
                {
                    foreach (Floor floor in floors)
                    {
                        Reference floorRef = ui.Selection.PickObject(ObjectType.PointOnElement,
                            new ElementReferenceFilter(new[] { floor.Id }),
                            "Chọn điểm neo trên mặt sàn " + floor.Id.Value + " (chọn đúng mặt/biên cần căn)");
                        Reference beamRef = ui.Selection.PickObject(ObjectType.PointOnElement,
                            new ElementReferenceFilter(beams.Select(b => b.Id)),
                            "Chọn điểm neo tương ứng trên dầm cần căn cho sàn " + floor.Id.Value);
                        if (floorRef.GlobalPoint == null || beamRef.GlobalPoint == null)
                            throw new InvalidOperationException("Revit không trả về tọa độ điểm neo đã chọn.");
                        anchors[floor.Id] = Tuple.Create(floorRef.GlobalPoint.Z, beamRef.GlobalPoint.Z);
                    }
                }
                double offset = UnitUtils.ConvertToInternalUnits(settings.OffsetMm, UnitTypeId.Millimeters);
                int aligned = 0;
                List<string> skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Căn sàn theo dầm"))
                {
                    tx.Start();
                    foreach (Floor floor in floors)
                    {
                        try
                        {
                            BoundingBoxXYZ floorBox = floor.get_BoundingBox(null);
                            if (floorBox == null) throw new InvalidOperationException("Không đọc được bounding box sàn.");
                            XYZ center = (floorBox.Min + floorBox.Max) * 0.5;
                            double floorZ, beamZ;
                            if (settings.UsePickedAnchors)
                            {
                                Tuple<double, double> anchor = anchors[floor.Id];
                                floorZ = anchor.Item1;
                                beamZ = anchor.Item2;
                            }
                            else
                            {
                                FamilyInstance beam = beams.Select(b => new { Beam = b, Box = b.get_BoundingBox(null) })
                                    .Where(x => x.Box != null && OverlapsInPlan(floorBox, x.Box))
                                    .Select(x => new { x.Beam, Distance = HorizontalDistance(center, x.Beam) })
                                    .OrderBy(x => x.Distance).Select(x => x.Beam).FirstOrDefault();
                                if (beam == null)
                                    throw new InvalidOperationException("Không tìm thấy dầm đã chọn giao với vùng sàn trong mặt bằng.");
                                BoundingBoxXYZ beamBox = beam.get_BoundingBox(null);
                                floorZ = settings.Option.FloorTop ? floorBox.Max.Z : floorBox.Min.Z;
                                beamZ = settings.Option.BeamTop ? beamBox.Max.Z : beamBox.Min.Z;
                            }
                            Parameter elevation = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                            if (elevation == null || elevation.IsReadOnly)
                                throw new InvalidOperationException("Không thể sửa tham số Height Offset From Level của sàn.");
                            elevation.Set(elevation.AsDouble() + beamZ + offset - floorZ);
                            aligned++;
                        }
                        catch (Exception ex) { skipped.Add("Sàn " + floor.Id.Value + ": " + ex.Message); }
                    }
                    if (aligned == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Căn sàn theo dầm", "Không căn được sàn nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi cao độ sàn.");
                }

                string report = settings.UsePickedAnchors
                    ? "Đã căn " + aligned + "/" + floors.Count + " sàn theo cặp điểm neo đã chọn."
                    : "Đã căn " + aligned + "/" + floors.Count + " sàn theo dầm gần tâm vùng sàn.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " sàn:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Căn sàn theo dầm", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Căn sàn theo dầm", ex.Message);
                return Result.Failed;
            }
        }

        private static Settings ShowSettings(IntPtr owner)
        {
            Settings result = null;
            var options = new List<AlignOption>
            {
                new AlignOption("Mặt trên sàn = mặt trên dầm", true, true),
                new AlignOption("Mặt dưới sàn = mặt dưới dầm", false, false),
                new AlignOption("Mặt dưới sàn = mặt trên dầm", false, true),
                new AlignOption("Mặt trên sàn = mặt dưới dầm", true, false)
            };
            var window = new Window
            {
                Title = "Căn cao độ sàn theo dầm - KStruct", Width = 430, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(14) };
            root.Children.Add(new TextBlock { Text = "Chọn hai mặt cần trùng cao độ:", Margin = new Thickness(0, 0, 0, 5) });
            var combo = new System.Windows.Controls.ComboBox
            {
                MinWidth = 390, MinHeight = 25, ItemsSource = options, SelectedIndex = 0, DisplayMemberPath = "Label"
            };
            root.Children.Add(combo);
            var anchors = new CheckBox
            {
                Content = new TextBlock
                {
                    Text = "Chọn cặp điểm neo tương ứng trên sàn và dầm (theo cao độ điểm đã chọn)",
                    TextWrapping = TextWrapping.Wrap
                },
                Margin = new Thickness(0, 8, 0, 3)
            };
            root.Children.Add(anchors);
            anchors.Checked += (s, e) => combo.IsEnabled = false;
            anchors.Unchecked += (s, e) => combo.IsEnabled = true;
            root.Children.Add(new TextBlock { Text = "Độ bù cao độ (mm):", Margin = new Thickness(0, 8, 0, 3) });
            var offset = new System.Windows.Controls.TextBox { MinWidth = 390, MinHeight = 24, Text = "0" };
            root.Children.Add(offset);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var apply = new Button { Content = "Căn sàn", MinWidth = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 75, IsCancel = true };
            apply.Click += (s, e) =>
            {
                double value;
                if ((anchors.IsChecked != true && combo.SelectedItem == null) || !double.TryParse(offset.Text, out value))
                {
                    TaskDialog.Show("KStruct - Căn sàn theo dầm", "Giá trị độ bù phải là số hợp lệ.");
                    return;
                }
                result = new Settings
                {
                    Option = combo.SelectedItem as AlignOption,
                    OffsetMm = value,
                    UsePickedAnchors = anchors.IsChecked == true
                };
                window.DialogResult = true;
            };
            buttons.Children.Add(apply);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            return window.ShowDialog() == true ? result : null;
        }

        private static bool IsFloor(Element element) => element is Floor;
        private static bool IsBeam(Element element) => element is FamilyInstance instance && instance.Category != null &&
            instance.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFraming;

        private static bool OverlapsInPlan(BoundingBoxXYZ a, BoundingBoxXYZ b)
        {
            return Math.Min(a.Max.X, b.Max.X) >= Math.Max(a.Min.X, b.Min.X) &&
                Math.Min(a.Max.Y, b.Max.Y) >= Math.Max(a.Min.Y, b.Min.Y);
        }

        private static double HorizontalDistance(XYZ point, FamilyInstance beam)
        {
            LocationCurve location = beam.Location as LocationCurve;
            if (location?.Curve == null) return double.MaxValue;
            IList<XYZ> samples = new List<XYZ>
            {
                location.Curve.GetEndPoint(0), location.Curve.Evaluate(0.5, true), location.Curve.GetEndPoint(1)
            };
            return samples.Min(p => new XYZ(p.X - point.X, p.Y - point.Y, 0).GetLength());
        }
    }
}
