using Locksy.Models;
using Locksy.Services;

namespace Locksy.Services.Abstractions;

public interface IWallpaperService
{
    Task<byte[]> ApplyLockScreenAsync(
        IReadOnlyList<TodoItem> todos,
        bool darkMode,
        WallpaperLayoutSettings settings,
        CancellationToken cancellationToken = default);
}
