using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Grid = System.Windows.Controls.Grid;

namespace SDK_HD.KStruct.NativeRebar
{
    internal static class BeamRebarShapeSelectionWindow
    {
        private sealed class ShapeChoice
        {
            public RebarShape Shape { get; private set; }
            public string Display { get; private set; }
            public ShapeChoice(RebarShape shape)
            {
                Shape = shape;
                Display = shape == null ? "(Chưa chọn)" : shape.Name + " | " + shape.RebarStyle +
                    " | Id " + shape.Id.Value;
            }
        }

        private sealed class HookChoice
        {
            public RebarHookType Hook { get; private set; }
            public string Display { get; private set; }
            public HookChoice(RebarHookType hook)
            {
                Hook = hook;
                Display = hook == null ? "(Không dùng HookType)" : hook.Name + " | Id " + hook.Id.Value;
            }
        }

        private sealed class ShapeRow
        {
            public string Label;
            public RebarStyle Style;
            public bool Required;
            public RebarShape Selected;
            public ComboBox Combo;
            public string GeometryWarning;
        }

        internal static ReinforceStraightBeamsCommand.Options Show(IntPtr owner, Document doc,
            IList<RebarHookType> hookTypes,
            ReinforceStraightBeamsCommand.Options options)
        {
            if (doc == null || options == null) return options;
            List<RebarShape> shapes = new FilteredElementCollector(doc).OfClass(typeof(RebarShape))
                .Cast<RebarShape>().OrderBy(shape => shape.RebarStyle)
                .ThenBy(shape => shape.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(shape => shape.Id.Value).ToList();
            var rows = new List<ShapeRow>
            {
                Row("Đai / stirrup-tie", RebarStyle.StirrupTie, true, options.TieShape),
                Row("Thép chủ trên (kể cả shape neo/móc nếu có)", RebarStyle.Standard, options.TopCount > 0, options.TopBarShape),
                Row("Thép chủ dưới (kể cả shape neo/móc nếu có)", RebarStyle.Standard, options.BottomCount > 0, options.BottomBarShape),
                Row("Thép gia cường trên - lớp 2", RebarStyle.Standard, options.TopLayer2Count > 0, options.TopLayer2Shape),
                Row("Thép gia cường trên - lớp 3", RebarStyle.Standard, options.TopLayer3Count > 0, options.TopLayer3Shape),
                Row("Thép gia cường dưới - lớp 2", RebarStyle.Standard, options.BottomLayer2Count > 0, options.BottomLayer2Shape),
                Row("Thép gia cường dưới - lớp 3", RebarStyle.Standard, options.BottomLayer3Count > 0, options.BottomLayer3Shape),
                Row("Thép chống phình hai bên", RebarStyle.Standard, options.SideBarCount > 0, options.SideBarShape)
            };

            // Do not infer a bent bar's Shape from its *name*. Standard shapes
            // such as M_01 can be geometrically incompatible with the requested
            // U/L anchorage. The user must explicitly choose a candidate; the
            // command subsequently validates it using actual curves and hooks.
            foreach (ShapeRow row in rows)
                row.GeometryWarning = GetUnverifiedBentBarWarning(row, options);

            var window = new Window
            {
                Title = "KStruct | Tự động nhận diện Rebar Shape thép dầm",
                Width = 850,
                Height = 700,
                MinWidth = 640,
                MinHeight = 520,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                WindowStyle = WindowStyle.SingleBorderWindow,
                ShowInTaskbar = false
            };
            if (owner != IntPtr.Zero) new WindowInteropHelper(window).Owner = owner;

            var root = new DockPanel { Margin = new Thickness(18) };
            var note = new TextBlock
            {
                Text = "KStruct tự đề xuất Rebar Shape từ các kiểu đang có trong project. Các lựa chọn được giữ nguyên nếu đã lưu và còn phù hợp về Style; khi có nhiều Shape, hệ thống ưu tiên các mẫu thông dụng. Có thể chỉnh từng dòng trước khi tiếp tục. Revit sẽ kiểm tra chính xác hình học, HookType và khả năng bố trí trước khi thay thép cũ.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            };
            DockPanel.SetDock(note, Dock.Top);
            root.Children.Add(note);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var auto = new Button { Content = "Tự nhận diện lại", MinWidth = 148, Margin = new Thickness(5, 0, 12, 0) };
            var cancel = new Button { Content = "Hủy", MinWidth = 90, Margin = new Thickness(5, 0, 0, 0), IsCancel = true };
            var accept = new Button { Content = "Tiếp tục", MinWidth = 110, Margin = new Thickness(5, 0, 0, 0), IsDefault = true };
            buttons.Children.Add(auto);
            buttons.Children.Add(cancel);
            buttons.Children.Add(accept);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            // Explain that a suggested name is only a candidate, never proof of geometry compatibility.
            var diagnostic = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 8),
                Foreground = System.Windows.Media.Brushes.DarkSlateBlue
            };
            DockPanel.SetDock(diagnostic, Dock.Bottom);
            root.Children.Add(diagnostic);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            List<HookChoice> startHookChoices = GetHookChoices(hookTypes, options.TieType, options.TieShape, 0);
            List<HookChoice> endHookChoices = GetHookChoices(hookTypes, options.TieType, options.TieShape, 1);
            AddHookRow(grid, 0, "HookType đầu đai (tùy chọn)", startHookChoices, options.TieStartHook, out ComboBox startHookCombo);
            AddHookRow(grid, 1, "HookType cuối đai (tùy chọn)", endHookChoices, options.TieEndHook, out ComboBox endHookCombo);
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = grid };
            root.Children.Add(scroll);
            for (int i = 0; i < rows.Count; i++)
            {
                ShapeRow row = rows[i];
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var label = new TextBlock
                {
                    Text = row.Label + (row.Required ? " *" : ""),
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 5, 10, 5)
                };
                Grid.SetRow(label, i + 2);
                Grid.SetColumn(label, 0);
                grid.Children.Add(label);
                List<ShapeChoice> choices = shapes.Where(shape => shape.RebarStyle == row.Style)
                    .Select(shape => new ShapeChoice(shape)).ToList();
                choices.Insert(0, new ShapeChoice(null));
                row.Combo = new ComboBox
                {
                    ItemsSource = choices,
                    DisplayMemberPath = "Display",
                    SelectedIndex = string.IsNullOrEmpty(row.GeometryWarning)
                        ? FindSuggestedShapeIndex(choices, row.Style, row.Selected) : 0,
                    MinHeight = 26,
                    Margin = new Thickness(0, 3, 0, 3),
                    IsEnabled = choices.Count > 1
                };
                Grid.SetRow(row.Combo, i + 2);
                Grid.SetColumn(row.Combo, 1);
                grid.Children.Add(row.Combo);
            }

