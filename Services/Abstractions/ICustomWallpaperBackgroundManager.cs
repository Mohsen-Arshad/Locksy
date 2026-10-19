using Locksy.Models;
using Microsoft.Maui.Storage;

namespace Locksy.Services.Abstractions;

public interface ICustomWallpaperBackgroundManager
{
    WallpaperBackgroundMode GetMode();
    string GetSelectedPresetKey();
    string? GetCurrentPreviewImagePath();
    string? GetCurrentPreviewPresetKey();
    IReadOnlyList<WallpaperSlideshowItem> GetSlideshowItems();
    int GetSlideshowIntervalMinutes();
    WallpaperBackgroundSettingsSnapshot GetSettingsSnapshot();
    Task SaveAsync(FileResult file);
    Task<IReadOnlyList<WallpaperSlideshowItem>> AddSlideshowImagesAsync(IEnumerable<FileResult> images);
    void SelectDefault();
    bool SelectPreset(string key);
    bool AddPresetToSlideshow(string key);
    bool SelectSlideshow();
    bool RemoveSlideshowItem(string id);
    void SetSlideshowIntervalMinutes(int minutes);
    void ClearSingleImage();
}
