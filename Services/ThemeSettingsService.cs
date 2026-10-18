using Locksy.Services.Abstractions;

namespace Locksy.Services;

public sealed class ThemeSettingsService : IThemeSettingsService
{
    public ThemePreference GetSavedPreference() => ThemeService.GetSavedPreference();
    public bool IsDarkMode() => ThemeService.IsDarkMode();
    public void SetPreference(ThemePreference preference) => ThemeService.SetPreference(preference);
    public void ApplySavedPreference() => ThemeService.ApplySavedPreference();
}
