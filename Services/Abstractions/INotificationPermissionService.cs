namespace Locksy.Services.Abstractions;

public interface INotificationPermissionService
{
    Task<bool> EnsurePermissionAsync(CancellationToken cancellationToken = default);
}
