namespace Locksy.Services.Abstractions;

public interface IExternalNavigationService
{
    Task OpenAsync(Uri uri);
}