            var rowStatus = new Dictionary<ShapeRow, TextBlock>();
            // Distinguish a manual selection from an automatic name-based suggestion.
            foreach (ShapeRow row in rows)
            {
                TextBlock status = new TextBlock
                {
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(4, 0, 0, 4),
                    Foreground = System.Windows.Media.Brushes.DarkSlateGray
                };
                rowStatus[row] = status;
                var original = row.Combo;
                var holder = new StackPanel();
                // WPF child must be detached before attaching to a new logical parent.
                // Otherwise AddLogicalChild throws InvalidOperationException on window open.
                grid.Children.Remove(original);
                holder.Children.Add(original);
                holder.Children.Add(status);
                Grid.SetRow(holder, rows.IndexOf(row) + 2);
                Grid.SetColumn(holder, 1);
                grid.Children.Add(holder);
            }

            Action refreshDiagnostic = () =>
            {
                int required = rows.Count(r => r.Required);
                int missing = rows.Count(r => r.Required && Selected(r) == null);
                RebarShape tieShape = Selected(rows[0]);
                int startAngle = tieShape?.GetDefaultHookAngle(0) ?? 0;
                int endAngle = tieShape?.GetDefaultHookAngle(1) ?? 0;
                bool missingStartHook = startAngle != 0 && (startHookCombo.SelectedItem as HookChoice)?.Hook == null;
                bool missingEndHook = endAngle != 0 && (endHookCombo.SelectedItem as HookChoice)?.Hook == null;
                foreach (ShapeRow row in rows)
                {
                    RebarShape chosen = Selected(row);
                    rowStatus[row].Text = chosen == null
                        ? (!string.IsNullOrEmpty(row.GeometryWarning)
                            ? row.GeometryWarning + " Chọn Shape thủ công; sẽ kiểm tra hình học trước khi tạo."
                            : (row.Required ? "Chưa chọn Shape bắt buộc" : "Không dùng Shape (nhóm không bắt buộc)"))
                        : "Shape đã chọn: " + chosen.Name + " | Chưa xác minh hình học";
                    rowStatus[row].Foreground = chosen == null && row.Required
                        ? System.Windows.Media.Brushes.Firebrick
                        : System.Windows.Media.Brushes.DarkSlateGray;
                }
                diagnostic.Text = "Đã chọn " + (required - missing) + "/" + required +
                    " nhóm bắt buộc. " + (missing > 0 ? "Còn " + missing + " nhóm chưa chọn. " : "") +
                    (missingStartHook || missingEndHook ? "Thiếu HookType tương thích cho đai. " : "") +
                    "Tên Shape chỉ là gợi ý; hình học, lớp bảo vệ và bố trí vẫn phải được kiểm chứng trước khi ghi Rebar.";
                accept.IsEnabled = missing == 0 && !missingStartHook && !missingEndHook;
            };
            foreach (ShapeRow shapeRow in rows)
                shapeRow.Combo.SelectionChanged += (sender, args) => refreshDiagnostic();
            startHookCombo.SelectionChanged += (sender, args) => refreshDiagnostic();
            endHookCombo.SelectionChanged += (sender, args) => refreshDiagnostic();

