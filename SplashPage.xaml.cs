using Microsoft.Maui.ApplicationModel;
using System.Diagnostics;
using Locksy.Models;
using Locksy.Factories;
using Locksy.Services.Abstractions;
using Locksy.Services;

namespace Locksy;

public partial class SplashPage : ContentPage
{
    private bool _animationStarted;
    private bool _transitionStarted;
    private CancellationTokenSource? _decorationAnimationCts;
    private MainPage? _preparedMainPage;
    private Exception? _mainPagePreparationError;
    private readonly IMainPageFactory _mainPageFactory;
    private readonly ITaskRepository _taskRepository;
    private readonly IThemeSettingsService _themeSettingsService;

    public SplashPage(
        IMainPageFactory mainPageFactory,
        ITaskRepository taskRepository,
        IThemeSettingsService themeSettingsService)
    {
        _mainPageFactory = mainPageFactory ?? throw new ArgumentNullException(nameof(mainPageFactory));
        _taskRepository = taskRepository ?? throw new ArgumentNullException(nameof(taskRepository));
        _themeSettingsService = themeSettingsService ?? throw new ArgumentNullException(nameof(themeSettingsService));
        InitializeComponent();
        NavigationPage.SetHasNavigationBar(this, false);
        LocalizationService.ApplyCulture();
        FlowDirection = LocalizationService.CurrentFlowDirection;
        RootGrid.FlowDirection = FlowDirection;
        TaglineLabel.Text = LocalizationService.Translate("Your tasks, right on your lock screen.");
        FooterLabel.Text = LocalizationService.Translate("A LITTLE MORE IN SIGHT.");
        VersionLabel.Text = $"{LocalizationService.Translate("Version")} {LocalizationService.LocalizeDigits(AppInfo.Current.VersionString)}";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_animationStarted)
            return;

        _animationStarted = true;
        _decorationAnimationCts = new CancellationTokenSource();
        _ = AnimateDecorationsAsync(_decorationAnimationCts.Token);

        // Start from a clean, deterministic state. The decorative effects are vector/raster
        // overlays animated through native transforms and opacity, never video or frame rendering.
        // Keep the gradient background visible from the first app frame. Only animate
        // the foreground elements; fading the entire root in created a black flash.
        RootGrid.Opacity = 1;
        LogoBadge.Opacity = 0;
        LogoBadge.Scale = 0.88;
        LogoBadge.TranslationY = 0;
        LogoHalo.Opacity = 0;
        BrandLabel.Opacity = 0;
        BrandLabel.TranslationY = 12;
        AccentPill.Opacity = 0;
        TaglineLabel.Opacity = 0;
        TaglineLabel.TranslationY = 8;
        ProgressFill.ScaleX = 0;
        FooterLabel.Opacity = 0;
        VersionLabel.Opacity = 0;

