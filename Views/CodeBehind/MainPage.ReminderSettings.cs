using System.Globalization;
using System.Diagnostics;
using Locksy.Services;
using Locksy.Models;
using Locksy.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace Locksy;

public partial class MainPage : ContentPage
{
    // The heavy color and alarm controls are materialized only when the user opens that section.
    // The compact title + disclosure rows appear immediately; no spinner or artificial delay.
    private sealed class NewReminderPopupVisualCache
    {
        public VerticalStackLayout BodyRoot { get; set; } = null!;
        public VerticalStackLayout FooterRoot { get; set; } = null!;
        public Entry DraftTitleEntry { get; set; } = null!;

        public string SelectedPaletteKey { get; set; } = TodoPalette.VibrantPaletteKey;
        public string SelectedColorKey { get; set; } = TodoPalette.NoColorKey;
        public bool ReminderAlarmEnabled { get; set; }
        public bool IsDarkTheme { get; set; }
        public DateTime DraftReminderAt { get; set; }

        public Label ColorSummary { get; set; } = null!;
        public Label AlarmSummary { get; set; } = null!;
        public Image ColorChevron { get; set; } = null!;
        public Image AlarmChevron { get; set; } = null!;
        public Border ColorContentFrame { get; set; } = null!;
        public Border AlarmContentFrame { get; set; } = null!;
        public VerticalStackLayout ColorContent { get; set; } = null!;
        public VerticalStackLayout AlarmContent { get; set; } = null!;

        public bool ColorSectionBuilt { get; set; }
        public bool AlarmSectionBuilt { get; set; }
        public bool ColorSectionExpanded { get; set; }
        public bool AlarmSectionExpanded { get; set; }

        public DatePicker? DatePicker { get; set; }
        public TimePicker? TimePicker { get; set; }
        public Border? AlarmToggleTrack { get; set; }
        public Ellipse? AlarmToggleThumb { get; set; }

