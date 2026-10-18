using Locksy.Services.Abstractions;
using Microsoft.Maui.Storage;

namespace Locksy.Services;

/// <summary>Adapter over the existing local wallpaper-background storage implementation.</summary>
public sealed class CustomWallpaperBackgroundManager : ICustomWallpaperBackgroundManager
{
    public string? GetExistingPath() => CustomWallpaperBackgroundService.GetExistingPath();
    public Task SaveAsync(FileResult file) => CustomWallpaperBackgroundService.SaveAsync(file);
    public void Clear() => CustomWallpaperBackgroundService.Clear();
}
