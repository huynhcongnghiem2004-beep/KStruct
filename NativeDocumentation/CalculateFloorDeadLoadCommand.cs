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
    public sealed class CalculateFloorDeadLoadCommand : IExternalCommand
    {
        private sealed class FloorFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Floor;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class FloorLoadRow
        {
            public Floor Floor;
            public double SelfWeight;
            public string Description;
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
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new FloorFilter(),
                        "Chọn sàn cần tính và gán tải tĩnh bản thân");
                    floors = picked.Select(r => doc.GetElement(r)).OfType<Floor>().Distinct().ToList();
                }
                if (floors.Count == 0) return Result.Cancelled;
                var rows = new List<FloorLoadRow>();
                var skipped = new List<string>();
                foreach (Floor floor in floors)
                {
                    try { rows.Add(CalculateSelfWeight(doc, floor)); }
                    catch (Exception ex) { skipped.Add("Sàn " + floor.Id.Value + ": " + ex.Message); }
                }
                if (rows.Count == 0)
                {
                    TaskDialog.Show("KStruct - Tính tải sàn", "Không tính được tải tự trọng từ các sàn đã chọn.\n" + string.Join("\n", skipped.Take(8)));
                    return Result.Cancelled;
                }
                List<LoadCase> cases = new FilteredElementCollector(doc).OfClass(typeof(LoadCase)).Cast<LoadCase>()
                    .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                List<AreaLoadType> types = new FilteredElementCollector(doc).OfClass(typeof(AreaLoadType)).Cast<AreaLoadType>()
                    .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (cases.Count == 0) throw new InvalidOperationException("Project chưa có Structural Load Case.");
                if (types.Count == 0) throw new InvalidOperationException("Project chưa có Area Load Type.");
                if (!ChooseOptions(data.Application.MainWindowHandle, rows, cases, types, out LoadCase loadCase,
                    out AreaLoadType loadType, out double finishLoad)) return Result.Cancelled;

                int created = 0;
                using (Transaction tx = new Transaction(doc, "KStruct - Tính tải tĩnh sàn"))
                {
                    tx.Start();
                    foreach (FloorLoadRow row in rows)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                if (!AreaLoad.IsValidHostId(doc, row.Floor.Id))
                                    throw new InvalidOperationException("Floor này không được Revit chấp nhận làm host AreaLoad.");
                                double totalKnPerSquareMeter = row.SelfWeight + finishLoad;
                                double internalMagnitude = UnitUtils.ConvertToInternalUnits(totalKnPerSquareMeter, UnitTypeId.KilonewtonsPerSquareMeter);
                                AreaLoad load = AreaLoad.Create(doc, row.Floor.Id, new XYZ(0, 0, -internalMagnitude), loadType);
                                if (load == null) throw new InvalidOperationException("AreaLoad.Create trả về null.");
                                load.LoadCaseId = loadCase.Id;
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận AreaLoad.");
                                created++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Sàn " + row.Floor.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (created == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Tính tải sàn", "Không gán được tải cho sàn nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận tải sàn.");
                }
                string report = "Đã tính và gán tải tĩnh đứng cho " + created + "/" + rows.Count + " sàn.\nLoad Case: " + loadCase.Name +
                    "\nTải hoàn thiện bổ sung: " + finishLoad.ToString("0.###") + " kN/m²";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " sàn:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Tính tải sàn", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Tính tải sàn", ex.Message);
                return Result.Failed;
            }
        }

        private static FloorLoadRow CalculateSelfWeight(Document doc, Floor floor)
        {
            FloorType type = doc.GetElement(floor.GetTypeId()) as FloorType;
            CompoundStructure structure = type?.GetCompoundStructure();
            if (structure == null) throw new InvalidOperationException("FloorType không có CompoundStructure để tính tự trọng.");
            double kgPerSquareMeter = 0;
            var details = new List<string>();
            foreach (CompoundStructureLayer layer in structure.GetLayers())
            {
                if (layer.Width <= 1e-9) continue;
                Material material = doc.GetElement(layer.MaterialId) as Material;
                if (material == null) throw new InvalidOperationException("Một lớp cấu tạo không có Material.");
                double densityKgPerCubicFoot = GetDensityKgPerCubicFoot(doc, material);
                if (densityKgPerCubicFoot <= 0)
                    throw new InvalidOperationException("Material '" + material.Name + "' chưa khai báo Structural Asset Density.");
                double widthMeters = UnitUtils.ConvertFromInternalUnits(layer.Width, UnitTypeId.Meters);
                double densityKgPerCubicMeter = densityKgPerCubicFoot / 0.028316846592;
                double layerKgPerSquareMeter = densityKgPerCubicMeter * widthMeters;
                kgPerSquareMeter += layerKgPerSquareMeter;
                details.Add(material.Name + " " + layerKgPerSquareMeter.ToString("0.#") + " kg/m²");
            }
            if (kgPerSquareMeter <= 0) throw new InvalidOperationException("Không tính được tự trọng từ các lớp FloorType.");
            double knPerSquareMeter = kgPerSquareMeter * 9.80665 / 1000.0;
            return new FloorLoadRow
            {
                Floor = floor,
                SelfWeight = knPerSquareMeter,
                Description = type.Name + " — " + string.Join("; ", details)
            };
        }

        internal static double GetDensityKgPerCubicFoot(Document doc, Material material)
        {
            if (material.StructuralAssetId != ElementId.InvalidElementId)
            {
                PropertySetElement set = doc.GetElement(material.StructuralAssetId) as PropertySetElement;
                StructuralAsset asset = set?.GetStructuralAsset();
                if (asset != null && asset.Density > 0) return asset.Density;
            }
            if (material.ThermalAssetId != ElementId.InvalidElementId)
            {
                PropertySetElement set = doc.GetElement(material.ThermalAssetId) as PropertySetElement;
                ThermalAsset asset = set?.GetThermalAsset();
                if (asset != null && asset.Density > 0) return asset.Density;
            }
            return 0;
        }

        private static bool ChooseOptions(IntPtr owner, IList<FloorLoadRow> rows, IList<LoadCase> cases,
            IList<AreaLoadType> types, out LoadCase loadCase, out AreaLoadType loadType, out double finishLoad)
        {
            loadCase = null;
            loadType = null;
            finishLoad = 0;
            var window = new Window
            {
                Title = "Tính tải tĩnh sàn - KStruct",
                Width = 550,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock
            {
                Text = "Tự trọng được tính từ chiều dày từng lớp và Density của Structural/Thermal Asset (kg/ft³). Tải hoàn thiện nhập thêm sẽ cộng vào tự trọng.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            root.Children.Add(new TextBlock { Text = "Sàn và tự trọng tính được:", Margin = new Thickness(0, 0, 0, 3) });
            var summary = new TextBlock
            {
                Text = string.Join("\n", rows.Take(8).Select(r => "Floor " + r.Floor.Id.Value + ": " + r.SelfWeight.ToString("0.###") + " kN/m² — " + r.Description)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            root.Children.Add(summary);
            if (rows.Count > 8) root.Children.Add(new TextBlock { Text = "… và " + (rows.Count - 8) + " sàn khác." });
            root.Children.Add(new TextBlock { Text = "Load Case:", Margin = new Thickness(0, 5, 0, 2) });
            var caseCombo = new System.Windows.Controls.ComboBox { ItemsSource = cases, DisplayMemberPath = "Name", SelectedIndex = 0, MinWidth = 510 };
            root.Children.Add(caseCombo);
            root.Children.Add(new TextBlock { Text = "Area Load Type:", Margin = new Thickness(0, 7, 0, 2) });
            var typeCombo = new System.Windows.Controls.ComboBox { ItemsSource = types, DisplayMemberPath = "Name", SelectedIndex = 0, MinWidth = 510 };
            root.Children.Add(typeCombo);
            root.Children.Add(new TextBlock { Text = "Tải hoàn thiện bổ sung (kN/m²):", Margin = new Thickness(0, 7, 0, 2) });
            var finishBox = new System.Windows.Controls.TextBox { Text = "0", MinWidth = 510 };
            root.Children.Add(finishBox);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "Gán tải", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (s, e) =>
            {
                double value;
                if (caseCombo.SelectedItem == null || typeCombo.SelectedItem == null ||
                    !double.TryParse(finishBox.Text, out value) || value < 0)
                {
                    TaskDialog.Show("KStruct - Tính tải sàn", "Chọn Load Case/Area Load Type và nhập tải hoàn thiện không âm.");
                    return;
                }
                window.DialogResult = true;
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            window.Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 720 };
            new WindowInteropHelper(window).Owner = owner;
            if (window.ShowDialog() != true) return false;
            loadCase = (LoadCase)caseCombo.SelectedItem;
            loadType = (AreaLoadType)typeCombo.SelectedItem;
            return double.TryParse(finishBox.Text, out finishLoad);
        }
    }
}