        public Dictionary<string, Border> PaletteBorders { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Label> PaletteLabels { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Border> ColorBorders { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Label> ColorLabels { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Border> ColorSwatches { get; } = new(StringComparer.Ordinal);

        public Action RefreshDraftColors { get; set; } = static () => { };
        public Action UpdateAlarmControls { get; set; } = static () => { };
    }

    private NewReminderPopupVisualCache? _newReminderPopupVisualCache;

    private async void NewTaskSettingsButton_Clicked(object? sender, EventArgs e)
    {
        DismissKeyboardForNavigation();
#if DEBUG
        var popupBuildStarted = Stopwatch.GetTimestamp();
#endif
        var initialTitle = (_viewModel.NewTaskText ?? string.Empty).Trim();
        var initialPaletteKey = _viewModel.NewTaskSettings.HasPendingSettings
            ? _viewModel.NewTaskSettings.PaletteKey
            : TodoPalette.VibrantPaletteKey;
        var initialColorKey = _viewModel.NewTaskSettings.HasPendingSettings
            ? _viewModel.NewTaskSettings.ColorKey
            : TodoPalette.NoColorKey;
        var alarmEnabled = _viewModel.NewTaskSettings.HasPendingSettings && _viewModel.NewTaskSettings.ReminderEnabled;
        var initialReminder = _viewModel.NewTaskSettings.ReminderAt is { } savedTime && savedTime > DateTime.Now
            ? savedTime
            : DateTime.Now.AddMinutes(5);

        var isFirstBuild = _newReminderPopupVisualCache is null;
        var cache = _newReminderPopupVisualCache ?? new NewReminderPopupVisualCache();
        cache.SelectedPaletteKey = initialPaletteKey;
        cache.SelectedColorKey = initialColorKey;
        cache.ReminderAlarmEnabled = alarmEnabled;
        cache.IsDarkTheme = _viewModel.Settings?.IsDarkMode ?? false;
        cache.DraftReminderAt = initialReminder;

        var completion = BeginPopup(
            "New reminder settings",
            "Set a title, then open color or alarm options only when you need them.",
            "options");
        _popupPreferredWidth = 370d;
        ApplyResponsivePopupBounds();

        if (isFirstBuild)
        {
            var body = ModalBody;
            var footer = ModalButtons;

            body.Children.Add(new Label
            {
                Text = LocalizationService.Translate("Reminder title"),
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = PopupColor("#303545", "#F1F2FA")
            });

            cache.DraftTitleEntry = new Entry
            {
                Text = initialTitle,
                FlowDirection = ResolveTaskFlowDirection(initialTitle, TaskTextDirection.Auto),
                Placeholder = LocalizationService.Translate("What do you need to remember?"),
                MaxLength = 300,
                ReturnType = ReturnType.Done,
                ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
                FontSize = 14,
                BackgroundColor = Colors.Transparent,
                TextColor = PopupColor("#303545", "#F1F2FA"),
                PlaceholderColor = PopupColor("#8C8798", "#9690A8")
            };
            cache.DraftTitleEntry.TextChanged += (_, args) =>
                cache.DraftTitleEntry.FlowDirection = ResolveTaskFlowDirection(args.NewTextValue, TaskTextDirection.Auto);
            body.Children.Add(new Border
            {
                Padding = new Thickness(10, 4),
                MinimumHeightRequest = 48,
                BackgroundColor = PopupColor("#E1E6F0", "#1A1B26"),
                Stroke = new SolidColorBrush(PopupColor("#D1D7E3", "#292B3A")),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(13) },
                Content = cache.DraftTitleEntry
            });

            cache.ColorSummary = new Label
            {
                Text = GetReminderColorSummary(cache),
                FontSize = 11,
                TextColor = PopupColor("#777E92", "#A7ADC4"),
                VerticalTextAlignment = TextAlignment.Center,
                HorizontalTextAlignment = TextAlignment.End,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            cache.ColorChevron = CreateDisclosureChevron();
            var colorHeader = CreateReminderDisclosureHeader("Task color", cache.ColorSummary, cache.ColorChevron);
            cache.ColorContent = new VerticalStackLayout { Spacing = 8, FlowDirection = LocalizationService.CurrentFlowDirection };
            cache.ColorContentFrame = CreateReminderDisclosureContent(cache.ColorContent);
            var colorSection = new VerticalStackLayout { Spacing = 4 };
            colorSection.Children.Add(colorHeader);
            colorSection.Children.Add(cache.ColorContentFrame);
            body.Children.Add(colorSection);
            var colorTap = new TapGestureRecognizer();
            colorTap.Tapped += (_, _) => ToggleReminderColorSection(cache);
            colorHeader.GestureRecognizers.Add(colorTap);

            cache.AlarmSummary = new Label
            {
                Text = GetReminderAlarmSummary(cache),
                FontSize = 11,
                TextColor = PopupColor("#777E92", "#A7ADC4"),
                VerticalTextAlignment = TextAlignment.Center,
                HorizontalTextAlignment = TextAlignment.End,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            cache.AlarmChevron = CreateDisclosureChevron();
            var alarmHeader = CreateReminderDisclosureHeader("Reminder alarm & time", cache.AlarmSummary, cache.AlarmChevron);
            cache.AlarmContent = new VerticalStackLayout { Spacing = 8, FlowDirection = LocalizationService.CurrentFlowDirection };
            cache.AlarmContentFrame = CreateReminderDisclosureContent(cache.AlarmContent);
            var alarmSection = new VerticalStackLayout { Spacing = 4 };
            alarmSection.Children.Add(alarmHeader);
            alarmSection.Children.Add(cache.AlarmContentFrame);
            body.Children.Add(alarmSection);
            var alarmTap = new TapGestureRecognizer();
            alarmTap.Tapped += (_, _) => ToggleReminderAlarmSection(cache);
            alarmHeader.GestureRecognizers.Add(alarmTap);

            bool ValidateDraft(bool adding)
            {
                var date = cache.DatePicker?.Date ?? cache.DraftReminderAt.Date;
                var time = cache.TimePicker?.Time ?? cache.DraftReminderAt.TimeOfDay;
                var reminderAt = date.Date.Add(time);
                if (cache.ReminderAlarmEnabled && reminderAt <= DateTime.Now)
                {
                    SetModalMessageText("Choose a future date and time for the alarm.");
                    ModalMessage.IsVisible = true;
                    return false;
                }
                if (adding && string.IsNullOrWhiteSpace(cache.DraftTitleEntry.Text))
                {
                    SetModalMessageText("Enter a reminder title before adding it.");
                    ModalMessage.IsVisible = true;
                    return false;
                }
                cache.DraftReminderAt = reminderAt;
                return true;
            }

            var actions = new Grid { ColumnSpacing = 8, RowSpacing = 7 };
            actions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            actions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            actions.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            actions.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            actions.Add(CreatePopupAction("Cancel", null, primary: false, destructive: false), 0, 0);
            actions.Add(CreatePopupAction("Save settings", "SAVE_SETTINGS", primary: false, destructive: false,
                customAction: () => { if (ValidateDraft(false)) CompletePopup("SAVE_SETTINGS"); }), 1, 0);
            var addAction = CreatePopupAction("Add reminder", "ADD_REMINDER", primary: true, destructive: false,
                customAction: () => { if (ValidateDraft(true)) CompletePopup("ADD_REMINDER"); });
            actions.Add(addAction, 0, 1);
            Grid.SetColumnSpan(addAction, 2);
            footer.Children.Add(actions);

            cache.BodyRoot = body;
            cache.FooterRoot = footer;
            _newReminderPopupVisualCache = cache;
        }
        else
        {
            cache.DraftTitleEntry.Text = initialTitle;
            cache.ColorSectionExpanded = false;
            cache.AlarmSectionExpanded = false;
            cache.ColorContentFrame.IsVisible = false;
            cache.AlarmContentFrame.IsVisible = false;
            cache.ColorChevron.Rotation = 0;
            cache.AlarmChevron.Rotation = 0;
            cache.ColorSummary.Text = GetReminderColorSummary(cache);
            cache.AlarmSummary.Text = GetReminderAlarmSummary(cache);

            if (cache.DatePicker is not null)
            {
                cache.DatePicker.MinimumDate = DateTime.Today;
                cache.DatePicker.Date = initialReminder.Date;
            }
            if (cache.TimePicker is not null)
                cache.TimePicker.Time = initialReminder.TimeOfDay;
            if (cache.ColorSectionBuilt)
                cache.RefreshDraftColors();
            if (cache.AlarmSectionBuilt)
                cache.UpdateAlarmControls();

            ModalBodyHost.Content = cache.BodyRoot;
            ModalButtonsHost.Content = cache.FooterRoot;
        }

        _ = ModalBodyScroll.ScrollToAsync(0, 0, false);
#if DEBUG
        Debug.WriteLine($"[Locksy.Perf] New reminder popup source={(isFirstBuild ? "compact-first-build" : "compact-cache")}; ms={Stopwatch.GetElapsedTime(popupBuildStarted).TotalMilliseconds:F1}; lazyColorBuilt={cache.ColorSectionBuilt}; lazyAlarmBuilt={cache.AlarmSectionBuilt}");
#endif
        await CompleteNewReminderSettingsPopupAsync(completion, cache);
    }

    private Image CreateDisclosureChevron() => new()
    {
        Source = "icon_chevron_down.svg",
        WidthRequest = 22,
        HeightRequest = 22,
        Aspect = Aspect.AspectFit,
        HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.Center
    };

    private Border CreateReminderDisclosureHeader(string title, Label summary, Image chevron)
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(28)) },
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
            ColumnSpacing = 8,
            RowSpacing = 3,
            Padding = new Thickness(12, 10),
            FlowDirection = LocalizationService.CurrentFlowDirection
        };
        grid.Add(new Label
        {
            Text = LocalizationService.Translate(title),
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = PopupColor("#303545", "#F1F2FA"),
            VerticalTextAlignment = TextAlignment.Center,
            FlowDirection = LocalizationService.CurrentFlowDirection
        }, 0, 0);
        grid.Add(summary, 0, 1);
        grid.Add(chevron, 1, 0);
        Grid.SetRowSpan(chevron, 2);
        var header = new Border
        {
            Padding = 0,
            BackgroundColor = PopupColor("#F0F2F8", "#252736"),
            Stroke = new SolidColorBrush(PopupColor("#D1D9E5", "#383A4D")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Content = grid
        };
        SemanticProperties.SetHint(header, LocalizationService.Translate("Tap to expand or collapse this section"));
        return header;
    }

    private Border CreateReminderDisclosureContent(VerticalStackLayout content) => new()
    {
        Padding = new Thickness(10, 8),
        Margin = new Thickness(0, 0, 0, 4),
        IsVisible = false,
        BackgroundColor = PopupColor("#E9EAF2", "#20212E"),
        Stroke = new SolidColorBrush(PopupColor("#D1D9E5", "#383A4D")),
        StrokeThickness = 1,
        StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(13) },
        Content = content
    };

