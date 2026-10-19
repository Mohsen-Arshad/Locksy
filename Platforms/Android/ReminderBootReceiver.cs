using Android.App;
using Android.Content;

namespace Locksy.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter(new[] { Intent.ActionBootCompleted })]
public sealed class ReminderBootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != Intent.ActionBootCompleted) return;
        ReminderScheduler.RescheduleFromStorage(context);
        WallpaperSlideshowAlarm.SyncAfterWallpaperApplied(context);
    }
}
