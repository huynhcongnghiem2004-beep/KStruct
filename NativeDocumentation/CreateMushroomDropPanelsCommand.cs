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
    public sealed class CreateMushroomDropPanelsCommand : IExternalCommand
    {
        private sealed class FloorFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Floor;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class ColumnFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => IsColumn(element as FamilyInstance);
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class FloorTypeChoice
        {
            public FloorType Type { get; }
            public double ThicknessMm { get; }
            public string Label { get; }
            public FloorTypeChoice(FloorType type, double thickness)
            {
                Type = type;
                ThicknessMm = UnitUtils.ConvertFromInternalUnits(thickness, UnitTypeId.Millimeters);
                Label = type.Name + " | tổng dày " + ThicknessMm.ToString("0.##") + " mm";
            }
            public override string ToString() => Label;
        }

        private sealed class Options
        {
            public FloorType Type;
            public double OverhangXmm, OverhangYmm;
            public bool AlignToColumn;
        }

        private sealed class SlabFrame
        {
            public Level Level;
            public double TopElevation;
            public List<List<XYZ>> Loops;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Element> selected = ui.Selection.GetElementIds().Select(doc.GetElement).ToList();
                Floor floor = selected.OfType<Floor>().FirstOrDefault();
                if (floor == null)
                {
                    Reference pickedFloor = ui.Selection.PickObject(ObjectType.Element, new FloorFilter(), "Chọn sàn phẳng để tạo drop panel");
                    floor = doc.GetElement(pickedFloor) as Floor;
                }
                if (floor == null) return Result.Cancelled;
                List<FamilyInstance> columns = selected.OfType<FamilyInstance>().Where(IsColumn).Distinct().ToList();
                if (columns.Count == 0)
                    columns = ui.Selection.PickObjects(ObjectType.Element, new ColumnFilter(), "Chọn cột nằm trong sàn để tạo drop panel")
                        .Select(reference => doc.GetElement(reference)).OfType<FamilyInstance>().Where(IsColumn).Distinct().ToList();
                if (columns.Count == 0) return Result.Cancelled;

                SlabFrame frame = ReadSlabFrame(doc, floor);
                double baseThickness = GetThickness(doc.GetElement(floor.GetTypeId()) as FloorType);
                List<FloorTypeChoice> choices = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>()
                    .Select(type => new FloorTypeChoice(type, GetThickness(type)))
                    .Where(choice => choice.ThicknessMm > UnitUtils.ConvertFromInternalUnits(baseThickness, UnitTypeId.Millimeters) + 1.0)
                    .OrderBy(choice => choice.Type.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (choices.Count == 0)
                    throw new InvalidOperationException("Project chưa có FloorType dày hơn sàn hiện tại. Hãy tạo type drop panel dày hơn rồi chạy lại.");
                Options options = ChooseOptions(data.Application.MainWindowHandle, choices);
                if (options == null) return Result.Cancelled;

                int created = 0, skipped = 0;
                var details = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Drop panel nấm"))
                {
                    tx.Start();
                    foreach (FamilyInstance column in columns)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                CurveLoop profile = CreatePanelProfile(doc, column, frame, options);
                                Floor panel = Floor.Create(doc, new List<CurveLoop> { profile }, options.Type.Id, frame.Level.Id, true, null, 0);
                                if (panel == null) throw new InvalidOperationException("Revit không tạo được Floor drop panel.");
                                Parameter offset = panel.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                                if (offset != null && !offset.IsReadOnly) offset.Set(frame.TopElevation - frame.Level.Elevation);
                                Parameter mark = panel.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                                if (mark != null && !mark.IsReadOnly) mark.Set("KSTR_DP_" + column.Id.Value);
                                doc.Regenerate();
                                // The thick panel cuts the overlapping base slab so the local section remains thick.
                                JoinGeometryUtils.JoinGeometry(doc, panel, floor);
                                doc.Regenerate();
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận panel hoặc nối hình học.");
                                created++;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped++;
                                if (details.Count < 8) details.Add("Cột " + column.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (created == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Drop panel", "Không tạo được drop panel nào.\n" + string.Join("\n", details));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các drop panel.");
                }
                string report = "Đã tạo " + created + "/" + columns.Count + " drop panel, loại '" + options.Type.Name + "', nối với sàn '" + floor.Name + "'.";
                if (skipped > 0) report += "\nBỏ qua " + skipped + " cột:\n" + string.Join("\n", details);
                TaskDialog.Show("KStruct - Drop panel", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Drop panel", ex.Message);
                return Result.Failed;
            }
        }

        private static bool IsColumn(FamilyInstance instance)
        {
            return instance != null && instance.Category != null &&
                (instance.Category.Id.Value == (long)BuiltInCategory.OST_StructuralColumns ||
                 instance.Category.Id.Value == (long)BuiltInCategory.OST_Columns);
        }

        private static SlabFrame ReadSlabFrame(Document doc, Floor floor)
        {
            PlanarFace face = HostObjectUtils.GetTopFaces(floor)
                .Select(reference => floor.GetGeometryObjectFromReference(reference) as PlanarFace)
                .Where(candidate => candidate != null && candidate.FaceNormal.Normalize().DotProduct(XYZ.BasisZ) > 0.999)
                .OrderByDescending(candidate => candidate.Area).FirstOrDefault();
            if (face == null) throw new InvalidOperationException("Chỉ hỗ trợ sàn có mặt trên phẳng nằm ngang.");
            List<List<XYZ>> loops = face.GetEdgesAsCurveLoops().Select(loop => loop.SelectMany(curve => curve.Tessellate())
                .Aggregate(new List<XYZ>(), (points, point) =>
                {
                    if (points.Count == 0 || points[points.Count - 1].DistanceTo(point) > doc.Application.ShortCurveTolerance / 10.0)
                        points.Add(point);
                    return points;
                })).Where(points => points.Count >= 3).ToList();
            if (loops.Count == 0) throw new InvalidOperationException("Không đọc được đường bao sàn.");
            Level level = doc.GetElement(floor.LevelId) as Level;
            if (level == null) throw new InvalidOperationException("Không xác định được Level của sàn.");
            return new SlabFrame { Level = level, TopElevation = face.Origin.Z, Loops = loops };
        }

        private static double GetThickness(FloorType type)
        {
            CompoundStructure structure = type == null ? null : type.GetCompoundStructure();
            return structure == null ? 0 : structure.GetWidth();
        }

        private static CurveLoop CreatePanelProfile(Document doc, FamilyInstance column, SlabFrame slab, Options options)
        {
            BoundingBoxXYZ box = column.get_BoundingBox(null);
            if (box == null) throw new InvalidOperationException("Không đọc được kích thước cột.");
            XYZ center = (column.Location as LocationPoint)?.Point;
            if (center == null)
            {
                Transform transform = box.Transform ?? Transform.Identity;
                center = transform.OfPoint((box.Min + box.Max) / 2.0);
            }
            double angle = options.AlignToColumn ? (column.Location as LocationPoint)?.Rotation ?? 0 : 0;
            XYZ axisX = new XYZ(Math.Cos(angle), Math.Sin(angle), 0);
            XYZ axisY = new XYZ(-Math.Sin(angle), Math.Cos(angle), 0);
            double halfX, halfY;
            if (options.AlignToColumn && column.Location is LocationPoint)
            {
                Transform transform = box.Transform ?? Transform.Identity;
                List<XYZ> corners = new List<XYZ>();
                for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                    corners.Add(transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X,
                        y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z)));
                halfX = corners.Max(point => Math.Abs((point - center).DotProduct(axisX)));
                halfY = corners.Max(point => Math.Abs((point - center).DotProduct(axisY)));
            }
            else
            {
                List<XYZ> corners = WorldCorners(box);
                halfX = (corners.Max(point => point.X) - corners.Min(point => point.X)) / 2.0;
                halfY = (corners.Max(point => point.Y) - corners.Min(point => point.Y)) / 2.0;
                axisX = XYZ.BasisX; axisY = XYZ.BasisY;
            }
            halfX += UnitUtils.ConvertToInternalUnits(options.OverhangXmm, UnitTypeId.Millimeters);
            halfY += UnitUtils.ConvertToInternalUnits(options.OverhangYmm, UnitTypeId.Millimeters);
            if (halfX <= doc.Application.ShortCurveTolerance || halfY <= doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Kích thước drop panel không hợp lệ.");
            List<XYZ> corners2D = new List<XYZ>
            {
                center - axisX * halfX - axisY * halfY,
                center + axisX * halfX - axisY * halfY,
                center + axisX * halfX + axisY * halfY,
                center - axisX * halfX + axisY * halfY
            }.Select(point => new XYZ(point.X, point.Y, slab.TopElevation)).ToList();
            if (!PanelInsideSlab(slab.Loops, corners2D, UnitUtils.ConvertToInternalUnits(2, UnitTypeId.Millimeters)))
                throw new InvalidOperationException("Drop panel vượt biên sàn hoặc giao với lỗ mở.");
            var profile = new CurveLoop();
            for (int index = 0; index < corners2D.Count; index++)
                profile.Append(Line.CreateBound(corners2D[index], corners2D[(index + 1) % corners2D.Count]));
            IList<CurveLoop> profiles = new List<CurveLoop> { profile };
            if (!BoundaryValidation.IsValidHorizontalBoundary(profiles))
                throw new InvalidOperationException("Biên drop panel không hợp lệ theo Revit.");
            return profile;
        }

        private static bool PanelInsideSlab(IList<List<XYZ>> loops, IList<XYZ> corners, double tolerance)
        {
            for (int edge = 0; edge < corners.Count; edge++)
            {
                XYZ a = corners[edge], b = corners[(edge + 1) % corners.Count];
                for (int sample = 0; sample <= 12; sample++)
                {
                    XYZ point = a + (b - a) * (sample / 12.0);
                    if (!PointInside(loops, point.X, point.Y) || DistanceToEdges(loops, point.X, point.Y) < tolerance)
                        return false;
                }
            }
            return true;
        }

        private static bool PointInside(IList<List<XYZ>> loops, double x, double y)
        {
            bool inside = false;
            foreach (List<XYZ> loop in loops)
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
            {
                XYZ a = loop[i], b = loop[j];
                if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        private static double DistanceToEdges(IList<List<XYZ>> loops, double x, double y)
        {
            double minimum = double.MaxValue;
            foreach (List<XYZ> loop in loops)
            for (int i = 0; i < loop.Count; i++)
            {
                XYZ a = loop[i], b = loop[(i + 1) % loop.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y, length2 = dx * dx + dy * dy;
                double t = length2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((x - a.X) * dx + (y - a.Y) * dy) / length2));
                minimum = Math.Min(minimum, Math.Sqrt((x - (a.X + t * dx)) * (x - (a.X + t * dx)) + (y - (a.Y + t * dy)) * (y - (a.Y + t * dy))));
            }
            return minimum;
        }

        private static List<XYZ> WorldCorners(BoundingBoxXYZ box)
        {
            Transform transform = box.Transform ?? Transform.Identity;
            var result = new List<XYZ>();
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                result.Add(transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X,
                    y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z)));
            return result;
        }

        private static Options ChooseOptions(IntPtr owner, IList<FloorTypeChoice> choices)
        {
            var window = new Window
            {
                Title = "Tạo Mushroom Drop Panel - KStruct", Width = 450, Height = 310,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock { Text = "Chọn FloorType dày hơn sàn hiện tại cho phần panel:", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 5) });
            var typeBox = new System.Windows.Controls.ComboBox { ItemsSource = choices, SelectedIndex = 0, MinWidth = 420 };
            root.Children.Add(typeBox);
            System.Windows.Controls.TextBox x = AddInput(root, "Phần nhô thêm theo X (mm):", "300");
            System.Windows.Controls.TextBox y = AddInput(root, "Phần nhô thêm theo Y (mm):", "300");
            var align = new CheckBox { Content = "Xoay panel theo hướng cột", IsChecked = true, Margin = new Thickness(0, 8, 0, 5) };
            root.Children.Add(align);
            root.Children.Add(new TextBlock { Text = "Panel được đặt cùng cao độ mặt trên sàn và nối hình học với sàn đã chọn.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Tạo drop panel", Width = 112, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            buttons.Children.Add(ok); buttons.Children.Add(cancel); root.Children.Add(buttons);
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            ok.Click += (sender, args) =>
            {
                double overhangX, overhangY;
                if (typeBox.SelectedItem == null || !double.TryParse(x.Text, out overhangX) ||
                    !double.TryParse(y.Text, out overhangY) || overhangX < 0 || overhangY < 0)
                {
                    TaskDialog.Show("KStruct - Drop panel", "Chọn FloorType hợp lệ và nhập phần nhô không âm.");
                    return;
                }
                window.DialogResult = true;
            };
            if (window.ShowDialog() != true) return null;
            return new Options
            {
                Type = ((FloorTypeChoice)typeBox.SelectedItem).Type,
                OverhangXmm = double.Parse(x.Text), OverhangYmm = double.Parse(y.Text),
                AlignToColumn = align.IsChecked == true
            };
        }

        private static System.Windows.Controls.TextBox AddInput(System.Windows.Controls.Panel root, string label, string value)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 5, 0, 2) });
            var box = new System.Windows.Controls.TextBox { Text = value, MinWidth = 420, MinHeight = 22 };
            root.Children.Add(box);
            return box;
        }
    }
}