    private void ToggleReminderColorSection(NewReminderPopupVisualCache cache)
    {
        cache.ColorSectionExpanded = !cache.ColorSectionExpanded;
        if (cache.ColorSectionExpanded && !cache.ColorSectionBuilt)
            BuildReminderColorSection(cache);
        cache.ColorContentFrame.IsVisible = cache.ColorSectionExpanded;
        cache.ColorChevron.Rotation = cache.ColorSectionExpanded ? 180 : 0;
        cache.ColorSummary.Text = GetReminderColorSummary(cache);
    }

    private void BuildReminderColorSection(NewReminderPopupVisualCache cache)
    {
        if (cache.ColorSectionBuilt)
            return;

        var content = cache.ColorContent;
        content.Children.Add(CreateReminderSubsectionLabel("Palette"));
        var paletteSelector = new Grid
        {
            ColumnSpacing = 6,
            HeightRequest = 40,
            Margin = new Thickness(0, 0, 0, 2),
            FlowDirection = LocalizationService.CurrentFlowDirection
        };
        foreach (var _ in TodoPalette.Palettes)
            paletteSelector.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        for (var paletteIndex = 0; paletteIndex < TodoPalette.Palettes.Count; paletteIndex++)
        {
            var palette = TodoPalette.Palettes[paletteIndex];
            var label = new Label
            {
                Text = LocalizationService.Translate(palette.Name),
                FontSize = 10,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.TailTruncation,
                TextColor = PopupColor("#5B536F", "#C6BEDD")
            };
            var border = new Border
            {
                Padding = new Thickness(3, 2),
                Content = label,
                BackgroundColor = PopupColor("#E9EAF2", "#20212E"),
                Stroke = new SolidColorBrush(PopupColor("#D1D6E3", "#38394C")),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(11) }
            };
            var paletteKey = palette.Key;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                cache.SelectedPaletteKey = paletteKey;
                cache.RefreshDraftColors();
            };
            border.GestureRecognizers.Add(tap);
            cache.PaletteBorders[paletteKey] = border;
            cache.PaletteLabels[paletteKey] = label;
            paletteSelector.Add(border, paletteIndex, 0);
        }
        content.Children.Add(paletteSelector);
        content.Children.Add(CreateReminderSubsectionLabel("Color"));

