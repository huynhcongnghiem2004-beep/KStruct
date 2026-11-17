using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Xml.Serialization;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;

namespace SDK_HD.KStruct.NativeRebar
{
    [Serializable]
    public sealed class RebarDiameterDefinition : INotifyPropertyChanged
    {
        private string _name;
        private int _realDiameter;
        private double _modelBarDiameter;
        private double _barDiameter;
        private double _standardBendDiameter;
        private double _standardHookBendDiameter;
        private double _stirrupTieBendDiameter;
        private double _maximumBendRadius;

        public string Name { get => _name; set => Set(ref _name, value); }
        public int RealDiameter { get => _realDiameter; set => Set(ref _realDiameter, value); }
        public double ModelBarDiameter { get => _modelBarDiameter; set => Set(ref _modelBarDiameter, value); }
        public double BarDiameter { get => _barDiameter; set => Set(ref _barDiameter, value); }
        public double StandardBendDiameter { get => _standardBendDiameter; set => Set(ref _standardBendDiameter, value); }
        public double StandardHookBendDiameter { get => _standardHookBendDiameter; set => Set(ref _standardHookBendDiameter, value); }
        public double StirrupTieBendDiameter { get => _stirrupTieBendDiameter; set => Set(ref _stirrupTieBendDiameter, value); }
        public double MaximumBendRadius { get => _maximumBendRadius; set => Set(ref _maximumBendRadius, value); }

        [field: NonSerialized]
        public event PropertyChangedEventHandler PropertyChanged;

        private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string property = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }

    [Serializable]
    public sealed class RebarDiameterSettingsFile
    {
        public RebarDiameterDefinition[] Definitions { get; set; } = new RebarDiameterDefinition[0];
    }

    internal static class RebarDiameterSettingsStore
    {
        internal static ObservableCollection<RebarDiameterDefinition> Load()
        {
            string path = GetPath();
            if (!File.Exists(path)) return Defaults();
            try
            {
                using (FileStream stream = File.OpenRead(path))
                {
                    var file = (RebarDiameterSettingsFile)new XmlSerializer(typeof(RebarDiameterSettingsFile)).Deserialize(stream);
                    return new ObservableCollection<RebarDiameterDefinition>((file?.Definitions ?? Array.Empty<RebarDiameterDefinition>())
                        .OrderBy(x => x.RealDiameter).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase));
                }
            }
            catch { return Defaults(); }
        }

        internal static void Save(IEnumerable<RebarDiameterDefinition> definitions)
        {
            string path = GetPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            var file = new RebarDiameterSettingsFile { Definitions = definitions.ToArray() };
            using (FileStream stream = File.Create(temporary))
                new XmlSerializer(typeof(RebarDiameterSettingsFile)).Serialize(stream, file);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }

        private static ObservableCollection<RebarDiameterDefinition> Defaults()
        {
            int[] diameters = { 6, 8, 10, 12, 14, 16, 18, 20, 22, 25, 28, 30, 32, 35, 40 };
            return new ObservableCollection<RebarDiameterDefinition>(diameters.Select(d => new RebarDiameterDefinition
            {
                Name = "KStruct D" + d,
                RealDiameter = d,
                ModelBarDiameter = d,
                BarDiameter = d,
                StandardBendDiameter = Math.Max(20, d + 10),
                StandardHookBendDiameter = Math.Max(20, d + 10),
                StirrupTieBendDiameter = Math.Max(20, d + 10),
                MaximumBendRadius = Math.Max(20, d + 10)
            }));
        }

