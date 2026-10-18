using Locksy.Models;
using Locksy.Platforms.Android;
using Locksy.Services.Abstractions;

namespace Locksy.Services;

public sealed class ReminderSchedulingService : IReminderSchedulingService
{
    public void Synchronize(IEnumerable<TodoItem> tasks) => ReminderScheduler.SyncTasks(tasks);

    public bool CanUseExactAlarms() => ReminderScheduler.CanUseExactAlarms();
}
