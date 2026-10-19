using Microsoft.Maui.Storage;

namespace Locksy.Services.Abstractions;

public interface IImagePickerService
{
    Task<FileResult?> PickImageAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FileResult>?> PickMultipleImagesAsync(CancellationToken cancellationToken = default);
}
