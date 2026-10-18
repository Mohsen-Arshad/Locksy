using Locksy.Services;
using Locksy.Services.Abstractions;
using Locksy.ViewModels.Abstractions;
using Microsoft.Maui.Controls;

namespace Locksy.ViewModels;

/// <summary>Owns theme/language and custom-background settings workflows.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private const string RepositoryUrl = "https://github.com/Mohsen-Arshad/Locksy";
    private readonly IThemeSettingsService _themeSettings;
    private readonly ILanguageSettingsService _languageSettings;
    private readonly IWallpaperUpdateController _wallpaperUpdateController;
    private readonly IImagePickerService _imagePickerService;
    private readonly ICustomWallpaperBackgroundManager _customBackgroundManager;
    private readonly IExternalNavigationService _externalNavigationService;
    private readonly INavigationBarThemeService _navigationBarThemeService;
    private readonly ILocalizationService _localizationService;

    public bool IsDarkMode => _themeSettings.IsDarkMode();
    public bool IsPersian => _languageSettings.IsPersian;
    public bool HasCustomBackground => _customBackgroundManager.GetExistingPath() is not null;
    public Command<ThemePreference> SetThemePreferenceCommand { get; }
    public Command<string> SetLanguageCommand { get; }

    public event EventHandler? LanguageChanged;

    public SettingsViewModel(
        IThemeSettingsService themeSettings,
        ILanguageSettingsService languageSettings,
        IWallpaperUpdateController wallpaperUpdateController,
        IImagePickerService imagePickerService,
        ICustomWallpaperBackgroundManager customBackgroundManager,
        IExternalNavigationService externalNavigationService,
        INavigationBarThemeService navigationBarThemeService,
        ILocalizationService localizationService)
    {
        _themeSettings = themeSettings ?? throw new ArgumentNullException(nameof(themeSettings));
        _languageSettings = languageSettings ?? throw new ArgumentNullException(nameof(languageSettings));
        _wallpaperUpdateController = wallpaperUpdateController ?? throw new ArgumentNullException(nameof(wallpaperUpdateController));
        _imagePickerService = imagePickerService ?? throw new ArgumentNullException(nameof(imagePickerService));
        _customBackgroundManager = customBackgroundManager ?? throw new ArgumentNullException(nameof(customBackgroundManager));
        _externalNavigationService = externalNavigationService ?? throw new ArgumentNullException(nameof(externalNavigationService));
        _navigationBarThemeService = navigationBarThemeService ?? throw new ArgumentNullException(nameof(navigationBarThemeService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        SetThemePreferenceCommand = new Command<ThemePreference>(ApplyThemePreference);
        SetLanguageCommand = new Command<string>(ApplyLanguage);
    }

    private void ApplyThemePreference(ThemePreference preference)
    {
        _themeSettings.SetPreference(preference);
        OnPropertyChanged(nameof(IsDarkMode));
        _navigationBarThemeService.RefreshTheme();
        _wallpaperUpdateController.StatusMessage = _localizationService.Translate(
            preference == ThemePreference.Dark ? "Dark theme selected." : "Light theme selected.");
        _wallpaperUpdateController.QueueAutoApply();
    }

    private void ApplyLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode)) return;
        _languageSettings.SetLanguage(languageCode);
        OnPropertyChanged(nameof(IsPersian));
        _wallpaperUpdateController.StatusMessage = _localizationService.Translate(
            _wallpaperUpdateController.IsAutoApplyEnabled
                ? "Language updated."
                : "Language updated. Tap Apply wallpaper to refresh lock screen.");
        _wallpaperUpdateController.QueueAutoApply();
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<SettingsOperationResult> ChooseCustomBackgroundAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var file = await _imagePickerService.PickImageAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (file is null)
                return new SettingsOperationResult(SettingsOperationStatus.Cancelled);

            await _customBackgroundManager.SaveAsync(file);
            OnPropertyChanged(nameof(HasCustomBackground));
            _wallpaperUpdateController.StatusMessage = _localizationService.Translate(
                _wallpaperUpdateController.IsAutoApplyEnabled
                    ? "Background image saved. It will be applied when Locksy moves to the background."
                    : "Background image saved. Tap Apply wallpaper to see it on your lock screen.");
            _wallpaperUpdateController.QueueAutoApply();
            return new SettingsOperationResult(SettingsOperationStatus.Succeeded);
        }
        catch (OperationCanceledException)
        {
            return new SettingsOperationResult(SettingsOperationStatus.Cancelled);
        }
        catch (Exception ex)
        {
            var message = ex.Message.Contains("25 MB", StringComparison.OrdinalIgnoreCase)
                ? "Please select an image smaller than 25 MB."
                : "The selected image could not be saved.";
            return new SettingsOperationResult(SettingsOperationStatus.Failed, _localizationService.Translate(message));
        }
    }

    public bool RemoveCustomBackground()
    {
        if (!HasCustomBackground) return false;
        _customBackgroundManager.Clear();
        OnPropertyChanged(nameof(HasCustomBackground));
        _wallpaperUpdateController.StatusMessage = _localizationService.Translate(
            _wallpaperUpdateController.IsAutoApplyEnabled
                ? "Custom background removed. The built-in background will be restored when Locksy moves to the background."
                : "Custom background removed. Tap Apply wallpaper to update your lock screen.");
        _wallpaperUpdateController.QueueAutoApply();
        return true;
    }

    public Task OpenRepositoryAsync() => _externalNavigationService.OpenAsync(new Uri(RepositoryUrl));
}
