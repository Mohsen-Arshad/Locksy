namespace Locksy.Models;

/// <summary>Which background source Locksy uses when composing the lock-screen wallpaper.</summary>
public enum WallpaperBackgroundMode
{
    Default = 0,
    Preset = 1,
    CustomImage = 2,
    Slideshow = 3
}
