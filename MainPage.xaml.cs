using Locksy.Models;
using Locksy.Platforms.Android;
using Locksy.Services;
using Locksy.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;


namespace Locksy;

public partial class MainPage : ContentPage
{
    private const int MaxWallpaperTasks = 8;
    private const int AutoApplyDebounceMilliseconds = 300;
    private const string AutoApplyPreferenceKey = "locksy-auto-apply";
    private const string LayoutPreferenceKey = "locksy-wallpaper-layout-v2";
    private const string VerticalOffsetPreferenceKey = "locksy-wallpaper-vertical-offset-v2";
    private const string ClockClearancePreferenceKey = "locksy-wallpaper-clock-clearance-v2";
    private const string LastWallpaperOperationPreferenceKey = "locksy-last-wallpaper-operation-utc-ticks";
    private const string AutoApplyPendingPreferenceKey = "locksy-auto-apply-pending";
    private const string AutoApplyQueuedStatus = "Auto update queued for background.";

    private readonly MainViewModel _viewModel;
    private readonly IWallpaperService _wallpaperService;
    // Summary names are ordered by the WallpaperLayout enum values.
    private static readonly string[] WallpaperLayoutNames =
    {
        "Tasks below top clock (legacy)",
        "Split around center clock",
        "Tasks top",
        "Centered task list",
        "Screen-aware (auto)",
        "Tasks bottom"
    };

    // UI order is deliberately more intuitive than the renderer enum order.
    private static readonly (string Name, WallpaperLayout Layout)[] WallpaperLayoutChoices =
    {
        ("Tasks bottom", WallpaperLayout.TasksBottom),
        ("Tasks top", WallpaperLayout.AboveBottomClock),
        ("Centered task list", WallpaperLayout.CenteredList),
        ("Split around center clock", WallpaperLayout.SplitAroundCenterClock),
        ("Screen-aware (auto)", WallpaperLayout.ScreenAware)
    };
    private int _selectedLayoutIndex;
    private float _verticalOffset;
    private float _clockClearance = 0.18f;
    private TaskCompletionSource<string?>? _popupCompletionSource;
    private bool _initializing = true;
    private bool _startupInitializationComplete;
    private bool _isApplying;
    private bool _autoApplyPending;
    private bool _isAddingTask;
    private bool _isSendingTaskToBackground;
    // Settings chosen before creation apply to the next reminder only.
    private bool _hasPendingNewTaskSettings;
    private string _pendingNewTaskColorKey = TodoPalette.DefaultKey;
    private string _pendingNewTaskPaletteKey = TodoPalette.VibrantPaletteKey;
    private bool _pendingNewTaskReminderEnabled;
    private DateTime? _pendingNewTaskReminderAt;
    private DateTime _lastBackPressUtc = DateTime.MinValue;
    private bool _compactEditorPopup;
#if ANDROID
    private readonly Dictionary<Android.Views.View, WeakReference<Label>> _nativeTitleViews = new();
    private readonly Dictionary<Android.Views.View, WeakReference<Border>> _nativeTaskRows = new();
    private readonly Dictionary<Android.Views.View, WeakReference<Label>> _nativeTaskGrips = new();
    private TodoItem? _reorderCandidate;
    private TodoItem? _draggedTask;
    private TodoItem? _hoveredTask;
    private Border? _draggedBorder;
    private Border? _hoveredBorder;
    private Android.Views.View? _reorderGripView;
    private double _draggedOriginalStrokeThickness = 1;
    private double _hoveredOriginalStrokeThickness = 1;
    private double _draggedOriginalOpacity = 1;
    private double _draggedOriginalScale = 1;
    private double _hoveredOriginalScale = 1;
    private double _draggedOriginalTranslationY;
    private int _draggedOriginalZIndex;
    private float _reorderStartRawX;
    private float _reorderStartRawY;
    private bool _isReorderDragging;
#endif

    public MainPage(bool startHidden = false, IEnumerable<TodoItem>? initialTasks = null)
    {
        InitializeComponent();
        NavigationPage.SetHasNavigationBar(this, false);
        // Keep the themed page surface visible during navigation. Only the content grid
        // starts transparent, so the native page transition never reveals a black window.
        Opacity = 1;
        PageContent.Opacity = startHidden ? 0 : 1;
        _viewModel = new MainViewModel(initialTasks);
        BindingContext = _viewModel;
        _wallpaperService = new Platforms.Android.AndroidWallpaperService();

        var savedTheme = ThemeService.GetSavedPreference();
        var startInDarkMode = savedTheme switch
        {
            ThemePreference.Dark => true,
            ThemePreference.Light => false,
            _ => ThemeService.IsDarkMode()
        };
        UpdateThemeToggleVisual(startInDarkMode);
        var savedLayout = Preferences.Default.Get(LayoutPreferenceKey, (int)WallpaperLayout.ScreenAware);
        // In earlier Locksy builds, the visible "Tasks bottom" option was stored as
        // BelowTopClock (0). Migrate that old visible choice to the true bottom anchor.
        if (savedLayout == (int)WallpaperLayout.BelowTopClock)
        {
            savedLayout = (int)WallpaperLayout.TasksBottom;
            Preferences.Default.Set(LayoutPreferenceKey, savedLayout);
        }
        _selectedLayoutIndex = Math.Clamp(savedLayout, 0, (int)WallpaperLayout.TasksBottom);
        _verticalOffset = Math.Clamp(Preferences.Default.Get(VerticalOffsetPreferenceKey, 0f), -0.20f, 0.20f);
        _clockClearance = Math.Clamp(Preferences.Default.Get(ClockClearancePreferenceKey, 0.18f), 0.08f, 0.30f);
        UpdateClockLayoutSummary();
        AutoApplySwitch.IsToggled = Preferences.Default.Get(AutoApplyPreferenceKey, true);
        UpdateAutoApplyVisual(AutoApplySwitch.IsToggled);
        _autoApplyPending = AutoApplySwitch.IsToggled
            && Preferences.Default.Get(AutoApplyPendingPreferenceKey, false);
        _initializing = false;

        if (_autoApplyPending)
            StatusLabel.Text = AutoApplyQueuedStatus;

        _viewModel.TasksChanged += OnTasksChanged;
#if ANDROID
        ReminderScheduler.SyncTasks(_viewModel.Tasks);
#endif
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (width <= 0 || height <= 0 || ModalCard is null || ModalBodyScroll is null)
            return;

        // Leave room for the page edges and Android system bars. The popup's body
        // absorbs the remaining height and scrolls independently of its fixed footer.
        var modalHeight = _compactEditorPopup
            ? Math.Min(460d, Math.Max(280d, height - 56d))
            : Math.Max(280d, height - 56d);
        var bodyHeight = _compactEditorPopup
            ? Math.Clamp(height - 470d, 130d, 190d)
            : Math.Clamp(height - 470d, 140d, 300d);
        if (Math.Abs(ModalCard.MaximumHeightRequest - modalHeight) > 1d)
            ModalCard.MaximumHeightRequest = modalHeight;
        if (Math.Abs(ModalBodyScroll.MaximumHeightRequest - bodyHeight) > 1d)
            ModalBodyScroll.MaximumHeightRequest = bodyHeight;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // SplashPage is the app's root during startup. Do not present another splash
        // here, and do not force Opacity to 1: a hidden page must remain hidden until
        // SplashPage crossfades it in. Avoid wallpaper rendering during the transition;
        // SplashPage explicitly starts deferred work after the crossfade completes.
        if (_startupInitializationComplete)
            return;

        _startupInitializationComplete = true;
        _viewModel.RefreshTaskAppearances();
    }

    // Called by SplashPage after the first UI transition. Do not invoke WallpaperManager
    // while the main screen is visible: some Xiaomi/POCO builds recreate the Activity and
    // flash a black frame. Only edits explicitly queued by the user are applied when the
    // host Activity is backgrounded.
    public void CompleteStartupTransition()
    {
        if (_autoApplyPending)
            StatusLabel.Text = AutoApplyQueuedStatus;
    }

    public void PrepareForStartupReveal() => PageContent.Opacity = 0;

