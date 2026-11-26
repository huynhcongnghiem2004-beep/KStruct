using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ColumnRebarPro.V3
{
    public partial class ColumnRebarV3Window
    {
        private DispatcherTimer _livePreviewTimer;
        private Action _pendingLivePreview;
        private bool _applyingLivePreview;

        private void InitializeLivePreview()
        {
            _livePreviewTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            { Interval = TimeSpan.FromMilliseconds(300) };
            _livePreviewTimer.Tick += (sender, args) =>
            {
                _livePreviewTimer.Stop();
                var update = _pendingLivePreview;
                _pendingLivePreview = null;
                if (_loading || !_activeId.HasValue) return;
                _applyingLivePreview = true;
                try { update?.Invoke(); }
                catch (Exception ex)
                {
                    SetLivePreviewStatus(ex.Message);
                    System.Diagnostics.Debug.WriteLine("KStruct live preview: " + ex);
                }
                finally { _applyingLivePreview = false; }
            };
            Closed += (sender, args) =>
            {
                _livePreviewTimer.Stop();
                _pendingLivePreview = null;
            };
        }

        private void QueueLivePreview(object sender, Action update)
        {
            var box = sender as TextBox;
            // Programmatic synchronization must not restart the timer or overwrite drafts.
            if (_loading || _compactSync || _applyingLivePreview || _livePreviewTimer == null ||
                box == null || !box.IsKeyboardFocused || !_activeId.HasValue) return;
            long? floorId = _activeId;
            _pendingLivePreview = () =>
            {
                if (_activeId != floorId || !box.IsKeyboardFocused) return;
                string draft = box.Text;
                int caret = box.SelectionStart, selection = box.SelectionLength;
                try { update(); }
                finally
                {
                    // Existing handlers normalize numbers; preserve typing and caret until blur.
                    box.Text = draft;
                    box.Select(Math.Min(caret, draft.Length), Math.Min(selection, draft.Length - Math.Min(caret, draft.Length)));
                }
            };
            _livePreviewTimer.Stop();
            _livePreviewTimer.Start();
        }

        private void QuickText_Changed(object sender, TextChangedEventArgs e) =>
            QueueLivePreview(sender, () => QuickText_LostFocus(sender, e));
        private void EditorText_Changed(object sender, TextChangedEventArgs e) =>
            QueueLivePreview(sender, () => Text_LostFocus(sender, e));
        private void FoundationText_Changed(object sender, TextChangedEventArgs e) =>
            QueueLivePreview(sender, () =>
            {
                ReadPositive((TextBox)sender, "Thông số neo móng");
                FoundationAnchor_Changed(sender, e);
                SetLivePreviewStatus(null);
            });
        private void RoofText_Changed(object sender, TextChangedEventArgs e) =>
            QueueLivePreview(sender, () =>
            {
                ReadPositive((TextBox)sender, "Chiều dài neo mái");
                RoofHook_Changed(sender, e);
                SetLivePreviewStatus(null);
            });

        private void SetLivePreviewStatus(string error)
        {
            if (LivePreviewStatus == null) return;
            LivePreviewStatus.Text = string.IsNullOrEmpty(error)
                ? "Đã cập nhật xem trước · chưa tạo thép trong mô hình"
                : "Giữ bản xem trước hợp lệ gần nhất · " + error;
            LivePreviewStatus.Foreground = string.IsNullOrEmpty(error) ? Brushes.DarkSlateBlue : Brushes.DarkRed;
        }
    }
}
