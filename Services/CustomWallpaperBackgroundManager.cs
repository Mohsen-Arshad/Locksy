using Locksy.Models;
using Locksy.Services.Abstractions;
using Microsoft.Maui.Storage;

namespace Locksy.Services;

/// <summary>Application-facing adapter for background selection and slideshow persistence.</summary>
public sealed class CustomWallpaperBackgroundManager : ICustomWallpaperBackgroundManager
{
    private readonly IWallpaperSlideshowScheduler _slideshowScheduler;

    public CustomWallpaperBackgroundManager(IWallpaperSlideshowScheduler slideshowScheduler) =>
        _slideshowScheduler = slideshowScheduler ?? throw new ArgumentNullException(nameof(slideshowScheduler));

    public WallpaperBackgroundMode GetMode() => CustomWallpaperBackgroundService.GetMode();
    public string GetSelectedPresetKey() => CustomWallpaperBackgroundService.GetSelectedPresetKey();
    public string? GetCurrentPreviewImagePath() => CustomWallpaperBackgroundService.GetCurrentPreviewImagePath();
    public string? GetCurrentPreviewPresetKey() => CustomWallpaperBackgroundService.GetCurrentPreviewPresetKey();
    public IReadOnlyList<WallpaperSlideshowItem> GetSlideshowItems() => CustomWallpaperBackgroundService.GetSlideshowItems();
    public int GetSlideshowIntervalMinutes() => CustomWallpaperBackgroundService.GetSlideshowIntervalMinutes();

    public async Task SaveAsync(FileResult file)
    {
        await CustomWallpaperBackgroundService.SaveAsync(file);
        _slideshowScheduler.CancelScheduledRotation();
    }

    public async Task<IReadOnlyList<WallpaperSlideshowItem>> AddSlideshowImagesAsync(IEnumerable<FileResult> images)
    {
        var items = await CustomWallpaperBackgroundService.AddSlideshowImagesAsync(images);
        _slideshowScheduler.CancelScheduledRotation();
        return items;
    }

    public void SelectDefault()
    {
        CustomWallpaperBackgroundService.SelectDefault();
        _slideshowScheduler.CancelScheduledRotation();
    }

    public bool SelectPreset(string key)
    {
        var selected = CustomWallpaperBackgroundService.SelectPreset(key);
        if (selected) _slideshowScheduler.CancelScheduledRotation();
        return selected;
    }

    public bool AddPresetToSlideshow(string key)
    {
        var added = CustomWallpaperBackgroundService.AddPresetToSlideshow(key);
        if (added) _slideshowScheduler.CancelScheduledRotation();
        return added;
    }

    public bool SelectSlideshow()
    {
        var selected = CustomWallpaperBackgroundService.SelectSlideshow();
        if (selected) _slideshowScheduler.CancelScheduledRotation();
        return selected;
    }

    public bool RemoveSlideshowItem(string id)
    {
        var removed = CustomWallpaperBackgroundService.RemoveSlideshowItem(id);
        if (removed) _slideshowScheduler.CancelScheduledRotation();
        return removed;
    }

    public void SetSlideshowIntervalMinutes(int minutes)
    {
        CustomWallpaperBackgroundService.SetSlideshowIntervalMinutes(minutes);
        _slideshowScheduler.CancelScheduledRotation();
    }

    public void ClearSingleImage()
    {
        CustomWallpaperBackgroundService.ClearSingleImage();
        CustomWallpaperBackgroundService.SelectDefault();
        _slideshowScheduler.CancelScheduledRotation();
    }
}
