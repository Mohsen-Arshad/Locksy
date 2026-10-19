namespace Locksy.Models;

/// <summary>A persisted slideshow frame. Image paths are app-private; preset keys refer to the built-in catalog.</summary>
public sealed record WallpaperSlideshowItem(
    string Id,
    string Kind,
    string Key,
    string? ImagePath,
    string DisplayName);
