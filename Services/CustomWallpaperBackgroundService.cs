using Microsoft.Maui.Storage;
using MauiPreferences = Microsoft.Maui.Storage.Preferences;


namespace Locksy.Services;

/// <summary>Persists a user-selected lock-screen background in app-private storage.</summary>
public static class CustomWallpaperBackgroundService
{
    private const string PreferenceKey = "locksy-custom-wallpaper-background-path";
    private const long MaxImageBytes = 25L * 1024L * 1024L;
    private static string StoredImagePath => Path.Combine(FileSystem.AppDataDirectory, "locksy-custom-background.img");

    public static string? GetExistingPath()
    {
        var path = MauiPreferences.Default.Get(PreferenceKey, string.Empty);
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
    }

    public static async Task SaveAsync(FileResult image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        var destination = StoredImagePath;
        var temporary = destination + ".tmp";

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
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        File.Move(temporary, destination, overwrite: true);
        MauiPreferences.Default.Set(PreferenceKey, destination);
    }

    public static void Clear()
    {
        var existing = MauiPreferences.Default.Get(PreferenceKey, string.Empty);
        MauiPreferences.Default.Remove(PreferenceKey);
        if (!string.IsNullOrWhiteSpace(existing)) TryDelete(existing);
        TryDelete(StoredImagePath);
        TryDelete(StoredImagePath + ".tmp");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* A stale private image must not prevent the user from using Locksy. */ }
    }
}
