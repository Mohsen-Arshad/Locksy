namespace Locksy.Models;

/// <summary>
/// One consistent, render-ready read of the wallpaper settings page. Keeping the selected
/// frame and slideshow items together avoids repeatedly reading Preferences and deserializing
/// slideshow state while constructing the UI.
/// </summary>
public sealed record WallpaperBackgroundSettingsSnapshot(
    WallpaperBackgroundMode Mode,
    string SelectedPresetKey,
    string? CurrentPreviewImagePath,
    string? CurrentPreviewPresetKey,
    int SlideshowIntervalMinutes,
    IReadOnlyList<WallpaperSlideshowItem> SlideshowItems);
