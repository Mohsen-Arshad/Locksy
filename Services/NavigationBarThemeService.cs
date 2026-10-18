using Locksy.Platforms.Android;
using Locksy.Services.Abstractions;

namespace Locksy.Services;

public sealed class NavigationBarThemeService : INavigationBarThemeService
{
    public void RefreshTheme() => MainActivity.RefreshNavigationBarTheme();
}
