namespace Locksy.ViewModels.Abstractions;

/// <summary>Small peer contract used by settings to request wallpaper refreshes without coupling to a concrete view model.</summary>
public interface IWallpaperUpdateController
{
    bool IsAutoApplyEnabled { get; }
    string StatusMessage { get; set; }
    void QueueAutoApply();
}
