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

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CreatePurlinsFromRoofCommand : IExternalCommand
    {
        private sealed class RoofFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) { return element is RoofBase; }
            public bool AllowReference(Reference reference, XYZ position) { return false; }
        }

        private sealed class TypeChoice
        {
            public FamilySymbol Symbol;
            public string Label;
            public TypeChoice(FamilySymbol symbol) { Symbol = symbol; Label = symbol.FamilyName + " : " + symbol.Name; }
        }

        private sealed class LayerControls
        {
            public CheckBox Enabled;
            public System.Windows.Controls.ComboBox Type;
            public System.Windows.Controls.ComboBox Direction;
            public System.Windows.Controls.TextBox Spacing;
            public System.Windows.Controls.TextBox Offset;
        }

        private sealed class Settings
        {
            public List<LayerSettings> Layers;
            public bool CreateBeamSystems;
        }

        private sealed class LayerSettings
        {
            public FamilySymbol Symbol;
            public double SpacingMm;
            public double OffsetMm;
            public bool AlongLongDirection;
        }

        private sealed class FaceRun
        {
            public XYZ StartA, EndA, StartB, EndB, Normal;
            public double Span;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                var roofs = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<RoofBase>().Distinct().ToList();
                if (roofs.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new RoofFilter(), "Chọn mái để tạo xà gồ");
                    roofs = picked.Select(r => doc.GetElement(r)).OfType<RoofBase>().Distinct().ToList();
                }
                if (roofs.Count == 0) return Result.Cancelled;

                List<FamilySymbol> symbols = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming)
                    .OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().OrderBy(s => s.FamilyName).ThenBy(s => s.Name).ToList();
                if (symbols.Count == 0) throw new InvalidOperationException("Project chưa có Structural Framing FamilySymbol.");
                Settings settings = ChooseSettings(data.Application.MainWindowHandle, symbols);
                if (settings == null) return Result.Cancelled;

                int created = 0;
                var skipped = new List<string>();
                using (var tx = new Transaction(doc, "KStruct - Tạo xà gồ từ mái"))
                {
                    tx.Start();
                    foreach (FamilySymbol symbol in settings.Layers.Select(x => x.Symbol).Distinct())
                        if (!symbol.IsActive) symbol.Activate();
                    foreach (RoofBase roof in roofs)
                    {
                        List<Reference> topFaces;
                        try { topFaces = HostObjectUtils.GetTopFaces(roof).ToList(); }
                        catch (Exception ex) { skipped.Add("Mái " + roof.Id.Value + ": " + ex.Message); continue; }

                        foreach (Reference faceRef in topFaces)
                        {
                            using (var sub = new SubTransaction(doc))
                            {
                                sub.Start();
                                try
                                {
                                    Face face = roof.GetGeometryObjectFromReference(faceRef) as Face;
                                    Level level = FindLevel(doc, roof);
                                    int count = 0;
                                    foreach (LayerSettings layer in settings.Layers)
                                    {
                                        if (settings.CreateBeamSystems)
                                            count += CreateBeamSystem(doc, face, level, layer);
                                        else
                                        {
                                            FaceRun run = GetFaceRun(face, layer.AlongLongDirection);
                                            count += CreatePurlins(doc, run, level, layer);
                                        }
                                    }
                                    if (sub.Commit() != TransactionStatus.Committed)
                                        throw new InvalidOperationException("Revit không chấp nhận xà gồ trên mặt mái.");
                                    created += count;
                                }
                                catch (Exception ex)
                                {
                                    if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                    skipped.Add("Mái " + roof.Id.Value + ": " + ex.Message);
                                }
                            }
                        }
                    }
                    if (created == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Xà gồ mái", "Không tạo được xà gồ. BeamSystem hỗ trợ mặt phẳng có biên thẳng; tạo từng thanh riêng hỗ trợ mặt tứ giác.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận xà gồ mới.");
                }

                string report = "Đã tạo " + created + (settings.CreateBeamSystems ? " BeamSystem" : " xà gồ") + " trên " + roofs.Count + " mái.";
                if (skipped.Count > 0) report += "\nMặt mái bỏ qua: " + skipped.Count + "\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Xà gồ mái", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Xà gồ mái", ex.Message);
                return Result.Failed;
            }
        }

        private static FaceRun GetFaceRun(Face face, bool alongLongDirection)
        {
            PlanarFace planar = face as PlanarFace;
            if (planar == null || planar.FaceNormal.Z <= 0.05)
                throw new InvalidOperationException("Chỉ hỗ trợ mặt mái phẳng hướng lên.");
            CurveLoop outer = planar.GetEdgesAsCurveLoops().OrderByDescending(loop => loop.Sum(c => c.Length)).FirstOrDefault();
            if (outer == null) throw new InvalidOperationException("Không đọc được biên mặt mái.");
            List<Line> lines = outer.Select(c => c as Line).ToList();
            if (lines.Count != 4 || lines.Any(l => l == null))
                throw new InvalidOperationException("Mặt mái phải có biên bốn cạnh thẳng.");

            List<Tuple<int, int>> pairs = new List<Tuple<int, int>>();
            for (int i = 0; i < lines.Count; i++)
                for (int j = i + 1; j < lines.Count; j++)
                    if (Math.Abs(lines[i].Direction.Normalize().DotProduct(lines[j].Direction.Normalize())) > 0.995)
                        pairs.Add(Tuple.Create(i, j));
            List<Tuple<int, int>> orderedPairs = pairs.OrderByDescending(p => lines[p.Item1].Length + lines[p.Item2].Length).ToList();
            var pair = orderedPairs.Count == 0 ? null : orderedPairs[Math.Min(alongLongDirection ? 0 : 1, orderedPairs.Count - 1)];
            if (pair == null) throw new InvalidOperationException("Không tìm thấy hai cạnh song song để chạy xà gồ.");

            Line a = lines[pair.Item1], b = lines[pair.Item2];
            XYZ a0 = a.GetEndPoint(0), a1 = a.GetEndPoint(1);
            XYZ b0 = b.GetEndPoint(0), b1 = b.GetEndPoint(1);
            if (a0.DistanceTo(b1) + a1.DistanceTo(b0) < a0.DistanceTo(b0) + a1.DistanceTo(b1))
            {
                XYZ temp = b0; b0 = b1; b1 = temp;
            }
            double span0 = a0.DistanceTo(b0), span1 = a1.DistanceTo(b1);
            if (span0 < 1e-4 || span1 < 1e-4) throw new InvalidOperationException("Khoảng cách cạnh mái không hợp lệ.");
            return new FaceRun { StartA = a0, EndA = a1, StartB = b0, EndB = b1,
                Span = (span0 + span1) / 2.0, Normal = planar.FaceNormal.Normalize() };
        }

        private static int CreatePurlins(Document doc, FaceRun run, Level level, LayerSettings settings)
        {
            double spacing = UnitUtils.ConvertToInternalUnits(settings.SpacingMm, UnitTypeId.Millimeters);
            double offset = UnitUtils.ConvertToInternalUnits(settings.OffsetMm, UnitTypeId.Millimeters);
            int count = Math.Max(1, (int)Math.Ceiling(run.Span / spacing) - 1);
            if (count > 500) throw new InvalidOperationException("Khoảng cách tạo ra hơn 500 hàng xà gồ trên một mặt mái.");
            int created = 0;
            for (int i = 1; i <= count; i++)
            {
                double t = i / (count + 1.0);
                XYZ a = run.StartA + (run.StartB - run.StartA) * t - run.Normal * offset;
                XYZ b = run.EndA + (run.EndB - run.EndA) * t - run.Normal * offset;
                if (a.DistanceTo(b) < doc.Application.ShortCurveTolerance) continue;
                FamilyInstance beam = doc.Create.NewFamilyInstance(Line.CreateBound(a, b), settings.Symbol, level, StructuralType.Beam);
                if (beam == null) throw new InvalidOperationException("Revit không tạo được xà gồ.");
                created++;
            }
            return created;
        }

        private static int CreateBeamSystem(Document doc, Face face, Level level, LayerSettings settings)
        {
            PlanarFace planar = face as PlanarFace;
            if (planar == null || planar.FaceNormal.Z <= 0.05)
                throw new InvalidOperationException("BeamSystem chỉ hỗ trợ mặt mái phẳng hướng lên.");
            List<CurveLoop> loops = planar.GetEdgesAsCurveLoops().ToList();
            CurveLoop outer = loops.OrderByDescending(loop => loop.Sum(c => c.Length)).FirstOrDefault();
            if (outer == null) throw new InvalidOperationException("Không đọc được biên mặt mái.");
            if (loops.Count != 1) throw new InvalidOperationException("BeamSystem chưa hỗ trợ mặt mái có lỗ mở.");
            if (outer.Any(curve => !(curve is Line)))
                throw new InvalidOperationException("Biên BeamSystem hiện yêu cầu các cạnh thẳng.");
            double offset = UnitUtils.ConvertToInternalUnits(settings.OffsetMm, UnitTypeId.Millimeters);
            XYZ normal = planar.FaceNormal.Normalize();
            XYZ translationVector = -normal * offset;
            List<Curve> profile = outer.Select(curve => curve.CreateTransformed(Transform.CreateTranslation(translationVector))).ToList();
            Line longest = outer.OfType<Line>().OrderByDescending(line => line.Length).FirstOrDefault();
            if (longest == null) throw new InvalidOperationException("Không tìm được cạnh thẳng để xác định hướng dầm.");
            XYZ longDirection = longest.Direction.Normalize();
            XYZ direction = settings.AlongLongDirection ? longDirection : normal.CrossProduct(longDirection).Normalize();
            BeamSystem system = BeamSystem.Create(doc, profile, level, direction, true);
            if (system == null) throw new InvalidOperationException("Revit không tạo được BeamSystem từ mặt mái.");
            system.BeamType = settings.Symbol;
            double spacing = UnitUtils.ConvertToInternalUnits(settings.SpacingMm, UnitTypeId.Millimeters);
            system.LayoutRule = new LayoutRuleFixedDistance(spacing, BeamSystemJustifyType.Center);
            ICollection<ElementId> beamIds = system.GetBeamIds();
            if (beamIds == null || beamIds.Count == 0)
                throw new InvalidOperationException("BeamSystem đã tạo nhưng Revit không sinh ra thanh dầm nào.");
            return beamIds.Count;
        }

        private static Level FindLevel(Document doc, RoofBase roof)
        {
            Level level = doc.GetElement(roof.LevelId) as Level;
            if (level != null) return level;
            BoundingBoxXYZ box = roof.get_BoundingBox(null);
            if (box == null) throw new InvalidOperationException("Không đọc được cao độ mái.");
            return new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => Math.Abs(l.Elevation - box.Min.Z)).FirstOrDefault();
        }

        private static Settings ChooseSettings(IntPtr owner, IList<FamilySymbol> symbols)
        {
            List<LayerSettings> result = null;
            List<TypeChoice> choices = symbols.Select(s => new TypeChoice(s)).ToList();
            var window = new Window
            {
                Title = "Tạo hệ dầm mái - KStruct", Width = 500, SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock
            {
                Text = "Chọn tối đa ba lớp Structural Framing, phương chạy và khoảng cách tối đa cho từng lớp.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            var createSystems = new CheckBox
            {
                Content = "Tạo BeamSystem Revit (bỏ chọn để tạo từng thanh Structural Framing)",
                IsChecked = true,
                Margin = new Thickness(0, 0, 0, 8)
            };
            root.Children.Add(createSystems);
            var controls = new List<LayerControls>();
            for (int i = 0; i < 3; i++)
            {
                int layerNumber = i + 1;
                var group = new GroupBox { Header = "Lớp " + layerNumber, Margin = new Thickness(0, 0, 0, 7), Padding = new Thickness(8) };
                var panel = new StackPanel();
                var enabled = new CheckBox { Content = "Tạo lớp " + layerNumber, IsChecked = i == 0, Margin = new Thickness(0, 0, 0, 4) };
                var typeCombo = new System.Windows.Controls.ComboBox
                {
                    MinWidth = 450, MinHeight = 24, DisplayMemberPath = "Label", ItemsSource = choices, SelectedIndex = 0
                };
                panel.Children.Add(typeCombo);
                panel.Children.Add(new TextBlock { Text = "Phương chạy:", Margin = new Thickness(0, 4, 0, 2) });
                var direction = new System.Windows.Controls.ComboBox
                {
                    MinWidth = 450, MinHeight = 24,
                    ItemsSource = new[] { "Theo cạnh dài của mặt mái", "Theo cạnh ngắn của mặt mái" },
                    SelectedIndex = 0
                };
                panel.Children.Add(direction);
                var spacing = AddInput(panel, "Khoảng cách tối đa (mm):", i == 0 ? "1000" : (i == 1 ? "1500" : "2000"));
                var offset = AddInput(panel, "Lùi dưới mặt mái (mm):", (50 + i * 50).ToString());
                panel.Children.Insert(0, enabled);
                group.Content = panel;
                root.Children.Add(group);
                Action updateEnabled = () =>
                {
                    bool active = enabled.IsChecked == true;
                    typeCombo.IsEnabled = active;
                    direction.IsEnabled = active;
                    spacing.IsEnabled = active;
                    offset.IsEnabled = active;
                };
                enabled.Checked += (s, e) => updateEnabled();
                enabled.Unchecked += (s, e) => updateEnabled();
                updateEnabled();
                controls.Add(new LayerControls { Enabled = enabled, Type = typeCombo, Direction = direction, Spacing = spacing, Offset = offset });
            }
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var ok = new Button { Content = "Tạo hệ dầm", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) =>
            {
                List<LayerSettings> selectedLayers = new List<LayerSettings>();
                bool invalid = false;
                foreach (LayerControls layer in controls.Where(x => x.Enabled.IsChecked == true))
                {
                    double spacingMm, offsetMm;
                    if (layer.Type.SelectedItem == null || !double.TryParse(layer.Spacing.Text, out spacingMm) || spacingMm <= 0 ||
                        !double.TryParse(layer.Offset.Text, out offsetMm) || offsetMm < 0)
                    {
                        invalid = true;
                        break;
                    }
                    selectedLayers.Add(new LayerSettings
                    {
                        Symbol = ((TypeChoice)layer.Type.SelectedItem).Symbol,
                        SpacingMm = spacingMm,
                        OffsetMm = offsetMm,
                        AlongLongDirection = layer.Direction.SelectedIndex == 0
                    });
                }
                if (invalid || selectedLayers.Count == 0)
                {
                    TaskDialog.Show("KStruct - Hệ dầm mái", "Bật ít nhất một lớp; mỗi lớp cần loại dầm, khoảng cách dương và độ lùi không âm.");
                    return;
                }
                result = selectedLayers;
                window.DialogResult = true;
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            if (window.ShowDialog() != true) return null;
            return new Settings { Layers = result, CreateBeamSystems = createSystems.IsChecked == true };
        }

        private static System.Windows.Controls.TextBox AddInput(System.Windows.Controls.Panel root, string label, string value)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 5, 0, 2) });
            var box = new System.Windows.Controls.TextBox { MinWidth = 390, MinHeight = 22, Text = value };
            root.Children.Add(box);
            return box;
        }
    }
}
