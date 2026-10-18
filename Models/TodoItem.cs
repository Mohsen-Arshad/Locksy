using System.Text.Json.Serialization;

namespace Locksy.Models;

/// <summary>
/// Persistent task data. It contains no MAUI controls, colors, localization, or view commands.
/// Presentation-only properties belong to TaskItemViewModel.
/// </summary>
public sealed class TodoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public string ColorKey { get; set; } = "none";
    public string ColorPaletteKey { get; set; } = "vibrant";
    public bool IsReminderEnabled { get; set; }
    public DateTime? ReminderAt { get; set; }

    /// <summary>Domain predicate used by notification scheduling and wallpaper rendering.</summary>
    [JsonIgnore]
    public bool IsReminderActive =>
        !IsCompleted && IsReminderEnabled && ReminderAt.HasValue && ReminderAt.Value > DateTime.Now;
}
