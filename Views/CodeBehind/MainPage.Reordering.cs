using System.Globalization;
using Locksy.Models;
using Locksy.Platforms.Android;
using Locksy.Services;
using Locksy.Services.Preferences;
using Locksy.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;


namespace Locksy;

public partial class MainPage : ContentPage
{
    private void TaskRow_HandlerChanged(object? sender, EventArgs e)
    {
#if ANDROID
        if (sender is not Border border || border.Handler?.PlatformView is not Android.Views.View nativeView)
            return;
        _nativeTaskRows[nativeView] = new WeakReference<Border>(border);
#endif
    }
    private void TaskGrip_HandlerChanged(object? sender, EventArgs e)
    {
#if ANDROID
        if (sender is not Label grip || grip.Handler?.PlatformView is not Android.Views.View nativeView)
            return;

        nativeView.Touch -= NativeGrip_Touch;
        nativeView.Touch += NativeGrip_Touch;
        nativeView.Clickable = true;
        nativeView.LongClickable = true;
        _nativeTaskGrips[nativeView] = new WeakReference<Label>(grip);
#endif
    }

#if ANDROID
    private void NativeGrip_Touch(object? sender, Android.Views.View.TouchEventArgs e)
    {
        if (sender is not Android.Views.View nativeGrip || e.Event is not Android.Views.MotionEvent motion)
            return;

        if (motion.ActionMasked == Android.Views.MotionEventActions.Down)
        {
            if (!_nativeTaskGrips.TryGetValue(nativeGrip, out var weakGrip) ||
                !weakGrip.TryGetTarget(out var grip) ||
                grip.BindingContext is not TaskItemViewModel task)
                return;

            _reorderCandidate = task;
            _reorderGripView = nativeGrip;
            _reorderStartRawX = motion.RawX;
            _reorderStartRawY = motion.RawY;
            _isReorderDragging = false;
            // The dotted handle is a dedicated drag affordance, so the list must not
            // steal its vertical gesture and interpret it as a scroll.
            nativeGrip.Parent?.RequestDisallowInterceptTouchEvent(true);
            e.Handled = true;
            return;
        }

        if (_reorderCandidate is null)
            return;

        switch (motion.ActionMasked)
        {
            case Android.Views.MotionEventActions.Move:
            {
                var context = global::Android.App.Application.Context;
                var density = Math.Max(1f, context.Resources?.DisplayMetrics?.Density ?? 1f);
                var dx = motion.RawX - _reorderStartRawX;
                var dy = motion.RawY - _reorderStartRawY;
                var touchSlop = 7f * density;

                if (!_isReorderDragging && (dx * dx + dy * dy) >= touchSlop * touchSlop)
                    BeginNativeReorder(_reorderCandidate, nativeGrip);

                if (_isReorderDragging)
                {
                    UpdateDragGhostPosition(motion.RawX, motion.RawY);
                    UpdateNativeDropTarget(motion.RawX, motion.RawY);
                }

                e.Handled = true;
                return;
            }

            case Android.Views.MotionEventActions.Up:
            {
                var didDrag = _isReorderDragging;
                var source = _draggedTask;
                var target = _hoveredTask;
                FinishNativeReorder();
                nativeGrip.Parent?.RequestDisallowInterceptTouchEvent(false);

                if (didDrag && source is not null && target is not null && source.Id != target.Id)
                {
                    _viewModel.MoveTask(source.Id, target.Id);
                    _wallpaperViewModel.StatusMessage = LocalizationService.IsPersian ? "جای کار تغییر کرد." : $"Moved ‘{source.Title}’.";
                }
                else if (didDrag)
                {
                    _wallpaperViewModel.StatusMessage = LocalizationService.Translate("Drop on another task to change its position.");
                }

                _reorderCandidate = null;
                _reorderGripView = null;
                e.Handled = true;
                return;
            }

            case Android.Views.MotionEventActions.Cancel:
            case Android.Views.MotionEventActions.Outside:
                FinishNativeReorder();
                nativeGrip.Parent?.RequestDisallowInterceptTouchEvent(false);
                _reorderCandidate = null;
                _reorderGripView = null;
                e.Handled = true;
                return;
        }
    }
    private void BeginNativeReorder(TaskItemViewModel task, Android.Views.View grip)
    {
        _isReorderDragging = true;
        _draggedTask = task;
        _draggedBorder = FindNativeRowForTask(task);
        if (_draggedBorder is not null)
        {
            _draggedOriginalStrokeThickness = _draggedBorder.StrokeThickness;
            _draggedOriginalOpacity = _draggedBorder.Opacity;
            _draggedOriginalScale = _draggedBorder.Scale;
            _draggedOriginalTranslationY = _draggedBorder.TranslationY;
            _draggedOriginalZIndex = _draggedBorder.ZIndex;

            // Keep the row in the list as a translucent placeholder. Moving the
            // visible preview into a root-level overlay avoids RecyclerView's native
            // child drawing order, which can paint a translated cell behind its peers.
            _draggedBorder.Opacity = 0.22;
            _draggedBorder.Scale = 0.99;
            _draggedBorder.TranslationY = _draggedOriginalTranslationY;

            DragGhostTitle.Text = task.Title;
            DragGhostTitle.TextDecorations = task.TitleDecorations;
            DragGhostTitle.TextColor = task.RowTextColor;
            DragGhostCheckBox.IsChecked = task.IsCompleted;
            DragGhostBorder.BackgroundColor = task.RowColor;
            DragGhostBorder.WidthRequest = Math.Max(180, _draggedBorder.Width);
            DragGhostBorder.HeightRequest = Math.Max(48, _draggedBorder.Height);
            DragGhostBorder.Opacity = 0.92;
            DragGhostBorder.Scale = 1.035;
            DragGhostBorder.TranslationX = 0;
            DragGhostBorder.TranslationY = 0;
            DragGhostBorder.IsVisible = true;
            DragOverlay.Opacity = 1;
            DragOverlay.IsVisible = true;

            // Elevation is an additional Android hint; the page-level overlay ZIndex
            // is the primary guarantee that the preview is drawn over every task row.
            if (DragGhostBorder.Handler?.PlatformView is Android.Views.View ghostNative)
            {
                var density = Math.Max(1f, global::Android.App.Application.Context.Resources?.DisplayMetrics?.Density ?? 1f);
                ghostNative.Elevation = 24f * density;
                ghostNative.TranslationZ = 24f * density;
                ghostNative.BringToFront();
            }
        }
        grip.PerformHapticFeedback(Android.Views.FeedbackConstants.LongPress);
        _wallpaperViewModel.StatusMessage = LocalizationService.IsPersian ? $"در حال جابه‌جایی «{task.Title}» · برای ثبت، روی یک کار رها کن." : $"Moving ‘{task.Title}’ · release over a task to place it.";
    }
    private void UpdateDragGhostPosition(float rawX, float rawY)
    {
        if (_draggedBorder is null || DragOverlay.Handler?.PlatformView is not Android.Views.View overlayNative)
            return;

        var density = Math.Max(1f, global::Android.App.Application.Context.Resources?.DisplayMetrics?.Density ?? 1f);
        var overlayLocation = new int[2];
        overlayNative.GetLocationOnScreen(overlayLocation);

        // Keep the preview horizontally aligned with the original card while it follows
        // the finger vertically. Both Android raw coordinates and native view bounds
        // are converted to MAUI's device-independent units.
        var leftDip = 0f;
        var nativeRow = FindNativeViewForRow(_draggedBorder);
        if (nativeRow is not null)
        {
            var rowLocation = new int[2];
            nativeRow.GetLocationOnScreen(rowLocation);
            leftDip = (rowLocation[0] - overlayLocation[0]) / density;
        }

        var ghostHeight = DragGhostBorder.Height > 0 ? DragGhostBorder.Height : DragGhostBorder.HeightRequest;
        var topDip = ((rawY - overlayLocation[1]) / density) - (ghostHeight / 2d);
        DragGhostBorder.TranslationX = leftDip;
        DragGhostBorder.TranslationY = topDip;
    }
    private Android.Views.View? FindNativeViewForRow(Border row)
    {
        foreach (var item in _nativeTaskRows.ToArray())
        {
            if (!item.Value.TryGetTarget(out var target))
            {
                _nativeTaskRows.Remove(item.Key);
                continue;
            }
            if (ReferenceEquals(target, row))
                return item.Key;
        }
        return null;
    }
    private void UpdateNativeDropTarget(float rawX, float rawY)
    {
        var targetBorder = FindNativeRowAt(rawX, rawY);
        var target = targetBorder?.BindingContext as TaskItemViewModel;
        if (target is not null && _draggedTask is not null && target.Id == _draggedTask.Id)
            target = null;

        if (target?.Id == _hoveredTask?.Id)
            return;

        ClearNativeDropTarget();
        if (target is null || targetBorder is null)
        {
            _wallpaperViewModel.StatusMessage = LocalizationService.Translate("Move over another task to place it.");
            return;
        }

        _hoveredTask = target;
        _hoveredBorder = targetBorder;
        _hoveredOriginalStrokeThickness = targetBorder.StrokeThickness;
        _hoveredOriginalScale = targetBorder.Scale;
        targetBorder.Stroke = new SolidColorBrush(Color.FromArgb((_viewModel.Settings?.IsDarkMode ?? false) ? "#B7A9FF" : "#8875E8"));
        targetBorder.StrokeThickness = 2.5;
        targetBorder.Scale = 1.012;
        _reorderGripView?.PerformHapticFeedback(Android.Views.FeedbackConstants.KeyboardTap);
        _wallpaperViewModel.StatusMessage = LocalizationService.IsPersian ? $"روی «{target.Title}» رها کن." : $"Drop on ‘{target.Title}’.";
    }
    private Border? FindNativeRowForTask(TaskItemViewModel task)
    {
        foreach (var item in _nativeTaskRows.ToArray())
        {
            if (!item.Value.TryGetTarget(out var border))
            {
                _nativeTaskRows.Remove(item.Key);
                continue;
            }
            if (border.BindingContext is TaskItemViewModel bound && bound.Id == task.Id)
                return border;
        }
        return null;
    }
    private Border? FindNativeRowAt(float rawX, float rawY)
    {
        Border? nearest = null;
        var nearestDistance = float.MaxValue;
        var density = Math.Max(1f, global::Android.App.Application.Context.Resources?.DisplayMetrics?.Density ?? 1f);

        foreach (var item in _nativeTaskRows.ToArray())
        {
            if (!item.Value.TryGetTarget(out var border))
            {
                _nativeTaskRows.Remove(item.Key);
                continue;
            }
            if (border.BindingContext is not TaskItemViewModel task ||
                (_draggedTask is not null && task.Id == _draggedTask.Id) ||
                item.Key.Width <= 0 || item.Key.Height <= 0)
                continue;

            var location = new int[2];
            item.Key.GetLocationOnScreen(location);
            var left = location[0];
            var top = location[1];
            var right = left + item.Key.Width;
            var bottom = top + item.Key.Height;
            if (rawX < left || rawX > right)
                continue;

            if (rawY >= top && rawY <= bottom)
                return border;

            var distance = rawY < top ? top - rawY : rawY - bottom;
            if (distance < nearestDistance && distance <= 20f * density)
            {
                nearestDistance = distance;
                nearest = border;
            }
        }
        return nearest;
    }
    private static void RestoreTaskRowStroke(Border border)
    {
        // Reapply a theme-aware brush binding. Assigning Border.Stroke directly during
        // the drag temporarily replaces its XAML AppThemeBinding, so restore it explicitly.
        border.SetAppTheme<Brush>(
            Border.StrokeProperty,
            new SolidColorBrush(Color.FromArgb("#E5E0EE")),
            new SolidColorBrush(Color.FromArgb("#373346")));
    }
    private void ClearNativeDropTarget()
    {
        if (_hoveredBorder is not null)
        {
            RestoreTaskRowStroke(_hoveredBorder);
            _hoveredBorder.StrokeThickness = _hoveredOriginalStrokeThickness;
            _hoveredBorder.Scale = _hoveredOriginalScale;
        }
        _hoveredBorder = null;
        _hoveredTask = null;
        _hoveredOriginalStrokeThickness = 1;
        _hoveredOriginalScale = 1;
    }
    private void FinishNativeReorder()
    {
        ClearNativeDropTarget();
        DragOverlay.IsVisible = false;
        DragGhostBorder.IsVisible = false;
        DragGhostBorder.TranslationX = 0;
        DragGhostBorder.TranslationY = 0;
        DragGhostBorder.Scale = 1;
        if (DragGhostBorder.Handler?.PlatformView is Android.Views.View ghostNative)
        {
            ghostNative.TranslationZ = 0;
            ghostNative.Elevation = 0;
        }

        if (_draggedBorder is not null)
        {
            RestoreTaskRowStroke(_draggedBorder);
            _draggedBorder.StrokeThickness = _draggedOriginalStrokeThickness;
            _draggedBorder.Opacity = _draggedOriginalOpacity;
            _draggedBorder.Scale = _draggedOriginalScale;
            _draggedBorder.TranslationY = _draggedOriginalTranslationY;
            _draggedBorder.ZIndex = _draggedOriginalZIndex;
        }
        _draggedBorder = null;
        _draggedTask = null;
        _isReorderDragging = false;
        _draggedOriginalStrokeThickness = 1;
        _draggedOriginalOpacity = 1;
        _draggedOriginalScale = 1;
        _draggedOriginalTranslationY = 0;
        _draggedOriginalZIndex = 0;
    }
#endif

