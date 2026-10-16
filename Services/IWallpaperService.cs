using Locksy.Models;

namespace Locksy.Services;

public interface IWallpaperService
{
    Task<byte[]> ApplyLockScreenAsync(
        IReadOnlyList<TodoItem> todos,
        bool darkMode,
        WallpaperLayoutSettings settings,
        CancellationToken cancellationToken = default);
}
