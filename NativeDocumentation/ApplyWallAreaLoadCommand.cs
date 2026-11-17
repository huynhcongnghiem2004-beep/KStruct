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

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ApplyWallAreaLoadCommand : IExternalCommand
    {
        private sealed class WallFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Wall;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class LoadOptions
        {
            public LoadCase Case;
            public AreaLoadType Type;
            public double Magnitude;
            public bool Inward;
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
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new WallFilter(), "Chọn tường cần gán tải phân bố theo pháp tuyến");
                    walls = picked.Select(r => doc.GetElement(r)).OfType<Wall>().Distinct().ToList();
                }
                if (walls.Count == 0) return Result.Cancelled;
                List<LoadCase> cases = new FilteredElementCollector(doc).OfClass(typeof(LoadCase)).Cast<LoadCase>()
                    .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                List<AreaLoadType> types = new FilteredElementCollector(doc).OfClass(typeof(AreaLoadType)).Cast<AreaLoadType>()
                    .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (cases.Count == 0) throw new InvalidOperationException("Project chưa có Structural Load Case.");
                if (types.Count == 0) throw new InvalidOperationException("Project chưa có Area Load Type.");
                LoadOptions options = ChooseOptions(data.Application.MainWindowHandle, cases, types);
                if (options == null) return Result.Cancelled;
                double magnitude = UnitUtils.ConvertToInternalUnits(options.Magnitude, UnitTypeId.KilonewtonsPerSquareMeter);

                int created = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Tải phân bố tường"))
                {
                    tx.Start();
                    foreach (Wall wall in walls)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                if (!AreaLoad.IsValidHostId(doc, wall.Id))
                                    throw new InvalidOperationException("Revit không cho phép Wall này làm host AreaLoad.");
                                XYZ normal = wall.Orientation;
                                if (normal == null || Math.Abs(normal.Z) > 0.01 || normal.GetLength() < 1e-8)
                                    throw new InvalidOperationException("Chỉ hỗ trợ tường đứng có pháp tuyến ngang.");
                                normal = normal.Normalize() * (options.Inward ? -magnitude : magnitude);
                                AreaLoad load = AreaLoad.Create(doc, wall.Id, normal, options.Type);
                                if (load == null) throw new InvalidOperationException("AreaLoad.Create trả về null.");
                                load.LoadCaseId = options.Case.Id;
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận AreaLoad.");
                                created++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Tường " + wall.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (created == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Tải tường", "Không tạo được tải trên tường nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận tải tường.");
                }
                string direction = options.Inward ? "vào phía trong" : "ra phía ngoài";
                string report = "Đã gán tải " + options.Magnitude.ToString("0.###") + " kN/m² " + direction + " cho " + created + "/" + walls.Count + " tường.\nLoad Case: " + options.Case.Name;
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " tường:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Tải tường", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Tải tường", ex.Message);
                return Result.Failed;
            }
        }

        private static LoadOptions ChooseOptions(IntPtr owner, IList<LoadCase> cases, IList<AreaLoadType> types)
        {
            var window = new Window
            {
                Title = "Tải phân bố tường - KStruct",
                Width = 390,
                Height = 275,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock { Text = "Structural Load Case:", Margin = new Thickness(0, 0, 0, 3) });
            var caseCombo = new System.Windows.Controls.ComboBox { MinWidth = 350, MinHeight = 24, DisplayMemberPath = "Name", ItemsSource = cases, SelectedIndex = 0 };
            root.Children.Add(caseCombo);
            root.Children.Add(new TextBlock { Text = "Area Load Type:", Margin = new Thickness(0, 7, 0, 3) });
            var typeCombo = new System.Windows.Controls.ComboBox { MinWidth = 350, MinHeight = 24, DisplayMemberPath = "Name", ItemsSource = types, SelectedIndex = 0 };
            root.Children.Add(typeCombo);
            root.Children.Add(new TextBlock { Text = "Cường độ tải (kN/m²):", Margin = new Thickness(0, 7, 0, 3) });
            var magnitudeBox = new System.Windows.Controls.TextBox { MinWidth = 350, MinHeight = 22, Text = "1" };
            root.Children.Add(magnitudeBox);
            var inward = new CheckBox { Content = "Hướng lực vào phía trong tường", Margin = new Thickness(0, 6, 0, 0) };
            root.Children.Add(inward);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 9, 0, 0) };
            var ok = new Button { Content = "Gán tải", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) =>
            {
                double value;
                if (caseCombo.SelectedItem == null || typeCombo.SelectedItem == null ||
                    !double.TryParse(magnitudeBox.Text, out value) || value <= 0)
                {
                    TaskDialog.Show("KStruct - Tải tường", "Chọn Load Case/Type và nhập tải dương theo kN/m².");
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
                Type = (AreaLoadType)typeCombo.SelectedItem,
                Magnitude = double.Parse(magnitudeBox.Text),
                Inward = inward.IsChecked == true
            };
        }
    }
}
