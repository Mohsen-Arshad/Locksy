using Locksy.Services.Abstractions;
using Microsoft.Maui.ApplicationModel;

namespace Locksy.Services;

public sealed class ExternalNavigationService : IExternalNavigationService
{
    public Task OpenAsync(Uri uri) => Launcher.Default.OpenAsync(uri);
}
