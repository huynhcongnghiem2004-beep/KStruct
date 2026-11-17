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

namespace SDK_HD.KStruct.NativeRebar
{
    [Transaction(TransactionMode.Manual)]
    public sealed class CountHostMembersOnRebarCommand : IExternalCommand
    {
        private enum Scope { Selection, ActiveView, EntireProject }

        private sealed class ScopeChoice
        {
            public Scope Value { get; private set; }
            public string Display { get; private set; }
            public ScopeChoice(Scope value, string display) { Value = value; Display = display; }
        }

        private sealed class ParameterChoice
        {
            public string Name { get; private set; }
            public string Display { get; private set; }
            public ParameterChoice(string name) { Name = name; Display = name; }
        }

        private sealed class LevelChoice
        {
            public Level Level { get; private set; }
            public string Display { get; private set; }
            public LevelChoice(Level level) { Level = level; Display = level == null ? "<None>" : level.Name; }
        }

        private sealed class HostFilterOptions
        {
            public Scope Scope;
            public string ParameterName;
            public Level Level;
            public bool Columns, Beams, Floors, Walls, Foundations;
        }

        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            UIDocument ui = data.Application.ActiveUIDocument;
            if (ui == null || ui.Document.IsFamilyDocument) return Result.Cancelled;
            Document doc = ui.Document;
            try
            {
                List<Rebar> sampleBars = new FilteredElementCollector(doc).OfClass(typeof(Rebar))
                    .WhereElementIsNotElementType().Cast<Rebar>().ToList();
                if (sampleBars.Count == 0)
                    throw new InvalidOperationException("Project chưa có Rebar để nhận kết quả đếm.");
                List<string> parameterNames = sampleBars[0].Parameters.Cast<Parameter>()
                    .Where(IsWritableInteger).Select(x => x.Definition.Name).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
                if (parameterNames.Count == 0)
                    throw new InvalidOperationException("Rebar chưa có tham số instance Integer có thể ghi.");

                List<Level> levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(x => x.Elevation).ToList();
                List<Element> currentSelection = ui.Selection.GetElementIds().Select(doc.GetElement)
                    .Where(x => x != null).ToList();
                HostFilterOptions options = ChooseOptions(data.Application.MainWindowHandle, parameterNames, levels,
                    currentSelection.Count > 0 ? Scope.Selection : Scope.ActiveView);
                if (options == null) return Result.Cancelled;

                List<Element> scopeElements;
                if (options.Scope == Scope.Selection)
                {
                    if (currentSelection.Count == 0)
                    {
                        IList<Reference> picked = ui.Selection.PickObjects(ObjectType.Element,
                            "Chọn các cấu kiện để đếm cấu kiện cùng Mark");
                        currentSelection = picked.Select(doc.GetElement).Where(x => x != null).ToList();
                    }
                    scopeElements = currentSelection;
                }
                else if (options.Scope == Scope.ActiveView)
                    scopeElements = new FilteredElementCollector(doc, ui.ActiveView.Id).WhereElementIsNotElementType().ToElements().ToList();
                else
                    scopeElements = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements().ToList();

                List<Element> hosts = FilterHosts(doc, scopeElements, options, levels);
                if (hosts.Count == 0)
                {
                    TaskDialog.Show("KStruct - Đếm cấu kiện", "Không tìm thấy cấu kiện kết cấu trong phạm vi đã chọn.");
                    return Result.Cancelled;
                }

                var allByCategory = new Dictionary<long, List<Element>>();
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                int changed = 0, missingHostedBars = 0, missingParameters = 0;
                using (Transaction tx = new Transaction(doc, "KStruct - Ghi số cấu kiện lên Rebar"))
                {
                    tx.Start();
                    foreach (Element host in hosts)
                    {
                        string mark = ReadMark(host);
                        if (string.IsNullOrWhiteSpace(mark)) continue;
                        IList<Element> hosted = GetHostedReinforcement(host);
                        if (hosted.Count == 0) { missingHostedBars++; continue; }
                        long categoryId = host.Category.Id.Value;
                        List<Element> categoryElements;
                        if (!allByCategory.TryGetValue(categoryId, out categoryElements))
                        {
                            categoryElements = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                                .Where(x => x.Category != null && x.Category.Id.Value == categoryId).ToList();
                            allByCategory.Add(categoryId, categoryElements);
                        }
                        string countKey = host.GetType().FullName + "|" + categoryId + "|" + mark;
                        int memberCount;
                        if (!counts.TryGetValue(countKey, out memberCount))
                        {
                            memberCount = categoryElements.Count(x => x.GetType() == host.GetType() &&
                                string.Equals(ReadMark(x), mark, StringComparison.Ordinal));
                            counts.Add(countKey, memberCount);
                        }
                        foreach (Element rebar in hosted)
                        {
                            Parameter parameter = rebar.LookupParameter(options.ParameterName);
                            if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.Integer)
                            {
                                missingParameters++;
                                continue;
                            }
                            if (parameter.AsInteger() == memberCount) continue;
                            parameter.Set(memberCount);
                            changed++;
                        }
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("Revit không chấp nhận cập nhật số cấu kiện.");
                }

                string report = "Đã ghi " + changed + " giá trị vào tham số '" + options.ParameterName + "' cho " + hosts.Count + " cấu kiện.";
                if (missingHostedBars > 0) report += "\nKhông có Rebar/AreaReinforcement: " + missingHostedBars + " cấu kiện.";
                if (missingParameters > 0) report += "\nBỏ qua phần tử không có tham số integer phù hợp: " + missingParameters + ".";
                TaskDialog.Show("KStruct - Đếm cấu kiện", report);
                return Result.Succeeded;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                message = ex.ToString();
                TaskDialog.Show("KStruct - Đếm cấu kiện", ex.Message);
                return Result.Failed;
            }
        }

