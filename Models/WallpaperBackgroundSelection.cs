namespace Locksy.Models;

/// <summary>Resolved, render-ready background selection.</summary>
public sealed record WallpaperBackgroundSelection(
    WallpaperBackgroundMode Mode,
    string PresetKey,
    string? ImagePath,
    bool IsSlideshowFrame);
