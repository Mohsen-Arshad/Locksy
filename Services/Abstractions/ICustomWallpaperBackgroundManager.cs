using Microsoft.Maui.Storage;

namespace Locksy.Services.Abstractions;

public interface ICustomWallpaperBackgroundManager
{
    string? GetExistingPath();
    Task SaveAsync(FileResult file);
    void Clear();
}
