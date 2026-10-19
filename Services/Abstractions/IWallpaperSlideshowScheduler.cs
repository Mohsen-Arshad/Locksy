namespace Locksy.Services.Abstractions;

/// <summary>Allows background-selection workflows to invalidate a scheduled rotation without depending on Android alarm APIs.</summary>
public interface IWallpaperSlideshowScheduler
{
    void CancelScheduledRotation();
}