        private static List<Element> FilterHosts(Document doc, IEnumerable<Element> elements,
            HostFilterOptions options, IList<Level> levels)
        {
            var result = new List<Element>();
            foreach (Element element in elements)
            {
                if (element == null || element.Category == null || element is Rebar) continue;
                BuiltInCategory category = (BuiltInCategory)element.Category.Id.Value;
                bool include =
                    (options.Columns && category == BuiltInCategory.OST_StructuralColumns) ||
                    (options.Beams && category == BuiltInCategory.OST_StructuralFraming) ||
                    (options.Floors && category == BuiltInCategory.OST_Floors && IsStructuralFloor(element)) ||
                    (options.Walls && category == BuiltInCategory.OST_Walls && IsStructuralWall(element)) ||
                    (options.Foundations && category == BuiltInCategory.OST_StructuralFoundation);
                if (!include || options.Level == null) { if (include) result.Add(element); continue; }
                BoundingBoxXYZ box = element.get_BoundingBox(null);
                if (box == null || levels.Count == 0) continue;
                double middle = (box.Min.Z + box.Max.Z) * 0.5;
                Level nearest = levels.OrderBy(x => Math.Abs(x.Elevation - middle)).FirstOrDefault();
                if (nearest != null && nearest.Id == options.Level.Id) result.Add(element);
            }
            return result.Distinct().ToList();
        }

