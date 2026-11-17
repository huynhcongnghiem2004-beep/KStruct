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
    public sealed class CreateFloorFromPickedFaceCommand : IExternalCommand
    {
        private sealed class HorizontalFaceFilter : ISelectionFilter
        {
            private readonly Document _document;
            public HorizontalFaceFilter(Document document) { _document = document; }
            public bool AllowElement(Element element) => element != null;
            public bool AllowReference(Reference reference, XYZ position)
            {
                try
                {
                    PlanarFace face = _document.GetElement(reference.ElementId)?.GetGeometryObjectFromReference(reference) as PlanarFace;
                    return face != null && Math.Abs(Math.Abs(face.FaceNormal.Z) - 1.0) < 1e-6;
                }
                catch { return false; }
            }
        }

        private sealed class FloorOptions
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
                IList<Reference> references = ui.Selection.PickObjects(ObjectType.Face,
                    new HorizontalFaceFilter(doc), "Chọn các mặt phẳng ngang để tạo sàn");
                if (references == null || references.Count == 0) return Result.Cancelled;
                List<FloorType> types = new FilteredElementCollector(doc).OfClass(typeof(FloorType))
                    .Cast<FloorType>().OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                List<Level> levels = new FilteredElementCollector(doc).OfClass(typeof(Level))
                    .Cast<Level>().OrderBy(l => l.Elevation).ToList();
                if (types.Count == 0) throw new InvalidOperationException("Project chưa có FloorType.");
                if (levels.Count == 0) throw new InvalidOperationException("Project chưa có Level.");
                FloorOptions options = ChooseOptions(data.Application.MainWindowHandle, types, levels);
                if (options == null) return Result.Cancelled;

                int created = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Tạo sàn từ mặt phẳng"))
                {
                    tx.Start();
                    foreach (Reference reference in references)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                Element source = doc.GetElement(reference.ElementId);
                                PlanarFace face = source?.GetGeometryObjectFromReference(reference) as PlanarFace;
                                if (face == null || Math.Abs(Math.Abs(face.FaceNormal.Z) - 1.0) >= 1e-6)
                                    throw new InvalidOperationException("Face không còn là mặt phẳng ngang.");
                                IList<CurveLoop> profile = face.GetEdgesAsCurveLoops();
                                if (profile == null || profile.Count == 0 || !BoundaryValidation.IsValidHorizontalBoundary(profile))
                                    throw new InvalidOperationException("Đường biên face không tạo thành profile sàn hợp lệ.");
                                double faceElevation = face.Origin.Z;
                                Floor floor = Floor.Create(doc, profile, options.Type.Id, options.Level.Id, options.Structural, null, 0);
                                if (floor == null) throw new InvalidOperationException("Floor.Create trả về null.");
                                Parameter offset = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                                if (offset != null && !offset.IsReadOnly)
                                    offset.Set(faceElevation - options.Level.Elevation);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận sàn mới.");
                                created++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Face " + reference.ElementId.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (created == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Tạo sàn từ face", "Không tạo được sàn nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các sàn mới.");
                }

                string report = "Đã tạo " + created + "/" + references.Count + " sàn từ mặt phẳng được chọn.\nLoại: " + options.Type.Name + "\nLevel: " + options.Level.Name;
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " face:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Tạo sàn từ face", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Tạo sàn từ face", ex.Message);
                return Result.Failed;
            }
        }

        private static FloorOptions ChooseOptions(IntPtr owner, IList<FloorType> types, IList<Level> levels)
        {
            var window = new Window
            {
                Title = "Tạo sàn từ face ngang - KStruct",
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
                Text = "Chỉ nhận face phẳng ngang. Profile lỗ rỗng được giữ từ các edge loop của face.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            root.Children.Add(new TextBlock { Text = "Floor Type:" });
            var typeCombo = new System.Windows.Controls.ComboBox
            {
                MinWidth = 370,
                MinHeight = 24,
                ItemsSource = types,
                DisplayMemberPath = "Name",
                SelectedIndex = 0
            };
            root.Children.Add(typeCombo);
            root.Children.Add(new TextBlock { Text = "Level:" , Margin = new Thickness(0, 8, 0, 2) });
            var levelCombo = new System.Windows.Controls.ComboBox
            {
                MinWidth = 370,
                MinHeight = 24,
                ItemsSource = levels,
                DisplayMemberPath = "Name",
                SelectedItem = levels.OrderBy(l => Math.Abs(l.Elevation - 0)).FirstOrDefault()
            };
            root.Children.Add(levelCombo);
            var structuralCheck = new CheckBox { Content = "Sàn kết cấu", IsChecked = true, Margin = new Thickness(0, 8, 0, 2) };
            root.Children.Add(structuralCheck);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "Tạo sàn", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (s, e) =>
            {
                if (typeCombo.SelectedItem == null || levelCombo.SelectedItem == null)
                {
                    TaskDialog.Show("KStruct - Tạo sàn từ face", "Chọn Floor Type và Level.");
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
            return new FloorOptions
            {
                Type = (FloorType)typeCombo.SelectedItem,
                Level = (Level)levelCombo.SelectedItem,
                Structural = structuralCheck.IsChecked.GetValueOrDefault()
            };
        }
    }
}
