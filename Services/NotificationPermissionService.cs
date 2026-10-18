using Locksy.Services.Abstractions;
using Microsoft.Maui.ApplicationModel;

namespace Locksy.Services;

public sealed class NotificationPermissionService : INotificationPermissionService
{
    public async Task<bool> EnsurePermissionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
#if ANDROID
        var result = await Permissions.RequestAsync<Permissions.PostNotifications>();
        cancellationToken.ThrowIfCancellationRequested();
        return result == PermissionStatus.Granted;
#else
        return true;
#endif
    }
}
