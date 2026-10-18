using System.Globalization;
using Locksy.Services;
using Locksy.ViewModels;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;


namespace Locksy;

public partial class MainPage : ContentPage
{
    private const string AutoApplyQueuedStatus = "Auto update queued for background.";
    // Official Locksy source repository.
    private const string ProjectGitHubUrl = "https://github.com/Mohsen-Arshad/Locksy";

    private readonly MainViewModel _viewModel;
    private readonly WallpaperViewModel _wallpaperViewModel;
    // UI order is deliberately more intuitive than the renderer enum order.
    private static readonly (string Name, WallpaperLayout Layout)[] WallpaperLayoutChoices =
    {
        ("Tasks bottom", WallpaperLayout.TasksBottom),
        ("Tasks top", WallpaperLayout.AboveBottomClock),
        ("Centered task list", WallpaperLayout.CenteredList),
        ("Split around center clock", WallpaperLayout.SplitAroundCenterClock),
        ("Screen-aware (auto)", WallpaperLayout.ScreenAware)
    };
    private TaskCompletionSource<string?>? _popupCompletionSource;
    private bool _initializing = true;
    private bool _startupInitializationComplete;
    private bool _isSendingTaskToBackground;
    private DateTime _lastBackPressUtc = DateTime.MinValue;
    private bool _settingsTransitionRunning;
    private bool _settingsFlowActive;
    private string _currentSettingsPage = "Settings";
    private bool _taskPopupFlowActive;
    private bool _taskPopupSubpageActive;
    private bool _layoutPreviewActive;
    private double _popupPreferredWidth = 380d;
#if ANDROID
    private readonly Dictionary<Android.Views.View, WeakReference<Label>> _nativeTitleViews = new();
    private readonly Dictionary<Android.Views.View, WeakReference<Border>> _nativeTaskRows = new();
    private readonly Dictionary<Android.Views.View, WeakReference<Label>> _nativeTaskGrips = new();
    private TaskItemViewModel? _reorderCandidate;
    private TaskItemViewModel? _draggedTask;
    private TaskItemViewModel? _hoveredTask;
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

    internal MainPage(MainViewModel viewModel, bool startHidden = false)
    {
        InitializeComponent();
        NavigationPage.SetHasNavigationBar(this, false);

        // The factory composes the root and child view models; the page only binds them.
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _wallpaperViewModel = viewModel.Wallpaper ?? throw new ArgumentException("The page requires a composed wallpaper view model.", nameof(viewModel));
        if (_viewModel.Settings is null || _viewModel.Reminders is null)
            throw new ArgumentException("The page requires composed settings and reminder view models.", nameof(viewModel));
        BindingContext = _viewModel;
        ApplyLocalization();

        // Keep the themed page surface visible during navigation. Only the content grid
        // starts transparent, so the native page transition never reveals a black window.
        Opacity = 1;
        PageContent.Opacity = startHidden ? 0 : 1;
        UpdateAutoApplyVisual(_wallpaperViewModel.IsAutoApplyEnabled);
        _wallpaperViewModel.NotificationRequested += OnWallpaperNotificationRequested;
        if (_viewModel.Settings is { } settingsViewModel)
        {
            settingsViewModel.LanguageChanged += OnLanguageChanged;
        }
        _initializing = false;


    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0 || height <= 0 || ModalCard is null || ModalBodyScroll is null)
            return;

        ApplyResponsivePopupBounds(width, height);
    }

    /// <summary>
    /// One responsive sizing policy for every in-app popup. The card sizes to its content
    /// until it reaches the safe viewport cap; only the body scrolls when content is too tall.
    /// The header and action footer therefore remain visible on short screens, while short
    /// dialogs do not reserve a giant empty body region.
    /// </summary>
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
        if (_wallpaperViewModel.IsAutoApplyPending)
            _wallpaperViewModel.StatusMessage = LocalizationService.Translate(AutoApplyQueuedStatus);
    }

    public void PrepareForStartupReveal() => PageContent.Opacity = 0;

    public Task RevealStartupContentAsync(uint duration = 320) =>
        PageContent.FadeTo(1, duration, Easing.CubicOut);

    public void ShowStartupContentImmediately()
    {
        Opacity = 1;
        PageContent.Opacity = 1;
    }

    private static string LocalizedDisplayText(string? value) =>
        LocalizationService.PrepareTextForDisplay(LocalizationService.Translate(value));

    private void ApplyLocalization()
    {
        FlowDirection = LocalizationService.CurrentFlowDirection;
        PageContent.FlowDirection = FlowDirection;
        ModalOverlay.FlowDirection = FlowDirection;
        ModalSectionLabel.FlowDirection = FlowDirection;
        ModalTitle.FlowDirection = FlowDirection;
        ModalMessage.FlowDirection = FlowDirection;
        ModalMessage.HorizontalTextAlignment = TextAlignment.Justify;
        ModalMessage.LineBreakMode = LineBreakMode.WordWrap;
        _viewModel.RefreshLocalizedTaskTitles();

        HeaderTaglineLabel.FlowDirection = FlowDirection;
        NewTaskEntry.FlowDirection = FlowDirection;
        HeaderTaglineLabel.Text = LocalizedDisplayText("Keep what matters in sight.");
        NewTaskEntry.Placeholder = LocalizedDisplayText("Add a task...");
        AddTaskButton.Text = LocalizedDisplayText("＋ Add");
        EmptyListTitleLabel.Text = LocalizedDisplayText("Your list is clear");
        EmptyListDescriptionLabel.Text = LocalizedDisplayText("Add your first task. It will be saved on this device.");
        ClockLayoutCaption.Text = LocalizedDisplayText("Tasks layout");
        AutoApplyCaption.Text = LocalizedDisplayText("Auto update");
        AutoApplyDescriptionLabel.Text = LocalizedDisplayText("Apply edits when app is in background");
        ClearCompletedButton.Text = LocalizedDisplayText("Clear completed");
        ApplyBusyLabelRefresh();
        FirstEightLabel.Text = LocalizedDisplayText("First 8 tasks on wallpaper");
        DragGhostTitle.Text = LocalizedDisplayText("Moving task");
        ModalSectionLabel.Text = LocalizedDisplayText("LOCKSY  ·  QUICK ACTION");
        SemanticProperties.SetDescription(SettingsButton, LocalizationService.Translate("Open application settings"));
        SemanticProperties.SetDescription(NewTaskSettingsButton, LocalizationService.Translate("New reminder settings"));
        SemanticProperties.SetDescription(AutoApplySwitch, LocalizationService.Translate("Automatically refresh wallpaper after task edits"));
        _wallpaperViewModel.RefreshLocalizedState();
    }

    private void ApplyBusyLabelRefresh()
    {
        ApplyingLabel.Text = LocalizedDisplayText("Applying…");
    }

    private async void OnWallpaperNotificationRequested(object? sender, WallpaperNotificationEventArgs e)
    {
        await MainThread.InvokeOnMainThreadAsync(() => ShowInfoPopupAsync(
            e.Title, e.Message, isError: e.IsError, previewBytes: e.PreviewBytes));
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLocalization();

    protected override void OnDisappearing()
    {
        // Queued auto-updates are persisted and run only by MainActivity after the
        // Activity is hidden. Do not cancel an already running wallpaper operation.
        base.OnDisappearing();
    }
}
