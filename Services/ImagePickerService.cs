using Locksy.Services.Abstractions;
using Microsoft.Maui.Storage;

namespace Locksy.Services;

public sealed class ImagePickerService : IImagePickerService
{
    public Task<FileResult?> PickImageAsync(CancellationToken cancellationToken = default) =>
        FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = LocalizationService.Translate("Select a background image"),
            FileTypes = FilePickerFileType.Images
        });
}
