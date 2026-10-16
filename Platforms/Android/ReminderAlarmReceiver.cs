using Android.App;
using Android.Content;

namespace Locksy.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class ReminderAlarmReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null || intent.Action != ReminderScheduler.GetReminderAction())
            return;

        var taskId = intent.GetStringExtra(ReminderScheduler.GetTaskIdExtra());
        var taskTitle = intent.GetStringExtra(ReminderScheduler.GetTaskTitleExtra());
        ReminderScheduler.ShowReminderNotification(context, taskId, taskTitle);
    }
}
