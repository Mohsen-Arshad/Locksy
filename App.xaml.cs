using Locksy.Services;
using Locksy.Factories;
using Locksy.Services.Abstractions;

namespace Locksy;

public partial class App : Application
{
    private const string LastWallpaperOperationPreferenceKey = "locksy-last-wallpaper-operation-utc-ticks";
    private readonly IMainPageFactory _mainPageFactory;
    private readonly ITaskRepository _taskRepository;
    private readonly IThemeSettingsService _themeSettingsService;
    private readonly IAppPreferencesService _preferences;
    private static readonly TimeSpan SplashSuppressionWindow = TimeSpan.FromMinutes(2);

    public App(
        IMainPageFactory mainPageFactory,
        ITaskRepository taskRepository,
        IThemeSettingsService themeSettingsService,
        IAppPreferencesService preferences)
    {
        _mainPageFactory = mainPageFactory ?? throw new ArgumentNullException(nameof(mainPageFactory));
        _taskRepository = taskRepository ?? throw new ArgumentNullException(nameof(taskRepository));
        _themeSettingsService = themeSettingsService ?? throw new ArgumentNullException(nameof(themeSettingsService));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        LocalizationService.EnsureEnglishDefaultForV122();
        LocalizationService.ApplyCulture();
        InitializeComponent();
        _themeSettingsService.ApplySavedPreference();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Consume the wallpaper-operation marker once. It is only meant to suppress the splash
        // when Android recreates the window as part of the current wallpaper update, not on every
        // ordinary app launch during the two-minute window.
        var suppressSplashForRecreation = WasWallpaperOperationRecent();
        _preferences.Remove(LastWallpaperOperationPreferenceKey);

        if (suppressSplashForRecreation)
            return new Window(_mainPageFactory.Create());

        // SplashPage uses Navigation.PushAsync for its crossfade, so keep it inside a NavigationPage.
        return new Window(new NavigationPage(new SplashPage(_mainPageFactory, _taskRepository, _themeSettingsService)));
    }

    private bool WasWallpaperOperationRecent()
    {
        var ticks = _preferences.GetInt64(LastWallpaperOperationPreferenceKey, 0L);
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
