using Locksy.Commands;
using Locksy.Models;
using Locksy.Services;
using Locksy.Services.Abstractions;
using Microsoft.Maui.Controls;
using Locksy.ViewModels.Abstractions;

namespace Locksy.ViewModels;

public sealed class WallpaperNotificationEventArgs : EventArgs
{
    public string Title { get; }
    public string Message { get; }
    public bool IsError { get; }
    public byte[]? PreviewBytes { get; }

    public WallpaperNotificationEventArgs(string title, string message, bool isError = false, byte[]? previewBytes = null)
    {
        Title = title;
        Message = message;
        IsError = isError;
        PreviewBytes = previewBytes;
    }
}

/// <summary>
/// Owns wallpaper preferences, queued auto-apply state, and the apply workflow. It has no
/// knowledge of labels, buttons, popups, or other view elements.
/// </summary>
public sealed class WallpaperViewModel : ObservableObject, IDisposable, IWallpaperUpdateController
{
    private const int MaxWallpaperTasks = 8;
    private const string AutoApplyPreferenceKey = "locksy-auto-apply";
    private const string LayoutPreferenceKey = "locksy-wallpaper-layout-v2";
    private const string VerticalOffsetPreferenceKey = "locksy-wallpaper-vertical-offset-v2";
    private const string ClockClearancePreferenceKey = "locksy-wallpaper-clock-clearance-v2";
    private const string LastWallpaperOperationPreferenceKey = "locksy-last-wallpaper-operation-utc-ticks";
    private const string AutoApplyPendingPreferenceKey = "locksy-auto-apply-pending";
    private const string AutoApplyQueuedStatus = "Auto update queued for background.";

    private static readonly string[] WallpaperLayoutNames =
    {
        "Tasks below top clock (legacy)", "Split around center clock", "Tasks top",
        "Centered task list", "Screen-aware (auto)", "Tasks bottom"
    };

    private readonly ITaskListSource _taskSource;
    private readonly IWallpaperService _wallpaperService;
    private readonly IAppPreferencesService _preferences;
    private readonly IThemeSettingsService _themeSettingsService;
    private readonly ILocalizationService _localizationService;
    private bool _isAutoApplyEnabled;
    private bool _isAutoApplyPending;
    private bool _isApplying;
    private bool _isAddingTask;
    private int _selectedLayoutIndex;
    private float _verticalOffset;
    private float _clockClearance = 0.18f;
    private string _statusMessage = string.Empty;
    private string _layoutSummary = string.Empty;
    private bool _disposed;

    public bool IsAutoApplyEnabled
    {
        get => _isAutoApplyEnabled;
        set
        {
            if (!SetProperty(ref _isAutoApplyEnabled, value)) return;
            _preferences.Set(AutoApplyPreferenceKey, value);
            if (value)
            {
                QueueAutoApply();
                if (_taskSource.GetTaskModels().Count == 0)
                    StatusMessage = _localizationService.Translate("Auto Apply is enabled; the clean wallpaper will be refreshed in the background.");
            }
            else
            {
                IsAutoApplyPending = false;
                _preferences.Set(AutoApplyPendingPreferenceKey, false);
                StatusMessage = _localizationService.Translate("Automatic wallpaper updates paused.");
            }
        }
    }

    public bool IsAutoApplyPending
    {
        get => _isAutoApplyPending;
        private set
        {
            if (!SetProperty(ref _isAutoApplyPending, value)) return;
            _preferences.Set(AutoApplyPendingPreferenceKey, value);
        }
    }

    public bool IsApplying
    {
        get => _isApplying;
        private set
        {
            if (!SetProperty(ref _isApplying, value)) return;
            OnPropertyChanged(nameof(ApplyButtonText));
            OnPropertyChanged(nameof(CanApply));
            ApplyWallpaperCommand.NotifyCanExecuteChanged();
        }
    }