        private static bool IsStructuralFloor(Element element)
        {
            Parameter parameter = element.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL);
            return parameter != null && parameter.HasValue && parameter.AsInteger() == 1;
        }

        private static bool IsStructuralWall(Element element)
        {
            Parameter parameter = element.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT);
            return parameter != null && parameter.HasValue && parameter.AsInteger() == 1;
        }

        private static string ReadMark(Element element)
        {
            Parameter parameter = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK) ?? element.LookupParameter("Mark");
            return parameter == null || !parameter.HasValue ? string.Empty : parameter.AsString() ?? string.Empty;
        }

        private static IList<Element> GetHostedReinforcement(Element host)
        {
            RebarHostData hostData = RebarHostData.GetRebarHostData(host);
            if (hostData == null) return new List<Element>();
            var result = hostData.GetRebarsInHost().Where(x => x != null && x.IsValidObject).Cast<Element>().ToList();
            result.AddRange(hostData.GetAreaReinforcementsInHost().Where(x => x != null && x.IsValidObject).Cast<Element>());
            return result;
        }

        private static bool IsWritableInteger(Parameter parameter)
        {
            return parameter != null && !parameter.IsReadOnly && parameter.UserModifiable &&
                parameter.StorageType == StorageType.Integer && parameter.Definition.GetDataType() == SpecTypeId.Int.Integer;
        }

        private static HostFilterOptions ChooseOptions(IntPtr owner, IList<string> parameterNames,
            IList<Level> levels, Scope defaultScope)
        {
            var window = new Window
            {
                Title = "Đếm số cấu kiện cùng Mark trên cốt thép - KStruct",
                Width = 500,
                Height = 520,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            var root = new StackPanel { Margin = new Thickness(12) };
            var scopeChoices = new List<ScopeChoice>
            {
                new ScopeChoice(Scope.Selection, "Phần tử đang chọn"),
                new ScopeChoice(Scope.ActiveView, "Phần tử trong view hiện hành"),
                new ScopeChoice(Scope.EntireProject, "Toàn bộ project")
            };
            root.Children.Add(new TextBlock { Text = "Phạm vi cấu kiện:", Margin = new Thickness(0, 0, 0, 4) });
            var scope = new System.Windows.Controls.ComboBox { MinWidth = 430, DisplayMemberPath = "Display", ItemsSource = scopeChoices,
                SelectedItem = scopeChoices.First(x => x.Value == defaultScope) };
            root.Children.Add(scope);
            var levelChoices = new List<LevelChoice> { new LevelChoice(null) };
            levelChoices.AddRange(levels.Select(x => new LevelChoice(x)));
            root.Children.Add(new TextBlock { Text = "Level gần tâm cấu kiện (tùy chọn):", Margin = new Thickness(0, 8, 0, 4) });
            var level = new System.Windows.Controls.ComboBox { MinWidth = 430, DisplayMemberPath = "Display", ItemsSource = levelChoices, SelectedIndex = 0 };
            root.Children.Add(level);
            root.Children.Add(new TextBlock { Text = "Loại cấu kiện:", Margin = new Thickness(0, 8, 0, 4) });
            var columns = new CheckBox { Content = "Cột", IsChecked = true };
            var beams = new CheckBox { Content = "Dầm", IsChecked = true };
            var floors = new CheckBox { Content = "Sàn kết cấu", IsChecked = true };
            var walls = new CheckBox { Content = "Tường kết cấu", IsChecked = true };
            var foundations = new CheckBox { Content = "Móng", IsChecked = true };
            foreach (CheckBox check in new[] { columns, beams, floors, walls, foundations })
                root.Children.Add(check);
            root.Children.Add(new TextBlock { Text = "Tham số Integer trên Rebar để ghi số cấu kiện:", Margin = new Thickness(0, 8, 0, 4) });
            var parameter = new System.Windows.Controls.ComboBox { MinWidth = 430, ItemsSource = parameterNames, SelectedIndex = 0 };
            root.Children.Add(parameter);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "Đếm và ghi", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Hủy", Width = 72, IsCancel = true };
            ok.Click += (sender, args) =>
            {
                if (parameter.SelectedItem == null || !(columns.IsChecked == true || beams.IsChecked == true || floors.IsChecked == true || walls.IsChecked == true || foundations.IsChecked == true))
                {
                    TaskDialog.Show("KStruct - Đếm cấu kiện", "Chọn tham số và ít nhất một loại cấu kiện.");
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
            ScopeChoice selectedScope = (ScopeChoice)scope.SelectedItem;
            return new HostFilterOptions
            {
                Scope = selectedScope.Value,
                ParameterName = parameter.SelectedItem.ToString(),
                Level = ((LevelChoice)level.SelectedItem).Level,
                Columns = columns.IsChecked == true,
                Beams = beams.IsChecked == true,
                Floors = floors.IsChecked == true,
                Walls = walls.IsChecked == true,
                Foundations = foundations.IsChecked == true
            };
        }
    }
}
