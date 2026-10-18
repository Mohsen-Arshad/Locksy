using Locksy.Models;

namespace Locksy.ViewModels.Abstractions;

/// <summary>Read-only task source contract consumed by independent presentation workflows.</summary>
public interface ITaskListSource
{
    event EventHandler? TasksChanged;
    IReadOnlyList<TodoItem> GetTaskModels();
}