    public Task RevealStartupContentAsync(uint duration = 320) =>
        PageContent.FadeTo(1, duration, Easing.CubicOut);

    public void ShowStartupContentImmediately()
    {
        Opacity = 1;
        PageContent.Opacity = 1;
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
        _isAddingTask = true;
        var addedWithDraftSettings = false;
        try
        {
            if (_hasPendingNewTaskSettings)
            {
                var title = (_viewModel.NewTaskText ?? string.Empty).Trim();
                if (title.Length > 0)
                {
                    if (_pendingNewTaskReminderEnabled &&
                        (!_pendingNewTaskReminderAt.HasValue || _pendingNewTaskReminderAt.Value <= DateTime.Now))
                    {
                        await ShowInfoPopupAsync(
                            "Choose a future time",
                            "The saved reminder time has passed. Open settings and choose a future date and time before adding this reminder.",
                            isError: true);
                        return;
                    }

                    if (_pendingNewTaskReminderEnabled && !await EnsureNotificationPermissionAsync())
                        return;

                    var created = _viewModel.AddTaskWithSettings(
                        title,
                        _pendingNewTaskColorKey,
                        _pendingNewTaskPaletteKey,
                        _pendingNewTaskReminderEnabled,
                        _pendingNewTaskReminderAt);
                    addedWithDraftSettings = created is not null;
                    if (addedWithDraftSettings)
                    {
                        _hasPendingNewTaskSettings = false;
                        _pendingNewTaskColorKey = TodoPalette.DefaultKey;
                        _pendingNewTaskPaletteKey = TodoPalette.VibrantPaletteKey;
                        _pendingNewTaskReminderEnabled = false;
                        _pendingNewTaskReminderAt = null;
                        StatusLabel.Text = created!.IsReminderActive
                            ? $"Reminder added · alarm {created.ReminderAt:dd MMM, HH:mm}."
                            : "Reminder added.";
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
            _isAddingTask = false;
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
            if (_autoApplyPending && AutoApplySwitch.IsToggled && !_isApplying)
                StatusLabel.Text = AutoApplyQueuedStatus;
        });
    }

    private async void NewTaskSettingsButton_Clicked(object? sender, EventArgs e)
    {
        var initialTitle = (_viewModel.NewTaskText ?? string.Empty).Trim();
        var selectedPaletteKey = _hasPendingNewTaskSettings
            ? _pendingNewTaskPaletteKey
            : TodoPalette.VibrantPaletteKey;
        var selectedColorKey = _hasPendingNewTaskSettings
            ? _pendingNewTaskColorKey
            : TodoPalette.DefaultKey;
        var alarmEnabled = _hasPendingNewTaskSettings && _pendingNewTaskReminderEnabled;
        var initialReminder = _pendingNewTaskReminderAt is { } savedTime && savedTime > DateTime.Now
            ? savedTime
            : DateTime.Now.AddMinutes(5);

        var completion = BeginPopup(
            "New reminder settings",
            "Enter the reminder, choose its color, and optionally set an alarm before adding it.",
            "◷");
        _compactEditorPopup = true;
        var availablePageWidth = Width > 0 ? Width : 360d;
        var popupWidth = Math.Max(270d, Math.Min(370d, availablePageWidth - 28d));
        ModalCard.WidthRequest = popupWidth;
        ModalCard.MaximumWidthRequest = popupWidth;
        ModalCard.MaximumHeightRequest = Math.Max(320d, Math.Min(660d, Height - 32d));
        ModalBodyScroll.MaximumHeightRequest = Math.Clamp(Height - 360d, 230d, 430d);

        // Larger clock mark, matching the user's request for better visibility in settings.
        var clockBadge = new Border
        {
            WidthRequest = 58,
            HeightRequest = 58,
            HorizontalOptions = LayoutOptions.Center,
            Padding = 0,
            BackgroundColor = PopupColor("#E7E1FF", "#3A3158"),
            Stroke = new SolidColorBrush(PopupColor("#CFC4FF", "#5A4B82")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(19) },
            Content = new Label
            {
                Text = "◷",
                FontSize = 38,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#8875E8"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };
        ModalBody.Children.Add(clockBadge);

        ModalBody.Children.Add(new Label
        {
            Text = "Reminder title",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = PopupColor("#303545", "#F1F2FA")
        });
        var draftTitleEntry = new Entry
        {
            Text = initialTitle,
            Placeholder = "What do you need to remember?",
            MaxLength = 300,
            ReturnType = ReturnType.Done,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
            FontSize = 14,
            BackgroundColor = Colors.Transparent,
            TextColor = PopupColor("#303545", "#F1F2FA"),
            PlaceholderColor = PopupColor("#8C8798", "#9690A8")
        };
        ModalBody.Children.Add(new Border
        {
            Padding = new Thickness(10, 4),
            MinimumHeightRequest = 48,
            BackgroundColor = PopupColor("#E1E6F0", "#1A1B26"),
            Stroke = new SolidColorBrush(PopupColor("#D1D7E3", "#292B3A")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(13) },
            Content = draftTitleEntry
        });

        ModalBody.Children.Add(new Label
        {
            Text = "Task color",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = PopupColor("#303545", "#F1F2FA")
        });

        var paletteSelector = new Grid
        {
            ColumnSpacing = 6,
            HeightRequest = 40,
            Margin = new Thickness(0, 0, 0, 2)
        };
        foreach (var _ in TodoPalette.Palettes)
            paletteSelector.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        var paletteBorders = new Dictionary<string, Border>(StringComparer.Ordinal);
        var paletteLabels = new Dictionary<string, Label>(StringComparer.Ordinal);

        var colorGrid = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
        for (var i = 0; i < 3; i++)
            colorGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var i = 0; i < (TodoPalette.Options.Count + 2) / 3; i++)
            colorGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var colorBorders = new Dictionary<string, Border>(StringComparer.Ordinal);
        var colorLabels = new Dictionary<string, Label>(StringComparer.Ordinal);
        var colorSwatches = new Dictionary<string, Border>(StringComparer.Ordinal);

        void RefreshDraftColors()
        {
            foreach (var palette in TodoPalette.Palettes)
            {
                var chosen = palette.Key == selectedPaletteKey;
                paletteBorders[palette.Key].BackgroundColor = chosen
                    ? PopupColor("#E1D9FF", "#3A3158")
                    : PopupColor("#E9EAF2", "#20212E");
                paletteBorders[palette.Key].Stroke = new SolidColorBrush(chosen
                    ? Color.FromArgb("#8A76EB")
                    : PopupColor("#D1D6E3", "#38394C"));
                paletteBorders[palette.Key].StrokeThickness = chosen ? 1.7 : 1;
                paletteLabels[palette.Key].TextColor = chosen
                    ? PopupColor("#46347F", "#F1ECFF")
                    : PopupColor("#5B536F", "#C6BEDD");
            }

            foreach (var option in TodoPalette.Options)
            {
                colorSwatches[option.Key].BackgroundColor = Color.FromArgb(TodoPalette.GetHex(option.Key, selectedPaletteKey));
                var chosen = option.Key == selectedColorKey;
                colorBorders[option.Key].BackgroundColor = chosen
                    ? PopupColor("#E4DFFF", "#38304F")
                    : PopupColor("#F0F2F8", "#252736");
                colorBorders[option.Key].Stroke = new SolidColorBrush(chosen
                    ? Color.FromArgb("#8A76EB")
                    : PopupColor("#D1D9E5", "#383A4D"));
                colorBorders[option.Key].StrokeThickness = chosen ? 1.7 : 1;
                colorLabels[option.Key].TextColor = PopupColor("#3F4559", "#E5E7F5");
            }
        }

        for (var i = 0; i < TodoPalette.Palettes.Count; i++)
        {
            var palette = TodoPalette.Palettes[i];
            var label = new Label
            {
                Text = palette.Name,
                FontSize = 10,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.TailTruncation,
                TextColor = PopupColor("#5B536F", "#C6BEDD")
            };
            var border = new Border
            {
                Padding = new Thickness(3, 2),
                Content = label,
                BackgroundColor = PopupColor("#E9EAF2", "#20212E"),
                Stroke = new SolidColorBrush(PopupColor("#D1D6E3", "#38394C")),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(11) }
            };
            var key = palette.Key;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                selectedPaletteKey = key;
                RefreshDraftColors();
            };
            border.GestureRecognizers.Add(tap);
            paletteBorders[key] = border;
            paletteLabels[key] = label;
            paletteSelector.Add(border, i, 0);
        }

        for (var i = 0; i < TodoPalette.Options.Count; i++)
        {
            var option = TodoPalette.Options[i];
            var swatch = new Border
            {
                WidthRequest = 17,
                HeightRequest = 17,
                BackgroundColor = Color.FromArgb(TodoPalette.GetHex(option.Key, selectedPaletteKey)),
                Stroke = new SolidColorBrush(PopupColor("#D1D7E3", "#53566B")),
                StrokeThickness = 0.7,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8.5) },
                VerticalOptions = LayoutOptions.Center
            };
            var label = new Label
            {
                Text = option.Name,
                FontSize = 10.5,
                VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.TailTruncation,
                TextColor = PopupColor("#3F4559", "#E5E7F5")
            };
            var cell = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(new GridLength(21)), new ColumnDefinition(GridLength.Star) },
                ColumnSpacing = 4,
                Padding = new Thickness(5, 1),
                HeightRequest = 34
            };
            cell.Add(swatch, 0, 0);
            cell.Add(label, 1, 0);
            var border = new Border
            {
                Padding = 2,
                HeightRequest = 36,
                Content = cell,
                BackgroundColor = PopupColor("#F0F2F8", "#252736"),
                Stroke = new SolidColorBrush(PopupColor("#D1D9E5", "#383A4D")),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) }
            };
            var colorKey = option.Key;
            var colorTap = new TapGestureRecognizer();
            colorTap.Tapped += (_, _) =>
            {
                selectedColorKey = colorKey;
                RefreshDraftColors();
            };
            border.GestureRecognizers.Add(colorTap);
            colorBorders[colorKey] = border;
            colorLabels[colorKey] = label;
            colorSwatches[colorKey] = swatch;
            colorGrid.Add(border, i % 3, i / 3);
        }
        ModalBody.Children.Add(paletteSelector);
        ModalBody.Children.Add(colorGrid);
        RefreshDraftColors();

        var alarmTitle = new Label
        {
            Text = "Reminder alarm",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = PopupColor("#303545", "#F1F2FA"),
            VerticalTextAlignment = TextAlignment.Center
        };
        var alarmSwitch = new Switch
        {
            IsToggled = alarmEnabled,
            OnColor = Color.FromArgb("#8875E8"),
            ThumbColor = Colors.White,
            VerticalOptions = LayoutOptions.Center
        };
        var alarmRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Margin = new Thickness(0, 7, 0, 0)
        };
        alarmRow.Add(alarmTitle, 0, 0);
        alarmRow.Add(alarmSwitch, 1, 0);
        ModalBody.Children.Add(alarmRow);

        var defaultReminder = initialReminder;
        var datePicker = new DatePicker
        {
            Date = defaultReminder.Date,
            MinimumDate = DateTime.Today,
            Format = "ddd, dd MMM yyyy",
            FontSize = 13,
            TextColor = PopupColor("#303545", "#F1F2FA"),
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill
        };
        var timePicker = new TimePicker
        {
            Time = new TimeSpan(defaultReminder.Hour, defaultReminder.Minute, 0),
            Format = "HH:mm",
            FontSize = 13,
            TextColor = PopupColor("#303545", "#F1F2FA"),
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill
        };
        var dateTimeGrid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 8
        };
        dateTimeGrid.Add(CreateReminderPickerBorder(datePicker), 0, 0);
        dateTimeGrid.Add(CreateReminderPickerBorder(timePicker), 1, 0);
        ModalBody.Children.Add(new Label
        {
            Text = "Reminder date and time",
            FontSize = 11,
            TextColor = PopupColor("#777E92", "#A7ADC4")
        });
        ModalBody.Children.Add(dateTimeGrid);

        void UpdateAlarmControls()
        {
            datePicker.IsEnabled = alarmSwitch.IsToggled;
            timePicker.IsEnabled = alarmSwitch.IsToggled;
            dateTimeGrid.Opacity = alarmSwitch.IsToggled ? 1 : 0.42;
        }
        alarmSwitch.Toggled += (_, _) => UpdateAlarmControls();
        UpdateAlarmControls();

        bool ValidateDraft(bool adding)
        {
            if (alarmSwitch.IsToggled && (datePicker.Date.Date + timePicker.Time) <= DateTime.Now)
            {
                ModalMessage.Text = "Choose a future date and time for the alarm.";
                ModalMessage.IsVisible = true;
                return false;
            }
            if (adding && string.IsNullOrWhiteSpace(draftTitleEntry.Text))
            {
                ModalMessage.Text = "Enter a reminder title before adding it.";
                ModalMessage.IsVisible = true;
                return false;
            }
            return true;
        }

        var actions = new Grid { ColumnSpacing = 8, RowSpacing = 7 };
        actions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        actions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        actions.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        actions.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        actions.Add(CreatePopupAction("Cancel", null, primary: false, destructive: false), 0, 0);
        actions.Add(CreatePopupAction("Save settings", "SAVE_SETTINGS", primary: false, destructive: false,
            customAction: () => { if (ValidateDraft(false)) CompletePopup("SAVE_SETTINGS"); }), 1, 0);
        var addAction = CreatePopupAction("Add reminder", "ADD_REMINDER", primary: true, destructive: false,
            customAction: () => { if (ValidateDraft(true)) CompletePopup("ADD_REMINDER"); });
        actions.Add(addAction, 0, 1);
        Grid.SetColumnSpan(addAction, 2);
        ModalButtons.Children.Add(actions);

        var result = await completion.Task;
        _compactEditorPopup = false;
        ModalCard.WidthRequest = -1;
        ModalCard.MaximumWidthRequest = 380;
        if (result is null) return;

        _viewModel.NewTaskText = (draftTitleEntry.Text ?? string.Empty).Trim();
        _pendingNewTaskColorKey = selectedColorKey;
        _pendingNewTaskPaletteKey = selectedPaletteKey;
        _pendingNewTaskReminderEnabled = alarmSwitch.IsToggled;
        _pendingNewTaskReminderAt = alarmSwitch.IsToggled
            ? DateTime.SpecifyKind(datePicker.Date.Date + timePicker.Time, DateTimeKind.Local)
            : null;
        _hasPendingNewTaskSettings = true;

        if (result == "SAVE_SETTINGS")
        {
            StatusLabel.Text = "Settings saved for the next reminder.";
            return;
        }

        if (result == "ADD_REMINDER")
            await AddTaskAndKeepInputReadyAsync();
    }

    private async Task<bool> EnsureNotificationPermissionAsync()
    {
#if ANDROID
        PermissionStatus notificationPermission;
        try
        {
            notificationPermission = await Permissions.RequestAsync<Permissions.PostNotifications>();
        }
        catch (Exception ex)
        {
            await ShowInfoPopupAsync("Notification permission", ex.Message, isError: true);
            return false;
        }
        if (notificationPermission != PermissionStatus.Granted)
        {
            await ShowInfoPopupAsync(
                "Notifications are disabled",
                "Locksy cannot show the reminder until notification permission is enabled in Android Settings.",
                isError: true);
            return false;
        }
#endif
        return true;
    }

    private void NewTaskEntry_Unfocused(object? sender, FocusEventArgs e)
    {
        // Keep queued work queued. Do not call WallpaperManager while the editor is in
        // the foreground, because affected OEMs can recreate the Activity during apply.
        if (_autoApplyPending && AutoApplySwitch.IsToggled && !_isSendingTaskToBackground)
            StatusLabel.Text = AutoApplyQueuedStatus;
    }

    protected override bool OnBackButtonPressed()
    {
        // Back dismisses an in-app dialog before it can leave the page.
        if (_popupCompletionSource is not null)
        {
            _lastBackPressUtc = DateTime.MinValue;
            CompletePopup(null);
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
            StatusLabel.Text = "Press back again to leave Locksy.";
#else
        StatusLabel.Text = "Press back again to leave Locksy.";
#endif
        return true;
    }

    // Called from MainActivity.OnResume. Reset only the temporary backgrounding guard.
    // A queued auto-update must never run on resume, or the UI can flash/restart again.
    public void OnHostResumed()
    {
        _isSendingTaskToBackground = false;
        if (_autoApplyPending && AutoApplySwitch.IsToggled)
            StatusLabel.Text = AutoApplyQueuedStatus;
    }

    // Invoked by MainActivity.OnStop after the Activity has actually left the foreground.
    // This is the only path that runs automatic WallpaperManager operations.
    public async Task ApplyQueuedAutoWallpaperWhenBackgroundedAsync(CancellationToken cancellationToken)
    {
        if (!_autoApplyPending || !AutoApplySwitch.IsToggled || _isApplying)
            return;

        // Clear before starting so an OEM Activity recreation cannot re-apply the same
        // snapshot. A failed operation re-queues itself for the next background transition.
        _autoApplyPending = false;
        Preferences.Default.Set(AutoApplyPendingPreferenceKey, false);

        var succeeded = await ApplyWallpaperAsync(showAlert: false, cancellationToken);
        if (!succeeded && AutoApplySwitch.IsToggled)
        {
            _autoApplyPending = true;
            Preferences.Default.Set(AutoApplyPendingPreferenceKey, true);
            StatusLabel.Text = "Auto update queued for background.";
        }
    }

    private void TaskTitle_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is Label { BindingContext: TodoItem task })
            MainThread.BeginInvokeOnMainThread(async () => await EditTaskAsync(task));
    }

    private async void TaskOptionsButton_Clicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: TodoItem task })
            await ShowTaskOptionsAsync(task);
    }

    private async Task ShowTaskOptionsAsync(TodoItem task)
    {
        var action = await ShowChoicePopupAsync(
            "Task options",
            $"Choose what to do with ‘{task.Title}’. ",
            new[] { "Edit task", "Change row color", "Set / edit alarm", "Delete task" },
            icons: new[] { "edit", "palette", "◷", "trash" },
            popupIcon: "options");

        switch (action)
        {
            case "Edit task":
                await EditTaskAsync(task);
                break;
            case "Change row color":
                await ChooseTaskColorAsync(task);
                break;
            case "Set / edit alarm":
                await EditTaskReminderAsync(task);
                break;
            case "Delete task":
                var confirmed = await ShowConfirmationPopupAsync(
                    "Delete this task?",
                    $"Remove ‘{task.Title}’ from your list? This can’t be undone.",
                    "Delete task",
                    destructive: true);
                if (confirmed) task.DeleteCommand?.Execute(null);
                break;
        }
    }

    private async Task EditTaskAsync(TodoItem task)
    {
        var value = await ShowPromptPopupAsync(
            "Edit task",
            "Make the change and save when you’re ready.",
            placeholder: "Task text",
            maxLength: 300,
            initialValue: task.Title);

        if (value is not null)
            _viewModel.EditTask(task, value);
    }

    private async Task EditTaskReminderAsync(TodoItem task)
    {
        var completion = BeginPopup(
            "Task alarm",
            "Choose a date and time for a one-time reminder. Android may delay reminders slightly if exact-alarm access is disabled.",
            "◷");

        _compactEditorPopup = true;
        var availablePageWidth = Width > 0 ? Width : 360d;
        var popupWidth = Math.Max(240d, Math.Min(330d, availablePageWidth - 36d));
        ModalCard.WidthRequest = popupWidth;
        ModalCard.MaximumWidthRequest = popupWidth;
        ModalCard.MaximumHeightRequest = Math.Min(500d, Math.Max(300d, Height - 48d));
        ModalBodyScroll.MaximumHeightRequest = Math.Clamp(Height - 430d, 170d, 250d);

        ModalBody.Children.Add(new Border
        {
            WidthRequest = 58,
            HeightRequest = 58,
            HorizontalOptions = LayoutOptions.Center,
            Padding = 0,
            BackgroundColor = PopupColor("#E7E1FF", "#3A3158"),
            Stroke = new SolidColorBrush(PopupColor("#CFC4FF", "#5A4B82")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(19) },
            Content = new Label
            {
                Text = "◷",
                FontSize = 38,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#8875E8"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        });

        var enabled = new Switch
        {
            IsToggled = task.IsReminderEnabled && task.ReminderAt.HasValue,
            OnColor = Color.FromArgb("#8875E8"),
            ThumbColor = Colors.White,
            VerticalOptions = LayoutOptions.Center
        };
        var enableLabel = new Label
        {
            Text = "Enable reminder",
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
            Text = "Reminder date",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = PopupColor("#777E92", "#A7ADC4")
        });
        ModalBody.Children.Add(dateBorder);
        ModalBody.Children.Add(new Label
        {
            Text = "Reminder time",
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
        actions.Add(CreatePopupAction("Cancel", null, primary: false, destructive: false), 0, 0);
        actions.Add(CreatePopupAction("Save alarm", "__SAVE_ALARM__", primary: true, destructive: false,
            customAction: () => CompletePopup("SAVE_ALARM")), 1, 0);
        ModalButtons.Children.Add(actions);

        var result = await completion.Task;
        _compactEditorPopup = false;
        ModalCard.WidthRequest = -1;
        ModalCard.MaximumWidthRequest = 380;
        if (result != "SAVE_ALARM") return;

        if (!enabled.IsToggled)
        {
            task.IsReminderEnabled = false;
            task.ReminderAt = null;
            StatusLabel.Text = "Reminder disabled.";
            return;
        }

        var scheduledAt = DateTime.SpecifyKind(datePicker.Date.Date + timePicker.Time, DateTimeKind.Local);
        if (scheduledAt <= DateTime.Now)
        {
            await ShowInfoPopupAsync("Choose a future time", "The alarm time must be later than the current time.", isError: true);
            return;
        }

        if (!await EnsureNotificationPermissionAsync())
            return;

        task.IsReminderEnabled = false;
        task.ReminderAt = scheduledAt;
        task.IsReminderEnabled = true;
#if ANDROID
        StatusLabel.Text = ReminderScheduler.CanUseExactAlarms()
            ? $"Alarm set for {scheduledAt:dd MMM, HH:mm}."
            : $"Alarm saved for {scheduledAt:dd MMM, HH:mm}; Android may deliver it a little late.";
#else
        StatusLabel.Text = $"Alarm set for {scheduledAt:dd MMM, HH:mm}.";
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

    private async Task ChooseTaskColorAsync(TodoItem task)
    {
        var selection = await ShowTaskPalettePopupAsync(task);
        if (string.IsNullOrWhiteSpace(selection)) return;

        var parts = selection.Split('|', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return;

        var paletteKey = parts[0];
        var colorKey = parts[1];
        if (!TodoPalette.IsValidPaletteKey(paletteKey)) return;

        var option = TodoPalette.Options.FirstOrDefault(item => item.Key == colorKey);
        if (option is null) return;

        _viewModel.ChangeColor(task, option.Key, paletteKey);
        StatusLabel.Text = $"{option.Name} · {TodoPalette.GetPaletteName(paletteKey)} palette.";
    }

    private Task<string?> ShowTaskPalettePopupAsync(TodoItem task)
    {
        var completion = BeginPopup(
            "Task color",
            "Choose a palette first, then a color. Your choice stays the same in Light and Dark mode.",
            "◉");

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
                Text = palette.Name,
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
                    Text = option.Name,
                    FontSize = 13,
                    FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None,
                    TextColor = PopupColor("#3F4559", "#E5E7F5"),
                    VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center
                }, 1, 0);
                row.Add(new Label
                {
                    Text = selected ? "✓" : string.Empty,
                    FontSize = 17,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#8A76EB"),
                    HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center,
                    VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center
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
                colorTap.Tapped += (_, _) => CompletePopup($"{selectedPaletteKey}|{selectedColorKey}");
                choiceBorder.GestureRecognizers.Add(colorTap);
                colorRows.Children.Add(choiceBorder);
            }
        }

        RefreshPaletteSelector();
        RefreshColorRows();
        ModalButtons.Children.Add(CreatePopupAction("Cancel", null, primary: false, destructive: false));
        return completion.Task;
    }

    // Native Android reordering gives the user a lifted card, a visible drop target,
    // haptic feedback on target changes, and a deterministic drop on release.
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
                grip.BindingContext is not TodoItem task)
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
                    StatusLabel.Text = $"Moved ‘{source.Title}’.";
                }
                else if (didDrag)
                {
                    StatusLabel.Text = "Drop on another task to change its position.";
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

    private void BeginNativeReorder(TodoItem task, Android.Views.View grip)
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
        StatusLabel.Text = $"Moving ‘{task.Title}’ · release over a task to place it.";
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
        var target = targetBorder?.BindingContext as TodoItem;
        if (target is not null && _draggedTask is not null && target.Id == _draggedTask.Id)
            target = null;

        if (target?.Id == _hoveredTask?.Id)
            return;

        ClearNativeDropTarget();
        if (target is null || targetBorder is null)
        {
            StatusLabel.Text = "Move over another task to place it.";
            return;
        }

        _hoveredTask = target;
        _hoveredBorder = targetBorder;
        _hoveredOriginalStrokeThickness = targetBorder.StrokeThickness;
        _hoveredOriginalScale = targetBorder.Scale;
        targetBorder.Stroke = new SolidColorBrush(Color.FromArgb(ThemeService.IsDarkMode() ? "#B7A9FF" : "#8875E8"));
        targetBorder.StrokeThickness = 2.5;
        targetBorder.Scale = 1.012;
        _reorderGripView?.PerformHapticFeedback(Android.Views.FeedbackConstants.KeyboardTap);
        StatusLabel.Text = $"Drop on ‘{target.Title}’.";
    }

    private Border? FindNativeRowForTask(TodoItem task)
    {
        foreach (var item in _nativeTaskRows.ToArray())
        {
            if (!item.Value.TryGetTarget(out var border))
            {
                _nativeTaskRows.Remove(item.Key);
                continue;
            }
            if (border.BindingContext is TodoItem bound && bound.Id == task.Id)
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
            if (border.BindingContext is not TodoItem task ||
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
            label.BindingContext is TodoItem task)
        {
            e.Handled = true;
            MainThread.BeginInvokeOnMainThread(async () => await ShowTaskOptionsAsync(task));
        }
    }
#endif

    private bool _themeToggleIsDark;

    private void ThemeToggle_Tapped(object? sender, TappedEventArgs e)
    {
        if (_initializing) return;

        _themeToggleIsDark = !_themeToggleIsDark;
        var preference = _themeToggleIsDark ? ThemePreference.Dark : ThemePreference.Light;
        ThemeService.SetPreference(preference);
        UpdateThemeToggleVisual(_themeToggleIsDark);
#if ANDROID
        MainActivity.RefreshNavigationBarTheme();
#endif
        _viewModel.RefreshTaskAppearances();
        StatusLabel.Text = _themeToggleIsDark ? "Dark theme selected." : "Light theme selected.";
        ScheduleAutoApply();
    }

    private void UpdateThemeToggleVisual(bool isDark)
    {
        _themeToggleIsDark = isDark;
        var surface = Color.FromArgb(isDark ? "#252736" : "#F0F2F8");
        var highlight = Color.FromArgb(isDark ? "#383A4D" : "#D4DBE7");
        var text = Color.FromArgb(isDark ? "#E3E5F2" : "#4A5064");

        ThemeToggleBorder.BackgroundColor = surface;
        ThemeToggleBorder.Stroke = new SolidColorBrush(highlight);
        ThemeToggleBorder.Shadow = CreateNeumorphicShadow(0.72f);
        ThemeToggleLabel.Text = isDark ? "DARK" : "LIGHT";
        ThemeToggleLabel.TextColor = text;
        MoonIconContainer.BackgroundColor = surface;
        MoonIconContainer.Stroke = new SolidColorBrush(highlight);
        MoonIconContainer.Shadow = CreateNeumorphicShadow(0.4f);
        SunIconContainer.BackgroundColor = surface;
        SunIconContainer.Stroke = new SolidColorBrush(highlight);
        SunIconContainer.Shadow = CreateNeumorphicShadow(0.4f);
        MoonIconContainer.IsVisible = isDark;
        SunIconContainer.IsVisible = !isDark;
        UpdateAutoApplyVisual(AutoApplySwitch.IsToggled);
    }

    private void AutoApplyTrack_Tapped(object? sender, TappedEventArgs e)
    {
        AutoApplySwitch.IsToggled = !AutoApplySwitch.IsToggled;
        UpdateAutoApplyVisual(AutoApplySwitch.IsToggled);
    }

    private void UpdateAutoApplyVisual(bool isEnabled)
    {
        if (AutoApplyTrack is null || AutoApplyThumb is null)
            return;

        var isDark = ThemeService.IsDarkMode();
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

    private async void ClockLayoutSelector_Tapped(object? sender, TappedEventArgs e)
    {
        if (_initializing) return;
        await ShowWallpaperLayoutSettingsAsync();
    }

    private async Task ShowWallpaperLayoutSettingsAsync()
    {
        var candidateLayoutIndex = _selectedLayoutIndex;
        var candidateOffset = _verticalOffset;
        var candidateClearance = _clockClearance;
        var completion = BeginPopup(
            "Wallpaper placement",
            "Auto adapts the layout to your screen ratio. Adjust the offset to line it up with your lock-screen clock.",
            "⌖");

        ModalBody.Children.Add(new Label
        {
            Text = "PLACEMENT PROFILE",
            FontSize = 10,
            CharacterSpacing = 1.0,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#8A76EB"),
            Margin = new Thickness(1, 2, 0, 0)
        });

        var profileGrid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        profileGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        profileGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var row = 0; row < 3; row++)
            profileGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var profileCards = new List<(Border Card, int Index)>();
        for (var i = 0; i < WallpaperLayoutChoices.Length; i++)
        {
            var option = WallpaperLayoutChoices[i];
            var layoutIndex = (int)option.Layout;
            var initiallySelected = candidateLayoutIndex == layoutIndex;
            var card = new Border
            {
                Padding = new Thickness(10, 9),
                MinimumHeightRequest = 44,
                BackgroundColor = initiallySelected ? PopupColor("#E2E7F1", "#2A2C3D") : PopupColor("#F0F2F8", "#252736"),
                Stroke = new SolidColorBrush(initiallySelected ? Color.FromArgb("#8A76EB") : PopupColor("#D1D9E5", "#383A4D")),
                StrokeThickness = initiallySelected ? 1.5 : 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(13) },
                Content = new Label
                {
                    Text = option.Name,
                    FontSize = 11,
                    FontAttributes = initiallySelected ? FontAttributes.Bold : FontAttributes.None,
                    TextColor = PopupColor("#3F4559", "#E5E7F5"),
                    VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center,
                    HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center,
                    LineBreakMode = LineBreakMode.WordWrap
                }
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                candidateLayoutIndex = layoutIndex;
                foreach (var item in profileCards)
                {
                    var selected = item.Index == candidateLayoutIndex;
                    item.Card.BackgroundColor = selected ? PopupColor("#E2E7F1", "#2A2C3D") : PopupColor("#F0F2F8", "#252736");
                    item.Card.Stroke = new SolidColorBrush(selected ? Color.FromArgb("#8A76EB") : PopupColor("#D1D9E5", "#383A4D"));
                    item.Card.StrokeThickness = selected ? 1.5 : 1;
                    item.Card.Shadow = CreateNeumorphicShadow(selected ? 0.26f : 0.38f);
                    if (item.Card.Content is Label label) label.FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None;
                }
            };
            card.GestureRecognizers.Add(tap);
            profileCards.Add((card, layoutIndex));
            profileGrid.Add(card, i % 2, i / 2);
        }
        ModalBody.Children.Add(profileGrid);

        var offsetTitle = new Label
        {
            Text = "Move content up or down",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = PopupColor("#3F4559", "#E5E7F5"),
            Margin = new Thickness(1, 6, 1, 0)
        };
        var offsetValue = new Label
        {
            Text = FormatOffsetLabel(candidateOffset),
            FontSize = 11,
            TextColor = Color.FromArgb("#8A76EB"),
            HorizontalTextAlignment = Microsoft.Maui.TextAlignment.End
        };
        var offsetLabels = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        offsetLabels.Add(offsetTitle, 0, 0);
        offsetLabels.Add(offsetValue, 1, 0);
        ModalBody.Children.Add(offsetLabels);
        var offsetSlider = new Slider
        {
            Minimum = 0, Maximum = 40, Value = candidateOffset * 100d + 20d,
            MinimumTrackColor = Color.FromArgb("#8875E8"),
            MaximumTrackColor = PopupColor("#DAD3E9", "#494257"),
            ThumbColor = Color.FromArgb("#8A76EB"),
            HorizontalOptions = LayoutOptions.Fill
        };
        var offsetUpLabel = new Label
        {
            Text = $"↑ Up {Math.Max(0, (int)Math.Round(-candidateOffset * 100f))}%",
            FontSize = 9,
            TextColor = PopupColor("#7E8599", "#9FA5BC")
        };
        var offsetDownLabel = new Label
        {
            Text = $"Down {Math.Max(0, (int)Math.Round(candidateOffset * 100f))}% ↓",
            FontSize = 9,
            TextColor = PopupColor("#7E8599", "#9FA5BC"),
            HorizontalTextAlignment = Microsoft.Maui.TextAlignment.End
        };
        offsetSlider.ValueChanged += (_, args) =>
        {
            candidateOffset = (float)((args.NewValue - 20d) / 100d);
            offsetValue.Text = FormatOffsetLabel(candidateOffset);
            offsetUpLabel.Text = $"↑ Up {Math.Max(0, (int)Math.Round(-candidateOffset * 100f))}%";
            offsetDownLabel.Text = $"Down {Math.Max(0, (int)Math.Round(candidateOffset * 100f))}% ↓";
        };
        ModalBody.Children.Add(offsetSlider);

        var offsetRange = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        offsetRange.Add(offsetUpLabel, 0, 0);
        offsetRange.Add(offsetDownLabel, 1, 0);
        ModalBody.Children.Add(offsetRange);

        var clearanceTitle = new Label
        {
            Text = "Empty space around clock",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = PopupColor("#3F4559", "#E5E7F5"),
            Margin = new Thickness(1, 4, 1, 0)
        };
        var clearanceValue = new Label
        {
            Text = $"{candidateClearance * 100:0}% of screen height",
            FontSize = 11,
            TextColor = Color.FromArgb("#8A76EB"),
            HorizontalTextAlignment = Microsoft.Maui.TextAlignment.End
        };
        var clearanceLabels = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        clearanceLabels.Add(clearanceTitle, 0, 0);
        clearanceLabels.Add(clearanceValue, 1, 0);
        ModalBody.Children.Add(clearanceLabels);
        var clearanceSlider = new Slider
        {
            Minimum = 8, Maximum = 30, Value = candidateClearance * 100d,
            MinimumTrackColor = Color.FromArgb("#8875E8"),
            MaximumTrackColor = PopupColor("#DAD3E9", "#494257"),
            ThumbColor = Color.FromArgb("#8A76EB"),
            HorizontalOptions = LayoutOptions.Fill
        };
        clearanceSlider.ValueChanged += (_, args) =>
        {
            candidateClearance = (float)(args.NewValue / 100d);
            clearanceValue.Text = $"{candidateClearance * 100:0}% of screen height";
        };
        ModalBody.Children.Add(clearanceSlider);
        ModalBody.Children.Add(new Label
        {
            Text = "Clock spacing applies mainly to Split around center clock. Offset moves the wallpaper content and its clear area together.",
            FontSize = 10,
            LineBreakMode = LineBreakMode.WordWrap,
            TextColor = PopupColor("#7E8599", "#9FA5BC")
        });

        var actions = new Grid { ColumnSpacing = 9 };
        actions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        actions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        actions.Add(CreatePopupAction("Cancel", null, primary: false, destructive: false), 0, 0);
        actions.Add(CreatePopupAction("Save", "__SAVE_LAYOUT__", primary: true, destructive: false, customAction: () => CompletePopup("SAVE_LAYOUT_SETTINGS")), 1, 0);
        ModalButtons.Children.Add(actions);

        var result = await completion.Task;
        if (result != "SAVE_LAYOUT_SETTINGS") return;

        _selectedLayoutIndex = candidateLayoutIndex;
        _verticalOffset = Math.Clamp(candidateOffset, -0.20f, 0.20f);
        _clockClearance = Math.Clamp(candidateClearance, 0.08f, 0.30f);
        Preferences.Default.Set(LayoutPreferenceKey, _selectedLayoutIndex);
        Preferences.Default.Set(VerticalOffsetPreferenceKey, _verticalOffset);
        Preferences.Default.Set(ClockClearancePreferenceKey, _clockClearance);
        UpdateClockLayoutSummary();
        StatusLabel.Text = "Wallpaper placement saved.";
        ScheduleAutoApply();
    }

    private static string FormatOffsetLabel(float offset)
    {
        var percent = (int)Math.Round(offset * 100f);
        return percent switch
        {
            < 0 => $"{Math.Abs(percent)}% up",
            > 0 => $"{percent}% down",
            _ => "No offset"
        };
    }

    private void UpdateClockLayoutSummary()
    {
        var name = WallpaperLayoutNames[Math.Clamp(_selectedLayoutIndex, 0, WallpaperLayoutNames.Length - 1)];
        var offset = (int)Math.Round(_verticalOffset * 100f);
        ClockLayoutValue.Text = offset == 0 ? name : $"{name} · {offset:+0;-0}%";
    }

    private void AutoApplySwitch_Toggled(object? sender, ToggledEventArgs e)
    {
        UpdateAutoApplyVisual(e.Value);
        if (_initializing) return;
        Preferences.Default.Set(AutoApplyPreferenceKey, e.Value);
        if (e.Value)
        {
            ScheduleAutoApply();
            if (_viewModel.Tasks.Count == 0)
                StatusLabel.Text = "Auto Apply is enabled; the clean wallpaper will be refreshed in the background.";
        }
        else
        {
            _autoApplyPending = false;
            Preferences.Default.Set(AutoApplyPendingPreferenceKey, false);
            StatusLabel.Text = "Automatic wallpaper updates paused.";
        }
    }

    private void OnTasksChanged(object? sender, EventArgs e)
    {
#if ANDROID
        ReminderScheduler.SyncTasks(_viewModel.Tasks);
#endif
        ScheduleAutoApply();
    }

    private void ScheduleAutoApply()
    {
        if (_initializing)
            return;

        if (!AutoApplySwitch.IsToggled)
        {
            _autoApplyPending = false;
            Preferences.Default.Set(AutoApplyPendingPreferenceKey, false);
            return;
        }

        // An empty list is also a valid wallpaper state. In particular, deleting the last
        // task must queue a clean background image instead of leaving the previous card behind.

        // Important: Auto Update only queues a fresh wallpaper while Locksy is visible.
        // Applying WallpaperManager calls here can make certain OEM ROMs tear down and
        // recreate the foreground Activity for every single task edit, showing black.
        // The actual apply is deferred to MainActivity.OnStop, after the UI is hidden.
        _autoApplyPending = true;
        Preferences.Default.Set(AutoApplyPendingPreferenceKey, true);

        // During AddTaskCommand.Execute, the collection change fires synchronously.
        // Keep the queued state, but avoid changing the status label while the Add Task
        // handler is restoring focus to the entry. The status can be refreshed afterward.
        if (!_isApplying && !_isAddingTask)
            StatusLabel.Text = AutoApplyQueuedStatus;
    }

    private async void ApplyButton_Clicked(object? sender, EventArgs e)
    {
        if (_isApplying)
            return;

        // Manual Apply is an explicit user request, so run immediately and show an actual
        // spinner. Clear the old queued snapshot first; edits made during the operation
        // will set it again through OnTasksChanged.
        _autoApplyPending = false;
        Preferences.Default.Set(AutoApplyPendingPreferenceKey, false);
        await ApplyWallpaperAsync(showAlert: true);
    }

    private async Task<bool> ApplyWallpaperAsync(bool showAlert, CancellationToken cancellationToken = default)
    {
        if (_isApplying)
            return false;

        var todos = _viewModel.Tasks.Take(MaxWallpaperTasks).ToList();
        _isApplying = true;
        SetApplyBusyState(true);
        StatusLabel.Text = "Preparing lock-screen wallpaper…";
        var succeeded = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var layoutSettings = new WallpaperLayoutSettings
            {
                Layout = (WallpaperLayout)Math.Clamp(_selectedLayoutIndex, 0, WallpaperLayoutNames.Length - 1),
                VerticalOffset = _verticalOffset,
                ClockClearance = _clockClearance
            };
            // Save the start time before entering Android's wallpaper API. Some OEMs
            // recreate the Activity immediately afterwards; App.CreateWindow suppresses
            // the animated splash during this short system recreation.
            Preferences.Default.Set(LastWallpaperOperationPreferenceKey, DateTime.UtcNow.Ticks);

            var previewBytes = await _wallpaperService.ApplyLockScreenAsync(
                todos,
                ThemeService.IsDarkMode(),
                layoutSettings,
                cancellationToken);

            succeeded = true;
            StatusLabel.Text = _autoApplyPending
                ? AutoApplyQueuedStatus
                : $"Lock screen updated at {DateTime.Now:HH:mm}.";
            if (showAlert)
                await ShowInfoPopupAsync(
                    "Wallpaper updated",
                    "Your lock-screen wallpaper is ready. Here is the exact image applied to your lock screen.",
                    previewBytes: previewBytes);
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "Wallpaper update cancelled.";
            return false;
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Could not update wallpaper.";
            if (showAlert)
                await ShowInfoPopupAsync("Couldn’t update wallpaper", ex.Message, isError: true);
            return false;
        }
        finally
        {
            _isApplying = false;
            SetApplyBusyState(false);
            if (_autoApplyPending)
                StatusLabel.Text = AutoApplyQueuedStatus;
            else if (!succeeded && !showAlert && AutoApplySwitch.IsToggled)
            {
                // Keep a failed background update queued for the next real background event.
                _autoApplyPending = true;
                Preferences.Default.Set(AutoApplyPendingPreferenceKey, true);
                StatusLabel.Text = "Auto update queued for background.";
            }
        }
    }

    private void SetApplyBusyState(bool isBusy)
    {
        ApplyButton.IsEnabled = !isBusy;
        ApplyButton.Text = isBusy ? string.Empty : "Apply wallpaper";
        ApplyBusyContent.IsVisible = isBusy;
        ApplySpinner.IsRunning = isBusy;
    }

    // All in-app dialogs use Locksy's own visual style instead of native MAUI sheets.
    private TaskCompletionSource<string?> BeginPopup(string title, string message, string icon = "✦")
    {
        if (_popupCompletionSource is not null)
            CompletePopup(null);

        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _popupCompletionSource = completion;
        _compactEditorPopup = false;
        ModalCard.WidthRequest = -1;
        ModalCard.MaximumWidthRequest = 380;
        ModalCard.MaximumHeightRequest = Math.Max(280d, Height - 56d);
        ModalBodyScroll.MaximumHeightRequest = Math.Clamp(Height - 470d, 140d, 300d);
        ModalTitle.Text = title;
        ModalMessage.Text = message;
        ModalMessage.IsVisible = !string.IsNullOrWhiteSpace(message);
        var useOptionsIcon = string.Equals(icon, "options", StringComparison.OrdinalIgnoreCase);
        ModalIconImage.IsVisible = useOptionsIcon;
        ModalIconLabel.IsVisible = !useOptionsIcon;
        if (useOptionsIcon)
            ModalIconImage.Source = ImageSource.FromFile("locksy_options_icon.svg");
        else
            ModalIconLabel.Text = icon;
        ModalIconLabel.FontSize = icon is "◷" or "⏰" ? 30 : 22;
        ModalBody.Children.Clear();
        ModalButtons.Children.Clear();
        _ = ModalBodyScroll.ScrollToAsync(0, 0, false);

        ModalCard.BackgroundColor = PopupColor("#F0F2F8", "#252736");
        ModalCard.Stroke = new SolidColorBrush(PopupColor("#CDD5E2", "#383A4D"));
        ModalCard.Shadow = null;
        // Disable hit testing on the entire page beneath the modal. This is separate
        // from the backdrop's hit target so even platform-native controls cannot leak taps.
        PageContent.InputTransparent = true;
        ModalOverlay.Opacity = 0;
        ModalCard.Scale = 0.94;
        ModalOverlay.IsVisible = true;
        _ = ModalOverlay.FadeTo(1, 160, Easing.CubicOut);
        _ = ModalCard.ScaleTo(1, 180, Easing.CubicOut);
        return completion;
    }

    private Color PopupColor(string light, string dark) =>
        Color.FromArgb(ThemeService.IsDarkMode() ? dark : light);

    // Consistent raised-surface treatment used by dynamically-created popup controls.
    // A crisp highlight stroke supplies the second edge that a single MAUI Shadow cannot provide.
    private Shadow CreateNeumorphicShadow(float strength = 1f)
    {
        var dark = ThemeService.IsDarkMode();
        return new Shadow
        {
            Brush = new SolidColorBrush(Color.FromArgb(dark ? "#11121A" : "#B5BDCC")),
            Offset = new Microsoft.Maui.Graphics.Point(1, 2),
            Radius = 9,
            Opacity = Math.Clamp((dark ? 0.30f : 0.28f) * strength, 0.05f, 0.38f)
        };
    }

    private Task<string?> ShowChoicePopupAsync(
        string title,
        string message,
        IReadOnlyList<string> choices,
        string cancelLabel = "Cancel",
        IReadOnlyList<string>? icons = null,
        string popupIcon = "✦")
    {
        var completion = BeginPopup(title, message, popupIcon);
        for (var i = 0; i < choices.Count; i++)
        {
            var choice = choices[i];
            var icon = icons is not null && i < icons.Count ? icons[i] : ChoiceIcon(choice, i);
            AddPopupChoice(choice, choice, icon, IsDestructive(choice));
        }
        ModalButtons.Children.Add(CreatePopupAction(cancelLabel, null, primary: false, destructive: false));
        return completion.Task;
    }

    private Task<string?> ShowPromptPopupAsync(
        string title,
        string message,
        string placeholder,
        int maxLength,
        string? initialValue = null)
    {
        var completion = BeginPopup(title, message, "✎");
        // Keep task editing more compact than the generic dialogs, but provide a real
        // multi-line editor with its own scrolling for long task descriptions.
        _compactEditorPopup = true;
        var availablePageWidth = Width > 0 ? Width : 360d;
        var promptWidth = Math.Max(220d, Math.Min(300d, availablePageWidth - 40d));
        ModalCard.WidthRequest = promptWidth;
        ModalCard.MaximumWidthRequest = promptWidth;
        ModalCard.MaximumHeightRequest = Math.Min(460d, Math.Max(280d, Height - 56d));
        ModalBodyScroll.MaximumHeightRequest = Math.Clamp(Height - 470d, 130d, 190d);

        var input = new Editor
        {
            Text = initialValue ?? string.Empty,
            Placeholder = placeholder,
            MaxLength = maxLength,
            AutoSize = EditorAutoSizeOption.Disabled,
            BackgroundColor = Colors.Transparent,
            TextColor = PopupColor("#303545", "#F1F2FA"),
            PlaceholderColor = PopupColor("#7E8599", "#9FA5BC"),
            FontSize = 14,
            HeightRequest = 152,
            VerticalOptions = LayoutOptions.Fill,
            Margin = new Thickness(0)
        };
        input.HandlerChanged += RoundedControl_HandlerChanged;
        var inputBorder = new Border
        {
            Padding = new Thickness(12, 8),
            HeightRequest = 170,
            BackgroundColor = PopupColor("#E1E6F0", "#1A1B26"),
            Stroke = new SolidColorBrush(PopupColor("#D1D7E3", "#292B3A")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(15) },
            Shadow = CreateNeumorphicShadow(0.24f),
            Content = input
        };
        ModalBody.Children.Add(inputBorder);
        ModalBody.Children.Add(new Label
        {
            Text = $"{(initialValue ?? string.Empty).Length}/{maxLength} characters · Scroll to read longer text",
            FontSize = 10,
            TextColor = PopupColor("#7E8599", "#9FA5BC")
        });
        input.TextChanged += (_, args) =>
        {
            if (ModalBody.Children.Count > 1 && ModalBody.Children[1] is Label counter)
                counter.Text = $"{(args.NewTextValue ?? string.Empty).Length}/{maxLength} characters · Scroll to read longer text";
        };

        var actions = new Grid { ColumnSpacing = 9 };
        actions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        actions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        actions.Add(CreatePopupAction("Cancel", null, primary: false, destructive: false), 0, 0);
        actions.Add(CreatePopupAction("Save changes", "__SAVE__", primary: true, destructive: false, customAction: () =>
        {
            CompletePopup(input.Text ?? string.Empty);
        }), 1, 0);
        ModalButtons.Children.Add(actions);
        return completion.Task;
    }

    private async Task<bool> ShowConfirmationPopupAsync(string title, string message, string confirmLabel, bool destructive = false)
    {
        var completion = BeginPopup(title, message, destructive ? "⌫" : "?");
        var actions = new Grid { ColumnSpacing = 9 };
        actions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        actions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        actions.Add(CreatePopupAction("Cancel", null, primary: false, destructive: false), 0, 0);
        actions.Add(CreatePopupAction(confirmLabel, confirmLabel, primary: !destructive, destructive: destructive), 1, 0);
        ModalButtons.Children.Add(actions);
        return await completion.Task == confirmLabel;
    }

    private async Task ShowInfoPopupAsync(string title, string message, bool isError = false, byte[]? previewBytes = null)
    {
        var completion = BeginPopup(title, message, isError ? "!" : "✓");
        if (!isError && previewBytes is { Length: > 0 })
        {
            var previewImage = new Image
            {
                Source = ImageSource.FromStream(() => new MemoryStream(previewBytes)),
                Aspect = Aspect.AspectFit,
                HeightRequest = 190,
                HorizontalOptions = LayoutOptions.Fill,
                BackgroundColor = PopupColor("#E1E6F0", "#1A1B26")
            };
            ModalBody.Children.Add(new Border
            {
                Padding = new Thickness(5),
                HeightRequest = 200,
                BackgroundColor = PopupColor("#E1E6F0", "#1A1B26"),
                Stroke = new SolidColorBrush(PopupColor("#D1D9E5", "#383A4D")),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
                Shadow = CreateNeumorphicShadow(0.34f),
                Content = previewImage
            });
        }
        ModalButtons.Children.Add(CreatePopupAction("Got it", "OK", primary: true, destructive: false));
        await completion.Task;
    }

    private Border CreatePopupAction(
        string text,
        string? result,
        bool primary,
        bool destructive,
        Action? customAction = null)
    {
        var background = destructive
            ? PopupColor("#FCE9ED", "#3A222E")
            : primary
                ? Color.FromArgb("#8875E8")
                : PopupColor("#F0F2F8", "#252736");
        var textColor = destructive
            ? PopupColor("#A83E59", "#FFC1D0")
            : primary
                ? Colors.White
                : PopupColor("#625A78", "#D8D0ED");
        var border = new Border
        {
            Padding = new Thickness(14, 12),
            MinimumHeightRequest = 46,
            BackgroundColor = background,
            Stroke = new SolidColorBrush(destructive ? PopupColor("#F0CDD5", "#573040") : primary ? Color.FromArgb("#8875E8") : PopupColor("#D1D9E5", "#383A4D")),
            StrokeThickness = primary ? 0 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Shadow = CreateNeumorphicShadow(primary ? 0.55f : 0.34f),
            HorizontalOptions = LayoutOptions.Fill
        };
        border.Content = new Label
        {
            Text = text,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = textColor,
            HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center,
            VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (customAction is not null) customAction();
            else CompletePopup(result);
        };
        border.GestureRecognizers.Add(tap);
        return border;
    }

    private void AddPopupChoice(string label, string result, string icon, bool destructive)
    {
        var row = new Grid { ColumnSpacing = 11, Padding = new Thickness(11, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(34)));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(14)));

        View iconContent;
        var vectorIconSource = TaskActionIconSource(icon);
        if (vectorIconSource is not null)
        {
            iconContent = new Image
            {
                Source = ImageSource.FromFile(vectorIconSource),
                WidthRequest = 21,
                HeightRequest = 21,
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
        }
        else
        {
            iconContent = new Label
            {
                Text = icon,
                FontSize = 16,
                TextColor = destructive ? PopupColor("#A83E59", "#FFC1D0") : Color.FromArgb("#8A76EB"),
                HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center,
                VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center
            };
        }

        var iconBack = new Border
        {
            WidthRequest = 34,
            HeightRequest = 34,
            BackgroundColor = destructive ? PopupColor("#FCE9ED", "#3A222E") : PopupColor("#E2E7F1", "#2A2C3D"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(11) },
            Stroke = new SolidColorBrush(PopupColor("#D1D9E5", "#383A4D")),
            StrokeThickness = 1,
            Shadow = CreateNeumorphicShadow(0.30f),
            VerticalOptions = LayoutOptions.Center,
            Content = iconContent
        };
        row.Add(iconBack, 0, 0);
        row.Add(new Label
        {
            Text = label,
            FontSize = 13,
            FontAttributes = FontAttributes.None,
            TextColor = destructive ? PopupColor("#A83E59", "#FFC1D0") : PopupColor("#3F4559", "#E5E7F5"),
            VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap
        }, 1, 0);
        row.Add(new Label
        {
            Text = "›",
            FontSize = 22,
            TextColor = PopupColor("#A29AB7", "#9189A8"),
            VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center,
            HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center
        }, 2, 0);

        var border = new Border
        {
            BackgroundColor = PopupColor("#F0F2F8", "#252736"),
            Stroke = new SolidColorBrush(PopupColor("#D1D9E5", "#383A4D")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(15) },
            Shadow = CreateNeumorphicShadow(0.38f),
            Content = row
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => CompletePopup(result);
        border.GestureRecognizers.Add(tap);
        ModalBody.Children.Add(border);
    }

    private static bool IsDestructive(string value) =>
        value.Contains("delete", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("remove", StringComparison.OrdinalIgnoreCase);

    private static string ChoiceIcon(string value, int index)
    {
        if (value.Contains("edit", StringComparison.OrdinalIgnoreCase)) return "edit";
        if (value.Contains("color", StringComparison.OrdinalIgnoreCase) || value.Contains("palette", StringComparison.OrdinalIgnoreCase)) return "palette";
        if (value.Contains("delete", StringComparison.OrdinalIgnoreCase) || value.Contains("remove", StringComparison.OrdinalIgnoreCase)) return "trash";
        return index switch { 0 => "✦", 1 => "◈", 2 => "○", _ => "•" };
    }

    private static string? TaskActionIconSource(string icon) => icon switch
    {
        "edit" => "locksy_edit_icon.svg",
        "palette" => "locksy_palette_icon.svg",
        "trash" => "locksy_trash_icon.svg",
        _ => null
    };

    private void ModalBackdrop_Tapped(object? sender, TappedEventArgs e)
    {
        // A tap on the dimmed area cancels/dismisses the current modal.
        // Taps inside ModalCard are handled by its child controls instead.
        if (_popupCompletionSource is not null)
            CompletePopup(null);
    }

    private void CompletePopup(string? result)
    {
        ModalOverlay.IsVisible = false;
        ModalOverlay.Opacity = 0;
        PageContent.InputTransparent = false;
        ModalCard.Scale = 0.96;
        ModalBody.Children.Clear();
        ModalButtons.Children.Clear();
        var completion = _popupCompletionSource;
        _popupCompletionSource = null;
        completion?.TrySetResult(result);
    }

    protected override void OnDisappearing()
    {
        // Queued auto-updates are persisted and run only by MainActivity after the
        // Activity is hidden. Do not cancel an already running wallpaper operation.
        base.OnDisappearing();
    }
}
