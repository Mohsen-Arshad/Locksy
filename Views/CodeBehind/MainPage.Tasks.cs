using System.Globalization;
using System.Diagnostics;
using Locksy.Helpers;
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
    private const int TaskEditorCharacterThreshold = 48;
    private const int TaskEditorWordThreshold = 7;
    private bool _taskTextEditorOpen;
    private bool _taskTextEditorOpening;
    private bool _taskTextEditorClosing;
    private bool _ignoreNextTaskEntryFocusExpansion;

    private void InvalidateNewReminderPopupVisualCache() => _newReminderPopupVisualCache = null;

    private void NewTaskEntry_TextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateNewTaskEntryFlowDirection(e.NewTextValue);

        if (!_taskTextEditorOpen && !_taskTextEditorOpening && ShouldOpenExpandedTaskEditor(e.NewTextValue))
            OpenExpandedTaskEditor(e.NewTextValue ?? string.Empty);
    }

    private void NewTaskEntry_Focused(object? sender, FocusEventArgs e)
    {
        if (_ignoreNextTaskEntryFocusExpansion)
        {
            _ignoreNextTaskEntryFocusExpansion = false;
            return;
        }

        var text = NewTaskEntry.Text;
        if (!_taskTextEditorOpen && !_taskTextEditorOpening && ShouldOpenExpandedTaskEditor(text))
            OpenExpandedTaskEditor(text ?? string.Empty);
    }

    private static bool ShouldOpenExpandedTaskEditor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (text.Length >= TaskEditorCharacterThreshold)
            return true;

        var words = 0;
        var insideWord = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                insideWord = false;
            }
            else if (!insideWord)
            {
                insideWord = true;
                words++;
                if (words >= TaskEditorWordThreshold)
                    return true;
            }
        }

        return false;
    }

    private void OpenExpandedTaskEditor(string text)
    {
        if (_taskTextEditorOpen || _taskTextEditorOpening || !IsVisible)
            return;

        _taskTextEditorOpening = true;
        _taskTextEditorOpen = true;

        // Synchronize before showing the overlay so the exact last typed character is kept.
        _viewModel.NewTaskText = text;
        TaskTextEditor.Text = text;
        TaskTextEditor.FlowDirection = NewTaskEntry.FlowDirection;
        TaskTextEditor.CursorPosition = text.Length;
        TaskTextEditor.SelectionLength = 0;

        TaskTextEditorOverlay.Opacity = 0;
        TaskTextEditorCard.Scale = 0.985;
        TaskTextEditorOverlay.IsVisible = true;
        PageContent.InputTransparent = true;

        // Focus the multiline editor as soon as the overlay is in the visual tree. The user
        // keeps typing immediately; no second tap is required and the existing draft is shared.
        Dispatcher.Dispatch(() =>
        {
            if (!TaskTextEditorOverlay.IsVisible)
            {
                _taskTextEditorOpen = false;
                _taskTextEditorOpening = false;
                return;
            }

            TaskTextEditor.Focus();
            TaskTextEditor.CursorPosition = (TaskTextEditor.Text ?? string.Empty).Length;
            TaskTextEditor.SelectionLength = 0;
            _ = TaskTextEditorOverlay.FadeToAsync(1, 150, Easing.CubicOut);
            _ = TaskTextEditorCard.ScaleToAsync(1, 170, Easing.CubicOut);
            _taskTextEditorOpening = false;
        });
    }

    private void TaskTextEditor_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var direction = TaskTextDirectionHelper.Resolve(
            e.NewTextValue, TaskTextDirection.Auto, LocalizationService.IsPersian);
        TaskTextEditor.FlowDirection = direction == TaskTextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft
            : Microsoft.Maui.FlowDirection.LeftToRight;
    }

    private async void TaskTextEditorDone_Clicked(object? sender, EventArgs e) =>
        await CloseExpandedTaskEditorAsync();

    private async Task CloseExpandedTaskEditorAsync()
    {
        if (!_taskTextEditorOpen || _taskTextEditorClosing)
            return;

        _taskTextEditorClosing = true;
        // Prevent the underlying Entry from reopening the overlay if Android restores its
        // focus while the editor is closing. The guard is cleared shortly afterwards so a
        // deliberate tap on a long draft can open the expanded editor again immediately.
        _ignoreNextTaskEntryFocusExpansion = true;
        try
        {
            TaskTextEditor.Unfocus();
            await Task.WhenAll(
                TaskTextEditorOverlay.FadeToAsync(0, 130, Easing.CubicIn),
                TaskTextEditorCard.ScaleToAsync(0.985, 130, Easing.CubicIn));
        }
        finally
        {
            TaskTextEditorOverlay.IsVisible = false;
            TaskTextEditorOverlay.Opacity = 0;
            TaskTextEditorCard.Scale = 1;
            PageContent.InputTransparent = false;
            _taskTextEditorOpen = false;
            _taskTextEditorOpening = false;
            _taskTextEditorClosing = false;
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(250),
                () => _ignoreNextTaskEntryFocusExpansion = false);
        }
    }

    private void UpdateNewTaskEntryFlowDirection(string? text)
    {
        var direction = TaskTextDirectionHelper.Resolve(
            text, TaskTextDirection.Auto, LocalizationService.IsPersian);
        NewTaskEntry.FlowDirection = direction == TaskTextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft
            : Microsoft.Maui.FlowDirection.LeftToRight;
    }

    private static Microsoft.Maui.FlowDirection ResolveTaskFlowDirection(
        string? text, TaskTextDirection requestedDirection)
    {
        var direction = TaskTextDirectionHelper.Resolve(
            text, requestedDirection, LocalizationService.IsPersian);
        return direction == TaskTextDirection.RightToLeft
            ? Microsoft.Maui.FlowDirection.RightToLeft
            : Microsoft.Maui.FlowDirection.LeftToRight;
    }

    private async void AddTaskEntry_Completed(object? sender, EventArgs e)
    {
        await AddTaskAndKeepInputReadyAsync();
    }
    private async void AddTaskButton_Clicked(object? sender, EventArgs e)
    {
        await AddTaskAndKeepInputReadyAsync();
    }
    private async Task AddTaskAndKeepInputReadyAsync()
    {
        // The button temporarily takes focus on some Android keyboards. Mark the add
        // operation before changing the collection so auto-apply cannot relaunch the
        // OEM wallpaper flow between two consecutive task entries.
        _wallpaperViewModel.SetAddingTask(true);
        var addedWithDraftSettings = false;
        try
        {
            if (_viewModel.NewTaskSettings.HasPendingSettings)
            {
                var title = (_viewModel.NewTaskText ?? string.Empty).Trim();
                if (title.Length > 0)
                {
                    if (_viewModel.NewTaskSettings.ReminderEnabled &&
                        (!_viewModel.NewTaskSettings.ReminderAt.HasValue || _viewModel.NewTaskSettings.ReminderAt.Value <= DateTime.Now))
                    {
                        await ShowInfoPopupAsync(
                            "Choose a future time",
                            "The saved reminder time has passed. Open settings and choose a future date and time before adding this reminder.",
                            isError: true);
                        return;
                    }

                    if (_viewModel.NewTaskSettings.ReminderEnabled && !await EnsureNotificationPermissionAsync())
                        return;

                    var created = _viewModel.AddTaskWithSettings(
                        title,
                        _viewModel.NewTaskSettings.ColorKey,
                        _viewModel.NewTaskSettings.PaletteKey,
                        _viewModel.NewTaskSettings.ReminderEnabled,
                        _viewModel.NewTaskSettings.ReminderAt);
                    addedWithDraftSettings = created is not null;
                    if (addedWithDraftSettings)
                    {
                        _viewModel.NewTaskSettings.Reset();
                        _wallpaperViewModel.StatusMessage = created!.IsReminderActive
                            ? (LocalizationService.IsPersian
                                ? LocalizationService.LocalizeDigits($"ÛŒØ§Ø¯Ø¢ÙˆØ± Ø§Ø¶Ø§ÙÙ‡ Ø´Ø¯ Â· Ø²Ù†Ú¯ {created.ReminderAt:yyyy/MM/dd HH:mm}")
                                : $"Reminder added Â· alarm {created.ReminderAt:dd MMM, HH:mm}.")
                            : LocalizationService.Translate("Reminder added.");
                    }
                }
            }
            else if (_viewModel.AddTaskCommand.CanExecute(null))
            {
                _viewModel.AddTaskCommand.Execute(null);
            }
        }
        finally
        {
            _wallpaperViewModel.SetAddingTask(false);
        }

        // Let the two-way binding finish clearing the Entry, then put the caret back
        // into the same field and reopen the keyboard for fast consecutive entry.
        await Task.Delay(90);
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (!IsVisible || NewTaskEntry is null)
                return;

            NewTaskEntry.Focus();
            NewTaskEntry.CursorPosition = (NewTaskEntry.Text ?? string.Empty).Length;

            // Refresh the queued message once the Add Task focus handoff is complete.
            if (_wallpaperViewModel.IsAutoApplyPending && AutoApplySwitch.IsToggled && !_wallpaperViewModel.IsApplying)
                _wallpaperViewModel.StatusMessage = LocalizationService.Translate(AutoApplyQueuedStatus);
        });
    }
    private async Task<bool> EnsureNotificationPermissionAsync(bool keepModalOpenOnDismiss = false)
    {
        try
        {
            if (await (_viewModel.Reminders?.EnsureNotificationPermissionAsync() ?? Task.FromResult(false)))
                return true;
        }
        catch (Exception ex)
        {
            await ShowInfoPopupAsync(
                "Notification permission",
                ex.Message,
                isError: true,
                keepModalOpenOnDismiss: keepModalOpenOnDismiss);
            return false;
        }

        await ShowInfoPopupAsync(
            "Notifications are disabled",
            "Locksy cannot show the reminder until notification permission is enabled in Android Settings.",
            isError: true,
            keepModalOpenOnDismiss: keepModalOpenOnDismiss);
        return false;
    }
    private void NewTaskEntry_Unfocused(object? sender, FocusEventArgs e)
    {
        // Keep queued work queued. Do not call WallpaperManager while the editor is in
        // the foreground, because affected OEMs can recreate the Activity during apply.
        if (_wallpaperViewModel.IsAutoApplyPending && _wallpaperViewModel.IsAutoApplyEnabled && !_isSendingTaskToBackground)
            _wallpaperViewModel.StatusMessage = LocalizationService.Translate(AutoApplyQueuedStatus);
    }
    protected override bool OnBackButtonPressed()
    {
        if (_taskTextEditorOpen)
        {
            _ = CloseExpandedTaskEditorAsync();
            return true;
        }

        // Back dismisses an in-app dialog before it can leave the page.
        if (_popupCompletionSource is not null)
        {
            _lastBackPressUtc = DateTime.MinValue;
            if (_layoutPreviewActive)
            {
                CompletePopupInPlace("BACK_TO_LAYOUT");
            }
            else if (_settingsFlowActive && !string.Equals(_currentSettingsPage, "Settings", StringComparison.Ordinal))
            {
                var parentPage = _currentSettingsPage switch
                {
                    "Personal background" or "Built-in backgrounds" or "Slideshow settings" => "Background image",
                    "Slideshow interval" or "Slideshow items" => "Slideshow settings",
                    _ => "Settings"
                };
                MainThread.BeginInvokeOnMainThread(async () =>
                    await NavigateSettingsPageAsync(parentPage, animate: true, reverse: true));
            }
            else if (_taskPopupFlowActive && _taskPopupSubpageActive)
            {
                CompletePopupInPlace(null);
            }
            else
            {
                CompletePopup(null);
            }
            return true;
        }

        var now = DateTime.UtcNow;
        if (_lastBackPressUtc != DateTime.MinValue && now - _lastBackPressUtc <= TimeSpan.FromSeconds(2))
        {
            _lastBackPressUtc = DateTime.MinValue;
#if ANDROID
            // Keep the Android task and its Activity alive in Recents. This is much
            // safer than Finish()/process exit: reopening Locksy resumes the same page
            // whenever Android retains the process, instead of starting at the splash.
            _isSendingTaskToBackground = true;
            var activity = Platform.CurrentActivity;
            if (activity is not null)
            {
                activity.MoveTaskToBack(true);
                return true;
            }
#endif
            return base.OnBackButtonPressed();
        }

        _lastBackPressUtc = now;
#if ANDROID
        var currentActivity = Platform.CurrentActivity;
        if (currentActivity is not null)
            Android.Widget.Toast.MakeText(currentActivity, "Press back again to leave Locksy", Android.Widget.ToastLength.Short)?.Show();
        else
            _wallpaperViewModel.StatusMessage = LocalizationService.Translate("Press back again to leave Locksy.");
#else
        _wallpaperViewModel.StatusMessage = LocalizationService.Translate("Press back again to leave Locksy.");
#endif
        return true;
    }

    // Called from MainActivity.OnResume. Reset only the temporary backgrounding guard.
    // A queued auto-update must never run on resume, or the UI can flash/restart again.
    public void OnHostResumed() => _wallpaperViewModel.OnHostResumed();
    public Task ApplyQueuedAutoWallpaperWhenBackgroundedAsync(CancellationToken cancellationToken) =>
        _wallpaperViewModel.ApplyQueuedAutoWallpaperWhenBackgroundedAsync(cancellationToken);
    private void TaskTitle_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Label { BindingContext: TaskItemViewModel task })
            return;
