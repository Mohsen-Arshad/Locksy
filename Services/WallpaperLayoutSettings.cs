namespace Locksy.Services;

/// <summary>Per-device tuning for the generated lock-screen wallpaper.</summary>
public sealed record WallpaperLayoutSettings
{
    public WallpaperLayout Layout { get; init; } = WallpaperLayout.ScreenAware;

    /// <summary>Vertical translation as a fraction of image height; -0.20 moves up and +0.20 moves down.</summary>
    public float VerticalOffset { get; init; }

    /// <summary>Height fraction reserved around the system clock when using the split layout.</summary>
    public float ClockClearance { get; init; } = 0.18f;
}
