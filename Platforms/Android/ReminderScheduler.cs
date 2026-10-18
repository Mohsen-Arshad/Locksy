using Android.App;
using Android.Content;
using Android.OS;
using Locksy.Models;
using System.Text.Json;
using System.Runtime.Versioning;

namespace Locksy.Platforms.Android;

/// <summary>Schedules one-shot reminders for task items and keeps pending alarms in sync.</summary>
internal static class ReminderScheduler
{
    internal const string NotificationChannelId = "locksy_task_reminders";
    private const string PreferencesFile = "locksy_reminder_state";
    private const string ScheduledIdsKey = "scheduled_task_ids";
    private const string ReminderAction = "com.mohsen.locksy.ACTION_TASK_REMINDER";
    private const string ExtraTaskId = "task_id";
    private const string ExtraTaskTitle = "task_title";

    public static void SyncTasks(IEnumerable<TodoItem> tasks)
    {
        var context = global::Android.App.Application.Context;
        if (context is null) return;
        SyncTasks(tasks, context);
    }

    internal static void SyncTasks(IEnumerable<TodoItem> tasks, Context context)
    {
        try
        {
            var taskList = tasks.ToList();
            var activeTasks = taskList
                .Where(task => task.Id != Guid.Empty && task.IsReminderEnabled &&
                               !task.IsCompleted && task.ReminderAt.HasValue &&
                               task.ReminderAt.Value > DateTime.Now &&
                               !string.IsNullOrWhiteSpace(task.Title))
                .ToDictionary(task => task.Id, task => task);

            var sharedPreferences = context.GetSharedPreferences(PreferencesFile, FileCreationMode.Private);
            var previousIds = (sharedPreferences?.GetString(ScheduledIdsKey, string.Empty) ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => Guid.TryParseExact(value, "N", out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToHashSet();

            var alarmManager = context.GetSystemService(Context.AlarmService) as AlarmManager;
            if (alarmManager is null) return;

            foreach (var removedId in previousIds.Except(activeTasks.Keys).ToArray())
                CancelAlarm(alarmManager, context, removedId);

            foreach (var task in activeTasks.Values)
                ScheduleAlarm(alarmManager, context, task);

            sharedPreferences?.Edit()?
                .PutString(ScheduledIdsKey, string.Join(";", activeTasks.Keys.Select(id => id.ToString("N"))))?
                .Apply();
        }
        catch
        {
            // A reminder scheduling problem must never crash the task-list UI.
        }
    }

    public static bool CanUseExactAlarms()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            try
            {
                var context = global::Android.App.Application.Context;
                var manager = context.GetSystemService(Context.AlarmService) as AlarmManager;
                return manager is not null && CanScheduleExactAlarmsOnAndroidS(manager);
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    [SupportedOSPlatform("android31.0")]
    private static bool CanScheduleExactAlarmsOnAndroidS(AlarmManager manager) =>
        manager.CanScheduleExactAlarms();

    internal static void RescheduleFromStorage(Context context)
    {
        try
        {
            var dataDirectory = context.FilesDir?.AbsolutePath;
            if (string.IsNullOrWhiteSpace(dataDirectory)) return;
            var filePath = Path.Combine(dataDirectory, "locksy-tasks.json");
            if (!File.Exists(filePath))
            {
                SyncTasks(Array.Empty<TodoItem>(), context);
                return;
            }

            var json = File.ReadAllText(filePath);
            var tasks = JsonSerializer.Deserialize<List<TodoItem>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<TodoItem>();
            SyncTasks(tasks, context);
        }
        catch
        {
            // Boot-time restoration is best-effort; Locksy can resync reminders next time it opens.
        }
    }

    internal static void ShowReminderNotification(Context context, string? taskIdText, string? taskTitle)
    {
        try
        {
            var manager = context.GetSystemService(Context.NotificationService) as NotificationManager;
            if (manager is null) return;

            var title = string.IsNullOrWhiteSpace(taskTitle) ? "Task reminder" : taskTitle;
            var notificationId = GetNotificationId(taskIdText);

            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                ShowModernNotification(context, manager, notificationId, title);
                return;
            }

            ShowLegacyNotification(context, manager, notificationId, title);
        }
        catch (Java.Lang.SecurityException)
        {
            // Notifications may be disabled by the user or by the Android vendor UI.
        }
        catch
        {
            // Notification delivery is isolated from alarm receiver lifecycle.
        }
    }

    [SupportedOSPlatform("android26.0")]
    private static void ShowModernNotification(
        Context context,
        NotificationManager manager,
        int notificationId,
        string title)
    {
        var channel = new NotificationChannel(
            NotificationChannelId,
            "Task reminders",
            NotificationImportance.High)
        {
            Description = "Alerts scheduled for Locksy tasks"
        };
        manager.CreateNotificationChannel(channel);

        // Channel importance replaces the legacy per-notification priority setting.
        var builder = new Notification.Builder(context, NotificationChannelId);
        builder.SetSmallIcon(global::Locksy.Resource.Drawable.locksy_icon_foreground);
        builder.SetContentTitle("Locksy reminder");
        builder.SetContentText(title);
        builder.SetAutoCancel(true);
        builder.SetCategory(Notification.CategoryAlarm);
        manager.Notify(notificationId, builder.Build());
    }

    [UnsupportedOSPlatform("android26.0")]
    private static void ShowLegacyNotification(
        Context context,
        NotificationManager manager,
        int notificationId,
        string title)
    {
        var builder = new Notification.Builder(context);
        builder.SetSmallIcon(global::Locksy.Resource.Drawable.locksy_icon_foreground);
        builder.SetContentTitle("Locksy reminder");
        builder.SetContentText(title);
        builder.SetAutoCancel(true);
        builder.SetCategory(Notification.CategoryAlarm);
        builder.SetPriority((int)NotificationPriority.High);
        manager.Notify(notificationId, builder.Build());
    }

    private static void ScheduleAlarm(AlarmManager manager, Context context, TodoItem task)
    {
        if (task.ReminderAt is not { } when) return;
        var triggerMillis = new DateTimeOffset(DateTime.SpecifyKind(when, DateTimeKind.Local))
            .ToUnixTimeMilliseconds();
        if (triggerMillis <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) return;

        using var intent = CreateReminderIntent(context, task.Id, task.Title);
        using var pendingIntent = PendingIntent.GetBroadcast(
            context,
            0,
            intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        if (pendingIntent is null) return;

        try
        {
            if (CanUseExactAlarms())
                manager.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, triggerMillis, pendingIntent);
            else
                manager.SetAndAllowWhileIdle(AlarmType.RtcWakeup, triggerMillis, pendingIntent);
        }
        catch (Java.Lang.SecurityException)
        {
            // Android 12+ may deny exact alarms. Keep the reminder as an inexact alarm instead.
            manager.SetAndAllowWhileIdle(AlarmType.RtcWakeup, triggerMillis, pendingIntent);
        }
    }

    private static void CancelAlarm(AlarmManager manager, Context context, Guid taskId)
    {
        using var intent = CreateReminderIntent(context, taskId, null);
        using var pendingIntent = PendingIntent.GetBroadcast(
            context,
            0,
            intent,
            PendingIntentFlags.NoCreate | PendingIntentFlags.Immutable);
        if (pendingIntent is null) return;
        manager.Cancel(pendingIntent);
        pendingIntent.Cancel();
    }

    private static Intent CreateReminderIntent(Context context, Guid taskId, string? title)
    {
        var intent = new Intent(context, typeof(ReminderAlarmReceiver));
        intent.SetAction(ReminderAction);
        intent.SetData(global::Android.Net.Uri.Parse($"locksy://task-reminder/{taskId:N}"));
        intent.PutExtra(ExtraTaskId, taskId.ToString("N"));
        if (!string.IsNullOrWhiteSpace(title)) intent.PutExtra(ExtraTaskTitle, title);
        return intent;
    }

    private static int GetNotificationId(string? taskIdText)
    {
        if (Guid.TryParseExact(taskIdText, "N", out var id))
        {
            var hash = id.GetHashCode() & 0x7FFFFFFF;
            return hash == 0 ? 1 : hash;
        }
        return 1;
    }

    internal static string GetReminderAction() => ReminderAction;
    internal static string GetTaskIdExtra() => ExtraTaskId;
    internal static string GetTaskTitleExtra() => ExtraTaskTitle;
}
