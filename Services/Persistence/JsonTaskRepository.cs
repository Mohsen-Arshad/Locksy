using System.Text.Json;
using System.Threading;
using Microsoft.Maui.Storage;
using Locksy.Models;
using Locksy.Services.Abstractions;

namespace Locksy.Services.Persistence;

// Local JSON persistence. UI actions only snapshot the tiny task list; serialization
// and disk I/O run off the UI thread. Latest-write-wins prevents stale writes racing.
public sealed class JsonTaskRepository : ITaskRepository
{
    public static JsonTaskRepository Shared { get; } = new();

    private string FilePath => Path.Combine(FileSystem.AppDataDirectory, "locksy-tasks.json");
    private readonly SemaphoreSlim SaveGate = new(1, 1);
    private long _requestedSaveVersion;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public List<TodoItem> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<TodoItem>();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<TodoItem>>(json, JsonOptions) ?? new List<TodoItem>();
        }
        catch
        {
            // A damaged/missing local file should not prevent the UI from opening.
            return new List<TodoItem>();
        }
    }

    public async Task<List<TodoItem>> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(FilePath)) return new List<TodoItem>();

            var json = await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return JsonSerializer.Deserialize<List<TodoItem>>(json, JsonOptions) ?? new List<TodoItem>();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // A damaged/missing local file should not prevent the UI from opening.
            return new List<TodoItem>();
        }
    }

    public void Save(IEnumerable<TodoItem> tasks)
    {
        try
        {
            // Snapshot on the caller thread because the ObservableCollection and items
            // belong to the UI thread. The immutable snapshot can safely be serialized later.
            var snapshot = tasks.Select(task => new PersistedTask(
                task.Id, task.Title, task.IsCompleted, task.ColorKey, task.ColorPaletteKey,
                task.IsReminderEnabled, task.ReminderAt)).ToArray();
            var version = Interlocked.Increment(ref _requestedSaveVersion);
            _ = Task.Run(() => PersistLatestAsync(snapshot, version));
        }
        catch
        {
            // Persistence must never block user interaction or take the app down.
        }
    }

    private async Task PersistLatestAsync(PersistedTask[] snapshot, long version)
    {
        var lockTaken = false;
        string? temporaryPath = null;
        try
        {
            // Skip work that is already obsolete before the relatively expensive serialization.
            if (version != Volatile.Read(ref _requestedSaveVersion)) return;

            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            if (version != Volatile.Read(ref _requestedSaveVersion)) return;

            await SaveGate.WaitAsync().ConfigureAwait(false);
            lockTaken = true;

            // If any newer edit was queued while this one was waiting, skip the stale write.
            if (version != Volatile.Read(ref _requestedSaveVersion)) return;

            var path = FilePath;
            temporaryPath = $"{path}.{version}.tmp";
            await File.WriteAllTextAsync(temporaryPath, json).ConfigureAwait(false);

            if (version != Volatile.Read(ref _requestedSaveVersion))
            {
                TryDelete(temporaryPath);
                return;
            }

            // Atomic replace prevents a partially-written JSON file if the app closes mid-save.
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            // Keep UI interaction available if storage is unavailable.
            if (temporaryPath is not null) TryDelete(temporaryPath);
        }
        finally
        {
            if (lockTaken) SaveGate.Release();
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    // Property names intentionally match TodoItem so existing JSON files remain compatible.
    private sealed record PersistedTask(
        Guid Id, string Title, bool IsCompleted, string ColorKey, string ColorPaletteKey,
        bool IsReminderEnabled, DateTime? ReminderAt);
}
