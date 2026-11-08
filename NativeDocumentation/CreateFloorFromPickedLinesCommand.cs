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
    public sealed class CreateFloorFromPickedLinesCommand : IExternalCommand
    {
        private sealed class ModelCurveFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is ModelCurve;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        internal sealed class Options
        {
            public FloorType Type;
            public Level Level;
            public bool Structural;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<ModelCurve> modelCurves = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<ModelCurve>().ToList();
                if (modelCurves.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new ModelCurveFilter(),
                        "Chọn Model Line/Arc tạo thành đường bao kín của sàn");
                    modelCurves = picked.Select(r => doc.GetElement(r)).OfType<ModelCurve>().ToList();
                }
                if (modelCurves.Count == 0) return Result.Cancelled;
                double tolerance = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Millimeters);
                List<CurveLoop> loops = BuildLoops(modelCurves.Select(c => c.GeometryCurve).ToList(), tolerance);
                if (!BoundaryValidation.IsValidHorizontalBoundary(loops))
                    throw new InvalidOperationException("Các đường không tạo thành profile sàn ngang hợp lệ; kiểm tra giao cắt, lỗ mở và hướng đường.");
                double profileElevation = loops[0].First().GetEndPoint(0).Z;

                List<FloorType> types = new FilteredElementCollector(doc).OfClass(typeof(FloorType))
                    .Cast<FloorType>().OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                List<Level> levels = new FilteredElementCollector(doc).OfClass(typeof(Level))
                    .Cast<Level>().OrderBy(l => l.Elevation).ToList();
                if (types.Count == 0) throw new InvalidOperationException("Project chưa có FloorType.");
                if (levels.Count == 0) throw new InvalidOperationException("Project chưa có Level.");
                Options options = ChooseOptions(data.Application.MainWindowHandle, types, levels, profileElevation, doc.ActiveView.GenLevel);
                if (options == null) return Result.Cancelled;

                using (Transaction tx = new Transaction(doc, "KStruct - Tạo sàn từ Model Lines"))
                {
                    tx.Start();
                    Floor floor = Floor.Create(doc, loops, options.Type.Id, options.Level.Id, options.Structural, null, 0);
                    if (floor == null) throw new InvalidOperationException("Floor.Create trả về null.");
                    Parameter offset = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                    if (offset != null && !offset.IsReadOnly) offset.Set(profileElevation - options.Level.Elevation);
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận sàn mới.");
                }

                TaskDialog.Show("KStruct - Tạo sàn từ đường", "Đã tạo sàn từ " + modelCurves.Count + " đường trong " + loops.Count + " vòng profile.\nLoại: " + options.Type.Name + "\nLevel: " + options.Level.Name);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Tạo sàn từ đường", ex.Message);
                return Result.Failed;
            }
        }

        private static List<CurveLoop> BuildLoops(IList<Curve> curves, double tolerance)
        {
            var pending = curves.Where(c => c != null && c.IsBound).ToList();
            if (pending.Count != curves.Count) throw new InvalidOperationException("Profile có đường không giới hạn.");
            if (pending.Count == 0) throw new InvalidOperationException("Không có đường hợp lệ.");
            var allPoints = pending.SelectMany(c => c.Tessellate()).ToList();
            double elevation = allPoints[0].Z;
            if (allPoints.Any(p => Math.Abs(p.Z - elevation) > tolerance))
                throw new InvalidOperationException("Các đường phải nằm trên cùng một cao độ ngang.");

            var result = new List<CurveLoop>();
            while (pending.Count > 0)
            {
                Curve seed = pending[0];
                pending.RemoveAt(0);
                XYZ start = seed.GetEndPoint(0);
                XYZ current = seed.GetEndPoint(1);
                var ordered = new List<Curve> { seed };
                int guard = curves.Count + 1;
                while (current.DistanceTo(start) > tolerance && guard-- > 0)
                {
                    int nextIndex = pending.FindIndex(c =>
                        c.GetEndPoint(0).DistanceTo(current) <= tolerance || c.GetEndPoint(1).DistanceTo(current) <= tolerance);
                    if (nextIndex < 0)
                        throw new InvalidOperationException("Có đầu đường hở; không thể ghép thành vòng kín.");
                    Curve next = pending[nextIndex];
                    pending.RemoveAt(nextIndex);
                    if (next.GetEndPoint(0).DistanceTo(current) <= tolerance)
                    {
                        ordered.Add(next);
                        current = next.GetEndPoint(1);
                    }
                    else
                    {
                        Curve reversed = next.CreateReversed();
                        ordered.Add(reversed);
                        current = reversed.GetEndPoint(1);
                    }
                }
                if (current.DistanceTo(start) > tolerance)
                    throw new InvalidOperationException("Không thể khép kín profile sàn.");
                var loop = new CurveLoop();
                foreach (Curve curve in ordered) loop.Append(curve);
                result.Add(loop);
            }
            return result;
        }

        internal static Options ChooseOptions(IntPtr owner, IList<FloorType> types, IList<Level> levels,
            double profileElevation, Level activeLevel)
        {
            var window = new Window
            {
                Title = "Tạo sàn từ Model Lines - KStruct",
                Width = 410,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock
            {
                Text = "Các Model Line/Arc phải tạo thành một hoặc nhiều vòng kín đồng cao độ. Có thể chọn thêm vòng trong làm lỗ mở.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            root.Children.Add(new TextBlock { Text = "Floor Type:" });
            var typeCombo = new System.Windows.Controls.ComboBox
            {
                MinWidth = 370, MinHeight = 24, ItemsSource = types, DisplayMemberPath = "Name", SelectedIndex = 0
            };
            root.Children.Add(typeCombo);
            root.Children.Add(new TextBlock { Text = "Level:", Margin = new Thickness(0, 8, 0, 2) });
            var levelCombo = new System.Windows.Controls.ComboBox
            {
                MinWidth = 370,
                MinHeight = 24,
                ItemsSource = levels,
                DisplayMemberPath = "Name",
                SelectedItem = activeLevel ?? levels.OrderBy(l => Math.Abs(l.Elevation - profileElevation)).FirstOrDefault()
            };
            root.Children.Add(levelCombo);
            var structural = new CheckBox { Content = "Sàn kết cấu", IsChecked = true, Margin = new Thickness(0, 8, 0, 2) };
            root.Children.Add(structural);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "Tạo sàn", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (s, e) =>
            {
                if (typeCombo.SelectedItem == null || levelCombo.SelectedItem == null)
                {
                    TaskDialog.Show("KStruct - Tạo sàn từ đường", "Chọn Floor Type và Level.");
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
            return new Options
            {
                Type = (FloorType)typeCombo.SelectedItem,
                Level = (Level)levelCombo.SelectedItem,
                Structural = structural.IsChecked.GetValueOrDefault()
            };
        }
    }
}