    // MAUI does not expose a cross-platform long-press gesture. Android's native
    // long-click is attached to the task title; the separate grip remains a drag source.
    private void TaskTitle_HandlerChanged(object? sender, EventArgs e)
    {
#if ANDROID
        if (sender is not Label label || label.Handler?.PlatformView is not Android.Views.View nativeView)
            return;

        nativeView.LongClickable = true;
        nativeView.LongClick -= NativeTitle_LongClick;
        nativeView.LongClick += NativeTitle_LongClick;
        _nativeTitleViews[nativeView] = new WeakReference<Label>(label);
#endif
    }

#if ANDROID
    private void NativeTitle_LongClick(object? sender, Android.Views.View.LongClickEventArgs e)
    {
        if (sender is Android.Views.View nativeView &&
            _nativeTitleViews.TryGetValue(nativeView, out var weakLabel) &&
            weakLabel.TryGetTarget(out var label) &&
            label.BindingContext is TaskItemViewModel task)
        {
            e.Handled = true;
            MainThread.BeginInvokeOnMainThread(async () => await ShowTaskOptionsAsync(task));
        }
    }
#endif
    private void AutoApplyTrack_Tapped(object? sender, TappedEventArgs e)
    {
        AutoApplySwitch.IsToggled = !AutoApplySwitch.IsToggled;
        UpdateAutoApplyVisual(AutoApplySwitch.IsToggled);
    }
    private void UpdateAutoApplyVisual(bool isEnabled)
    {
        if (AutoApplyTrack is null || AutoApplyThumb is null)
            return;

        var isDark = (_viewModel.Settings?.IsDarkMode ?? false);
        AutoApplyTrack.BackgroundColor = Color.FromArgb(isEnabled
            ? "#8875E8"
            : isDark ? "#14151F" : "#C8D1E0");
        AutoApplyTrack.Stroke = new SolidColorBrush(Color.FromArgb(isEnabled
            ? (isDark ? "#A99BFF" : "#8875E8")
            : isDark ? "#3B3E52" : "#B7C2D4"));
        AutoApplyThumb.Fill = Color.FromArgb(isEnabled
            ? "#F8F6FF"
            : isDark ? "#777F95" : "#7F899F");
        AutoApplyThumb.HorizontalOptions = isEnabled ? LayoutOptions.End : LayoutOptions.Start;
    }
    private void RoundedControl_HandlerChanged(object? sender, EventArgs e)
    {
#if ANDROID
        if (sender is VisualElement element && element.Handler?.PlatformView is Android.Views.View nativeView)
        {
            // Let the rounded parent Border provide the outline instead of Android's default underline/box.
            nativeView.Background = null;
        }
#endif
    }
}
