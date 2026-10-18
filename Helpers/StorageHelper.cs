using Locksy.Models;
using Locksy.Services.Persistence;

namespace Locksy.Helpers;

/// <summary>
/// Backward-compatible startup adapter. New application code should inject ITaskRepository.
/// </summary>
public static class StorageHelper
{
    public static List<TodoItem> Load() => JsonTaskRepository.Shared.Load();
    public static Task<List<TodoItem>> LoadAsync(CancellationToken cancellationToken = default) =>
        JsonTaskRepository.Shared.LoadAsync(cancellationToken);
    public static void Save(IEnumerable<TodoItem> tasks) => JsonTaskRepository.Shared.Save(tasks);
}
