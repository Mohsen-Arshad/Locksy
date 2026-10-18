using Locksy.Models;
using Locksy.Services;

namespace Locksy.ViewModels;

/// <summary>Transient draft state for configuring the next task before it is created.</summary>
public sealed class NewTaskSettingsViewModel : ObservableObject
{
    private bool _hasPendingSettings;
    private string _colorKey = TodoPalette.NoColorKey;
    private string _paletteKey = TodoPalette.VibrantPaletteKey;
    private bool _reminderEnabled;
    private DateTime? _reminderAt;

    public bool HasPendingSettings { get => _hasPendingSettings; private set => SetProperty(ref _hasPendingSettings, value); }
    public string ColorKey { get => _colorKey; private set => SetProperty(ref _colorKey, value); }
    public string PaletteKey { get => _paletteKey; private set => SetProperty(ref _paletteKey, value); }
    public bool ReminderEnabled { get => _reminderEnabled; private set => SetProperty(ref _reminderEnabled, value); }
    public DateTime? ReminderAt { get => _reminderAt; private set => SetProperty(ref _reminderAt, value); }

    public void Save(string colorKey, string paletteKey, bool reminderEnabled, DateTime? reminderAt)
    {
        ColorKey = TodoPalette.IsValidKey(colorKey) ? colorKey : TodoPalette.NoColorKey;
        PaletteKey = TodoPalette.IsValidPaletteKey(paletteKey) ? paletteKey : TodoPalette.VibrantPaletteKey;
        ReminderEnabled = reminderEnabled;
        ReminderAt = reminderEnabled ? reminderAt : null;
        HasPendingSettings = true;
    }

    public void Reset()
    {
        HasPendingSettings = false;
        ColorKey = TodoPalette.NoColorKey;
        PaletteKey = TodoPalette.VibrantPaletteKey;
        ReminderEnabled = false;
        ReminderAt = null;
    }
}
