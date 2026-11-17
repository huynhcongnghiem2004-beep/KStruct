using System;
using System.Collections.Generic;
using System.Globalization;
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
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfVisibility = System.Windows.Visibility;

namespace SDK_HD.KStruct.NativeDocumentation
{
    [Transaction(TransactionMode.Manual)]
    public sealed class QuickStructuralModelCommand : IExternalCommand
    {
        private enum ModelKind { Column, Beam, Floor, Wall, Footing }

        private sealed class KindChoice
        {
            public ModelKind Kind { get; }
            public string Label { get; }
            public KindChoice(ModelKind kind, string label) { Kind = kind; Label = label; }
            public override string ToString() => Label;
        }

        private sealed class LevelChoice
        {
            public Level Level { get; }
            public string Label { get; }
            public LevelChoice(Level level) { Level = level; Label = level.Name + " | " + UnitUtils.ConvertFromInternalUnits(level.Elevation, UnitTypeId.Millimeters).ToString("0.##") + " mm"; }
            public override string ToString() => Label;
        }

        private sealed class ElementChoice
        {
            public ElementType Type { get; }
            public string Label => (Type is FamilySymbol symbol ? symbol.FamilyName + " : " : "") + Type.Name;
            public ElementChoice(ElementType type) { Type = type; }
            public override string ToString() => Label;
        }

        private sealed class Options
        {
            public ModelKind Kind;
            public ElementType Type;
            public Level BaseLevel;
            public Level TopLevel;
            public double WallHeightMm;
        }

        private sealed class ColumnFilter : ISelectionFilter
        {
            public bool AllowElement(Element element) => element is FamilyInstance && element.Category != null &&
                element.Category.Id.Value == (long)BuiltInCategory.OST_StructuralColumns;
            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Level> levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(level => level.Elevation).ToList();
                if (levels.Count == 0) throw new InvalidOperationException("Project chưa có Level.");
                Options options = ChooseOptions(data.Application.MainWindowHandle, doc, levels);
                if (options == null) return Result.Cancelled;

                IList<FamilyInstance> columns = new List<FamilyInstance>();
                if (options.Kind == ModelKind.Footing)
                {
                    columns = ui.Selection.GetElementIds().Select(doc.GetElement).OfType<FamilyInstance>()
                        .Where(element => element.Category != null && element.Category.Id.Value == (long)BuiltInCategory.OST_StructuralColumns)
                        .Distinct().ToList();
                    if (columns.Count == 0)
                    {
                        IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element, new ColumnFilter(),
                            "Chọn một hoặc nhiều cột để đặt móng nhanh");
                        columns = picked.Select(reference => doc.GetElement(reference)).OfType<FamilyInstance>().Distinct().ToList();
                    }
                    if (columns.Count == 0) return Result.Cancelled;
                }

                XYZ first = null, second = null;
                if (options.Kind == ModelKind.Column)
                    first = ui.Selection.PickPoint("Chọn vị trí đặt cột");
                else if (options.Kind == ModelKind.Beam || options.Kind == ModelKind.Wall || options.Kind == ModelKind.Floor)
                {
                    first = ui.Selection.PickPoint(options.Kind == ModelKind.Floor ? "Chọn góc thứ nhất của sàn chữ nhật" : "Chọn điểm đầu");
                    second = ui.Selection.PickPoint(options.Kind == ModelKind.Floor ? "Chọn góc đối diện của sàn chữ nhật" : "Chọn điểm cuối");
                    if (first.DistanceTo(second) < doc.Application.ShortCurveTolerance * 2)
                        throw new InvalidOperationException("Hai điểm phải cách nhau đủ xa để tạo cấu kiện.");
                    if (options.Kind == ModelKind.Floor &&
                        (Math.Abs(first.X - second.X) < doc.Application.ShortCurveTolerance * 2 ||
                         Math.Abs(first.Y - second.Y) < doc.Application.ShortCurveTolerance * 2))
                        throw new InvalidOperationException("Sàn chữ nhật cần khác nhau cả tọa độ X và Y.");
                }