        try
        {
            await Task.WhenAll(
                LogoBadge.FadeToAsync(1, 260, Easing.CubicOut),
                LogoBadge.ScaleToAsync(1, 360, Easing.SpringOut),
                LogoHalo.FadeToAsync(1, 360, Easing.CubicOut));
            await Task.WhenAll(
                BrandLabel.FadeToAsync(1, 300, Easing.CubicOut),
                BrandLabel.TranslateToAsync(0, 0, 300, Easing.CubicOut),
                AccentPill.FadeToAsync(1, 300, Easing.CubicOut));

            await Task.WhenAll(
                TaglineLabel.FadeToAsync(1, 370, Easing.CubicOut),
                TaglineLabel.TranslateToAsync(0, 0, 370, Easing.CubicOut),
                FooterLabel.FadeToAsync(1, 360, Easing.CubicOut),
                VersionLabel.FadeToAsync(1, 360, Easing.CubicOut));

            // Build the main page while the branded splash is still visible. Its XAML,
            // view model and local task JSON are prepared before the transition, eliminating
            // the expensive page-construction gap that used to show a black screen.
            var mainPagePreparation = PrepareMainPageAsync(_decorationAnimationCts.Token);
            // Let the progress animation and main-page preparation overlap. There is no
            // artificial minimum splash delay: transition as soon as both are ready.
            await Task.WhenAll(
                ProgressFill.ScaleXToAsync(1, 850, Easing.CubicOut),
                mainPagePreparation);

            if (_preparedMainPage is null)
            {
                ShowStartupFailure(_mainPagePreparationError);
                return;
            }

            // Crossfade once into the already-created page; do not recreate it at transition time.
            await ReturnToMainPageAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Locksy startup] Splash transition failed: {ex}");
            // If animation is interrupted by a lifecycle change, continue to the main
            // page. Transition guard prevents duplicate navigation or a second splash.
            try
            {
                await ReturnToMainPageAsync();
            }
            catch (Exception navigationException)
            {
                Debug.WriteLine($"[Locksy startup] Fallback navigation failed: {navigationException}");
                if (Application.Current?.Windows.FirstOrDefault()?.Page is NavigationPage navigationPage
                    && navigationPage.CurrentPage is MainPage mainPage)
                    mainPage.Opacity = 1;
                else
                    ShowStartupFailure(navigationException);
            }
        }
    }

    private async Task PrepareMainPageAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Read the tiny local JSON file asynchronously while the splash remains visible;
            // avoid synchronous disk access in MainPage's constructor on the UI thread.
            var initialTasks = await _taskRepository.LoadAsync(cancellationToken);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (!cancellationToken.IsCancellationRequested && _preparedMainPage is null)
                    _preparedMainPage = _mainPageFactory.Create(startHidden: true, initialTasks: initialTasks);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _mainPagePreparationError = ex;
            Debug.WriteLine($"[Locksy startup] MainPage preparation failed: {ex}");
        }
    }

    private async Task ReturnToMainPageAsync()
    {
        if (_transitionStarted)
            return;

        _transitionStarted = true;
        var window = Window ?? Application.Current?.Windows.FirstOrDefault();
        MainPage? mainPage = _preparedMainPage;
        _preparedMainPage = null;

        try
        {
            // Construct inside the guarded section. A constructor failure must never be
            // swallowed by SplashPage with the app left looking permanently loading.
            mainPage ??= _mainPageFactory.Create(startHidden: true);

            // Re-hide just the content; the themed page background stays opaque during
            // the native NavigationPage transition, so there is no black intermediate frame.
            mainPage.PrepareForStartupReveal();

            // Let the native Android/MAUI page transition show the splash underneath;
            // reveal the controls at the same time for a smooth slide-and-fade entrance.
            var navigationTask = Navigation.PushAsync(mainPage, animated: true);
            await Task.Delay(45);
            var revealTask = mainPage.RevealStartupContentAsync(320);
            await Task.WhenAll(navigationTask, revealTask);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Locksy startup] MainPage navigation failed: {ex}");
            if (mainPage is null)
            {
                ShowStartupFailure(ex);
                _transitionStarted = false;
                return;
            }

            try
            {
                mainPage.ShowStartupContentImmediately();
                if (window is not null)
                {
                    var surface = Color.FromArgb(_themeSettingsService.IsDarkMode() ? "#20212D" : "#E6E9F1");
                    window.Page = new NavigationPage(mainPage)
                    {
                        BackgroundColor = surface,
                        BarBackgroundColor = surface
                    };
                }
                mainPage.CompleteStartupTransition();
            }
            catch (Exception fallbackException)
            {
                Debug.WriteLine($"[Locksy startup] Immediate navigation fallback failed: {fallbackException}");
                ShowStartupFailure(fallbackException);
            }
            return;
        }

        if (mainPage is null)
        {
            ShowStartupFailure(_mainPagePreparationError);
            return;
        }

        mainPage.CompleteStartupTransition();
        try { Navigation.RemovePage(this); }
        catch { /* Navigation may be restoring; the main page remains usable. */ }
    }

    private void ShowStartupFailure(Exception? exception)
    {
        if (exception is not null)
            Debug.WriteLine($"[Locksy startup] Unable to create the main page: {exception}");

        // Display an explicit failure state instead of leaving the animated splash in
        // a completed-but-stuck state when startup fails on a device-specific issue.
        TaglineLabel.Text = LocalizationService.Translate("Locksy could not start.");
        FooterLabel.Text = LocalizationService.Translate("Please close and reopen the app.");
        TaglineLabel.Opacity = 1;
        FooterLabel.Opacity = 1;
        ProgressFill.ScaleX = 1;
    }

    protected override void OnDisappearing()
    {
        _decorationAnimationCts?.Cancel();
        _decorationAnimationCts?.Dispose();
        _decorationAnimationCts = null;

        TopGlowOrbit.CancelAnimations();
        BottomGlowOrbit.CancelAnimations();
        TopGlowAlt.CancelAnimations();
        BottomGlowAlt.CancelAnimations();
        base.OnDisappearing();
    }

    private async Task AnimateDecorationsAsync(CancellationToken cancellationToken)
    {
        // Slow counter-rotation makes the corner lights feel like a travelling highlight,
        // while low-opacity gradient cross-fades make the color feel alive without flashing.
        await Task.WhenAll(
            AnimateOrbitAsync(TopGlowOrbit, 4200, reverse: false, cancellationToken),
            AnimateOrbitAsync(BottomGlowOrbit, 5600, reverse: true, cancellationToken),
            PulseGradientAsync(TopGlowAlt, 0.10, 0.54, 2600, cancellationToken),
            PulseGradientAsync(BottomGlowAlt, 0.08, 0.46, 3100, cancellationToken));
    }

    private static async Task AnimateOrbitAsync(
        VisualElement element,
        uint duration,
        bool reverse,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (reverse)
                {
                    element.Rotation = 360;
                    await element.RotateToAsync(0, duration, Easing.Linear);
                }
                else
                {
                    element.Rotation = 0;
                    await element.RotateToAsync(360, duration, Easing.Linear);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private static async Task PulseGradientAsync(
        VisualElement element,
        double lowOpacity,
        double highOpacity,
        uint duration,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await element.FadeToAsync(highOpacity, duration, Easing.SinInOut);
                await element.FadeToAsync(lowOpacity, duration, Easing.SinInOut);
            }
        }
        catch (OperationCanceledException) { }
    }
}