            // Shape ComboBox controls must be initialized before wiring events.
            // Otherwise rows[0].Combo is null and the command fails before creating any rebar.
            rows[0].Combo.SelectionChanged += (sender, args) =>
            {
                RebarShape selectedShape = Selected(rows[0]);
                UpdateHookChoices(startHookCombo, GetHookChoices(hookTypes, options.TieType, selectedShape, 0), options.TieStartHook);
                UpdateHookChoices(endHookCombo, GetHookChoices(hookTypes, options.TieType, selectedShape, 1), options.TieEndHook);
            };
            // Synchronize hook availability for the initially selected stirrup shape.
            RebarShape initialTieShape = Selected(rows[0]);
            UpdateHookChoices(startHookCombo,
                GetHookChoices(hookTypes, options.TieType, initialTieShape, 0), options.TieStartHook);
            UpdateHookChoices(endHookCombo,
                GetHookChoices(hookTypes, options.TieType, initialTieShape, 1), options.TieEndHook);

            // Manual overrides are never silently reset during ordinary selection changes.
            // The explicit button allows the user to request fresh suggestions.
            auto.Click += (sender, args) =>
            {
                foreach (ShapeRow row in rows)
                {
                    var available = row.Combo.ItemsSource.Cast<ShapeChoice>().ToList();
                    // Never overwrite a manual selection or silently propose a
                    // generic straight Shape for requested bends.
                    row.Combo.SelectedIndex = string.IsNullOrEmpty(row.GeometryWarning)
                        ? FindSuggestedShapeIndex(available, row.Style, null) : 0;
                }
                RebarShape tie = Selected(rows[0]);
                UpdateHookChoices(startHookCombo,
                    GetHookChoices(hookTypes, options.TieType, tie, 0), options.TieStartHook);
                UpdateHookChoices(endHookCombo,
                    GetHookChoices(hookTypes, options.TieType, tie, 1), options.TieEndHook);
            };

            refreshDiagnostic();

