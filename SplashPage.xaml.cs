using Microsoft.Maui.ApplicationModel;
using Locksy.Helpers;
using Locksy.Models;

namespace Locksy;

public partial class SplashPage : ContentPage
{
    private bool _animationStarted;
    private bool _transitionStarted;
    private CancellationTokenSource? _decorationAnimationCts;
    private MainPage? _preparedMainPage;

    public SplashPage()
    {
        InitializeComponent();
        NavigationPage.SetHasNavigationBar(this, false);
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

        try
        {
            await Task.WhenAll(
                LogoBadge.FadeTo(1, 260, Easing.CubicOut),
                LogoBadge.ScaleTo(1, 360, Easing.SpringOut),
                LogoHalo.FadeTo(1, 360, Easing.CubicOut));
            await Task.WhenAll(
                BrandLabel.FadeTo(1, 300, Easing.CubicOut),
                BrandLabel.TranslateTo(0, 0, 300, Easing.CubicOut),
                AccentPill.FadeTo(1, 300, Easing.CubicOut));

            await Task.WhenAll(
                TaglineLabel.FadeTo(1, 370, Easing.CubicOut),
                TaglineLabel.TranslateTo(0, 0, 370, Easing.CubicOut),
                FooterLabel.FadeTo(1, 360, Easing.CubicOut));

            // Build the main page while the branded splash is still visible. Its XAML,
            // view model and local task JSON are prepared before the transition, eliminating
            // the expensive page-construction gap that used to show a black screen.
            var mainPagePreparation = PrepareMainPageAsync(_decorationAnimationCts.Token);
            await ProgressFill.ScaleXTo(1, 850, Easing.CubicOut);
            await Task.Delay(3500, _decorationAnimationCts.Token);
            await mainPagePreparation;

            // Crossfade once into the already-created page; do not recreate it at transition time.
            await ReturnToMainPageAsync();
        }
        catch (Exception)
        {
            // If animation is interrupted by a lifecycle change, continue to the main
            // page. Transition guard prevents duplicate navigation or a second splash.
            try
            {
                await ReturnToMainPageAsync();
            }
            catch
            {
                if (Application.Current?.Windows.FirstOrDefault()?.Page is NavigationPage navigationPage
                    && navigationPage.CurrentPage is MainPage mainPage)
                    mainPage.Opacity = 1;
            }
        }
    }

    private async Task PrepareMainPageAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Read the tiny local JSON file asynchronously while the splash remains visible;
            // avoid synchronous disk access in MainPage's constructor on the UI thread.
            await Task.Delay(120, cancellationToken);
            var initialTasks = await StorageHelper.LoadAsync(cancellationToken);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (!cancellationToken.IsCancellationRequested && _preparedMainPage is null)
                    _preparedMainPage = new MainPage(startHidden: true, initialTasks: initialTasks);
            });
        }
        catch (OperationCanceledException) { }
    }

    private async Task ReturnToMainPageAsync()
    {
        if (_transitionStarted)
            return;

        _transitionStarted = true;
        var window = Window ?? Application.Current?.Windows.FirstOrDefault();
        var mainPage = _preparedMainPage ?? new MainPage(startHidden: true);
        _preparedMainPage = null;
        // Re-hide just the content; the themed page background stays opaque during
        // the native NavigationPage transition, so there is no black intermediate frame.
        mainPage.PrepareForStartupReveal();

        try
        {
            // Let the native Android/MAUI page transition show the splash underneath;
            // reveal the controls at the same time for a smooth slide-and-fade entrance.
            var navigationTask = Navigation.PushAsync(mainPage, animated: true);
            await Task.Delay(45);
            var revealTask = mainPage.RevealStartupContentAsync(320);
            await Task.WhenAll(navigationTask, revealTask);
        }
        catch
        {
            try
            {
                mainPage.ShowStartupContentImmediately();
                if (window is not null)
                {
                    var surface = Color.FromArgb(Locksy.Services.ThemeService.IsDarkMode() ? "#20212D" : "#E6E9F1");
                    window.Page = new NavigationPage(mainPage)
                    {
                        BackgroundColor = surface,
                        BarBackgroundColor = surface
                    };
                }
                mainPage.CompleteStartupTransition();
            }
            catch { }
            return;
        }

        mainPage.CompleteStartupTransition();
        try { Navigation.RemovePage(this); }
        catch { /* Navigation may be restoring; the main page remains usable. */ }
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
                    await element.RotateTo(0, duration, Easing.Linear);
                }
                else
                {
                    element.Rotation = 0;
                    await element.RotateTo(360, duration, Easing.Linear);
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
                await element.FadeTo(highOpacity, duration, Easing.SinInOut);
                await element.FadeTo(lowOpacity, duration, Easing.SinInOut);
            }
        }
        catch (OperationCanceledException) { }
    }
}
