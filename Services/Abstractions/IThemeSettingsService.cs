using Locksy.Services;

namespace Locksy.Services.Abstractions;

public interface IThemeSettingsService
{
    ThemePreference GetSavedPreference();
    bool IsDarkMode();
    void SetPreference(ThemePreference preference);
    void ApplySavedPreference();
}
