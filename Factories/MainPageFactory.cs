using Locksy.Models;
using Locksy.Services;
using Locksy.Services.Abstractions;
using Locksy.ViewModels;

namespace Locksy.Factories;

/// <summary>Composition root for a page and its collaborating view models.</summary>
public sealed class MainPageFactory : IMainPageFactory
{
    private readonly ITaskRepository _taskRepository;
    private readonly IWallpaperService _wallpaperService;
    private readonly IAppPreferencesService _preferences;
    private readonly IThemeSettingsService _themeSettings;
    private readonly ILanguageSettingsService _languageSettings;
    private readonly IImagePickerService _imagePickerService;
    private readonly IExternalNavigationService _externalNavigationService;
    private readonly ICustomWallpaperBackgroundManager _customBackgroundManager;
    private readonly INotificationPermissionService _notificationPermissionService;
    private readonly IReminderSchedulingService _reminderSchedulingService;
    private readonly INavigationBarThemeService _navigationBarThemeService;
    private readonly ILocalizationService _localizationService;

    public MainPageFactory(
        ITaskRepository taskRepository,
        IWallpaperService wallpaperService,
        IAppPreferencesService preferences,
        IThemeSettingsService themeSettings,
        ILanguageSettingsService languageSettings,
        IImagePickerService imagePickerService,
        IExternalNavigationService externalNavigationService,
        ICustomWallpaperBackgroundManager customBackgroundManager,
        INotificationPermissionService notificationPermissionService,
        IReminderSchedulingService reminderSchedulingService,
        INavigationBarThemeService navigationBarThemeService,
        ILocalizationService localizationService)
    {
        _taskRepository = taskRepository ?? throw new ArgumentNullException(nameof(taskRepository));
        _wallpaperService = wallpaperService ?? throw new ArgumentNullException(nameof(wallpaperService));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _themeSettings = themeSettings ?? throw new ArgumentNullException(nameof(themeSettings));
        _languageSettings = languageSettings ?? throw new ArgumentNullException(nameof(languageSettings));
        _imagePickerService = imagePickerService ?? throw new ArgumentNullException(nameof(imagePickerService));
        _externalNavigationService = externalNavigationService ?? throw new ArgumentNullException(nameof(externalNavigationService));
        _customBackgroundManager = customBackgroundManager ?? throw new ArgumentNullException(nameof(customBackgroundManager));
        _notificationPermissionService = notificationPermissionService ?? throw new ArgumentNullException(nameof(notificationPermissionService));
        _reminderSchedulingService = reminderSchedulingService ?? throw new ArgumentNullException(nameof(reminderSchedulingService));
        _navigationBarThemeService = navigationBarThemeService ?? throw new ArgumentNullException(nameof(navigationBarThemeService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
    }

    public MainPage Create(bool startHidden = false, IEnumerable<TodoItem>? initialTasks = null)
    {
        var mainViewModel = new MainViewModel(_taskRepository, _localizationService);
        mainViewModel.InitializeTasks(initialTasks);

        var wallpaperViewModel = new WallpaperViewModel(mainViewModel, _wallpaperService, _preferences, _themeSettings, _localizationService);
        mainViewModel.AttachWallpaperViewModel(wallpaperViewModel);

        var settingsViewModel = new SettingsViewModel(
            _themeSettings,
            _languageSettings,
            wallpaperViewModel,
            _imagePickerService,
            _customBackgroundManager,
            _externalNavigationService,
            _navigationBarThemeService,
            _localizationService);
        mainViewModel.AttachSettingsViewModel(settingsViewModel);

        var reminderViewModel = new ReminderViewModel(mainViewModel, _notificationPermissionService, _reminderSchedulingService);
        mainViewModel.AttachReminderViewModel(reminderViewModel);

        return new MainPage(mainViewModel, startHidden);
    }
}
