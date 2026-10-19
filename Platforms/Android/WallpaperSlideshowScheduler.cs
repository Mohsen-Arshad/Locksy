using Android.App;
using Android.Content;
using Android.OS;
using Locksy.Models;
using Locksy.Services;
using Locksy.Services.Persistence;
using Locksy.Services.Abstractions;
using Microsoft.Maui.Storage;
using System.Runtime.Versioning;

namespace Locksy.Platforms.Android;

/// <summary>Schedules best-effort periodic lock-screen regeneration for a selected wallpaper slideshow.</summary>
internal static class WallpaperSlideshowAlarm
{
    internal const string Action = "com.mohsen.locksy.ACTION_WALLPAPER_SLIDESHOW_TICK";
    private const int RequestCode = 7316;

    public static void Cancel(Context? suppliedContext = null)
    {
        try
        {
            var context = suppliedContext ?? global::Android.App.Application.Context;
            if (context is null) return;
            var alarmManager = context.GetSystemService(Context.AlarmService) as AlarmManager;
            if (alarmManager is null) return;
            var pendingIntent = CreatePendingIntent(context);
            alarmManager.Cancel(pendingIntent);
            pendingIntent.Cancel();
        }
        catch
        {
            // Background scheduling is best-effort; wallpaper selection remains usable if unavailable.
        }
    }

    public static void SyncAfterWallpaperApplied(Context? suppliedContext = null)
    {
        try
        {
            var context = suppliedContext ?? global::Android.App.Application.Context;
            if (context is null) return;
            if (!CustomWallpaperBackgroundService.IsSlideshowConfigured)
            {
                Cancel(context);
                return;
            }

            var alarmManager = context.GetSystemService(Context.AlarmService) as AlarmManager;
            if (alarmManager is null) return;
            var pendingIntent = CreatePendingIntent(context);
            alarmManager.Cancel(pendingIntent);
            var delayMillis = (long)CustomWallpaperBackgroundService.GetSlideshowIntervalMinutes() * 60_000L;
            var triggerAtMillis = Java.Lang.JavaSystem.CurrentTimeMillis() + delayMillis;
            if (OperatingSystem.IsAndroidVersionAtLeast(23))
                SetAllowWhileIdle(alarmManager, triggerAtMillis, pendingIntent);
            else
                alarmManager.Set(AlarmType.RtcWakeup, triggerAtMillis, pendingIntent);
        }
        catch
        {
            // A scheduling failure must not turn a successfully applied wallpaper into an error.
        }
    }

    private static PendingIntent CreatePendingIntent(Context context)
    {
        var intent = new Intent(context, typeof(WallpaperSlideshowReceiver));
        intent.SetAction(Action);
        return PendingIntent.GetBroadcast(context, RequestCode, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    [SupportedOSPlatform("android23.0")]
    private static void SetAllowWhileIdle(AlarmManager alarmManager, long triggerAtMillis, PendingIntent pendingIntent) =>
        alarmManager.SetAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAtMillis, pendingIntent);
}

[BroadcastReceiver(Enabled = true, Exported = false)]
[IntentFilter(new[] { WallpaperSlideshowAlarm.Action })]
public sealed class WallpaperSlideshowReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != WallpaperSlideshowAlarm.Action)
            return;

        var pendingResult = GoAsync();
        if (pendingResult is null)
            return;

        _ = ApplyNextSlideAsync(context.ApplicationContext ?? context, pendingResult);
    }

    private static async Task ApplyNextSlideAsync(Context context, BroadcastReceiver.PendingResult pendingResult)
    {
        try
        {
            if (!CustomWallpaperBackgroundService.IsSlideshowConfigured)
            {
                WallpaperSlideshowAlarm.Cancel(context);
                return;
            }

            CustomWallpaperBackgroundService.AdvanceSlideshowIndex();
            var tasks = await JsonTaskRepository.Shared.LoadAsync().ConfigureAwait(false);
            var prefs = Preferences.Default;
            var layoutIndex = Math.Clamp(prefs.Get("locksy-wallpaper-layout-v2", (int)WallpaperLayout.ScreenAware), 0, 5);
            var verticalOffset = Math.Clamp(prefs.Get("locksy-wallpaper-vertical-offset-v2", 0f), -0.20f, 0.20f);
            var clockClearance = Math.Clamp(prefs.Get("locksy-wallpaper-clock-clearance-v2", 0.18f), 0.08f, 0.30f);
            var layout = new WallpaperLayoutSettings
            {
                Layout = (WallpaperLayout)layoutIndex,
                VerticalOffset = verticalOffset,
                ClockClearance = clockClearance
            };

            var savedTheme = ThemeService.GetSavedPreference();
            var isSystemDark = false;
            var configuration = context.Resources?.Configuration;
            if (configuration is not null)
            {
                isSystemDark = (configuration.UiMode & global::Android.Content.Res.UiMode.NightMask)
                    == global::Android.Content.Res.UiMode.NightYes;
            }

            var darkMode = savedTheme switch
            {
                ThemePreference.Dark => true,
                ThemePreference.Light => false,
                _ => isSystemDark
            };

            var service = new AndroidWallpaperService();
            await service.ApplyLockScreenAsync(tasks, darkMode, layout).ConfigureAwait(false);
        }
        catch
        {
            // The next explicit app open or wallpaper apply can recover from any transient OEM error.
            WallpaperSlideshowAlarm.SyncAfterWallpaperApplied(context);
        }
        finally
        {
            pendingResult.Finish();
        }
    }
}

/// <summary>DI adapter keeps settings workflows independent of the native AlarmManager implementation.</summary>
public sealed class AndroidWallpaperSlideshowScheduler : IWallpaperSlideshowScheduler
{
    public void CancelScheduledRotation() => WallpaperSlideshowAlarm.Cancel();
}
