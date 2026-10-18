using Locksy.Helpers;
using Locksy.Models;
using Locksy.Services;
using Locksy.Services.Abstractions;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Locksy.ViewModels;

/// <summary>
/// Adapts persistent task data for the UI. The wrapped model stays free of MAUI presentation
/// concerns while this type exposes observable, localized, bindable properties.
/// </summary>
public sealed class TaskItemViewModel : ObservableObject
{
    private readonly ILocalizationService _localizationService;
    internal TodoItem Model { get; }

    public Guid Id => Model.Id;

    public string Title
    {
        get => Model.Title;
        set
        {
            var normalized = value ?? string.Empty;
            if (Model.Title == normalized) return;
            Model.Title = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(TitleFlowDirection));
        }
    }

    public string DisplayTitle => _localizationService.PrepareTaskTextForDisplay(Title, TextDirection);

    public TaskTextDirection TextDirection
    {
        get => Enum.IsDefined(Model.TextDirection) ? Model.TextDirection : TaskTextDirection.Auto;
        set
        {
            var normalized = Enum.IsDefined(value) ? value : TaskTextDirection.Auto;
            if (Model.TextDirection == normalized) return;
            Model.TextDirection = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(TitleFlowDirection));
        }
    }

    public FlowDirection TitleFlowDirection =>
        TaskTextDirectionHelper.Resolve(Title, TextDirection, _localizationService.IsPersian) == TaskTextDirection.RightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

    public bool IsCompleted
    {
        get => Model.IsCompleted;
        set
        {
            if (Model.IsCompleted == value) return;
            Model.IsCompleted = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TitleDecorations));
            OnPropertyChanged(nameof(IsReminderActive));
        }
    }

    public string ColorKey
    {
        get => TodoPalette.IsValidKey(Model.ColorKey) ? Model.ColorKey : TodoPalette.NoColorKey;
        set
        {
            var normalized = TodoPalette.IsValidKey(value) ? value : TodoPalette.NoColorKey;
            if (Model.ColorKey == normalized) return;
            Model.ColorKey = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RowColor));
            OnPropertyChanged(nameof(RowTextColor));
        }
    }

    public string ColorPaletteKey
    {
        get => TodoPalette.IsValidPaletteKey(Model.ColorPaletteKey)
            ? Model.ColorPaletteKey
            : TodoPalette.VibrantPaletteKey;
        set
        {
            var normalized = TodoPalette.IsValidPaletteKey(value)
                ? value
                : TodoPalette.VibrantPaletteKey;
            if (Model.ColorPaletteKey == normalized) return;
            Model.ColorPaletteKey = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RowColor));
            OnPropertyChanged(nameof(RowTextColor));
        }
    }

    public bool IsReminderEnabled
    {
        get => Model.IsReminderEnabled;
        set
        {
            if (Model.IsReminderEnabled == value) return;
            Model.IsReminderEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsReminderActive));
        }
    }

    public DateTime? ReminderAt
    {
        get => Model.ReminderAt;
        set
        {
            if (Model.ReminderAt == value) return;
            Model.ReminderAt = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsReminderActive));
        }
    }

    public bool IsReminderActive => Model.IsReminderActive;
    public Color RowColor => Color.FromArgb(TodoPalette.GetHex(ColorKey, ColorPaletteKey));
    public Color RowTextColor => Color.FromArgb(TodoPalette.GetTextHex(ColorKey, ColorPaletteKey));
    public TextDecorations TitleDecorations => IsCompleted ? TextDecorations.Strikethrough : TextDecorations.None;

    public TaskItemViewModel(TodoItem model, ILocalizationService localizationService)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));

        // Normalize older or manually edited JSON data at the presentation boundary before
        // any palette-dependent controls are rendered.
        if (!TodoPalette.IsValidKey(Model.ColorKey)) Model.ColorKey = TodoPalette.NoColorKey;
        if (!TodoPalette.IsValidPaletteKey(Model.ColorPaletteKey))
            Model.ColorPaletteKey = TodoPalette.VibrantPaletteKey;
        Model.Title ??= string.Empty;
        if (!Enum.IsDefined(Model.TextDirection)) Model.TextDirection = TaskTextDirection.Auto;
    }

    public void RefreshLocalizedTitle()
    {
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(TitleFlowDirection));
    }

    public void RefreshAppearance()
    {
        OnPropertyChanged(nameof(RowColor));
        OnPropertyChanged(nameof(RowTextColor));
    }
}
