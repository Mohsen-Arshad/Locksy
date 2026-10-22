using System.Text.Json;
using Locksy.Models;
using Microsoft.Maui.Storage;
using MauiPreferences = Microsoft.Maui.Storage.Preferences;

namespace Locksy.Services;

/// <summary>
/// Persists background selections and private slideshow assets. The Android renderer resolves
/// this configuration at wallpaper-generation time, keeping image decoding out of the ViewModel.
/// </summary>
public static class CustomWallpaperBackgroundService
{
    private const string LegacyImagePathPreferenceKey = "locksy-custom-wallpaper-background-path";
    private const string ModePreferenceKey = "locksy-wallpaper-background-mode";
    private const string PresetPreferenceKey = "locksy-wallpaper-background-preset";
    private const string SlideshowItemsPreferenceKey = "locksy-wallpaper-slideshow-items-v1";
    private const string SlideshowIndexPreferenceKey = "locksy-wallpaper-slideshow-index-v1";
    private const string SlideshowIntervalPreferenceKey = "locksy-wallpaper-slideshow-interval-minutes-v1";
    private const long MaxImageBytes = 25L * 1024L * 1024L;
    private const int MaxSlideshowItems = 20;

    private static string StoredImagePath => Path.Combine(FileSystem.AppDataDirectory, "locksy-custom-background.img");
    private static string SlideshowDirectory => Path.Combine(FileSystem.AppDataDirectory, "wallpaper-slideshow");
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static WallpaperBackgroundMode GetMode()
    {
        var stored = MauiPreferences.Default.Get(ModePreferenceKey, string.Empty);
        if (Enum.TryParse<WallpaperBackgroundMode>(stored, true, out var mode) && Enum.IsDefined(mode))
            return mode;

        // Migrate installations that only had the original single-image preference.
        var legacyPath = MauiPreferences.Default.Get(LegacyImagePathPreferenceKey, string.Empty);
        return !string.IsNullOrWhiteSpace(legacyPath) && File.Exists(legacyPath)
            ? WallpaperBackgroundMode.CustomImage
            : WallpaperBackgroundMode.Default;
    }

    public static string GetSelectedPresetKey() =>
        WallpaperBackgroundCatalog.Find(MauiPreferences.Default.Get(PresetPreferenceKey, ""))?.Key ?? "ocean";

    public static int GetSlideshowIntervalMinutes()
    {
        var interval = MauiPreferences.Default.Get(SlideshowIntervalPreferenceKey, 5);
        return interval is 5 or 10 or 15 or 30 or 60 ? interval : 5;
    }

    public static bool IsSlideshowConfigured =>
        GetMode() == WallpaperBackgroundMode.Slideshow && GetSlideshowItems().Count >= 2;

    public static string? GetExistingPath()
    {
        var path = MauiPreferences.Default.Get(LegacyImagePathPreferenceKey, string.Empty);
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
    }

    public static string? GetCurrentPreviewImagePath() => GetSelectionForRendering().ImagePath;

    public static string? GetCurrentPreviewPresetKey()
    {
        var selection = GetSelectionForRendering();
        return selection.Mode == WallpaperBackgroundMode.Preset ? selection.PresetKey : null;
    }

    public static WallpaperBackgroundSelection GetSelectionForRendering()
    {
        var mode = GetMode();
        var selectedPresetKey = mode == WallpaperBackgroundMode.Preset ? GetSelectedPresetKey() : string.Empty;
        var items = mode == WallpaperBackgroundMode.Slideshow
            ? GetSlideshowItems()
            : Array.Empty<WallpaperSlideshowItem>();
        return ResolveSelectionForRendering(mode, selectedPresetKey, items, verifyImagePath: true);
    }

    /// <summary>
    /// Retrieves the fields used by the Settings background page from one preference/list read.
    /// In particular, a slideshow is deserialized and its files validated only once.
    /// </summary>
    public static WallpaperBackgroundSettingsSnapshot GetSettingsSnapshot()
    {
        var mode = GetMode();
        var selectedPresetKey = GetSelectedPresetKey();
        var interval = GetSlideshowIntervalMinutes();
        var items = GetSlideshowItems();
        var selection = ResolveSelectionForRendering(mode, selectedPresetKey, items, verifyImagePath: false);

        return new WallpaperBackgroundSettingsSnapshot(
            mode,
            selectedPresetKey,
            selection.ImagePath,
            selection.Mode == WallpaperBackgroundMode.Preset ? selection.PresetKey : null,
            interval,
            items);
    }

