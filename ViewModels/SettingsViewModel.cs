using Locksy.Models;
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
    public WallpaperBackgroundMode BackgroundMode => _customBackgroundManager.GetMode();
    public bool HasCustomBackground => BackgroundMode != WallpaperBackgroundMode.Default;
    public string SelectedPresetKey => _customBackgroundManager.GetSelectedPresetKey();
    public string? CurrentBackgroundPreviewPath => _customBackgroundManager.GetCurrentPreviewImagePath();
    public string? CurrentBackgroundPresetKey => _customBackgroundManager.GetCurrentPreviewPresetKey();
    public string BackgroundSummary => BackgroundMode switch
    {
        WallpaperBackgroundMode.Preset => "A built-in background is currently active.",
        WallpaperBackgroundMode.CustomImage => "A custom image is currently active.",
        WallpaperBackgroundMode.Slideshow => "Wallpaper slideshow is currently active.",
        _ => "The default Locksy background is currently active."
    };
    public IReadOnlyList<WallpaperBackgroundPreset> BackgroundPresets => WallpaperBackgroundCatalog.Presets;
    public IReadOnlyList<WallpaperSlideshowItem> SlideshowItems => _customBackgroundManager.GetSlideshowItems();
    public int SlideshowIntervalMinutes => _customBackgroundManager.GetSlideshowIntervalMinutes();
    public bool IsSlideshowSelected => BackgroundMode == WallpaperBackgroundMode.Slideshow;
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

    public void SelectDefaultBackground()
    {
        _customBackgroundManager.SelectDefault();
        RefreshBackgroundProperties();
        _wallpaperUpdateController.StatusMessage = _localizationService.Translate(
            _wallpaperUpdateController.IsAutoApplyEnabled
                ? "Default background selected. It will be applied when Locksy moves to the background."
                : "Default background selected. Tap Apply wallpaper to update your lock screen.");
        _wallpaperUpdateController.QueueAutoApply();
    }

    public void SelectPresetBackground(string key)
    {
        if (!_customBackgroundManager.SelectPreset(key)) return;
        RefreshBackgroundProperties();
        _wallpaperUpdateController.StatusMessage = _localizationService.Translate(
            _wallpaperUpdateController.IsAutoApplyEnabled
                ? "Background preset selected. It will be applied when Locksy moves to the background."
                : "Background preset selected. Tap Apply wallpaper to update your lock screen.");
        _wallpaperUpdateController.QueueAutoApply();
    }

    public bool AddPresetToSlideshow(string key)
    {
        var added = _customBackgroundManager.AddPresetToSlideshow(key);
        RefreshBackgroundProperties();
        _wallpaperUpdateController.StatusMessage = _localizationService.Translate(added
            ? "Background added to slideshow."
            : "The slideshow already has the maximum number of items.");
        return added;
    }

    public bool ActivateSlideshow()
    {
        if (!_customBackgroundManager.SelectSlideshow())
        {
            _wallpaperUpdateController.StatusMessage = _localizationService.Translate(
                "Add at least two backgrounds to start a slideshow.");
            return false;
        }

        RefreshBackgroundProperties();
        _wallpaperUpdateController.StatusMessage = _localizationService.Translate(
            _wallpaperUpdateController.IsAutoApplyEnabled
                ? "Slideshow selected. The rotation will start when the wallpaper is applied."
                : "Slideshow selected. Tap Apply wallpaper to start the rotation.");
        _wallpaperUpdateController.QueueAutoApply();
        return true;
    }

    public bool RemoveSlideshowItem(string id)
    {
        var removed = _customBackgroundManager.RemoveSlideshowItem(id);
        RefreshBackgroundProperties();
        if (removed)
        {
            _wallpaperUpdateController.StatusMessage = _localizationService.Translate(
                "Slideshow item removed. Apply the wallpaper to update the rotation.");
            _wallpaperUpdateController.QueueAutoApply();
        }
        return removed;
    }

    public void SetSlideshowIntervalMinutes(int minutes)
    {
        _customBackgroundManager.SetSlideshowIntervalMinutes(minutes);
        OnPropertyChanged(nameof(SlideshowIntervalMinutes));
        _wallpaperUpdateController.StatusMessage = _localizationService.Translate(
            "Slideshow interval saved. Apply the wallpaper to restart its timer.");
        _wallpaperUpdateController.QueueAutoApply();
    }

    public async Task<SettingsOperationResult> ChooseSlideshowImagesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var files = await _imagePickerService.PickMultipleImagesAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (files is null || files.Count == 0)
                return new SettingsOperationResult(SettingsOperationStatus.Cancelled);

            await _customBackgroundManager.AddSlideshowImagesAsync(files);
            var items = _customBackgroundManager.GetSlideshowItems();
            if (items.Count >= 2)
                _customBackgroundManager.SelectSlideshow();
            RefreshBackgroundProperties();

            _wallpaperUpdateController.StatusMessage = _localizationService.Translate(items.Count >= 2
                ? (_wallpaperUpdateController.IsAutoApplyEnabled
                    ? "Slideshow selected. The rotation will start when the wallpaper is applied."
                    : "Slideshow selected. Tap Apply wallpaper to start the rotation.")
                : "Photo added. Add at least one more background to start a slideshow.");

            if (items.Count >= 2)
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
                : ex.Message.Contains("maximum number", StringComparison.OrdinalIgnoreCase)
                    ? "The slideshow already has the maximum number of items."
                    : "The selected images could not be saved.";
            return new SettingsOperationResult(SettingsOperationStatus.Failed, _localizationService.Translate(message));
        }
    }

    private void RefreshBackgroundProperties()
    {
        OnPropertyChanged(nameof(BackgroundMode));
        OnPropertyChanged(nameof(HasCustomBackground));
        OnPropertyChanged(nameof(SelectedPresetKey));
        OnPropertyChanged(nameof(CurrentBackgroundPreviewPath));
        OnPropertyChanged(nameof(CurrentBackgroundPresetKey));
        OnPropertyChanged(nameof(BackgroundSummary));
        OnPropertyChanged(nameof(SlideshowItems));
        OnPropertyChanged(nameof(IsSlideshowSelected));
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
            RefreshBackgroundProperties();
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
        if (BackgroundMode == WallpaperBackgroundMode.Default) return false;
        _customBackgroundManager.ClearSingleImage();
        RefreshBackgroundProperties();
        _wallpaperUpdateController.StatusMessage = _localizationService.Translate(
            _wallpaperUpdateController.IsAutoApplyEnabled
                ? "Default background selected. It will be applied when Locksy moves to the background."
                : "Default background selected. Tap Apply wallpaper to update your lock screen.");
        _wallpaperUpdateController.QueueAutoApply();
        return true;
    }

    public Task OpenRepositoryAsync() => _externalNavigationService.OpenAsync(new Uri(RepositoryUrl));
}
