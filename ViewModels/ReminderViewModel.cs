using Locksy.Services.Abstractions;
using Locksy.ViewModels.Abstractions;

namespace Locksy.ViewModels;

/// <summary>Coordinates reminder permissions and scheduling without exposing platform APIs to the page.</summary>
public sealed class ReminderViewModel : ObservableObject, IDisposable
{
    private readonly ITaskListSource _taskSource;
    private readonly INotificationPermissionService _notificationPermissionService;
    private readonly IReminderSchedulingService _reminderSchedulingService;
    private bool _disposed;

    public ReminderViewModel(
        ITaskListSource taskSource,
        INotificationPermissionService notificationPermissionService,
        IReminderSchedulingService reminderSchedulingService)
    {
        _taskSource = taskSource ?? throw new ArgumentNullException(nameof(taskSource));
        _notificationPermissionService = notificationPermissionService ?? throw new ArgumentNullException(nameof(notificationPermissionService));
        _reminderSchedulingService = reminderSchedulingService ?? throw new ArgumentNullException(nameof(reminderSchedulingService));
        _taskSource.TasksChanged += OnTasksChanged;
        Synchronize();
    }

    public Task<bool> EnsureNotificationPermissionAsync(CancellationToken cancellationToken = default) =>
        _notificationPermissionService.EnsurePermissionAsync(cancellationToken);

    public bool CanUseExactAlarms() => _reminderSchedulingService.CanUseExactAlarms();

    public void Synchronize() => _reminderSchedulingService.Synchronize(_taskSource.GetTaskModels());

    private void OnTasksChanged(object? sender, EventArgs e) => Synchronize();

    public void Dispose()
    {
        if (_disposed) return;
        _taskSource.TasksChanged -= OnTasksChanged;
        _disposed = true;
    }
}
