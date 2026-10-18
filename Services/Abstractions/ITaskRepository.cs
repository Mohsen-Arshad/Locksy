using Locksy.Models;

namespace Locksy.Services.Abstractions;

/// <summary>Owns task persistence; view models depend on this contract, not file I/O.</summary>
public interface ITaskRepository
{
    List<TodoItem> Load();
    Task<List<TodoItem>> LoadAsync(CancellationToken cancellationToken = default);
    void Save(IEnumerable<TodoItem> tasks);
}