            accept.Click += (sender, args) =>
            {
                var missing = rows.Where(row => row.Required && Selected(row) == null)
                    .Select(row => row.Label).ToList();
                if (Selected(rows[0]) != null)
                {
                    if (Selected(rows[0]).GetDefaultHookAngle(0) != 0 && SelectedHook(startHookCombo,
                        hookTypes, options.TieType, Selected(rows[0]), 0) == null)
                        missing.Add("HookType đầu tương thích với shape đai");
                    if (Selected(rows[0]).GetDefaultHookAngle(1) != 0 && SelectedHook(endHookCombo,
                        hookTypes, options.TieType, Selected(rows[0]), 1) == null)
                        missing.Add("HookType cuối tương thích với shape đai");
                }
                if (missing.Count > 0)
                {
                    MessageBox.Show(window, "Thiếu shape/HookType tương thích cho: " + string.Join(", ", missing) +
                        ". Nạp shape hoặc HookType phù hợp vào project rồi chọn lại.", "KStruct - Rebar Shape",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                options.TieShape = Selected(rows[0]);
                options.TieStartHook = SelectedHook(startHookCombo, hookTypes, options.TieType, options.TieShape, 0);
                options.TieEndHook = SelectedHook(endHookCombo, hookTypes, options.TieType, options.TieShape, 1);
                options.TopBarShape = Selected(rows[1]);
                options.BottomBarShape = Selected(rows[2]);
                options.TopLayer2Shape = Selected(rows[3]);
                options.TopLayer3Shape = Selected(rows[4]);
                options.BottomLayer2Shape = Selected(rows[5]);
                options.BottomLayer3Shape = Selected(rows[6]);
                options.SideBarShape = Selected(rows[7]);
                window.DialogResult = true;
            };
            window.Content = root;
            return window.ShowDialog() == true ? options : null;
        }

        // Non-interactive replacement for Show(): fills every missing Shape/Hook choice.
        // Tie: saved choice, else best-scored StirrupTie shape. Hooks: compatible 135-degree
        // hook when the shape needs one. Straight longitudinal groups: saved choice, else
        // M_01-style suggestion. Bent/stepped groups stay null on purpose: the real Revit
        // curves pick a geometrically matching Shape at creation time.
        internal static ReinforceStraightBeamsCommand.Options AutoAssign(Document doc,
            ReinforceStraightBeamsCommand.Options options)
        {
            if (doc == null || options == null) return options;
            List<RebarHookType> hookTypes = new FilteredElementCollector(doc).OfClass(typeof(RebarHookType))
                .Cast<RebarHookType>().OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            List<RebarShape> shapes = new FilteredElementCollector(doc).OfClass(typeof(RebarShape))
                .Cast<RebarShape>().ToList();

            if (options.TieShape == null || options.TieShape.RebarStyle != RebarStyle.StirrupTie)
                options.TieShape = shapes.Where(x => x.RebarStyle == RebarStyle.StirrupTie)
                    .OrderByDescending(x => ScoreShapeName(x, RebarStyle.StirrupTie))
                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Id.Value).FirstOrDefault();
            if (options.TieShape != null)
            {
                List<HookChoice> startChoices = GetHookChoices(hookTypes, options.TieType, options.TieShape, 0);
                List<HookChoice> endChoices = GetHookChoices(hookTypes, options.TieType, options.TieShape, 1);
                options.TieStartHook = PickHook(startChoices, options.TieStartHook);
                options.TieEndHook = PickHook(endChoices, options.TieEndHook);
            }

            var rows = new[]
            {
                Tuple.Create(Row("Thép chủ trên", RebarStyle.Standard, options.TopCount > 0, options.TopBarShape), 1),
                Tuple.Create(Row("Thép chủ dưới", RebarStyle.Standard, options.BottomCount > 0, options.BottomBarShape), 2),
                Tuple.Create(Row("Thép gia cường trên - lớp 2", RebarStyle.Standard, options.TopLayer2Count > 0, options.TopLayer2Shape), 3),
                Tuple.Create(Row("Thép gia cường trên - lớp 3", RebarStyle.Standard, options.TopLayer3Count > 0, options.TopLayer3Shape), 4),
                Tuple.Create(Row("Thép gia cường dưới - lớp 2", RebarStyle.Standard, options.BottomLayer2Count > 0, options.BottomLayer2Shape), 5),
                Tuple.Create(Row("Thép gia cường dưới - lớp 3", RebarStyle.Standard, options.BottomLayer3Count > 0, options.BottomLayer3Shape), 6),
                Tuple.Create(Row("Thép chống phình hai bên", RebarStyle.Standard, options.SideBarCount > 0, options.SideBarShape), 7)
            };
            foreach (var item in rows)
            {
                ShapeRow row = item.Item1;
                if (row.Selected != null && row.Selected.RebarStyle != RebarStyle.Standard) row.Selected = null;
                if (row.Selected == null && row.Required && GetUnverifiedBentBarWarning(row, options) == null)
                    row.Selected = shapes.Where(x => x.RebarStyle == RebarStyle.Standard)
                        .OrderByDescending(x => ScoreShapeName(x, RebarStyle.Standard))
                        .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(x => x.Id.Value).FirstOrDefault();
                switch (item.Item2)
                {
                    case 1: options.TopBarShape = row.Selected; break;
                    case 2: options.BottomBarShape = row.Selected; break;
                    case 3: options.TopLayer2Shape = row.Selected; break;
                    case 4: options.TopLayer3Shape = row.Selected; break;
                    case 5: options.BottomLayer2Shape = row.Selected; break;
                    case 6: options.BottomLayer3Shape = row.Selected; break;
                    default: options.SideBarShape = row.Selected; break;
                }
            }
            return options;
        }