#if ANDROID
        if (_recentLongPressTaskTaps.TryGetValue(task.Id, out var longPressAt))
        {
            _recentLongPressTaskTaps.Remove(task.Id);
            if (DateTime.UtcNow - longPressAt < TimeSpan.FromMilliseconds(1500))
                return; // The native tap recognizer can also fire after a long press; edit only once.
        }
#endif
        MainThread.BeginInvokeOnMainThread(async () => await EditTaskAsync(task));
    }
    private async void TaskOptionsButton_Clicked(object? sender, EventArgs e)
    {
        if (sender is ImageButton { BindingContext: TaskItemViewModel task })
            await ShowTaskOptionsAsync(task);
    }
    private async Task ShowTaskOptionsAsync(TaskItemViewModel task)
    {
        _taskPopupFlowActive = true;
        try
        {
            // Keep one modal alive while moving from options into Edit, Color, Alarm,
            // and Delete confirmation screens. Each child screen returns to this menu.
            while (true)
            {
                var action = await ShowChoicePopupAsync(
                    "Task options",
                    "Choose what to do with a task.",
                    new[] { "Edit task", "Text direction", "Change row color", "Set / edit alarm", "Delete task" },
                    cancelLabel: "Close",
                    icons: new[] { "edit", "direction", "palette", "alarm", "trash" },
                    popupIcon: "options",
                    keepOpenOnSelection: true);

                if (action is null)
                    return;

                _taskPopupSubpageActive = true;
                switch (action)
                {
                    case "Edit task":
                        await EditTaskAsync(task, keepModalOpenOnCompletion: true);
                        break;

                    case "Text direction":
                        await ChooseTaskTextDirectionAsync(task, keepModalOpenOnSelection: true);
                        break;

                    case "Change row color":
                        await ChooseTaskColorAsync(task, keepModalOpenOnSelection: true);
                        break;

                    case "Set / edit alarm":
                        await EditTaskReminderAsync(task, keepModalOpenAfterEdit: true);
                        break;

                    case "Delete task":
                        var confirmed = await ShowConfirmationPopupAsync(
                            "Delete this task?",
                            LocalizationService.IsPersian
                                ? $"Ø­Ø°Ù Â«{task.Title}Â» Ø§Ø² ÙÙ‡Ø±Ø³ØªØŸ Ø§ÛŒÙ† Ú©Ø§Ø± Ù‚Ø§Ø¨Ù„ Ø¨Ø§Ø²Ú¯Ø´Øª Ù†ÛŒØ³Øª."
                                : $"Remove â€˜{task.Title}â€™ from your list? This canâ€™t be undone.",
                            "Delete task",
                            destructive: true,
                            keepModalOpenOnDismiss: true);
                        if (confirmed)
                        {
                            _viewModel.DeleteTask(task);
                            CompletePopup(null);
                            return;
                        }
                        break;
                }
                _taskPopupSubpageActive = false;
            }
        }
        finally
        {
            _taskPopupFlowActive = false;
            _taskPopupSubpageActive = false;
            // If an unexpected child action exits while no popup task owns the still-open
            // surface, close it rather than leave a blocked overlay behind.
            if (ModalOverlay.IsVisible && _popupCompletionSource is null)
                CompletePopup(null);
        }
    }
    private async Task EditTaskAsync(TaskItemViewModel task, bool keepModalOpenOnCompletion = false)
    {
        var value = await ShowPromptPopupAsync(
            "Edit task",
            "Make the change and save when youâ€™re ready.",
            placeholder: LocalizationService.Translate("Task text"),
            maxLength: 300,
            initialValue: task.Title,
            keepModalOpenOnCompletion: keepModalOpenOnCompletion,
            textDirection: task.TextDirection);

        if (value is not null)
            _viewModel.EditTask(task, value);
    }
    private async Task EditTaskReminderAsync(TaskItemViewModel task, bool keepModalOpenAfterEdit = false)
    {
        var completion = BeginPopup(
            "Task alarm",
            "Choose a date and time for a one-time reminder. Android may delay reminders slightly if exact-alarm access is disabled.",
            "alarm");

        _popupPreferredWidth = keepModalOpenAfterEdit ? 380d : 330d;
        ApplyResponsivePopupBounds();

        var enabled = new Microsoft.Maui.Controls.Switch
        {
            IsToggled = task.IsReminderEnabled && task.ReminderAt.HasValue,
            OnColor = Color.FromArgb("#8875E8"),
            ThumbColor = Colors.White,
            VerticalOptions = LayoutOptions.Center
        };
        var enableLabel = new Label
        {
            Text = LocalizationService.Translate("Enable reminder"),
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = PopupColor("#303545", "#F1F2FA"),
            VerticalTextAlignment = TextAlignment.Center
        };
        var enableRow = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        enableRow.Add(enableLabel, 0, 0);
        enableRow.Add(enabled, 1, 0);
        ModalBody.Children.Add(enableRow);

        var defaultReminder = task.ReminderAt is { } oldReminder && oldReminder > DateTime.Now
            ? oldReminder
            : DateTime.Now.AddMinutes(5);
        var initialDate = defaultReminder.Date < DateTime.Today ? DateTime.Today : defaultReminder.Date;
        var initialTime = defaultReminder.TimeOfDay;
        var datePicker = new DatePicker
        {
            Date = initialDate,
            MinimumDate = DateTime.Today,
            Format = "ddd, dd MMM yyyy",
            FontSize = 14,
            TextColor = PopupColor("#303545", "#F1F2FA"),
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill
        };
        var timePicker = new TimePicker
        {
            Time = new TimeSpan(initialTime.Hours, initialTime.Minutes, 0),
            Format = "HH:mm",
            FontSize = 14,
            TextColor = PopupColor("#303545", "#F1F2FA"),
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill
        };
        var dateBorder = CreateReminderPickerBorder(datePicker);
        var timeBorder = CreateReminderPickerBorder(timePicker);
        ModalBody.Children.Add(new Label
        {
            Text = LocalizationService.Translate("Reminder date"),
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = PopupColor("#777E92", "#A7ADC4")
        });
        ModalBody.Children.Add(dateBorder);
        ModalBody.Children.Add(new Label
        {
            Text = LocalizationService.Translate("Reminder time"),
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = PopupColor("#777E92", "#A7ADC4")
        });
        ModalBody.Children.Add(timeBorder);

        void UpdateEnabledState()
        {
            datePicker.IsEnabled = enabled.IsToggled;
            timePicker.IsEnabled = enabled.IsToggled;
            dateBorder.Opacity = enabled.IsToggled ? 1 : 0.42;
            timeBorder.Opacity = enabled.IsToggled ? 1 : 0.42;
        }
        enabled.Toggled += (_, _) => UpdateEnabledState();
        UpdateEnabledState();

        var actions = new Grid { ColumnSpacing = 9 };
        actions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        actions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        actions.Add(CreatePopupAction(keepModalOpenAfterEdit ? "Back to task options" : "Cancel", null, primary: false, destructive: false,
            customAction: () =>
            {
                if (keepModalOpenAfterEdit) CompletePopupInPlace(null);
                else CompletePopup(null);
            }), 0, 0);
        actions.Add(CreatePopupAction("Save alarm", "__SAVE_ALARM__", primary: true, destructive: false,
            customAction: () =>
            {
                if (enabled.IsToggled)
                {
                    var candidate = DateTime.SpecifyKind((datePicker.Date ?? DateTime.Today).Date.Add(timePicker.Time ?? TimeSpan.Zero), DateTimeKind.Local);
                    if (candidate <= DateTime.Now)
                    {
                        SetModalMessageText("The alarm time must be later than the current time.");
                        ModalMessage.TextColor = Color.FromArgb("#D45C72");
                        ModalMessage.IsVisible = true;
                        return;
                    }
                }
                if (keepModalOpenAfterEdit) CompletePopupInPlace("SAVE_ALARM");
                else CompletePopup("SAVE_ALARM");
            }), 1, 0);
        ModalButtons.Children.Add(actions);

        var result = await completion.Task;
        _popupPreferredWidth = 380d;
        ApplyResponsivePopupBounds();
        if (result != "SAVE_ALARM") return;

        if (!enabled.IsToggled)
        {
            task.IsReminderEnabled = false;
            task.ReminderAt = null;
            _wallpaperViewModel.StatusMessage = LocalizationService.Translate("Reminder disabled.");
            return;
        }

        var scheduledAt = DateTime.SpecifyKind((datePicker.Date ?? DateTime.Today).Date.Add(timePicker.Time ?? TimeSpan.Zero), DateTimeKind.Local);
        if (scheduledAt <= DateTime.Now)
        {
            await ShowInfoPopupAsync("Choose a future time", "The alarm time must be later than the current time.", isError: true,
                keepModalOpenOnDismiss: keepModalOpenAfterEdit);
            return;
        }

        if (!await EnsureNotificationPermissionAsync(keepModalOpenOnDismiss: keepModalOpenAfterEdit))
            return;

        task.IsReminderEnabled = false;
        task.ReminderAt = scheduledAt;
        task.IsReminderEnabled = true;
#if ANDROID
        _wallpaperViewModel.StatusMessage = LocalizationService.IsPersian
            ? LocalizationService.LocalizeDigits($"Ø²Ù†Ú¯ Ø¨Ø±Ø§ÛŒ {scheduledAt:yyyy/MM/dd HH:mm} ØªÙ†Ø¸ÛŒÙ… Ø´Ø¯.")
            : (_viewModel.Reminders?.CanUseExactAlarms() == true)
                ? $"Alarm set for {scheduledAt:dd MMM, HH:mm}."
                : $"Alarm saved for {scheduledAt:dd MMM, HH:mm}; Android may deliver it a little late.";
#else
        _wallpaperViewModel.StatusMessage = LocalizationService.IsPersian ? LocalizationService.LocalizeDigits($"Ø²Ù†Ú¯ Ø¨Ø±Ø§ÛŒ {scheduledAt:yyyy/MM/dd HH:mm} ØªÙ†Ø¸ÛŒÙ… Ø´Ø¯.") : $"Alarm set for {scheduledAt:dd MMM, HH:mm}.";
#endif
    }
    private Border CreateReminderPickerBorder(View picker) => new()
    {
        Padding = new Thickness(10, 4),
        MinimumHeightRequest = 48,
        BackgroundColor = PopupColor("#E1E6F0", "#1A1B26"),
        Stroke = new SolidColorBrush(PopupColor("#D1D7E3", "#292B3A")),
        StrokeThickness = 1,
        StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(13) },
        Shadow = CreateNeumorphicShadow(0.20f),
        Content = picker
    };
    private async Task ChooseTaskTextDirectionAsync(TaskItemViewModel task, bool keepModalOpenOnSelection = false)
    {
        var currentSetting = task.TextDirection switch
        {
            TaskTextDirection.LeftToRight => "Left to right",
            TaskTextDirection.RightToLeft => "Right to left",
            _ => "Automatic"
        };
        var currentSettingLabel = LocalizationService.Translate("Current setting:");
        var currentSettingText = LocalizationService.Translate(currentSetting);
        var instruction = LocalizationService.Translate(
            "Choose how this task's text should be arranged. Automatic follows the first letter's writing direction.");
        var message = $"{currentSettingLabel} {currentSettingText}\n{instruction}";
        var choice = await ShowChoicePopupAsync(
            "Task text direction",
            message,
            new[] { "Automatic", "Left to right", "Right to left" },
            cancelLabel: "Back to task options",
            icons: new[] { "direction-auto", "direction-ltr", "direction-rtl" },
            popupIcon: "direction",
            keepOpenOnSelection: keepModalOpenOnSelection);

        if (choice is null) return;
        task.TextDirection = choice switch
        {
            "Left to right" => TaskTextDirection.LeftToRight,
            "Right to left" => TaskTextDirection.RightToLeft,
            _ => TaskTextDirection.Auto
        };
    }

    private async Task ChooseTaskColorAsync(TaskItemViewModel task, bool keepModalOpenOnSelection = false)
    {
        var selection = await ShowTaskPalettePopupAsync(task, keepModalOpenOnSelection);
        if (string.IsNullOrWhiteSpace(selection)) return;

        var parts = selection.Split('|', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return;

        var paletteKey = parts[0];
        var colorKey = parts[1];
        if (!TodoPalette.IsValidPaletteKey(paletteKey)) return;

        var option = TodoPalette.Options.FirstOrDefault(item => item.Key == colorKey);
        if (option is null) return;

        _viewModel.ChangeColor(task, option.Key, paletteKey);
        _wallpaperViewModel.StatusMessage = LocalizationService.IsPersian ? $"{LocalizationService.Translate(option.Name)} Â· Ù¾Ø§Ù„Øª {LocalizationService.Translate(TodoPalette.GetPaletteName(paletteKey))}" : $"{option.Name} Â· {TodoPalette.GetPaletteName(paletteKey)} palette.";
    }
    private Task<string?> ShowTaskPalettePopupAsync(TaskItemViewModel task, bool keepModalOpenOnSelection = false)
    {
        var completion = BeginPopup(
            "Task color",
            "Choose a palette first, then a color. Your choice stays the same in Light and Dark mode.",
            "palette");

        var selectedPaletteKey = TodoPalette.IsValidPaletteKey(task.ColorPaletteKey)
            ? task.ColorPaletteKey
            : TodoPalette.VibrantPaletteKey;

        var paletteSelector = new Grid
        {
            ColumnSpacing = 6,
            Margin = new Thickness(0, 0, 0, 10),
            HeightRequest = 40
        };
        for (var i = 0; i < TodoPalette.Palettes.Count; i++)
            paletteSelector.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        var paletteBorders = new Dictionary<string, Border>(StringComparer.Ordinal);
        var paletteLabels = new Dictionary<string, Label>(StringComparer.Ordinal);

        // Declare the captured layout before attaching event handlers so it is
        // definitely assigned when RefreshColorRows is invoked by a tap.
        var colorRows = new VerticalStackLayout { Spacing = 7 };

        for (var paletteIndex = 0; paletteIndex < TodoPalette.Palettes.Count; paletteIndex++)
        {
            var palette = TodoPalette.Palettes[paletteIndex];
            var label = new Label
            {
                Text = LocalizationService.Translate(palette.Name),
                FontSize = 10.5,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center,
                VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center,
                TextColor = PopupColor("#5B536F", "#C6BEDD")
            };
            var border = new Border
            {
                Padding = new Thickness(4, 2),
                Content = label,
                BackgroundColor = PopupColor("#E9EAF2", "#20212E"),
                Stroke = new SolidColorBrush(PopupColor("#D1D6E3", "#38394C")),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(11) },
                VerticalOptions = LayoutOptions.Fill,
                HorizontalOptions = LayoutOptions.Fill
            };
            var key = palette.Key;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                selectedPaletteKey = key;
                RefreshPaletteSelector();
                RefreshColorRows();
            };
            border.GestureRecognizers.Add(tap);
            paletteBorders[key] = border;
            paletteLabels[key] = label;
            paletteSelector.Add(border, paletteIndex, 0);
        }

        ModalBody.Children.Add(paletteSelector);
        ModalBody.Children.Add(colorRows);

        void RefreshPaletteSelector()
        {
            foreach (var palette in TodoPalette.Palettes)
            {
                var selected = selectedPaletteKey == palette.Key;
                paletteBorders[palette.Key].BackgroundColor = selected
                    ? PopupColor("#E1D9FF", "#3A3158")
                    : PopupColor("#E9EAF2", "#20212E");
                paletteBorders[palette.Key].Stroke = new SolidColorBrush(selected
                    ? Color.FromArgb("#8A76EB")
                    : PopupColor("#D1D6E3", "#38394C"));
                paletteBorders[palette.Key].StrokeThickness = selected ? 1.6 : 1;
                paletteLabels[palette.Key].TextColor = selected
                    ? PopupColor("#46347F", "#F1ECFF")
                    : PopupColor("#5B536F", "#C6BEDD");
            }
        }

        void RefreshColorRows()
        {
            colorRows.Children.Clear();
            foreach (var option in TodoPalette.Options)
            {
                // The swatch comes from the selected palette, never from app theme.
                var swatchColor = Color.FromArgb(TodoPalette.GetHex(option.Key, selectedPaletteKey));
                var selected = task.ColorKey == option.Key && task.ColorPaletteKey == selectedPaletteKey;
                var row = new Grid
                {
                    ColumnSpacing = 12,
                    Padding = new Thickness(12, 7),
                    HeightRequest = 52
                };
                row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(34)));
                row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(24)));

                row.Add(new Border
                {
                    WidthRequest = 28,
                    HeightRequest = 28,
                    BackgroundColor = swatchColor,
                    Stroke = new SolidColorBrush(PopupColor("#D9D1E7", "#5A536C")),
                    StrokeThickness = 1,
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
                    Shadow = CreateNeumorphicShadow(0.24f),
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }, 0, 0);
                row.Add(new Label
                {
                    Text = LocalizationService.Translate(option.Name),
                    FontSize = 13,
                    FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None,
                    TextColor = PopupColor("#3F4559", "#E5E7F5"),
                    VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center
                }, 1, 0);
                row.Add(new Image
                {
                    Source = "icon_check.svg",
                    WidthRequest = 22,
                    HeightRequest = 22,
                    Aspect = Aspect.AspectFit,
                    IsVisible = selected,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }, 2, 0);

                var choiceBorder = new Border
                {
                    BackgroundColor = selected ? PopupColor("#E2E7F1", "#2A2C3D") : PopupColor("#F0F2F8", "#252736"),
                    Stroke = new SolidColorBrush(selected ? Color.FromArgb("#8A76EB") : PopupColor("#D1D9E5", "#383A4D")),
                    StrokeThickness = selected ? 1.5 : 1,
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
                    Shadow = CreateNeumorphicShadow(selected ? 0.26f : 0.38f),
                    Content = row
                };
                var colorTap = new TapGestureRecognizer();
                var selectedColorKey = option.Key;
                colorTap.Tapped += (_, _) =>
                {
                    var result = $"{selectedPaletteKey}|{selectedColorKey}";
                    if (keepModalOpenOnSelection) CompletePopupInPlace(result);
                    else CompletePopup(result);
                };
                choiceBorder.GestureRecognizers.Add(colorTap);
                colorRows.Children.Add(choiceBorder);
            }
        }

        RefreshPaletteSelector();
        RefreshColorRows();
        ModalButtons.Children.Add(CreatePopupAction(keepModalOpenOnSelection ? "Back to task options" : "Cancel", null, primary: false, destructive: false,
            customAction: () =>
            {
                if (keepModalOpenOnSelection) CompletePopupInPlace(null);
                else CompletePopup(null);
            }));
        return completion.Task;
    }

    // Native Android reordering gives the user a lifted card, a visible drop target,
    // haptic feedback on target changes, and a deterministic drop on release.
}



