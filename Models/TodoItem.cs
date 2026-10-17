using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Input;
using Locksy.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Locksy.Models;

public sealed class TodoItem : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private bool _isCompleted;
    private string _colorKey = TodoPalette.NoColorKey;
    private string _colorPaletteKey = TodoPalette.VibrantPaletteKey;
    private bool _isReminderEnabled;
    private DateTime? _reminderAt;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title
    {
        get => _title;
        set
        {
            var normalized = value ?? string.Empty;
            if (_title == normalized) return;
            _title = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayTitle));
        }
    }

    [JsonIgnore]
    public string DisplayTitle => LocalizationService.PrepareTextForDisplay(Title);

    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            if (_isCompleted == value) return;
            _isCompleted = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TitleDecorations));
            OnPropertyChanged(nameof(IsReminderActive));
        }
    }

    /// <summary>Whether this task has a one-time reminder configured.</summary>
    public bool IsReminderEnabled
    {
        get => _isReminderEnabled;
        set
        {
            if (_isReminderEnabled == value) return;
            _isReminderEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsReminderActive));
        }
    }

    /// <summary>Local date and time for the task reminder.</summary>
    public DateTime? ReminderAt
    {
        get => _reminderAt;
        set
        {
            if (_reminderAt == value) return;
            _reminderAt = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsReminderActive));
        }
    }

    [JsonIgnore]
    public bool IsReminderActive =>
        !IsCompleted && IsReminderEnabled && ReminderAt.HasValue && ReminderAt.Value > DateTime.Now;

    // Persist the palette key instead of a platform Color object.
    public string ColorKey
    {
        get => _colorKey;
        set
        {
            var normalized = TodoPalette.IsValidKey(value) ? value : TodoPalette.NoColorKey;
            if (_colorKey == normalized) return;
            _colorKey = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RowColor));
            OnPropertyChanged(nameof(RowTextColor));
        }
    }

    /// <summary>
    /// Palette variant selected for this task. It is intentionally independent
    /// of the app's Light/Dark appearance so the chosen tint remains consistent.
    /// </summary>
    public string ColorPaletteKey
    {
        get => _colorPaletteKey;
        set
        {
            var normalized = TodoPalette.IsValidPaletteKey(value)
                ? value
                : TodoPalette.VibrantPaletteKey;
            if (_colorPaletteKey == normalized) return;
            _colorPaletteKey = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RowColor));
            OnPropertyChanged(nameof(RowTextColor));
        }
    }

    [JsonIgnore]
    public Color RowColor => Color.FromArgb(TodoPalette.GetHex(ColorKey, ColorPaletteKey));

    [JsonIgnore]
    public Color RowTextColor => Color.FromArgb(TodoPalette.GetTextHex(ColorKey, ColorPaletteKey));

    [JsonIgnore]
    public TextDecorations TitleDecorations => IsCompleted ? TextDecorations.Strikethrough : TextDecorations.None;

    [JsonIgnore]
    public ICommand? DeleteCommand { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RefreshLocalizedTitle() => OnPropertyChanged(nameof(DisplayTitle));

    public void RefreshAppearance()
    {
        OnPropertyChanged(nameof(RowColor));
        OnPropertyChanged(nameof(RowTextColor));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