        private static RebarHookType PickHook(IList<HookChoice> choices, RebarHookType current)
        {
            if (current != null && choices.Any(c => c.Hook?.Id == current.Id)) return current;
            int index = FindSuggestedHookIndex(choices);
            return index >= 0 && index < choices.Count ? choices[index].Hook : null;
        }

        // Deliberately conservative preflight for the most common false auto-match:
        // a straight name-ranked Shape selected for a top bar with bent anchorage,
        // or a stepped-bottom bar requiring multiple segments. No automatic
        // compatibility claim is possible until actual Revit curves exist.
        private static string GetUnverifiedBentBarWarning(ShapeRow row,
            ReinforceStraightBeamsCommand.Options options)
        {
            if (row == null || row.Style != RebarStyle.Standard || !row.Required || options == null)
                return null;
            string key = row.Label.StartsWith("Thép chủ trên", StringComparison.Ordinal) ? "top" :
                row.Label.StartsWith("Thép chủ dưới", StringComparison.Ordinal) ? "bottom" :
                row.Label.StartsWith("Thép gia cường trên - lớp 2", StringComparison.Ordinal) ? "top1" :
                row.Label.StartsWith("Thép gia cường trên - lớp 3", StringComparison.Ordinal) ? "top2" :
                row.Label.StartsWith("Thép gia cường dưới - lớp 2", StringComparison.Ordinal) ? "bottom1" :
                row.Label.StartsWith("Thép gia cường dưới - lớp 3", StringComparison.Ordinal) ? "bottom2" : null;
            if (key == null) return null;
            var detailedBars = options.DetailedBars ?? new List<ReinforceStraightBeamsCommand.DetailedBarSpec>();
            bool hasBentAnchor = key.StartsWith("top", StringComparison.Ordinal) &&
                detailedBars.Any(bar => bar != null && bar.Key == key && bar.HookDownMm > 0);
            bool hasSteppedBottom = key.StartsWith("bottom", StringComparison.Ordinal) &&
                options.PreviewBeamSections != null &&
                options.PreviewBeamSections.Where(section => section != null)
                    .Select(section => section.BottomElevationMm)
                    .Distinct().Skip(1).Any();
            if (!hasBentAnchor && !hasSteppedBottom) return null;
            return hasBentAnchor
                ? "Nhóm có neo gập/uốn xuống: không tự gán Shape thẳng theo tên."
                : "Dầm có đáy thay đổi cao độ: Shape phải khớp các đoạn thép uốn chuyển tiếp.";
        }

        // Prefer a previously saved project shape. Otherwise suggest a common project
        // shape by style/name; final geometric compatibility is checked by the Rebar engine.
        // This is a proposal, NOT proof that the selected shape can form every bar.
        private static int FindSuggestedShapeIndex(IList<ShapeChoice> choices,
            RebarStyle style, RebarShape saved)
        {
            if (saved != null)
            {
                int savedIndex = choices.ToList().FindIndex(choice =>
                    choice.Shape != null && choice.Shape.Id == saved.Id &&
                    choice.Shape.RebarStyle == style);
                if (savedIndex > 0) return savedIndex;
            }

            var candidates = choices.Select((choice, index) => new { choice, index })
                .Where(item => item.choice.Shape != null && item.choice.Shape.RebarStyle == style)
                .OrderByDescending(item => ScoreShapeName(item.choice.Shape, style))
                .ThenBy(item => item.choice.Shape.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.choice.Shape.Id.Value)
                .ToList();
            return candidates.Count == 0 ? 0 : candidates[0].index;
        }

