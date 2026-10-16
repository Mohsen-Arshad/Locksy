using Locksy.Services;
using Microsoft.Maui.Storage;

namespace Locksy;

public partial class App : Application
{
    private const string LastWallpaperOperationPreferenceKey = "locksy-last-wallpaper-operation-utc-ticks";
    private static readonly TimeSpan SplashSuppressionWindow = TimeSpan.FromMinutes(2);

    public App()
    {
        InitializeComponent();
        ThemeService.ApplySavedPreference();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Consume the wallpaper-operation marker once. It is only meant to suppress the splash
        // when Android recreates the window as part of the current wallpaper update, not on every
        // ordinary app launch during the two-minute window.
        var suppressSplashForRecreation = WasWallpaperOperationRecent();
        Preferences.Default.Remove(LastWallpaperOperationPreferenceKey);

        if (suppressSplashForRecreation)
            return new Window(new MainPage());

        // SplashPage uses Navigation.PushAsync for its crossfade, so keep it inside a NavigationPage.
        return new Window(new NavigationPage(new SplashPage()));
    }

    private static bool WasWallpaperOperationRecent()
    {
        var ticks = Preferences.Default.Get(LastWallpaperOperationPreferenceKey, 0L);
        if (ticks <= 0)
            return false;

        try
        {
            var lastOperationUtc = new DateTime(ticks, DateTimeKind.Utc);
            var age = DateTime.UtcNow - lastOperationUtc;
            return age >= TimeSpan.Zero && age < SplashSuppressionWindow;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
