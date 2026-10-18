using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidColor = Android.Graphics.Color;
using Locksy.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;

namespace Locksy;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density,
    ScreenOrientation = ScreenOrientation.Portrait)]
public class MainActivity : MauiAppCompatActivity
{
    private EventHandler<AppThemeChangedEventArgs>? _themeChangedHandler;
    private bool _isInForeground;
    private CancellationTokenSource? _backgroundWallpaperApplyCts;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ApplyNavigationBarStyle();

        // Keep the system navigation area in sync even when the user follows Android's
        // system theme rather than selecting Light or Dark inside Locksy.
        if (Microsoft.Maui.Controls.Application.Current is { } app)
        {
            _themeChangedHandler = (_, _) => ApplyNavigationBarStyle();
            app.RequestedThemeChanged += _themeChangedHandler;
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        _isInForeground = true;
        // If the user returns while an automatic wallpaper image is still being rendered,
        // cancel it before the native WallpaperManager call whenever possible.
        _backgroundWallpaperApplyCts?.Cancel();
        // Some Android skins restore their own navigation-bar contrast when the activity resumes.
        ApplyNavigationBarStyle();

        // A double-back action backgrounds the task rather than finishing the Activity.
        // Reset its temporary guard on resume; queued auto-apply waits until the next stop.
        if (Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page is NavigationPage navigation &&
            navigation.CurrentPage is MainPage mainPage)
        {
            mainPage.OnHostResumed();
        }
    }


    protected override void OnPause()
    {
        _isInForeground = false;
        base.OnPause();
    }

    protected override void OnStop()
    {
        var mainPage = FindCurrentMainPage();
        base.OnStop();
        if (mainPage is not null)
            _ = ApplyQueuedWallpaperAfterBackgroundAsync(mainPage);
    }

    private static MainPage? FindCurrentMainPage()
    {
        if (Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page is NavigationPage navigation &&
            navigation.CurrentPage is MainPage mainPage)
            return mainPage;

        return null;
    }

    private async Task ApplyQueuedWallpaperAfterBackgroundAsync(MainPage mainPage)
    {
        CancellationTokenSource? source = null;
        try
        {
            // Give Android time to complete the foreground-to-background transition.
            // If the user returns immediately, skip the apply rather than causing a flash.
            await Task.Delay(650);
            if (_isInForeground || IsDestroyed)
                return;

            var operationCts = new CancellationTokenSource();
            source = operationCts;
            _backgroundWallpaperApplyCts = operationCts;
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (!_isInForeground && !IsDestroyed)
                    await mainPage.ApplyQueuedAutoWallpaperWhenBackgroundedAsync(operationCts.Token);
            });
        }
        catch (System.OperationCanceledException)
        {
            // The user returned to Locksy before the native wallpaper operation started.
        }
        catch (Exception)
        {
            // Background auto-apply is best-effort; failures are kept queued by MainPage.
        }
        finally
        {
            if (ReferenceEquals(_backgroundWallpaperApplyCts, source))
                _backgroundWallpaperApplyCts = null;
            source?.Dispose();
        }
    }

    protected override void OnDestroy()
    {
        if (_themeChangedHandler is not null && Microsoft.Maui.Controls.Application.Current is { } app)
            app.RequestedThemeChanged -= _themeChangedHandler;
        _themeChangedHandler = null;
        base.OnDestroy();
    }

    public static void RefreshNavigationBarTheme()
    {
        if (Platform.CurrentActivity is MainActivity activity)
            activity.ApplyNavigationBarStyle();
    }

    private void ApplyNavigationBarStyle()
    {
        var window = Window;
        if (window is null) return;

        var isDark = ThemeService.IsDarkMode();
        var navigationColor = AndroidColor.ParseColor(isDark ? "#20212D" : "#E6E9F1");

        // Android 15 (API 35) enforces edge-to-edge and deprecates the legacy
        // navigation-bar color/divider APIs. Use OS version helpers so the
        // platform analyzer can prove each API is called only where supported.
        if (!OperatingSystem.IsAndroidVersionAtLeast(35))
        {
            window.SetNavigationBarColor(navigationColor);

            // NavigationBarDividerColor exists from API 28 and was deprecated on API 35.
            if (OperatingSystem.IsAndroidVersionAtLeast(28))
            {
                if (!OperatingSystem.IsAndroidVersionAtLeast(35))
                    window.NavigationBarDividerColor = navigationColor;
            }

            // NavigationBarContrastEnforced exists from API 29 and was deprecated on API 35.
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                if (!OperatingSystem.IsAndroidVersionAtLeast(35))
                    window.NavigationBarContrastEnforced = false;
            }
        }

        // Use the native API on Android 11+; this API is part of the platform
        // binding shipped with the Android SDK and does not depend on a specific
        // AndroidX.Core binding version.
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var controller = window.InsetsController;
            if (controller is not null)
            {
                var lightNavigationBars = (int)WindowInsetsControllerAppearance.LightNavigationBars;
                controller.SetSystemBarsAppearance(isDark ? 0 : lightNavigationBars, lightNavigationBars);
            }
        }
        else if (OperatingSystem.IsAndroidVersionAtLeast(26) && window.DecorView is { } decor)
        {
            // Before API 30, AndroidX's native equivalent is the legacy view flag.
            // This branch is deliberately limited to API 26-29, where it is supported.
            if (isDark)
                decor.SystemUiFlags &= ~SystemUiFlags.LightNavigationBar;
            else
                decor.SystemUiFlags |= SystemUiFlags.LightNavigationBar;
        }
    }
}
