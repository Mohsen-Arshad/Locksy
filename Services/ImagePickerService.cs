using Locksy.Services.Abstractions;
using Microsoft.Maui.Storage;

namespace Locksy.Services;

public sealed class ImagePickerService : IImagePickerService
{
    private static PickOptions ImageOptions(string title) => new()
    {
        PickerTitle = LocalizationService.Translate(title),
        FileTypes = FilePickerFileType.Images
    };

    public Task<FileResult?> PickImageAsync(CancellationToken cancellationToken = default) =>
        FilePicker.Default.PickAsync(ImageOptions("Select a background image"));

    public async Task<IReadOnlyList<FileResult>?> PickMultipleImagesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var results = await FilePicker.Default.PickMultipleAsync(ImageOptions("Select slideshow images"));
        cancellationToken.ThrowIfCancellationRequested();
        return results?.ToArray();
    }
}
