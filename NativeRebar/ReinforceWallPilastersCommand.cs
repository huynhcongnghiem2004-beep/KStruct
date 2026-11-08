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
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfPanel = System.Windows.Controls.Panel;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ReinforceWallPilastersCommand : IExternalCommand
    {
        private sealed class WallFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is Wall;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }
        private sealed class TypeChoice
        {
            public RebarBarType Type;
            public string Display;
            public TypeChoice(RebarBarType type) { Type = type; Display = type.Name + " | Ø" + UnitUtils.ConvertFromInternalUnits(type.BarNominalDiameter, UnitTypeId.Millimeters).ToString("0.#") + " mm"; }
        }
        private sealed class Settings
        {
            public RebarBarType MainType, TieType;
            public int BarsPerFaceAtEachEnd;
            public double ZoneLengthMm, CoverMm, EndClearMm, TieSpacingMm;
            public bool Replace;
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
                    walls = ui.Selection.PickObjects(ObjectType.Element, new WallFilter(), "Chọn tường thẳng đứng cần bố trí thép bổ trụ")
                        .Select(reference => doc.GetElement(reference)).OfType<Wall>().Distinct().ToList();
                if (walls.Count == 0) return Result.Cancelled;
                List<RebarBarType> types = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
                    .OrderBy(type => type.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (types.Count == 0) throw new InvalidOperationException("Project chưa có RebarBarType.");
                Settings settings = ChooseOptions(data.Application.MainWindowHandle, types);
                if (settings == null) return Result.Cancelled;

                int completed = 0, barsCreated = 0, removed = 0;
                var skipped = new List<string>();
                using (Transaction transaction = new Transaction(doc, "KStruct - Thép bổ trụ tường"))
                {
                    transaction.Start();
                    foreach (Wall wall in walls)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                if (settings.Replace) removed += RemoveOwnedRebar(doc, wall.Id);
                                int count = CreateWallPilasterBars(doc, wall, settings);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận thép bổ trụ tường.");
                                completed++;
                                barsCreated += count;
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
                        transaction.RollBack();
                        TaskDialog.Show("KStruct - Thép bổ trụ tường", "Không tạo được thép cho tường nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các thanh thép bổ trụ.");
                }
                string report = "Đã tạo " + barsCreated + " thanh trên " + completed + "/" + walls.Count + " tường: thanh dọc tập trung ở hai đầu và thanh nối hai mặt theo bước đứng.";
                if (settings.Replace) report += " Đã thay " + removed + " thanh KStruct cùng nhóm.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " tường:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Thép bổ trụ tường", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Thép bổ trụ tường", ex.Message);
                return Result.Failed;
            }
        }

        private static int CreateWallPilasterBars(Document doc, Wall wall, Settings settings)
        {
            Line centerline = (wall.Location as LocationCurve)?.Curve as Line;
            BoundingBoxXYZ box = wall.get_BoundingBox(null);
            if (centerline == null || box == null)
                throw new InvalidOperationException("Chỉ hỗ trợ tường có tim thẳng và bounding box.");
            XYZ axis = centerline.Direction.Normalize();
            XYZ normal = wall.Orientation.Normalize();
            if (Math.Abs(axis.Z) > 1e-3 || Math.Abs(normal.Z) > 1e-3 || Math.Abs(axis.DotProduct(normal)) > 1e-3)
                throw new InvalidOperationException("Tường phải đứng, có tim ngang và hai mặt xác định.");

            double cover = UnitUtils.ConvertToInternalUnits(settings.CoverMm, UnitTypeId.Millimeters);
            double endClear = UnitUtils.ConvertToInternalUnits(settings.EndClearMm, UnitTypeId.Millimeters);
            double zone = UnitUtils.ConvertToInternalUnits(settings.ZoneLengthMm, UnitTypeId.Millimeters);
            double tieSpacing = UnitUtils.ConvertToInternalUnits(settings.TieSpacingMm, UnitTypeId.Millimeters);
            double dia = settings.MainType.BarNominalDiameter;
            double tieDia = settings.TieType.BarNominalDiameter;
            double faceOffset = wall.Width / 2.0 - cover - dia / 2.0;
            double z0 = box.Min.Z + cover + Math.Max(dia, tieDia) / 2.0;
            double z1 = box.Max.Z - cover - Math.Max(dia, tieDia) / 2.0;
            if (faceOffset <= 0 || z1 - z0 <= doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Bề dày/chiều cao tường không đủ cho cover và đường kính thép.");
            if (zone <= 2 * endClear || zone >= centerline.Length / 2.0)
                throw new InvalidOperationException("Chiều dài vùng bổ trụ phải lớn hơn hai lớp cover và nhỏ hơn nửa chiều dài tường.");

            double usableZone = zone - 2 * endClear;
            int barsPerFace = settings.BarsPerFaceAtEachEnd;
            int verticalCount = Math.Max(1, (int)Math.Ceiling((z1 - z0) / tieSpacing) + 1);
            if ((long)barsPerFace * 4 + (long)barsPerFace * verticalCount * 2 > 4000)
                throw new InvalidOperationException("Mật độ thép bổ trụ vượt quá 4000 thanh trên một tường.");

            XYZ origin = centerline.GetEndPoint(0);
            int created = 0;
            for (int endIndex = 0; endIndex < 2; endIndex++)
            for (int barIndex = 0; barIndex < barsPerFace; barIndex++)
            {
                double along = endClear + (barsPerFace == 1 ? usableZone / 2.0 : usableZone * barIndex / (barsPerFace - 1.0));
                XYZ plan = origin + axis * (endIndex == 0 ? along : centerline.Length - along);
                for (int side = -1; side <= 1; side += 2)
                {
                    XYZ point = plan + normal * (side * faceOffset);
                    XYZ a = new XYZ(point.X, point.Y, z0);
                    XYZ b = new XYZ(point.X, point.Y, z1);
                    Rebar vertical = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.Standard, settings.MainType,
                        null, null, wall, axis, new List<Curve> { Line.CreateBound(a, b) },
                        RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                    if (vertical == null) throw new InvalidOperationException("Revit không tạo được thanh dọc bổ trụ.");
                    SetPartition(vertical, wall.Id, "V_" + endIndex + "_" + barIndex + "_" + side);
                    created++;
                }
            }

            for (int endIndex = 0; endIndex < 2; endIndex++)
            for (int barIndex = 0; barIndex < barsPerFace; barIndex++)
            {
                double along = endClear + (barsPerFace == 1 ? usableZone / 2.0 : usableZone * barIndex / (barsPerFace - 1.0));
                XYZ plan = origin + axis * (endIndex == 0 ? along : centerline.Length - along);
                for (int i = 0; i < verticalCount; i++)
                {
                    double z = verticalCount == 1 ? (z0 + z1) / 2.0 : z0 + (z1 - z0) * i / (verticalCount - 1.0);
                    XYZ a = new XYZ(plan.X, plan.Y, z) - normal * faceOffset;
                    XYZ b = new XYZ(plan.X, plan.Y, z) + normal * faceOffset;
                    Rebar tie = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.Standard, settings.TieType,
                        null, null, wall, axis, new List<Curve> { Line.CreateBound(a, b) },
                        RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                    if (tie == null) throw new InvalidOperationException("Revit không tạo được thanh nối hai mặt.");
                    SetPartition(tie, wall.Id, "TIE_" + endIndex + "_" + barIndex + "_" + i);
                    created++;
                }
            }
            return created;
        }

        private static int RemoveOwnedRebar(Document doc, ElementId wallId)
        {
            string prefix = "KSTR_PILASTER_" + wallId.Value + "_";
            List<ElementId> ids = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
                .Where(rebar => rebar.GetHostId() == wallId)
                .Where(rebar => (rebar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM)?.AsString() ?? "")
                    .StartsWith(prefix, StringComparison.Ordinal))
                .Select(rebar => rebar.Id).ToList();
            if (ids.Count > 0) doc.Delete(ids);
            return ids.Count;
        }

        private static void SetPartition(Rebar rebar, ElementId wallId, string suffix)
        {
            Parameter partition = rebar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
            if (partition != null && !partition.IsReadOnly) partition.Set("KSTR_PILASTER_" + wallId.Value + "_" + suffix);
        }

        private static Settings ChooseOptions(IntPtr owner, IList<RebarBarType> types)
        {
            List<TypeChoice> choices = types.Select(type => new TypeChoice(type)).ToList();
            var window = new Window { Title = "Thép bổ trụ tường - KStruct", Width = 430, Height = 360,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false };
            var root = new StackPanel { Margin = new Thickness(12) };
            WpfComboBox mainType = AddType(root, "Thép dọc bổ trụ:", choices, 0);
            WpfComboBox tieType = AddType(root, "Thanh nối hai mặt:", choices, Math.Min(1, choices.Count - 1));
            WpfTextBox count = AddInput(root, "Số thanh dọc trên mỗi mặt, mỗi đầu:", "2");
            WpfTextBox zone = AddInput(root, "Chiều dài vùng tăng cường ở mỗi đầu (mm):", "600");
            WpfTextBox cover = AddInput(root, "Cover các mặt tường (mm):", "40");
            WpfTextBox endClear = AddInput(root, "Lùi thanh khỏi đầu tường (mm):", "40");
            WpfTextBox tieSpacing = AddInput(root, "Bước thanh nối hai mặt theo chiều cao (mm):", "200");
            var replace = new CheckBox { Content = "Thay thép bổ trụ KStruct đã tạo trước đó", IsChecked = true, Margin = new Thickness(0, 4, 0, 2) };
            root.Children.Add(replace);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
            var ok = new Button { Content = "Tạo thép", Width = 88, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) =>
            {
                int n; double l, c, e, s;
                if (mainType.SelectedItem == null || tieType.SelectedItem == null || !int.TryParse(count.Text, out n) || n < 1 || n > 12 ||
                    !double.TryParse(zone.Text, out l) || l <= 0 || !double.TryParse(cover.Text, out c) || c <= 0 ||
                    !double.TryParse(endClear.Text, out e) || e < 0 || !double.TryParse(tieSpacing.Text, out s) || s <= 0)
                {
                    TaskDialog.Show("KStruct - Thép bổ trụ tường", "Số thanh phải từ 1 đến 12; chiều dài, cover và bước thép phải hợp lệ.");
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
            return new Settings
            {
                MainType = ((TypeChoice)mainType.SelectedItem).Type,
                TieType = ((TypeChoice)tieType.SelectedItem).Type,
                BarsPerFaceAtEachEnd = int.Parse(count.Text),
                ZoneLengthMm = double.Parse(zone.Text), CoverMm = double.Parse(cover.Text),
                EndClearMm = double.Parse(endClear.Text), TieSpacingMm = double.Parse(tieSpacing.Text),
                Replace = replace.IsChecked == true
            };
        }

        private static WpfComboBox AddType(WpfPanel root, string label, IList<TypeChoice> types, int selected)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2) });
            var combo = new WpfComboBox { ItemsSource = types, DisplayMemberPath = "Display", SelectedIndex = selected, MinWidth = 390, Margin = new Thickness(0, 0, 0, 5) };
            root.Children.Add(combo);
            return combo;
        }

        private static WpfTextBox AddInput(WpfPanel root, string label, string value)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 2, 0, 1) });
            var box = new WpfTextBox { Text = value, MinWidth = 390, MinHeight = 20 };
            root.Children.Add(box);
            return box;
        }
    }
}