    private static WallpaperBackgroundSelection ResolveSelectionForRendering(
        WallpaperBackgroundMode mode,
        string selectedPresetKey,
        IReadOnlyList<WallpaperSlideshowItem> items,
        bool verifyImagePath)
    {
        switch (mode)
        {
            case WallpaperBackgroundMode.Preset:
                return new WallpaperBackgroundSelection(mode, selectedPresetKey, null, false);

            case WallpaperBackgroundMode.CustomImage:
            {
                var path = GetExistingPath();
                return path is null
                    ? new WallpaperBackgroundSelection(WallpaperBackgroundMode.Default, "", null, false)
                    : new WallpaperBackgroundSelection(mode, "", path, false);
            }

            case WallpaperBackgroundMode.Slideshow:
            {
                if (items.Count == 0)
                    return new WallpaperBackgroundSelection(WallpaperBackgroundMode.Default, "", null, false);

                var savedIndex = MauiPreferences.Default.Get(SlideshowIndexPreferenceKey, 0);
                var index = NormalizeIndex(savedIndex, items.Count);
                var item = items[index];
                if (string.Equals(item.Kind, "preset", StringComparison.OrdinalIgnoreCase) &&
                    WallpaperBackgroundCatalog.Find(item.Key) is not null)
                    return new WallpaperBackgroundSelection(WallpaperBackgroundMode.Preset, item.Key, null, true);

                // The settings snapshot is built off the UI thread and already receives validated
                // slideshow items. The normal wallpaper-application path retains its second check
                // to preserve its previous behavior if a file disappears between these operations.
                if (string.Equals(item.Kind, "image", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(item.ImagePath) &&
                    (!verifyImagePath || File.Exists(item.ImagePath)))
                    return new WallpaperBackgroundSelection(WallpaperBackgroundMode.CustomImage, "", item.ImagePath, true);

                return new WallpaperBackgroundSelection(WallpaperBackgroundMode.Default, "", null, true);
            }

            default:
                return new WallpaperBackgroundSelection(WallpaperBackgroundMode.Default, "", null, false);
        }
    }

    public static IReadOnlyList<WallpaperSlideshowItem> GetSlideshowItems()
    {
        try
        {
            var json = MauiPreferences.Default.Get(SlideshowItemsPreferenceKey, "[]");
            var items = JsonSerializer.Deserialize<List<WallpaperSlideshowItem>>(json, JsonOptions) ?? new();
            return items.Where(item =>
                    !string.IsNullOrWhiteSpace(item.Id) &&
                    (string.Equals(item.Kind, "preset", StringComparison.OrdinalIgnoreCase)
                        ? WallpaperBackgroundCatalog.Find(item.Key) is not null
                        : string.Equals(item.Kind, "image", StringComparison.OrdinalIgnoreCase) &&
                          !string.IsNullOrWhiteSpace(item.ImagePath) && File.Exists(item.ImagePath)))
                .ToArray();
        }
        catch
        {
            return Array.Empty<WallpaperSlideshowItem>();
        }
    }

    public static void SelectDefault()
    {
        MauiPreferences.Default.Set(ModePreferenceKey, WallpaperBackgroundMode.Default.ToString());
    }

    public static bool SelectPreset(string key)
    {
        var preset = WallpaperBackgroundCatalog.Find(key);
        if (preset is null) return false;
        MauiPreferences.Default.Set(PresetPreferenceKey, preset.Key);
        MauiPreferences.Default.Set(ModePreferenceKey, WallpaperBackgroundMode.Preset.ToString());
        return true;
    }

    public static bool SelectSlideshow()
    {
        if (GetSlideshowItems().Count < 2) return false;
        MauiPreferences.Default.Set(SlideshowIndexPreferenceKey, 0);
        MauiPreferences.Default.Set(ModePreferenceKey, WallpaperBackgroundMode.Slideshow.ToString());
        return true;
    }

    public static void SetSlideshowIntervalMinutes(int minutes)
    {
        if (minutes is not (5 or 10 or 15 or 30 or 60))
            throw new ArgumentOutOfRangeException(nameof(minutes), "Slideshow interval must be one of the supported values.");
        MauiPreferences.Default.Set(SlideshowIntervalPreferenceKey, minutes);
    }

    public static async Task SaveAsync(FileResult image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        Directory.CreateDirectory(FileSystem.AppDataDirectory);

        var previousPath = MauiPreferences.Default.Get(LegacyImagePathPreferenceKey, string.Empty);
        // Never overwrite the same path. Android/MAUI image loaders can cache by filename,
        // leaving the settings preview stuck on the first image after later selections.
        var destination = Path.Combine(
            FileSystem.AppDataDirectory,
            $"locksy-custom-background-{Guid.NewGuid():N}.img");

        try
        {
            await CopyImageAsync(image, destination, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // Switch the active path only after the complete image has been copied.
            MauiPreferences.Default.Set(LegacyImagePathPreferenceKey, destination);
            MauiPreferences.Default.Set(ModePreferenceKey, WallpaperBackgroundMode.CustomImage.ToString());

            if (!string.IsNullOrWhiteSpace(previousPath) &&
                IsManagedCustomBackgroundPath(previousPath))
            {
                TryDelete(previousPath);
            }

            // Clean up a legacy fixed-name image after migration to a unique path.
            TryDelete(StoredImagePath);
        }
        catch
        {
            TryDelete(destination);
            TryDelete(destination + ".tmp");
            throw;
        }
    }

    public static async Task<IReadOnlyList<WallpaperSlideshowItem>> AddSlideshowImagesAsync(
        IEnumerable<FileResult> images,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(images);
        var items = GetSlideshowItems().ToList();
        if (items.Count >= MaxSlideshowItems)
            throw new InvalidOperationException("The slideshow already has the maximum number of items.");

        Directory.CreateDirectory(SlideshowDirectory);
        var addedPaths = new List<string>();
        try
        {
            foreach (var image in images)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (items.Count >= MaxSlideshowItems) break;
                if (image is null) continue;

                var id = $"image:{Guid.NewGuid():N}";
                var destination = Path.Combine(SlideshowDirectory, $"{Guid.NewGuid():N}.img");
                await CopyImageAsync(image, destination, cancellationToken);
                addedPaths.Add(destination);
                var displayName = Path.GetFileNameWithoutExtension(image.FileName);
                if (string.IsNullOrWhiteSpace(displayName)) displayName = "Photo";
                items.Add(new WallpaperSlideshowItem(id, "image", string.Empty, destination, displayName));
            }

            SaveSlideshowItems(items);
            return GetSlideshowItems();
        }
        catch
        {
            foreach (var path in addedPaths) TryDelete(path);
            throw;
        }
    }

    public static bool AddPresetToSlideshow(string key)
    {
        var preset = WallpaperBackgroundCatalog.Find(key);
        if (preset is null) return false;
        var items = GetSlideshowItems().ToList();
        if (items.Any(item => item.Kind == "preset" && item.Key == preset.Key)) return true;
        if (items.Count >= MaxSlideshowItems) return false;
        items.Add(new WallpaperSlideshowItem($"preset:{preset.Key}", "preset", preset.Key, null, preset.Title));
        SaveSlideshowItems(items);
        return true;
    }

    public static bool RemoveSlideshowItem(string id)
    {
        var items = GetSlideshowItems().ToList();
        var target = items.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        if (target is null) return false;
        items.Remove(target);
        SaveSlideshowItems(items);

        if (!string.IsNullOrWhiteSpace(target.ImagePath) && IsUnderSlideshowDirectory(target.ImagePath))
            TryDelete(target.ImagePath);

        var nextCount = items.Count;
        if (GetMode() == WallpaperBackgroundMode.Slideshow && nextCount < 2)
            SelectDefault();
        else if (nextCount > 0)
            MauiPreferences.Default.Set(SlideshowIndexPreferenceKey,
                NormalizeIndex(MauiPreferences.Default.Get(SlideshowIndexPreferenceKey, 0), nextCount));
        else
            MauiPreferences.Default.Set(SlideshowIndexPreferenceKey, 0);
        return true;
    }

    public static void ClearSingleImage()
    {
        var existing = MauiPreferences.Default.Get(LegacyImagePathPreferenceKey, string.Empty);
        MauiPreferences.Default.Remove(LegacyImagePathPreferenceKey);
        // Clearing the image must also clear the saved mode; otherwise GetMode() would keep
        // returning CustomImage and the UI could continue to display the removed photo state.
        MauiPreferences.Default.Set(ModePreferenceKey, WallpaperBackgroundMode.Default.ToString());
        if (!string.IsNullOrWhiteSpace(existing) && IsManagedCustomBackgroundPath(existing))
            TryDelete(existing);
        TryDelete(StoredImagePath);
        TryDelete(StoredImagePath + ".tmp");
    }

    public static void AdvanceSlideshowIndex()
    {
        var count = GetSlideshowItems().Count;
        if (GetMode() != WallpaperBackgroundMode.Slideshow || count < 2) return;
        var current = NormalizeIndex(MauiPreferences.Default.Get(SlideshowIndexPreferenceKey, 0), count);
        MauiPreferences.Default.Set(SlideshowIndexPreferenceKey, (current + 1) % count);
    }

    private static int NormalizeIndex(int index, int count)
    {
        if (count <= 0) return 0;
        var normalized = index % count;
        return normalized < 0 ? normalized + count : normalized;
    }

    private static void SaveSlideshowItems(IReadOnlyList<WallpaperSlideshowItem> items) =>
        MauiPreferences.Default.Set(SlideshowItemsPreferenceKey, JsonSerializer.Serialize(items, JsonOptions));

    private static async Task CopyImageAsync(FileResult image, string destination, CancellationToken cancellationToken)
    {
        var temporary = destination.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ? destination : destination + ".tmp";
        try
        {
            await using var input = await image.OpenReadAsync();
            await using var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0) break;
                total += read;
                if (total > MaxImageBytes)
                    throw new InvalidOperationException("Please select an image smaller than 25 MB.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            if (total == 0)
                throw new InvalidOperationException("The selected image is empty or could not be read.");
            await output.FlushAsync(cancellationToken);
            output.Close();
            if (!string.Equals(temporary, destination, StringComparison.Ordinal))
                File.Move(temporary, destination, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    private static bool IsManagedCustomBackgroundPath(string path)
    {
        try
        {
            var root = Path.GetFullPath(FileSystem.AppDataDirectory) + Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(path);
            return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(candidate).StartsWith("locksy-custom-background", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool IsUnderSlideshowDirectory(string path)
    {
        try
        {
            var root = Path.GetFullPath(SlideshowDirectory) + Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(path);
            return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* Orphaned private images must never prevent Locksy from starting. */ }
    }
}
