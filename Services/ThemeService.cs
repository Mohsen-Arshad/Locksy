using Microsoft.Maui.Controls;
using MauiPreferences = Microsoft.Maui.Storage.Preferences;

namespace Locksy.Services;

public static class ThemeService
{
    private const string PreferenceKey = "locksy-theme";

    public static ThemePreference GetSavedPreference()
    {
        var value = MauiPreferences.Default.Get(PreferenceKey, nameof(ThemePreference.Dark));
        return Enum.TryParse<ThemePreference>(value, ignoreCase: true, out var preference)
            ? preference
            : ThemePreference.Dark;
    }

    public static void SetPreference(ThemePreference preference)
    {
        MauiPreferences.Default.Set(PreferenceKey, preference.ToString());
        Apply(preference);
    }

    public static void ApplySavedPreference() => Apply(GetSavedPreference());

    public static bool IsDarkMode()
    {
        if (Application.Current is not { } app) return false;
        return app.UserAppTheme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => app.RequestedTheme == AppTheme.Dark
        };
    }

    private static void Apply(ThemePreference preference)
    {
        if (Application.Current is not { } app) return;

        app.UserAppTheme = preference switch
        {
            ThemePreference.Light => AppTheme.Light,
            ThemePreference.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified
        };
    }
}
