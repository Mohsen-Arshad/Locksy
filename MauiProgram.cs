using Locksy.Factories;
using Locksy.Platforms.Android;
using Locksy.Services;
using Locksy.Services.Abstractions;
using Locksy.Services.Persistence;
using Locksy.Services.Preferences;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;

namespace Locksy;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        // Register one shared repository instance so DI and Android compatibility adapters
        // serialize writes through the same latest-write-wins gate.
        builder.Services.AddSingleton<ITaskRepository>(JsonTaskRepository.Shared);
        builder.Services.AddSingleton<IAppPreferencesService, MauiAppPreferencesService>();
        builder.Services.AddSingleton<IWallpaperService, AndroidWallpaperService>();
        builder.Services.AddSingleton<IThemeSettingsService, ThemeSettingsService>();
        builder.Services.AddSingleton<ILocalizationService, LocalizationServiceAdapter>();
        builder.Services.AddSingleton<ILanguageSettingsService, LanguageSettingsService>();
        builder.Services.AddSingleton<IImagePickerService, ImagePickerService>();
        builder.Services.AddSingleton<IExternalNavigationService, ExternalNavigationService>();
        builder.Services.AddSingleton<ICustomWallpaperBackgroundManager, CustomWallpaperBackgroundManager>();
        builder.Services.AddSingleton<INotificationPermissionService, NotificationPermissionService>();
        builder.Services.AddSingleton<IReminderSchedulingService, ReminderSchedulingService>();
        builder.Services.AddSingleton<INavigationBarThemeService, NavigationBarThemeService>();
        builder.Services.AddSingleton<IMainPageFactory, MainPageFactory>();

        return builder.Build();
    }
}