    public bool CanApply => !IsApplying;
    public string ApplyButtonText => _localizationService.Translate(IsApplying ? "Applying…" : "Apply wallpaper");

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value ?? string.Empty);
    }

    public string LayoutSummary
    {
        get => _layoutSummary;
        private set => SetProperty(ref _layoutSummary, value);
    }

    public int SelectedLayoutIndex => _selectedLayoutIndex;
    public float VerticalOffset => _verticalOffset;
    public float ClockClearance => _clockClearance;
    public AsyncCommand ApplyWallpaperCommand { get; }

    public event EventHandler<WallpaperNotificationEventArgs>? NotificationRequested;

    public WallpaperViewModel(
        ITaskListSource taskSource,
        IWallpaperService wallpaperService,
        IAppPreferencesService preferences,
        IThemeSettingsService themeSettingsService,
        ILocalizationService localizationService)
    {
        _taskSource = taskSource ?? throw new ArgumentNullException(nameof(taskSource));
        _wallpaperService = wallpaperService ?? throw new ArgumentNullException(nameof(wallpaperService));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _themeSettingsService = themeSettingsService ?? throw new ArgumentNullException(nameof(themeSettingsService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));

        var savedLayout = _preferences.GetInt32(LayoutPreferenceKey, (int)WallpaperLayout.ScreenAware);
        // Migrate the old visible "Tasks bottom" option to the actual bottom anchor.
        if (savedLayout == (int)WallpaperLayout.BelowTopClock)
        {
            savedLayout = (int)WallpaperLayout.TasksBottom;
            _preferences.Set(LayoutPreferenceKey, savedLayout);
        }

        _selectedLayoutIndex = Math.Clamp(savedLayout, 0, (int)WallpaperLayout.TasksBottom);
        _verticalOffset = Math.Clamp(_preferences.GetSingle(VerticalOffsetPreferenceKey, 0f), -0.20f, 0.20f);
        _clockClearance = Math.Clamp(_preferences.GetSingle(ClockClearancePreferenceKey, 0.18f), 0.08f, 0.30f);
        _isAutoApplyEnabled = _preferences.GetBoolean(AutoApplyPreferenceKey, true);
        _isAutoApplyPending = _isAutoApplyEnabled && _preferences.GetBoolean(AutoApplyPendingPreferenceKey, false);
        _statusMessage = _isAutoApplyPending ? _localizationService.Translate(AutoApplyQueuedStatus) :
            _localizationService.Translate("Ready · Drag ⠿ to reorder");
        RefreshLayoutSummary();

        ApplyWallpaperCommand = new AsyncCommand(
            cancellationToken => ApplyWallpaperAsync(showAlert: true, cancellationToken),
            () => !IsApplying);
        _taskSource.TasksChanged += OnTasksChanged;
    }

    public void SetAddingTask(bool value) => _isAddingTask = value;

    public void SetStatus(string message) => StatusMessage = message;

    public void RefreshLocalizedState()
    {
        RefreshLayoutSummary();
        if (IsAutoApplyPending)
            StatusMessage = _localizationService.Translate(AutoApplyQueuedStatus);
    }

    public void RefreshLayoutSummary()
    {
        var name = _localizationService.Translate(WallpaperLayoutNames[Math.Clamp(_selectedLayoutIndex, 0, WallpaperLayoutNames.Length - 1)]);
        var offset = (int)Math.Round(_verticalOffset * 100f);
        LayoutSummary = _localizationService.IsPersian
            ? (offset == 0 ? name : _localizationService.LocalizeDigits($"{name} · {offset:+0;-0}٪"))
            : (offset == 0 ? name : $"{name} · {offset:+0;-0}%");
    }

    public void SaveLayoutSettings(int layoutIndex, float verticalOffset, float clockClearance)
    {
        _selectedLayoutIndex = Math.Clamp(layoutIndex, 0, WallpaperLayoutNames.Length - 1);
        _verticalOffset = Math.Clamp(verticalOffset, -0.20f, 0.20f);
        _clockClearance = Math.Clamp(clockClearance, 0.08f, 0.30f);
        _preferences.Set(LayoutPreferenceKey, _selectedLayoutIndex);
        _preferences.Set(VerticalOffsetPreferenceKey, _verticalOffset);
        _preferences.Set(ClockClearancePreferenceKey, _clockClearance);
        OnPropertyChanged(nameof(SelectedLayoutIndex));
        OnPropertyChanged(nameof(VerticalOffset));
        OnPropertyChanged(nameof(ClockClearance));
        RefreshLayoutSummary();
        StatusMessage = _localizationService.Translate("Wallpaper placement saved.");
        QueueAutoApply();
    }

    public WallpaperLayoutSettings CreateLayoutSettings(int? layoutIndex = null, float? verticalOffset = null, float? clockClearance = null) => new()
    {
        Layout = (WallpaperLayout)Math.Clamp(layoutIndex ?? _selectedLayoutIndex, 0, WallpaperLayoutNames.Length - 1),
        VerticalOffset = Math.Clamp(verticalOffset ?? _verticalOffset, -0.20f, 0.20f),
        ClockClearance = Math.Clamp(clockClearance ?? _clockClearance, 0.08f, 0.30f)
    };

    public void QueueAutoApply()
    {
        if (!IsAutoApplyEnabled)
        {
            IsAutoApplyPending = false;
            return;
        }

        // WallpaperManager is deliberately deferred until the activity leaves the foreground.
        IsAutoApplyPending = true;
        if (!IsApplying && !_isAddingTask)
            StatusMessage = _localizationService.Translate(AutoApplyQueuedStatus);
    }

    public void OnHostResumed()
    {
        if (IsAutoApplyPending && IsAutoApplyEnabled)
            StatusMessage = _localizationService.Translate(AutoApplyQueuedStatus);
    }

    public async Task ApplyQueuedAutoWallpaperWhenBackgroundedAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAutoApplyPending || !IsAutoApplyEnabled || IsApplying) return;
        IsAutoApplyPending = false;
        var succeeded = await ApplyWallpaperAsync(showAlert: false, cancellationToken: cancellationToken);
        if (!succeeded && IsAutoApplyEnabled)
        {
            IsAutoApplyPending = true;
            StatusMessage = _localizationService.Translate(AutoApplyQueuedStatus);
        }
    }

    private void OnTasksChanged(object? sender, EventArgs e) => QueueAutoApply();

    private async Task<bool> ApplyWallpaperAsync(bool showAlert, CancellationToken cancellationToken)
    {
        if (IsApplying) return false;

        var tasks = _taskSource.GetTaskModels().Take(MaxWallpaperTasks).ToList();
        IsApplying = true;
        StatusMessage = _localizationService.Translate("Preparing lock-screen wallpaper…");
        var succeeded = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _preferences.Set(LastWallpaperOperationPreferenceKey, DateTime.UtcNow.Ticks);
            var previewBytes = await _wallpaperService.ApplyLockScreenAsync(
                tasks, _themeSettingsService.IsDarkMode(), CreateLayoutSettings(), cancellationToken);

            succeeded = true;
            StatusMessage = IsAutoApplyPending
                ? _localizationService.Translate(AutoApplyQueuedStatus)
                : (_localizationService.IsPersian
                    ? $"صفحهٔ قفل در ساعت {DateTime.Now:HH:mm} به‌روز شد."
                    : $"Lock screen updated at {DateTime.Now:HH:mm}.");

            if (showAlert)
                NotificationRequested?.Invoke(this, new WallpaperNotificationEventArgs(
                    "Wallpaper updated",
                    "Your lock-screen wallpaper is ready. Here is the exact image applied to your lock screen.",
                    previewBytes: previewBytes));
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _localizationService.Translate("Wallpaper update cancelled.");
            return false;
        }
        catch (Exception ex)
        {
            StatusMessage = _localizationService.Translate("Could not update wallpaper.");
            if (showAlert)
                NotificationRequested?.Invoke(this, new WallpaperNotificationEventArgs(
                    "Couldn’t update wallpaper", ex.Message, isError: true));
            return false;
        }
        finally
        {
            IsApplying = false;
            if (IsAutoApplyPending)
                StatusMessage = _localizationService.Translate(AutoApplyQueuedStatus);
            else if (!succeeded && !showAlert && IsAutoApplyEnabled)
            {
                // Preserve retry semantics after a failed background apply.
                IsAutoApplyPending = true;
                StatusMessage = _localizationService.Translate(AutoApplyQueuedStatus);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _taskSource.TasksChanged -= OnTasksChanged;
        _disposed = true;
    }
}
