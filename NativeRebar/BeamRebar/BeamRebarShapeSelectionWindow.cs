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

            var window = new Window
            {
                Title = "Chọn Rebar Shape có sẵn cho thép dầm",
                Width = 760,
                Height = 670,
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
                Text = "Mỗi nhóm dùng đúng Rebar Shape đã chọn trong project. Đai được tạo trực tiếp từ StirrupTie shape và ScaleToBox (không dò shape từ đường bao 4 cạnh); có thể chọn HookType riêng ở đầu/cuối. Style, khả năng dùng HookType và kích thước hình học/layout thực tế được kiểm tra trước khi đặt. Shape/hook không tương thích sẽ báo lỗi và giữ nguyên thép cũ.",
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
            var cancel = new Button { Content = "Hủy", MinWidth = 90, Margin = new Thickness(5, 0, 0, 0), IsCancel = true };
            var accept = new Button { Content = "Tiếp tục", MinWidth = 110, Margin = new Thickness(5, 0, 0, 0), IsDefault = true };
            buttons.Children.Add(cancel);
            buttons.Children.Add(accept);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

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
                    SelectedIndex = Math.Max(0, choices.FindIndex(choice => choice.Shape?.Id == row.Selected?.Id)),
                    MinHeight = 26,
                    Margin = new Thickness(0, 3, 0, 3),
                    IsEnabled = choices.Count > 1
                };
                Grid.SetRow(row.Combo, i + 2);
                Grid.SetColumn(row.Combo, 1);
                grid.Children.Add(row.Combo);
            }

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
            return window.ShowDialog() == true ? options : null;
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
            var compatible = (hookTypes ?? new List<RebarHookType>())
                .Where(hook => hook != null && hook.Style == RebarStyle.StirrupTie &&
                    barType != null && HasHookPermission(barType, hook) &&
                    (shape == null || HookAngleMatchesDefault(hook, requiredAngle)))
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
            combo.SelectedIndex = index >= 0 ? index : 0;
            combo.IsEnabled = choices.Any(choice => choice.Hook != null);
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
