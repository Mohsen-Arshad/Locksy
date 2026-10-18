using Locksy.Models;

namespace Locksy.Services.Abstractions;

public interface IReminderSchedulingService
{
    void Synchronize(IEnumerable<TodoItem> tasks);
    bool CanUseExactAlarms();
}