                int created = 0;
                using (Transaction transaction = new Transaction(doc, "KStruct - Quick Model"))
                {
                    transaction.Start();
                    if (options.Type is FamilySymbol symbol && !symbol.IsActive) symbol.Activate();
                    if (options.Kind == ModelKind.Column)
                    {
                        FamilySymbol columnType = (FamilySymbol)options.Type;
                        FamilyInstance column = doc.Create.NewFamilyInstance(AtLevel(first, options.BaseLevel), columnType,
                            options.BaseLevel, StructuralType.Column);
                        Parameter topLevel = column.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
                        if (topLevel != null && !topLevel.IsReadOnly) topLevel.Set(options.TopLevel.Id);
                        Parameter baseOffset = column.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
                        if (baseOffset != null && !baseOffset.IsReadOnly) baseOffset.Set(0.0);
                        Parameter topOffset = column.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
                        if (topOffset != null && !topOffset.IsReadOnly) topOffset.Set(0.0);
                        created = 1;
                    }
                    else if (options.Kind == ModelKind.Beam)
                    {
                        XYZ a = new XYZ(first.X, first.Y, first.Z);
                        XYZ b = new XYZ(second.X, second.Y, second.Z);
                        doc.Create.NewFamilyInstance(Line.CreateBound(a, b), (FamilySymbol)options.Type,
                            options.BaseLevel, StructuralType.Beam);
                        created = 1;
                    }
                    else if (options.Kind == ModelKind.Floor)
                    {
                        double z = options.BaseLevel.Elevation;
                        XYZ[] points =
                        {
                            new XYZ(first.X, first.Y, z), new XYZ(second.X, first.Y, z),
                            new XYZ(second.X, second.Y, z), new XYZ(first.X, second.Y, z)
                        };
                        var loop = new CurveLoop();
                        for (int i = 0; i < points.Length; i++) loop.Append(Line.CreateBound(points[i], points[(i + 1) % points.Length]));
                        Floor.Create(doc, new List<CurveLoop> { loop }, options.Type.Id, options.BaseLevel.Id);
                        created = 1;
                    }
                    else if (options.Kind == ModelKind.Wall)
                    {
                        XYZ a = AtLevel(first, options.BaseLevel), b = AtLevel(second, options.BaseLevel);
                        Wall.Create(doc, Line.CreateBound(a, b), options.Type.Id, options.BaseLevel.Id,
                            UnitUtils.ConvertToInternalUnits(options.WallHeightMm, UnitTypeId.Millimeters), 0, false, false);
                        created = 1;
                    }
                    else
                    {
                        FamilySymbol footingType = (FamilySymbol)options.Type;
                        foreach (FamilyInstance column in columns)
                        {
                            if (!(column.Location is LocationPoint location)) continue;
                            doc.Create.NewFamilyInstance(AtLevel(location.Point, options.BaseLevel), footingType,
                                options.BaseLevel, StructuralType.Footing);
                            created++;
                        }
                    }
                    if (created == 0)
                    {
                        transaction.RollBack();
                        TaskDialog.Show("KStruct - Quick Model", "Không tạo được cấu kiện. Kiểm tra Family Type, Level và hình học đã chọn.");
                        return Result.Cancelled;
                    }
                    if (transaction.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận cấu kiện mới.");
                }
                TaskDialog.Show("KStruct - Quick Model", "Đã tạo " + created + " cấu kiện.\nLoại: " + options.Type.Name + "\nLevel: " + options.BaseLevel.Name);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Quick Model", ex.Message);
                return Result.Failed;
            }
        }

        private static XYZ AtLevel(XYZ point, Level level) => new XYZ(point.X, point.Y, level.Elevation);