        internal static int ScoreShapeName(RebarShape shape, RebarStyle style)
        {
            string name = (shape.Name ?? "").ToUpperInvariant().Replace(" ", "");
            if (style == RebarStyle.StirrupTie)
            {
                if (name.Contains("BS_M_T1") || name.Contains("STIRRUP") || name.Contains("ĐAI")) return 50;
                if (name.Contains("T1") || name.Contains("TIE")) return 30;
                return 1;
            }
            if (name == "M_01" || name == "M-01") return 50;
            if (name.Contains("M_01") || name.Contains("STRAIGHT")) return 40;
            if (name == "M_00" || name == "M-00") return 30;
            return 1;
        }

        private static ShapeRow Row(string label, RebarStyle style, bool required, RebarShape selected)
        {
            return new ShapeRow { Label = label, Style = style, Required = required, Selected = selected };
        }

        private static RebarShape Selected(ShapeRow row) =>
            (row.Combo?.SelectedItem as ShapeChoice)?.Shape;

        private static void AddHookRow(Grid grid, int row, string labelText, IList<HookChoice> choices,
            RebarHookType selected, out ComboBox combo)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock
            {
                Text = labelText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 5, 10, 5)
            };
            Grid.SetRow(label, row);
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);
            int index = choices.ToList().FindIndex(choice => choice.Hook?.Id == selected?.Id);
            combo = new ComboBox
            {
                ItemsSource = choices,
                DisplayMemberPath = "Display",
                SelectedIndex = Math.Max(0, index),
                MinHeight = 26,
                Margin = new Thickness(0, 3, 0, 3)
            };
            Grid.SetRow(combo, row);
            Grid.SetColumn(combo, 1);
            grid.Children.Add(combo);
        }

        private static List<HookChoice> GetHookChoices(IList<RebarHookType> hookTypes, RebarBarType barType,
            RebarShape shape, int end)
        {
            int requiredAngle = shape == null ? 0 : shape.GetDefaultHookAngle(end);
            if (shape != null && requiredAngle == 0)
                return new List<HookChoice> { new HookChoice(null) };
            var compatible = (hookTypes ?? new List<RebarHookType>())
                .Where(hook => hook != null && hook.Style == RebarStyle.StirrupTie &&
                    barType != null && HasHookPermission(barType, hook) &&
                    (shape == null || (requiredAngle != 0 && HookAngleMatchesDefault(hook, requiredAngle))))
                .OrderBy(hook => hook.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(hook => new HookChoice(hook)).ToList();
            compatible.Insert(0, new HookChoice(null));
            return compatible;
        }

        private static bool HasHookPermission(RebarBarType type, RebarHookType hook)
        {
            try { return type.GetHookPermission(hook.Id); }
            catch { return false; }
        }

        private static bool HookAngleMatchesDefault(RebarHookType hook, int defaultAngle)
        {
            double degrees = hook.HookAngle * 180.0 / Math.PI;
            return Math.Abs(degrees - defaultAngle) < 0.1 ||
                Math.Abs(hook.HookAngle - defaultAngle) < 0.1;
        }

        private static void UpdateHookChoices(ComboBox combo, IList<HookChoice> choices, RebarHookType selected)
        {
            int index = choices.ToList().FindIndex(choice => choice.Hook?.Id == selected?.Id);
            combo.ItemsSource = choices;
            // An explicit saved selection is retained when compatible. A new
            // suggestion is only made when the current shape requires a hook.
            combo.SelectedIndex = index > 0 ? index : FindSuggestedHookIndex(choices);
            combo.IsEnabled = choices.Any(choice => choice.Hook != null);
        }

        private static int FindSuggestedHookIndex(IList<HookChoice> choices)
        {
            var matches = choices.Select((choice, index) => new { choice, index })
                .Where(item => item.choice.Hook != null)
                .OrderByDescending(item =>
                {
                    double angle = item.choice.Hook.HookAngle * 180.0 / Math.PI;
                    return Math.Abs(angle - 135.0) < 0.1 ? 10 : 0;
                })
                .ThenBy(item => item.choice.Hook.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return matches.Count == 0 ? 0 : matches[0].index;
        }

        private static RebarHookType SelectedHook(ComboBox combo, IList<RebarHookType> hooks,
            RebarBarType type, RebarShape shape, int end)
        {
            RebarHookType selected = (combo.SelectedItem as HookChoice)?.Hook;
            if (selected == null) return null;
            bool stillCompatible = GetHookChoices(hooks, type, shape, end)
                .Any(choice => choice.Hook?.Id == selected.Id);
            return stillCompatible ? selected : null;
        }
    }
}
