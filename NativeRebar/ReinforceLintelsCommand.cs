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
    public sealed class ReinforceLintelsCommand : IExternalCommand
    {
        private sealed class InsertFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => IsHostedOpening(element);
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class BarChoice
        {
            public RebarBarType Type { get; private set; }
            public string Name { get; private set; }
            public BarChoice(RebarBarType type)
            {
                Type = type;
                double dia = UnitUtils.ConvertFromInternalUnits(type.BarNominalDiameter, UnitTypeId.Millimeters);
                Name = type.Name + " | Ø" + dia.ToString("0.#") + " mm";
            }
        }

        private sealed class Options
        {
            public RebarBarType MainType, TieType;
            public double DepthMm, BearingMm, CoverMm, TieSpacingMm;
            public bool Replace;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<FamilyInstance> inserts = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .OfType<FamilyInstance>().Where(IsHostedOpening).Distinct().ToList();
                if (inserts.Count == 0)
                {
                    inserts = ui.Selection.PickObjects(ObjectType.Element, new InsertFilter(),
                            "Chọn cửa/cửa sổ đặt trong tường cần bố trí thép lanh tô")
                        .Select(reference => doc.GetElement(reference)).OfType<FamilyInstance>()
                        .Where(IsHostedOpening).Distinct().ToList();
                }
                if (inserts.Count == 0) return Result.Cancelled;

                List<RebarBarType> types = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType))
                    .Cast<RebarBarType>().OrderBy(type => type.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (types.Count == 0) throw new InvalidOperationException("Project chưa có RebarBarType.");
                Options settings = ChooseOptions(data.Application.MainWindowHandle, types);
                if (settings == null) return Result.Cancelled;

                int completed = 0, barsCreated = 0, replaced = 0;
                var skipped = new List<string>();
                using (Transaction tx = new Transaction(doc, "KStruct - Thép lanh tô"))
                {
                    tx.Start();
                    foreach (FamilyInstance insert in inserts)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                Wall wall = insert.Host as Wall;
                                if (wall == null) throw new InvalidOperationException("Cửa/cửa sổ không có Wall host.");
                                if (settings.Replace) replaced += RemoveOwnedRebar(doc, wall.Id, insert.Id);
                                int created = CreateLintel(doc, wall, insert, settings);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận thép lanh tô.");
                                completed++;
                                barsCreated += created;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Cửa/cửa sổ " + insert.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (completed == 0)
                    {
                        tx.RollBack();
                        TaskDialog.Show("KStruct - Thép lanh tô", "Không tạo được thép cho đối tượng nào.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận các thanh thép lanh tô.");
                }

                string report = "Đã tạo " + barsCreated + " thanh cho " + completed + "/" + inserts.Count +
                    " cửa/cửa sổ: 4 thanh dọc và đai khép kín trong vùng lanh tô.";
                if (settings.Replace) report += " Đã thay " + replaced + " thanh KStruct cùng cửa/cửa sổ.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " đối tượng:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Thép lanh tô", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Thép lanh tô", ex.Message);
                return Result.Failed;
            }
        }

        private static bool IsHostedOpening(Element element)
        {
            FamilyInstance instance = element as FamilyInstance;
            if (instance == null || !(instance.Host is Wall) || instance.Category == null) return false;
            long category = instance.Category.Id.Value;
            return category == (long)BuiltInCategory.OST_Doors || category == (long)BuiltInCategory.OST_Windows;
        }

        private static int CreateLintel(Document doc, Wall wall, FamilyInstance insert, Options settings)
        {
            Line axisLine = (wall.Location as LocationCurve)?.Curve as Line;
            BoundingBoxXYZ wallBox = wall.get_BoundingBox(null);
            BoundingBoxXYZ openingBox = insert.get_BoundingBox(null);
            if (axisLine == null || wallBox == null || openingBox == null)
                throw new InvalidOperationException("Chỉ hỗ trợ tường thẳng và cửa/cửa sổ có bounding box.");
            XYZ axis = axisLine.Direction.Normalize();
            XYZ faceNormal = wall.Orientation.Normalize();
            if (Math.Abs(axis.Z) > 1e-3 || Math.Abs(faceNormal.Z) > 1e-3 || Math.Abs(axis.DotProduct(faceNormal)) > 1e-3)
                throw new InvalidOperationException("Tường phải đứng, có tim ngang và hai mặt song song.");

            List<XYZ> corners = GetCorners(openingBox);
            XYZ origin = axisLine.GetEndPoint(0);
            double openingStart = corners.Min(point => (point - origin).DotProduct(axis));
            double openingEnd = corners.Max(point => (point - origin).DotProduct(axis));
            double headZ = corners.Max(point => point.Z);
            double bearing = UnitUtils.ConvertToInternalUnits(settings.BearingMm, UnitTypeId.Millimeters);
            double depth = UnitUtils.ConvertToInternalUnits(settings.DepthMm, UnitTypeId.Millimeters);
            double cover = UnitUtils.ConvertToInternalUnits(settings.CoverMm, UnitTypeId.Millimeters);
            double tieSpacing = UnitUtils.ConvertToInternalUnits(settings.TieSpacingMm, UnitTypeId.Millimeters);
            double mainDia = settings.MainType.BarNominalDiameter;
            double tieDia = settings.TieType.BarNominalDiameter;
            double start = Math.Max(0, openingStart - bearing);
            double end = Math.Min(axisLine.Length, openingEnd + bearing);
            if (openingEnd <= openingStart || end - start <= doc.Application.ShortCurveTolerance)
                throw new InvalidOperationException("Không xác định được khẩu độ cửa/cửa sổ trên tim tường.");
            double tieOffset = wall.Width / 2.0 - cover - tieDia / 2.0;
            double barOffset = wall.Width / 2.0 - cover - tieDia - mainDia / 2.0;
            double bottomZ = headZ + cover + tieDia + mainDia / 2.0;
            double topZ = headZ + depth - cover - tieDia - mainDia / 2.0;
            if (tieOffset <= 0 || barOffset <= 0 || topZ - bottomZ <= doc.Application.ShortCurveTolerance ||
                topZ + tieDia / 2.0 > wallBox.Max.Z || bottomZ - tieDia / 2.0 < wallBox.Min.Z)
                throw new InvalidOperationException("Không đủ bề dày/chiều cao tường phía trên lỗ mở cho cover và cấu hình thép.");

            int created = 0;
            foreach (int side in new[] { -1, 1 })
            foreach (double z in new[] { bottomZ, topZ })
            {
                XYZ a = origin + axis * start + faceNormal * (side * barOffset) + XYZ.BasisZ * (z - origin.Z);
                XYZ b = origin + axis * end + faceNormal * (side * barOffset) + XYZ.BasisZ * (z - origin.Z);
                Rebar longitudinal = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.Standard, settings.MainType,
                    null, null, wall, faceNormal, new List<Curve> { Line.CreateBound(a, b) },
                    RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                if (longitudinal == null) throw new InvalidOperationException("Revit không tạo được thép dọc lanh tô.");
                SetPartition(longitudinal, wall.Id, insert.Id, "LONG_" + side + "_" + (z == bottomZ ? "BOT" : "TOP"));
                created++;
            }

            int tieCount = Math.Max(2, (int)Math.Ceiling((end - start) / tieSpacing) + 1);
            if (tieCount > 2000) throw new InvalidOperationException("Số đai vượt quá giới hạn 2000.");
            for (int i = 0; i < tieCount; i++)
            {
                double station = start + (end - start) * i / (tieCount - 1.0);
                double tieBottomZ = headZ + cover + tieDia / 2.0;
                double tieTopZ = headZ + depth - cover - tieDia / 2.0;
                XYZ plan = origin + axis * station - XYZ.BasisZ * origin.Z;
                XYZ p0 = plan - faceNormal * tieOffset + XYZ.BasisZ * tieBottomZ;
                XYZ p1 = plan + faceNormal * tieOffset + XYZ.BasisZ * tieBottomZ;
                XYZ p2 = plan + faceNormal * tieOffset + XYZ.BasisZ * tieTopZ;
                XYZ p3 = plan - faceNormal * tieOffset + XYZ.BasisZ * tieTopZ;
                List<Curve> loop = new List<Curve>
                {
                    Line.CreateBound(p0, p1), Line.CreateBound(p1, p2),
                    Line.CreateBound(p2, p3), Line.CreateBound(p3, p0)
                };
                Rebar tie = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.StirrupTie, settings.TieType,
                    null, null, wall, axis, loop, RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                if (tie == null) throw new InvalidOperationException("Revit không tạo được đai lanh tô số " + (i + 1) + ".");
                SetPartition(tie, wall.Id, insert.Id, "TIE_" + i);
                created++;
            }
            return created;
        }

        private static List<XYZ> GetCorners(BoundingBoxXYZ box)
        {
            var result = new List<XYZ>();
            Transform transform = box.Transform ?? Transform.Identity;
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                result.Add(transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X,
                    y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z)));
            return result;
        }

        private static int RemoveOwnedRebar(Document doc, ElementId wallId, ElementId insertId)
        {
            string prefix = "KSTR_LINTEL_" + wallId.Value + "_" + insertId.Value + "_";
            List<ElementId> ids = new FilteredElementCollector(doc).OfClass(typeof(Rebar)).Cast<Rebar>()
                .Where(rebar => rebar.GetHostId() == wallId)
                .Where(rebar => (rebar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM)?.AsString() ?? "")
                    .StartsWith(prefix, StringComparison.Ordinal)).Select(rebar => rebar.Id).ToList();
            if (ids.Count > 0) doc.Delete(ids);
            return ids.Count;
        }

        private static void SetPartition(Rebar rebar, ElementId wallId, ElementId insertId, string suffix)
        {
            Parameter partition = rebar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
            if (partition != null && !partition.IsReadOnly)
                partition.Set("KSTR_LINTEL_" + wallId.Value + "_" + insertId.Value + "_" + suffix);
        }

        private static Options ChooseOptions(IntPtr owner, IList<RebarBarType> types)
        {
            List<BarChoice> choices = types.Select(type => new BarChoice(type)).ToList();
            var window = new Window { Title = "Thép lanh tô - KStruct", Width = 430, Height = 300,
                ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow, ShowInTaskbar = false };
            var root = new StackPanel { Margin = new Thickness(12) };
            WpfComboBox mainType = AddType(root, "Thép dọc:", choices, 0);
            WpfComboBox tieType = AddType(root, "Thép đai:", choices, Math.Min(1, choices.Count - 1));
            WpfTextBox depth = AddInput(root, "Chiều cao vùng lanh tô phía trên lỗ mở (mm):", "300");
            WpfTextBox bearing = AddInput(root, "Chiều dài gối mỗi bên lỗ mở (mm):", "300");
            WpfTextBox cover = AddInput(root, "Cover (mm):", "40");
            WpfTextBox spacing = AddInput(root, "Bước đai (mm):", "150");
            var replace = new CheckBox { Content = "Thay thép lanh tô KStruct đã tạo cho cửa/cửa sổ", IsChecked = true, Margin = new Thickness(0, 3, 0, 2) };
            root.Children.Add(replace);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 5, 0, 0) };
            var ok = new Button { Content = "Tạo thép", Width = 86, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            buttons.Children.Add(ok);
            buttons.Children.Add(new Button { Content = "Hủy", Width = 72, IsCancel = true });
            root.Children.Add(buttons);
            ok.Click += (sender, args) =>
            {
                double d, b, c, s;
                if (mainType.SelectedItem == null || tieType.SelectedItem == null ||
                    !double.TryParse(depth.Text, out d) || d <= 0 || !double.TryParse(bearing.Text, out b) || b < 0 ||
                    !double.TryParse(cover.Text, out c) || c <= 0 || !double.TryParse(spacing.Text, out s) || s <= 0)
                {
                    TaskDialog.Show("KStruct - Thép lanh tô", "Chiều cao và bước đai phải lớn hơn 0; cover phải dương, gối không được âm.");
                    return;
                }
                window.DialogResult = true;
            };
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            if (window.ShowDialog() != true) return null;
            return new Options
            {
                MainType = ((BarChoice)mainType.SelectedItem).Type,
                TieType = ((BarChoice)tieType.SelectedItem).Type,
                DepthMm = double.Parse(depth.Text), BearingMm = double.Parse(bearing.Text),
                CoverMm = double.Parse(cover.Text), TieSpacingMm = double.Parse(spacing.Text), Replace = replace.IsChecked == true
            };
        }

        private static WpfComboBox AddType(WpfPanel root, string label, IList<BarChoice> choices, int index)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2) });
            var combo = new WpfComboBox { ItemsSource = choices, DisplayMemberPath = "Name", SelectedIndex = index, MinWidth = 390, Margin = new Thickness(0, 0, 0, 4) };
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