        private static Options ChooseOptions(IntPtr owner, Document doc, IList<Level> levels)
        {
            var window = new Window
            {
                Title = "Quick Model - KStruct",
                Width = 500,
                Height = 355,
                MinWidth = 460,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            new WindowInteropHelper(window).Owner = owner;
            var root = new WpfGrid { Margin = new Thickness(14) };
            for (int i = 0; i < 8; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var kindBox = new WpfComboBox
            {
                ItemsSource = new[]
                {
                    new KindChoice(ModelKind.Column, "Cột kết cấu"),
                    new KindChoice(ModelKind.Beam, "Dầm kết cấu"),
                    new KindChoice(ModelKind.Floor, "Sàn chữ nhật"),
                    new KindChoice(ModelKind.Wall, "Tường thẳng"),
                    new KindChoice(ModelKind.Footing, "Móng dưới cột đã chọn")
                },
                MinHeight = 27,
                SelectedIndex = 0,
                Margin = new Thickness(0, 3, 0, 8)
            };
            var typeBox = new WpfComboBox { MinHeight = 27, Margin = new Thickness(0, 3, 0, 8) };
            var baseBox = new WpfComboBox
            {
                ItemsSource = levels.Select(level => new LevelChoice(level)).ToList(),
                MinHeight = 27,
                SelectedIndex = 0,
                Margin = new Thickness(0, 3, 0, 8)
            };
            var topBox = new WpfComboBox
            {
                ItemsSource = levels.Select(level => new LevelChoice(level)).ToList(),
                MinHeight = 27,
                SelectedIndex = Math.Min(1, levels.Count - 1),
                Margin = new Thickness(0, 3, 0, 8)
            };
            var heightBox = new WpfTextBox { Text = "3000", MinHeight = 25, Margin = new Thickness(0, 3, 0, 8) };
            var topLabel = new TextBlock { Text = "Level đỉnh cột:", FontWeight = FontWeights.SemiBold };
            var heightLabel = new TextBlock { Text = "Chiều cao tường (mm):", FontWeight = FontWeights.SemiBold };
            root.Children.Add(new TextBlock { Text = "Loại cấu kiện:", FontWeight = FontWeights.SemiBold });
            WpfGrid.SetRow(kindBox, 1); root.Children.Add(kindBox);
            var typeLabel = new TextBlock { Text = "Family Type / Floor Type:", FontWeight = FontWeights.SemiBold };
            WpfGrid.SetRow(typeLabel, 2); root.Children.Add(typeLabel);
            WpfGrid.SetRow(typeBox, 3); root.Children.Add(typeBox);
            var baseLabel = new TextBlock { Text = "Level đặt cấu kiện:", FontWeight = FontWeights.SemiBold };
            WpfGrid.SetRow(baseLabel, 4); root.Children.Add(baseLabel);
            WpfGrid.SetRow(baseBox, 5); root.Children.Add(baseBox);
            WpfGrid.SetRow(topLabel, 6); root.Children.Add(topLabel);
            topBox.Margin = new Thickness(0, 3, 0, 8); WpfGrid.SetRow(topBox, 7); root.Children.Add(topBox);
            var heightPanel = new StackPanel { Visibility = WpfVisibility.Collapsed };
            heightPanel.Children.Add(heightLabel); heightPanel.Children.Add(heightBox);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            WpfGrid.SetRow(heightPanel, 8); root.Children.Add(heightPanel);
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = "Chọn vị trí / tạo", MinWidth = 120, IsDefault = true, Margin = new Thickness(0, 0, 7, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 70, IsCancel = true };
            footer.Children.Add(ok); footer.Children.Add(cancel);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            WpfGrid.SetRow(footer, 9); root.Children.Add(footer);

            Action refreshTypes = () =>
            {
                KindChoice selected = kindBox.SelectedItem as KindChoice;
                IEnumerable<ElementType> types = Enumerable.Empty<ElementType>();
                if (selected != null)
                {
                    switch (selected.Kind)
                    {
                        case ModelKind.Column: types = CollectSymbols(doc, BuiltInCategory.OST_StructuralColumns); break;
                        case ModelKind.Beam: types = CollectSymbols(doc, BuiltInCategory.OST_StructuralFraming); break;
                        case ModelKind.Floor: types = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>(); break;
                        case ModelKind.Wall: types = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().Where(type => type.Kind == WallKind.Basic); break;
                        case ModelKind.Footing: types = CollectSymbols(doc, BuiltInCategory.OST_StructuralFoundation); break;
                    }
                }
                var choices = types.OrderBy(type => type.Name, StringComparer.CurrentCultureIgnoreCase).Select(type => new ElementChoice(type)).ToList();
                typeBox.ItemsSource = choices;
                typeBox.SelectedIndex = choices.Count > 0 ? 0 : -1;
                topLabel.Visibility = selected?.Kind == ModelKind.Column ? WpfVisibility.Visible : WpfVisibility.Collapsed;
                topBox.Visibility = selected?.Kind == ModelKind.Column ? WpfVisibility.Visible : WpfVisibility.Collapsed;
                heightPanel.Visibility = selected?.Kind == ModelKind.Wall ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            };
            kindBox.SelectionChanged += (sender, args) => refreshTypes();
            refreshTypes();
            Options result = null;
            ok.Click += (sender, args) =>
            {
                KindChoice kind = kindBox.SelectedItem as KindChoice;
                ElementChoice type = typeBox.SelectedItem as ElementChoice;
                LevelChoice baseLevel = baseBox.SelectedItem as LevelChoice;
                LevelChoice topLevel = topBox.SelectedItem as LevelChoice;
                double wallHeight = 0;
                if (kind == null || type == null || baseLevel == null ||
                    (kind.Kind == ModelKind.Column && (topLevel == null || topLevel.Level.Elevation <= baseLevel.Level.Elevation)) ||
                    (kind.Kind == ModelKind.Wall && (!double.TryParse(heightBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out wallHeight) || wallHeight <= 0)))
                {
                    MessageBox.Show(window, "Chọn Family Type/Level hợp lệ; cột cần Level đỉnh cao hơn Level đáy, tường cần chiều cao dương.",
                        "KStruct - Quick Model", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (kind.Kind != ModelKind.Wall) wallHeight = 0;
                result = new Options { Kind = kind.Kind, Type = type.Type, BaseLevel = baseLevel.Level,
                    TopLevel = topLevel?.Level, WallHeightMm = wallHeight };
                window.DialogResult = true;
            };
            window.Content = root;
            return window.ShowDialog() == true ? result : null;
        }

        private static IEnumerable<FamilySymbol> CollectSymbols(Document doc, BuiltInCategory category) =>
            new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).OfCategory(category).Cast<FamilySymbol>();
    }
}
