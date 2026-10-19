namespace Locksy.Models;

/// <summary>
/// Describes a built-in wallpaper background preset.
/// This is a shared data model consumed by settings UI and wallpaper rendering.
/// </summary>
public sealed record WallpaperBackgroundPreset(
    string Key,
    string Title,
    string Subtitle,
    string TopColor,
    string BottomColor,
    string AccentColor,
    string SecondaryAccentColor);
