using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SDK_HD.KStruct.FootingRebar
{
    public partial class FootingRebarWindow
    {
        private FootingRebarPreference _savedPreference;
        private bool _restoringPreference;
        private bool _restoreSavedLean;
        private bool _detectedLean;
        public bool RememberForNextFooting => ChkRememberRebar.IsChecked == true;
        public bool ReplaceOwnedRebar => ChkReplaceOwned.IsChecked == true;
        public bool AllowAlongsideOtherRebar => ChkAllowAlongside.IsChecked == true;

        private void InitializeHistory(FootingRebarPreference saved, string summary, bool remember, int ownedCount, int otherCount, bool restoreLean)
        {
            _savedPreference = saved; _restoreSavedLean = restoreLean; _detectedLean = ChkHasLeanConcrete.IsChecked == true;
            TxtHistoryStatus.Text = summary ?? "Chưa có thông số lần tạo trước. Kích thước được đọc từ móng hiện tại.";
            ChkRememberRebar.IsChecked = remember;
            ChkReplaceOwned.Visibility = ownedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            ChkReplaceOwned.Content = new TextBlock { Text = "Cập nhật " + ownedCount + " nhóm thép KStruct của móng này (không tạo trùng)", TextWrapping = TextWrapping.Wrap };
            ChkAllowAlongside.Visibility = otherCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            ChkAllowAlongside.Content = new TextBlock { Text = "Cho phép tạo cạnh " + otherCount + " nhóm thép chưa nhận diện; giữ nguyên thép đó", TextWrapping = TextWrapping.Wrap };
            BtnReloadRebar.IsEnabled = saved != null;
            if (saved != null) ApplySavedPreference();
        }

        internal void ConfigureForUpdate()
        {
            Title = "KStruct - Cập nhật thép móng cọc, móng đơn";
            TxtDialogHeading.Text = "Cập nhật thép móng";
            InputTabs.SelectedIndex = 0;
            ChkReplaceOwned.IsChecked = true;
            ChkReplaceOwned.IsEnabled = false;
            ChkAllowAlongside.Visibility = Visibility.Collapsed;
            BtnAcceptRebar.Content = "Cập nhật thép móng";
            TxtHistoryStatus.Text += "\nChế độ cập nhật: chỉnh đường kính, bước, cover và neo của bộ đã nhận diện. " +
                "Chiều dài thanh được tính lại theo kích thước móng hiện tại; không đổi kích thước bê tông móng. Thép khác được giữ nguyên.";
        }

        private void OnReloadRebarClick(object sender, RoutedEventArgs e) => ApplySavedPreference();

        private void ApplySavedPreference()
        {
            if (_savedPreference == null) return;
            try
            {
                FootingRebarFullSettings s = _savedPreference.ToSettings();
                if (s.Bottom.AlongX.Mode != SpacingMode.EvenWithinMaxSpacing || s.Bottom.AlongY.Mode != SpacingMode.EvenWithinMaxSpacing ||
                    s.Top.AlongX.Mode != SpacingMode.EvenWithinMaxSpacing || s.Top.AlongY.Mode != SpacingMode.EvenWithinMaxSpacing ||
                    !Equivalent(s.Top.AlongX, s.Top.AlongY) || !s.SideTies.HasCStirrups)
                    throw new InvalidOperationException("Cấu hình cũ có cách chia thép/thép trên không thể biểu diễn đầy đủ trong cửa sổ này. Giữ cấu hình hiện tại để tránh thay đổi ngầm.");
                _restoringPreference = true;
                _model = new FootingModel(_model.LengthX, _model.WidthY, _model.Height, s.FootingType, _model.Name);
                CboFootingType.SelectedIndex = s.FootingType == FootingType.PileCap ? 1 : 0;
                if (_restoreSavedLean) ChkHasLeanConcrete.IsChecked = s.Cover.HasLeanConcrete;
                TxtCoverBottom.Text = Number(s.Cover.Bottom); TxtCoverSide.Text = Number(s.Cover.Side); TxtCoverTop.Text = Number(s.Cover.Top);
                ChkEnableX.IsChecked = s.Bottom.AlongX.Enabled; ChkEnableY.IsChecked = s.Bottom.AlongY.Enabled;
                var missing = new List<string>();
                ChooseDiameter(CboDiameterX, s.Bottom.AlongX.Diameter, s.Bottom.AlongX.Enabled, "đáy X", missing);
                ChooseDiameter(CboDiameterY, s.Bottom.AlongY.Diameter, s.Bottom.AlongY.Enabled, "đáy Y", missing);
                TxtSpacingX.Text = Number(s.Bottom.AlongX.Spacing); TxtSpacingY.Text = Number(s.Bottom.AlongY.Spacing);
                CboHookX.SelectedIndex = HookIndex(s.Bottom.AlongX.HookType, false); CboHookY.SelectedIndex = HookIndex(s.Bottom.AlongY.HookType, false);
                TxtHookLengthX.Text = Number(s.Bottom.AlongX.HookLength); TxtHookLengthY.Text = Number(s.Bottom.AlongY.HookLength);
                ChkEnableTop.IsChecked = s.Top.Enabled; ExpanderTop.IsExpanded = s.Top.Enabled;
                ChooseDiameter(CboDiameterTop, s.Top.AlongX.Diameter, s.Top.Enabled, "lớp trên", missing);
                TxtSpacingTop.Text = Number(s.Top.AlongX.Spacing); CboTopHook.SelectedIndex = HookIndex(s.Top.AlongX.HookType, true);
                TxtTopHookLength.Text = Number(s.Top.AlongX.HookLength);
                ChkEnableSideTies.IsChecked = s.SideTies.Enabled; ExpanderSideTies.IsExpanded = s.SideTies.Enabled;
                ChooseDiameter(CboDiameterTie, s.SideTies.Diameter, s.SideTies.Enabled, "đai hông", missing);
                TxtSpacingTie.Text = Number(s.SideTies.Spacing);
                if (missing.Count > 0) MessageBox.Show(this, "Project hiện tại thiếu đường kính đã lưu: " + string.Join(", ", missing) +
                    ". Hãy nạp/chọn loại thép phù hợp trước khi tạo; không tự chọn đường kính thay thế.", "KStruct — Khôi phục thép móng");
                if (_detectedLean != s.Cover.HasLeanConcrete)
                    MessageBox.Show(this, "Tình trạng bê tông lót của móng hiện tại khác cấu hình trước. Hãy đối chiếu checkbox bê tông lót và lớp bảo vệ trước khi tạo.", "KStruct — Bê tông lót");
            }
            catch (Exception ex) { MessageBox.Show(this, "Không khôi phục được thông số đã lưu.\n" + ex.Message, "KStruct"); }
            finally { _restoringPreference = false; RenderPreview(); }
        }
        private static bool Equivalent(BarLayerSettings a, BarLayerSettings b) => a.Enabled == b.Enabled && a.Diameter == b.Diameter &&
            a.Spacing == b.Spacing && a.Mode == b.Mode && a.HookType == b.HookType && a.HookLength == b.HookLength;
        private static int HookIndex(HookTypeOption hook, bool top) => hook == HookTypeOption.None ? 2 :
            (top ? hook == HookTypeOption.HookDown : hook == HookTypeOption.HookUp) ? 0 : 1;
        private static string Number(double value) => value.ToString("R", System.Globalization.CultureInfo.CurrentCulture);
        private static void ChooseDiameter(ComboBox combo, double diameter, bool enabled, string label, IList<string> missing)
        {
            object match = combo.Items.Cast<object>().FirstOrDefault(x => x is double value && Math.Abs(value - diameter) < 0.01);
            combo.SelectedItem = match;
            if (match == null && enabled) missing.Add(label + " Ø" + Number(diameter));
        }
    }
}