        private static string GetPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KStruct", "RebarDiameterSettings.xml");
    }

    [Transaction(TransactionMode.Manual)]
    public sealed class RebarDiameterSettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            try
            {
                ObservableCollection<RebarDiameterDefinition> definitions = RebarDiameterSettingsStore.Load();
                bool? accepted = ShowSettings(data.Application.MainWindowHandle, ui.Document, definitions);
                if (accepted != true) return Result.Cancelled;
                RebarDiameterSettingsStore.Save(definitions);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Đường kính thép", ex.Message);
                return Result.Failed;
            }
        }

        private static bool? ShowSettings(IntPtr owner, Document doc, ObservableCollection<RebarDiameterDefinition> definitions)
        {
            var window = new Window
            {
                Title = "Cấu hình đường kính và bán kính uốn Rebar - KStruct",
                Width = 1120,
                Height = 610,
                MinWidth = 900,
                MinHeight = 450,
                ResizeMode = ResizeMode.CanResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new DockPanel { Margin = new Thickness(10) };
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var getData = new Button { Content = "Đọc RebarBarType trong project", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 8, 0) };
            var add = new Button { Content = "Thêm đường kính", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 8, 0) };
            var remove = new Button { Content = "Xóa dòng", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 8, 0) };
            var createTypes = new Button { Content = "Tạo / cập nhật RebarBarType", Padding = new Thickness(8, 4, 8, 4) };
            toolbar.Children.Add(getData);
            toolbar.Children.Add(add);
            toolbar.Children.Add(remove);
            toolbar.Children.Add(createTypes);
            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);

            var grid = new DataGrid
            {
                ItemsSource = definitions,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                IsReadOnly = false,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
            };
            AddColumn(grid, "Type name", nameof(RebarDiameterDefinition.Name), 150);
            AddColumn(grid, "Real Ø", nameof(RebarDiameterDefinition.RealDiameter), 70);
            AddColumn(grid, "Model Ø (mm)", nameof(RebarDiameterDefinition.ModelBarDiameter), 90);
            AddColumn(grid, "Bar Ø (mm)", nameof(RebarDiameterDefinition.BarDiameter), 90);
            AddColumn(grid, "Bend Ø (mm)", nameof(RebarDiameterDefinition.StandardBendDiameter), 100);
            AddColumn(grid, "Hook bend Ø (mm)", nameof(RebarDiameterDefinition.StandardHookBendDiameter), 115);
            AddColumn(grid, "Stirrup bend Ø (mm)", nameof(RebarDiameterDefinition.StirrupTieBendDiameter), 125);
            AddColumn(grid, "Max bend radius (mm)", nameof(RebarDiameterDefinition.MaximumBendRadius), 140);
            root.Children.Add(grid);

            var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            footer.Children.Add(new TextBlock
            {
                Text = "Thiết lập được lưu riêng trong %APPDATA%\\KStruct. Cập nhật type có thể ảnh hưởng các Rebar đang dùng type đó.",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = System.Windows.Media.Brushes.DimGray
            });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var save = new Button { Content = "Lưu", Width = 80, IsDefault = true, Margin = new Thickness(8, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 70, IsCancel = true };
            buttons.Children.Add(save);
            buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Right);
            footer.Children.Add(buttons);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            getData.Click += (sender, args) => LoadFromProject(doc, definitions);
            add.Click += (sender, args) =>
            {
                int d = definitions.Count == 0 ? 10 : Math.Max(1, definitions.Max(x => x.RealDiameter) + 2);
                definitions.Add(new RebarDiameterDefinition
                {
                    Name = "KStruct D" + d, RealDiameter = d, ModelBarDiameter = d, BarDiameter = d,
                    StandardBendDiameter = d + 10, StandardHookBendDiameter = d + 10,
                    StirrupTieBendDiameter = d + 10, MaximumBendRadius = d + 10
                });
            };
            remove.Click += (sender, args) =>
            {
                RebarDiameterDefinition row = grid.SelectedItem as RebarDiameterDefinition;
                if (row != null) definitions.Remove(row);
            };
            createTypes.Click += (sender, args) =>
            {
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
                string error = Validate(definitions);
                if (error != null) { TaskDialog.Show("KStruct - Đường kính thép", error); return; }
                TaskDialogResult answer = TaskDialog.Show("KStruct - Cập nhật Bar Types",
                    "Tạo hoặc cập nhật " + definitions.Count + " RebarBarType theo bảng?\nCác type đang được Rebar sử dụng sẽ bị cập nhật theo thông số mới.",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No);
                if (answer != TaskDialogResult.Yes) return;
                try
                {
                    RebarDiameterSettingsStore.Save(definitions);
                    CreateOrUpdateBarTypes(doc, definitions);
                    TaskDialog.Show("KStruct - Đường kính thép", "Đã tạo / cập nhật RebarBarType theo bảng.");
                }
                catch (Exception ex) { TaskDialog.Show("KStruct - Đường kính thép", ex.Message); }
            };
            save.Click += (sender, args) =>
            {
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
                string error = Validate(definitions);
                if (error != null) { TaskDialog.Show("KStruct - Đường kính thép", error); return; }
                window.DialogResult = true;
            };
            window.Content = root;
            new WindowInteropHelper(window).Owner = owner;
            return window.ShowDialog();
        }

        private static void AddColumn(DataGrid grid, string header, string property, double width)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new System.Windows.Data.Binding(property)
                {
                    Mode = System.Windows.Data.BindingMode.TwoWay,
                    UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
                },
                Width = new DataGridLength(width)
            });
        }

        private static void LoadFromProject(Document doc, ObservableCollection<RebarDiameterDefinition> definitions)
        {
            List<RebarBarType> types = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType))
                .Cast<RebarBarType>().OrderBy(x => x.BarNominalDiameter).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (types.Count == 0) { TaskDialog.Show("KStruct", "Project chưa có RebarBarType."); return; }
            var rows = types.Select(type =>
                {
                    double realDiameter = UnitUtils.ConvertFromInternalUnits(type.BarNominalDiameter, UnitTypeId.Millimeters);
                    return new RebarDiameterDefinition
                    {
                        Name = type.Name,
                        RealDiameter = (int)Math.Round(realDiameter),
                        BarDiameter = realDiameter,
                        ModelBarDiameter = UnitUtils.ConvertFromInternalUnits(type.BarModelDiameter, UnitTypeId.Millimeters),
                        StandardBendDiameter = UnitUtils.ConvertFromInternalUnits(type.StandardBendDiameter, UnitTypeId.Millimeters),
                        StandardHookBendDiameter = UnitUtils.ConvertFromInternalUnits(type.StandardHookBendDiameter, UnitTypeId.Millimeters),
                        StirrupTieBendDiameter = UnitUtils.ConvertFromInternalUnits(type.StirrupTieBendDiameter, UnitTypeId.Millimeters),
                        MaximumBendRadius = UnitUtils.ConvertFromInternalUnits(type.MaximumBendRadius, UnitTypeId.Millimeters)
                    };
                }).ToList();
            definitions.Clear();
            foreach (RebarDiameterDefinition row in rows) definitions.Add(row);
        }

        private static string Validate(ObservableCollection<RebarDiameterDefinition> definitions)
        {
            if (definitions == null || definitions.Count == 0) return "Thêm ít nhất một dòng đường kính.";
            if (definitions.Any(x => x == null || string.IsNullOrWhiteSpace(x.Name) || x.RealDiameter <= 0 ||
                x.ModelBarDiameter <= 0 || x.BarDiameter <= 0 || x.StandardBendDiameter <= 0 ||
                x.StandardHookBendDiameter <= 0 || x.StirrupTieBendDiameter <= 0 || x.MaximumBendRadius <= 0))
                return "Tên type và mọi đường kính/bán kính phải có giá trị dương.";
            if (definitions.GroupBy(x => x.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
                return "Tên RebarBarType trong bảng phải duy nhất.";
            return null;
        }

        private static void CreateOrUpdateBarTypes(Document doc, ObservableCollection<RebarDiameterDefinition> definitions)
        {
            using (Transaction tx = new Transaction(doc, "KStruct - Tạo/cập nhật RebarBarType"))
            {
                tx.Start();
                List<RebarBarType> existing = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType))
                    .Cast<RebarBarType>().ToList();
                RebarBarType template = existing.FirstOrDefault();
                bool useDefaultAsFirstType = template == null;
                if (template == null) template = doc.GetElement(RebarBarType.CreateDefaultRebarBarType(doc)) as RebarBarType;
                foreach (RebarDiameterDefinition definition in definitions)
                {
                    string name = definition.Name.Trim();
                    RebarBarType type = existing.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (type == null)
                    {
                        if (template == null) throw new InvalidOperationException("Không tạo được RebarBarType mẫu.");
                        if (useDefaultAsFirstType)
                        {
                            type = template;
                            type.Name = name;
                            useDefaultAsFirstType = false;
                        }
                        else type = template.Duplicate(name) as RebarBarType;
                        if (type == null) throw new InvalidOperationException("Không duplicate được RebarBarType '" + name + "'.");
                        existing.Add(type);
                    }
                    double barDiameter = UnitUtils.ConvertToInternalUnits(definition.BarDiameter, UnitTypeId.Millimeters);
                    double modelDiameter = UnitUtils.ConvertToInternalUnits(definition.ModelBarDiameter, UnitTypeId.Millimeters);
                    type.BarNominalDiameter = barDiameter;
                    type.BarModelDiameter = modelDiameter;
                    type.StandardBendDiameter = UnitUtils.ConvertToInternalUnits(Math.Max(definition.StandardBendDiameter, definition.BarDiameter + 10), UnitTypeId.Millimeters);
                    type.StandardHookBendDiameter = UnitUtils.ConvertToInternalUnits(Math.Max(definition.StandardHookBendDiameter, definition.BarDiameter + 10), UnitTypeId.Millimeters);
                    type.StirrupTieBendDiameter = UnitUtils.ConvertToInternalUnits(Math.Max(definition.StirrupTieBendDiameter, definition.BarDiameter + 10), UnitTypeId.Millimeters);
                    type.MaximumBendRadius = UnitUtils.ConvertToInternalUnits(Math.Max(definition.MaximumBendRadius, definition.BarDiameter), UnitTypeId.Millimeters);
                }
                if (tx.Commit() != TransactionStatus.Committed)
                    throw new InvalidOperationException("Revit không chấp nhận cập nhật RebarBarType.");
            }
        }
    }
}