        var colorGrid = new Grid { ColumnSpacing = 6, RowSpacing = 6, FlowDirection = LocalizationService.CurrentFlowDirection };
        for (var i = 0; i < 3; i++)
            colorGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var i = 0; i < (TodoPalette.Options.Count + 2) / 3; i++)
            colorGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var index = 0; index < TodoPalette.Options.Count; index++)
        {
            var option = TodoPalette.Options[index];
            var swatch = new Border
            {
                WidthRequest = 17,
                HeightRequest = 17,
                BackgroundColor = Color.FromArgb(TodoPalette.GetHex(option.Key, cache.SelectedPaletteKey)),
                Stroke = new SolidColorBrush(PopupColor("#D1D7E3", "#53566B")),
                StrokeThickness = 0.7,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8.5) },
                VerticalOptions = LayoutOptions.Center
            };
            var label = new Label
            {
                Text = LocalizationService.Translate(option.Name),
                FontSize = 10.5,
                VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.TailTruncation,
                TextColor = PopupColor("#3F4559", "#E5E7F5")
            };
            var cell = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(new GridLength(21)), new ColumnDefinition(GridLength.Star) },
                ColumnSpacing = 4,
                Padding = new Thickness(5, 1),
                HeightRequest = 34
            };
            cell.Add(swatch, 0, 0);
            cell.Add(label, 1, 0);
            var border = new Border
            {
                Padding = 2,
                HeightRequest = 36,
                Content = cell,
                BackgroundColor = PopupColor("#F0F2F8", "#252736"),
                Stroke = new SolidColorBrush(PopupColor("#D1D9E5", "#383A4D")),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) }
            };
            var colorKey = option.Key;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                cache.SelectedColorKey = colorKey;
                cache.RefreshDraftColors();
            };
            border.GestureRecognizers.Add(tap);
            cache.ColorBorders[colorKey] = border;
            cache.ColorLabels[colorKey] = label;
            cache.ColorSwatches[colorKey] = swatch;
            colorGrid.Add(border, index % 3, index / 3);
        }
        content.Children.Add(colorGrid);

        cache.RefreshDraftColors = () =>
        {
            foreach (var palette in TodoPalette.Palettes)
            {
                var chosen = palette.Key == cache.SelectedPaletteKey;
                var border = cache.PaletteBorders[palette.Key];
                border.BackgroundColor = chosen ? PopupColor("#E1D9FF", "#3A3158") : PopupColor("#E9EAF2", "#20212E");
                border.Stroke = new SolidColorBrush(chosen ? Color.FromArgb("#8A76EB") : PopupColor("#D1D6E3", "#38394C"));
                border.StrokeThickness = chosen ? 1.7 : 1;
                cache.PaletteLabels[palette.Key].TextColor = chosen ? PopupColor("#46347F", "#F1ECFF") : PopupColor("#5B536F", "#C6BEDD");
            }
            foreach (var option in TodoPalette.Options)
            {
                cache.ColorSwatches[option.Key].BackgroundColor = Color.FromArgb(TodoPalette.GetHex(option.Key, cache.SelectedPaletteKey));
                var chosen = option.Key == cache.SelectedColorKey;
                var border = cache.ColorBorders[option.Key];
                border.BackgroundColor = chosen ? PopupColor("#E4DFFF", "#38304F") : PopupColor("#F0F2F8", "#252736");
                border.Stroke = new SolidColorBrush(chosen ? Color.FromArgb("#8A76EB") : PopupColor("#D1D9E5", "#383A4D"));
                border.StrokeThickness = chosen ? 1.7 : 1;
                cache.ColorLabels[option.Key].TextColor = PopupColor("#3F4559", "#E5E7F5");
            }
            cache.ColorSummary.Text = GetReminderColorSummary(cache);
        };
        cache.ColorSectionBuilt = true;
        cache.RefreshDraftColors();
    }

    private void ToggleReminderAlarmSection(NewReminderPopupVisualCache cache)
    {
        cache.AlarmSectionExpanded = !cache.AlarmSectionExpanded;
        if (cache.AlarmSectionExpanded && !cache.AlarmSectionBuilt)
            BuildReminderAlarmSection(cache);
        cache.AlarmContentFrame.IsVisible = cache.AlarmSectionExpanded;
        cache.AlarmChevron.Rotation = cache.AlarmSectionExpanded ? 180 : 0;
        cache.AlarmSummary.Text = GetReminderAlarmSummary(cache);
    }

    private void BuildReminderAlarmSection(NewReminderPopupVisualCache cache)
    {
        if (cache.AlarmSectionBuilt)
            return;

        var dark = cache.IsDarkTheme;
        var alarmTitle = new Label
        {
            Text = LocalizationService.Translate("Reminder alarm"),
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = PopupColor("#303545", "#F1F2FA"),
            VerticalTextAlignment = TextAlignment.Center
        };
        var thumb = new Ellipse
        {
            WidthRequest = 19,
            HeightRequest = 19,
            Fill = Color.FromArgb(cache.ReminderAlarmEnabled ? "#F8F6FF" : dark ? "#777F95" : "#7F899F"),
            HorizontalOptions = cache.ReminderAlarmEnabled ? LayoutOptions.End : LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center
        };
        cache.AlarmToggleThumb = thumb;
        var toggleContent = new Grid();
        toggleContent.Add(thumb);
        var track = new Border
        {
            WidthRequest = 46,
            HeightRequest = 27,
            Padding = 3,
            BackgroundColor = GetToggleTrackBackgroundColor(cache.ReminderAlarmEnabled, dark),
            Stroke = new SolidColorBrush(Color.FromArgb(cache.ReminderAlarmEnabled
                ? (dark ? "#A99BFF" : "#8875E8")
                : dark ? "#3B3E52" : "#B7C2D4")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            Content = toggleContent
        };
        cache.AlarmToggleTrack = track;
        SemanticProperties.SetDescription(track, LocalizationService.Translate("Reminder alarm"));
        var alarmRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Margin = new Thickness(0, 1, 0, 5)
        };
        alarmRow.Add(alarmTitle, 0, 0);
        alarmRow.Add(track, 1, 0);
        cache.AlarmContent.Children.Add(alarmRow);

        var reminder = cache.DraftReminderAt;
        var datePicker = new DatePicker
        {
            Date = reminder.Date < DateTime.Today ? DateTime.Today : reminder.Date,
            MinimumDate = DateTime.Today,
            Format = "ddd, dd MMM yyyy",
            FontSize = 13,
            TextColor = PopupColor("#303545", "#F1F2FA"),
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill
        };
        cache.DatePicker = datePicker;
        var timePicker = new TimePicker
        {
            Time = reminder.TimeOfDay,
            Format = "HH:mm",
            FontSize = 13,
            TextColor = PopupColor("#303545", "#F1F2FA"),
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill
        };
        cache.TimePicker = timePicker;

        var dateTimeGrid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 8
        };
        dateTimeGrid.Add(CreateReminderPickerBorder(datePicker), 0, 0);
        dateTimeGrid.Add(CreateReminderPickerBorder(timePicker), 1, 0);
        cache.AlarmContent.Children.Add(new Label
        {
            Text = LocalizationService.Translate("Reminder date and time"),
            FontSize = 11,
            TextColor = PopupColor("#777E92", "#A7ADC4")
        });
        cache.AlarmContent.Children.Add(dateTimeGrid);

        void UpdateAlarmControls()
        {
            datePicker.IsEnabled = cache.ReminderAlarmEnabled;
            timePicker.IsEnabled = cache.ReminderAlarmEnabled;
            dateTimeGrid.Opacity = cache.ReminderAlarmEnabled ? 1 : 0.42;
            track.BackgroundColor = GetToggleTrackBackgroundColor(cache.ReminderAlarmEnabled, cache.IsDarkTheme);
            track.Stroke = new SolidColorBrush(Color.FromArgb(cache.ReminderAlarmEnabled
                ? (cache.IsDarkTheme ? "#A99BFF" : "#8875E8")
                : cache.IsDarkTheme ? "#3B3E52" : "#B7C2D4"));
            thumb.Fill = Color.FromArgb(cache.ReminderAlarmEnabled ? "#F8F6FF" : cache.IsDarkTheme ? "#777F95" : "#7F899F");
            thumb.HorizontalOptions = cache.ReminderAlarmEnabled ? LayoutOptions.End : LayoutOptions.Start;
            cache.AlarmSummary.Text = GetReminderAlarmSummary(cache);
        }
        cache.UpdateAlarmControls = UpdateAlarmControls;
        var toggleTap = new TapGestureRecognizer();
        toggleTap.Tapped += (_, _) =>
        {
            cache.ReminderAlarmEnabled = !cache.ReminderAlarmEnabled;
            UpdateAlarmControls();
        };
        track.GestureRecognizers.Add(toggleTap);
        datePicker.DateSelected += (_, _) =>
        {
            cache.DraftReminderAt = (datePicker.Date ?? cache.DraftReminderAt.Date).Date.Add(timePicker.Time ?? cache.DraftReminderAt.TimeOfDay);
            cache.AlarmSummary.Text = GetReminderAlarmSummary(cache);
        };
        timePicker.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TimePicker.Time))
            {
                cache.DraftReminderAt = (datePicker.Date ?? cache.DraftReminderAt.Date).Date.Add(timePicker.Time ?? cache.DraftReminderAt.TimeOfDay);
                cache.AlarmSummary.Text = GetReminderAlarmSummary(cache);
            }
        };
        UpdateAlarmControls();
        cache.AlarmSectionBuilt = true;
    }

    private Label CreateReminderSubsectionLabel(string text) => new()
    {
        Text = LocalizationService.Translate(text),
        FontSize = 11,
        FontAttributes = FontAttributes.Bold,
        TextColor = PopupColor("#77718D", "#B5ADC9"),
        Margin = new Thickness(1, 3, 1, 0)
    };

    private string GetReminderColorSummary(NewReminderPopupVisualCache cache)
    {
        var option = TodoPalette.GetOption(cache.SelectedColorKey);
        return $"{LocalizationService.Translate(TodoPalette.GetPaletteName(cache.SelectedPaletteKey))} · {LocalizationService.Translate(option.Name)}";
    }

    private string GetReminderAlarmSummary(NewReminderPopupVisualCache cache)
    {
        if (!cache.ReminderAlarmEnabled)
            return LocalizationService.Translate("Off · tap to configure");

        var date = cache.DatePicker?.Date ?? cache.DraftReminderAt.Date;
        var time = cache.TimePicker?.Time ?? cache.DraftReminderAt.TimeOfDay;
        var dateText = date.ToString("dd MMM", CultureInfo.CurrentCulture);
        var timeText = time.ToString(@"hh\:mm", CultureInfo.CurrentCulture);
        var onText = LocalizationService.Translate("On");
        var combinedTime = $"{dateText} {timeText}";
        return $"{onText} · {LocalizationService.LocalizeDigits(combinedTime)}";
    }

    private async Task CompleteNewReminderSettingsPopupAsync(
        TaskCompletionSource<string?> completion,
        NewReminderPopupVisualCache cache)
    {
        var result = await completion.Task;
        _popupPreferredWidth = 380d;
        ApplyResponsivePopupBounds();
        if (result is null)
            return;

        _viewModel.NewTaskText = (cache.DraftTitleEntry.Text ?? string.Empty).Trim();
        var alarmDate = cache.DatePicker?.Date ?? cache.DraftReminderAt.Date;
        var alarmTime = cache.TimePicker?.Time ?? cache.DraftReminderAt.TimeOfDay;
        _viewModel.NewTaskSettings.Save(
            cache.SelectedColorKey,
            cache.SelectedPaletteKey,
            cache.ReminderAlarmEnabled,
            cache.ReminderAlarmEnabled
                ? DateTime.SpecifyKind(alarmDate.Date.Add(alarmTime), DateTimeKind.Local)
                : null);

        if (result == "SAVE_SETTINGS")
        {
            _wallpaperViewModel.StatusMessage = LocalizationService.Translate("Settings saved for the next reminder.");
            return;
        }

        if (result == "ADD_REMINDER")
            await AddTaskAndKeepInputReadyAsync();
    }
}
