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

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class RoundColumnRebarCommand : IExternalCommand
    {
        private sealed class ColumnFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is FamilyInstance fi && fi.Category != null &&
                fi.Category.Id.Value == (long)BuiltInCategory.OST_StructuralColumns;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        private sealed class BarTypeChoice
        {
            public RebarBarType Type { get; private set; }
            public string Display { get; private set; }
            public BarTypeChoice(RebarBarType type)
            {
                Type = type;
                Parameter p = type.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER);
                double mm = p != null && p.HasValue ? UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.Millimeters) : 0;
                Display = type.Name + " | Ø" + mm.ToString("0.##") + " mm";
            }
        }

        private sealed class Settings
        {
            public RebarBarType MainType;
            public RebarBarType TieType;
            public int MainCount;
            public double CoverMm;
            public double TieSpacingMm;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<FamilyInstance> columns = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<FamilyInstance>()
                    .Where(c => c.Category != null && c.Category.Id.Value == (long)BuiltInCategory.OST_StructuralColumns).Distinct().ToList();
                if (columns.Count == 0)
                {
                    IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new ColumnFilter(), "Chọn cột tròn cần bố trí cốt thép");
                    columns = picked.Select(r => doc.GetElement(r)).OfType<FamilyInstance>().Distinct().ToList();
                }
                if (columns.Count == 0) return Result.Cancelled;

                List<RebarBarType> barTypes = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType))
                    .Cast<RebarBarType>().OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (barTypes.Count == 0) throw new InvalidOperationException("Project chưa có RebarBarType.");
                Settings settings = ChooseSettings(data.Application.MainWindowHandle, barTypes);
                if (settings == null) return Result.Cancelled;

                int completed = 0;
                int mainBars = 0;
                int ties = 0;
                int replacedBars = 0;
                var skipped = new List<string>();
                using (Transaction transaction = new Transaction(doc, "KStruct - Cốt thép cột tròn"))
                {
                    transaction.Start();
                    foreach (FamilyInstance column in columns)
                    {
                        using (SubTransaction sub = new SubTransaction(doc))
                        {
                            sub.Start();
                            try
                            {
                                List<ElementId> previousManagedBars = FindManagedBars(doc, column.Id);
                                ColumnGeometry geometry = ReadColumnGeometry(column, settings);
                                CreateLongitudinalBars(doc, column, geometry, settings);
                                int columnTies = CreateCircularTies(doc, column, geometry, settings);
                                if (settings.MainCount + columnTies <= 0)
                                    throw new InvalidOperationException("Không tạo được bộ thép mới; bộ KStruct cũ được giữ nguyên.");
                                if (previousManagedBars.Count > 0) doc.Delete(previousManagedBars);
                                if (sub.Commit() != TransactionStatus.Committed)
                                    throw new InvalidOperationException("Revit không chấp nhận cốt thép cột.");
                                completed++;
                                replacedBars += previousManagedBars.Count;
                                mainBars += settings.MainCount;
                                ties += columnTies;
                            }
                            catch (Exception ex)
                            {
                                if (sub.GetStatus() == TransactionStatus.Started) sub.RollBack();
                                skipped.Add("Cột " + column.Id.Value + ": " + ex.Message);
                            }
                        }
                    }
                    if (completed == 0)
                    {
                        transaction.RollBack();
                        TaskDialog.Show("KStruct - Cột tròn", "Không tạo được cốt thép. Kiểm tra cột, loại thép và hình học.\n" + string.Join("\n", skipped.Take(8)));
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận thay đổi cốt thép.");
                }

                string report = "Đã bố trí cốt thép cho " + completed + "/" + columns.Count + " cột tròn.\n" +
                    "Thép dọc: " + mainBars + " thanh; đai vòng: " + ties + " cái.";
                if (replacedBars > 0) report += "\nĐã thay " + replacedBars + " thanh KStruct cũ sau khi tạo bộ mới thành công.";
                if (skipped.Count > 0) report += "\nBỏ qua " + skipped.Count + " cột:\n" + string.Join("\n", skipped.Take(8));
                TaskDialog.Show("KStruct - Cột tròn", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Cột tròn", ex.Message);
                return Result.Failed;
            }
        }

        private static List<ElementId> FindManagedBars(Document doc, ElementId columnId)
        {
            string partitionName = "KSTR_ROUND_COLUMN_" + columnId.Value;
            return new FilteredElementCollector(doc).OfClass(typeof(Rebar)).WhereElementIsNotElementType()
                .Cast<Rebar>().Where(bar => bar.GetHostId() == columnId)
                .Where(bar =>
                {
                    Parameter partition = bar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
                    return partition != null && partition.HasValue &&
                        string.Equals(partition.AsString(), partitionName, StringComparison.OrdinalIgnoreCase);
                }).Select(bar => bar.Id).ToList();
        }

        private static void SetPartition(Rebar rebar, ElementId columnId)
        {
            Parameter partition = rebar.get_Parameter(BuiltInParameter.NUMBER_PARTITION_PARAM);
            if (partition != null && !partition.IsReadOnly) partition.Set("KSTR_ROUND_COLUMN_" + columnId.Value);
        }

        private sealed class ColumnGeometry
        {
            public XYZ Center;
            public double Radius;
            public double Bottom;
            public double Top;
            public double MainRadius;
            public double TieRadius;
        }

        private static ColumnGeometry ReadColumnGeometry(FamilyInstance column, Settings settings)
        {
            if (!(column.Location is LocationPoint location)) throw new InvalidOperationException("Chỉ hỗ trợ cột có LocationPoint.");
            BoundingBoxXYZ box = column.get_BoundingBox(null);
            if (box == null) throw new InvalidOperationException("Không đọc được bounding box.");
            double width = box.Max.X - box.Min.X;
            double depth = box.Max.Y - box.Min.Y;
            if (width <= 0 || depth <= 0 || Math.Abs(width - depth) / Math.Max(width, depth) > 0.08)
                throw new InvalidOperationException("Tiết diện không gần hình tròn theo bounding box.");
            double radius = (width + depth) / 4.0;
            double cover = UnitUtils.ConvertToInternalUnits(settings.CoverMm, UnitTypeId.Millimeters);
            double mainDia = settings.MainType.BarNominalDiameter;
            double tieDia = settings.TieType.BarNominalDiameter;
            double mainRadius = radius - cover - tieDia - mainDia / 2.0;
            double tieRadius = radius - cover - tieDia / 2.0;
            if (mainRadius <= 0 || tieRadius <= 0) throw new InvalidOperationException("Cover và đường kính thép lớn hơn bán kính hữu dụng của cột.");
            if (location.Point.DistanceTo(new XYZ((box.Min.X + box.Max.X) / 2.0, (box.Min.Y + box.Max.Y) / 2.0, location.Point.Z)) > radius * 0.2)
                throw new InvalidOperationException("Tâm cột lệch nhiều so với bounding box; không thể đặt thép chính xác.");
            double bottom = box.Min.Z + cover + tieDia / 2.0;
            double top = box.Max.Z - cover - tieDia / 2.0;
            if (top <= bottom) throw new InvalidOperationException("Chiều cao cột không đủ để bố trí thép.");
            return new ColumnGeometry
            {
                Center = new XYZ(location.Point.X, location.Point.Y, 0),
                Radius = radius,
                Bottom = bottom,
                Top = top,
                MainRadius = mainRadius,
                TieRadius = tieRadius
            };
        }

        private static void CreateLongitudinalBars(Document doc, FamilyInstance column, ColumnGeometry g, Settings settings)
        {
            double start = g.Bottom + settings.TieType.BarNominalDiameter / 2.0 + settings.MainType.BarNominalDiameter / 2.0;
            double end = g.Top - settings.TieType.BarNominalDiameter / 2.0 - settings.MainType.BarNominalDiameter / 2.0;
            for (int i = 0; i < settings.MainCount; i++)
            {
                double angle = 2.0 * Math.PI * i / settings.MainCount;
                double x = g.Center.X + g.MainRadius * Math.Cos(angle);
                double y = g.Center.Y + g.MainRadius * Math.Sin(angle);
                XYZ a = new XYZ(x, y, start);
                XYZ b = new XYZ(x, y, end);
                XYZ normal = new XYZ(Math.Cos(angle), Math.Sin(angle), 0);
                Rebar rebar = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.Standard, settings.MainType,
                    null, null, column, normal, new List<Curve> { Line.CreateBound(a, b) },
                    RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                if (rebar == null) throw new InvalidOperationException("Revit không tạo được thép dọc tại vị trí " + (i + 1) + ".");
                SetPartition(rebar, column.Id);
            }
        }

        private static int CreateCircularTies(Document doc, FamilyInstance column, ColumnGeometry g, Settings settings)
        {
            double first = g.Bottom;
            double last = g.Top;
            double spacing = UnitUtils.ConvertToInternalUnits(settings.TieSpacingMm, UnitTypeId.Millimeters);
            int count = Math.Max(1, (int)Math.Ceiling((last - first) / spacing) + 1);
            if (count == 1) first = last = (first + last) / 2.0;
            XYZ xAxis = XYZ.BasisX;
            XYZ yAxis = XYZ.BasisY;
            for (int i = 0; i < count; i++)
            {
                double z = count == 1 ? first : first + (last - first) * i / (count - 1.0);
                XYZ center = new XYZ(g.Center.X, g.Center.Y, z);
                Arc firstHalf = Arc.Create(center, g.TieRadius, 0, Math.PI, xAxis, yAxis);
                Arc secondHalf = Arc.Create(center, g.TieRadius, Math.PI, 2 * Math.PI, xAxis, yAxis);
                Rebar tie = NativeRebarFactory.CreateFromCurves(doc, RebarStyle.StirrupTie, settings.TieType,
                    null, null, column, XYZ.BasisZ, new List<Curve> { firstHalf, secondHalf },
                    RebarHookOrientation.Left, RebarHookOrientation.Right, true, true);
                if (tie == null) throw new InvalidOperationException("Revit không tạo được đai vòng tại cao độ " + UnitUtils.ConvertFromInternalUnits(z, UnitTypeId.Millimeters).ToString("0") + " mm.");
                SetPartition(tie, column.Id);
            }
            return count;
        }

        private static Settings ChooseSettings(IntPtr owner, IList<RebarBarType> types)
        {
            List<BarTypeChoice> choices = types.Select(t => new BarTypeChoice(t)).ToList();
            var window = new Window
            {
                Title = "Cốt thép cột tròn - KStruct",
                Width = 430,
                Height = 310,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            var mainCombo = AddTypeRow(root, "Thép dọc:", choices, 0);
            var tieCombo = AddTypeRow(root, "Đai vòng:", choices, Math.Min(1, choices.Count - 1));
            var countBox = AddTextRow(root, "Số thanh dọc (tối thiểu 4):", "8");
            var coverBox = AddTextRow(root, "Lớp bảo vệ bê tông (mm):", "40");
            var spacingBox = AddTextRow(root, "Bước đai tối đa (mm):", "150");
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "Tạo thép", Width = 82, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) =>
            {
                int count;
                double cover, spacing;
                if (mainCombo.SelectedItem == null || tieCombo.SelectedItem == null ||
                    !int.TryParse(countBox.Text, out count) || count < 4 ||
                    !double.TryParse(coverBox.Text, out cover) || cover <= 0 ||
                    !double.TryParse(spacingBox.Text, out spacing) || spacing <= 0)
                {
                    TaskDialog.Show("KStruct - Cột tròn", "Kiểm tra số thanh, cover và bước đai (các giá trị phải lớn hơn 0; tối thiểu 4 thanh dọc).");
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
                MainType = ((BarTypeChoice)mainCombo.SelectedItem).Type,
                TieType = ((BarTypeChoice)tieCombo.SelectedItem).Type,
                MainCount = int.Parse(countBox.Text),
                CoverMm = double.Parse(coverBox.Text),
                TieSpacingMm = double.Parse(spacingBox.Text)
            };
        }

        private static System.Windows.Controls.ComboBox AddTypeRow(System.Windows.Controls.Panel root, string label, IList<BarTypeChoice> choices, int selected)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 3) });
            var combo = new System.Windows.Controls.ComboBox { MinWidth = 390, MinHeight = 25, DisplayMemberPath = "Display", ItemsSource = choices, SelectedIndex = selected, Margin = new Thickness(0, 0, 0, 7) };
            root.Children.Add(combo);
            return combo;
        }

        private static System.Windows.Controls.TextBox AddTextRow(System.Windows.Controls.Panel root, string label, string value)
        {
            root.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 3) });
            var box = new System.Windows.Controls.TextBox { MinWidth = 390, MinHeight = 23, Text = value, Margin = new Thickness(0, 0, 0, 7) };
            root.Children.Add(box);
            return box;
        }
    }
}
