using System.Globalization;
using Locksy.Models;
using Locksy.Platforms.Android;
using Locksy.Services;
using Locksy.Services.Preferences;
using Locksy.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;


namespace Locksy;

public partial class MainPage : ContentPage
{
    private async void ClockLayoutSelector_Tapped(object? sender, TappedEventArgs e)
    {
        if (_initializing) return;
        await ShowWallpaperLayoutSettingsAsync();
    }
    private async Task ShowWallpaperLayoutSettingsAsync()
    {
        _dismissLayoutPopupFlowRequested = false;
        // Keep the in-progress selections outside the modal loop so Preview can return
        // to the same editor state without saving or losing the user's draft values.
        var candidateLayoutIndex = _wallpaperViewModel.SelectedLayoutIndex;
        var candidateOffset = _wallpaperViewModel.VerticalOffset;
        var candidateClearance = _wallpaperViewModel.ClockClearance;

        while (true)
        {
            var completion = BeginPopup(
                "Wallpaper placement",
                "Auto adapts the layout to your screen ratio. Adjust the offset to line it up with your lock-screen clock.",
                "layout");

            ModalBody.Children.Add(new Label
            {
                Text = LocalizationService.Translate("PLACEMENT PROFILE"),
                FontSize = 10,
                CharacterSpacing = 1.0,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#8A76EB"),
                Margin = new Thickness(1, 2, 0, 0)
            });

            var profileGrid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
            profileGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            profileGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            for (var row = 0; row < 3; row++)
                profileGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var profileCards = new List<(Border Card, int Index)>();
            for (var i = 0; i < WallpaperLayoutChoices.Length; i++)
            {
                var option = WallpaperLayoutChoices[i];
                var layoutIndex = (int)option.Layout;
                var initiallySelected = candidateLayoutIndex == layoutIndex;
                var card = new Border
                {
                    Padding = new Thickness(10, 9),
                    MinimumHeightRequest = 44,
                    BackgroundColor = initiallySelected ? PopupColor("#E2E7F1", "#2A2C3D") : PopupColor("#F0F2F8", "#252736"),
                    Stroke = new SolidColorBrush(initiallySelected ? Color.FromArgb("#8A76EB") : PopupColor("#D1D9E5", "#383A4D")),
                    StrokeThickness = initiallySelected ? 1.5 : 1,
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(13) },
                    Content = new Label
                    {
                        Text = LocalizationService.Translate(option.Name),
                        FontSize = 11,
                        FontAttributes = initiallySelected ? FontAttributes.Bold : FontAttributes.None,
                        TextColor = PopupColor("#3F4559", "#E5E7F5"),
                        VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center,
                        HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center,
                        LineBreakMode = LineBreakMode.WordWrap
                    }
                };
                var tap = new TapGestureRecognizer();
                tap.Tapped += (_, _) =>
                {
                    candidateLayoutIndex = layoutIndex;
                    foreach (var item in profileCards)
                    {
                        var selected = item.Index == candidateLayoutIndex;
                        item.Card.BackgroundColor = selected ? PopupColor("#E2E7F1", "#2A2C3D") : PopupColor("#F0F2F8", "#252736");
                        item.Card.Stroke = new SolidColorBrush(selected ? Color.FromArgb("#8A76EB") : PopupColor("#D1D9E5", "#383A4D"));
                        item.Card.StrokeThickness = selected ? 1.5 : 1;
                        item.Card.Shadow = CreateNeumorphicShadow(selected ? 0.26f : 0.38f);
                        if (item.Card.Content is Label label) label.FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None;
                    }
                };
                card.GestureRecognizers.Add(tap);
                profileCards.Add((card, layoutIndex));
                profileGrid.Add(card, i % 2, i / 2);
            }
            ModalBody.Children.Add(profileGrid);

            var offsetTitle = new Label
            {
                Text = LocalizationService.Translate("Move content up or down"),
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = PopupColor("#3F4559", "#E5E7F5"),
                Margin = new Thickness(1, 6, 1, 0)
            };
            var offsetValue = new Label
            {
                Text = FormatOffsetLabel(candidateOffset),
                FontSize = 11,
                TextColor = Color.FromArgb("#8A76EB"),
                HorizontalTextAlignment = Microsoft.Maui.TextAlignment.End
            };
            var offsetLabels = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            offsetLabels.Add(offsetTitle, 0, 0);
            offsetLabels.Add(offsetValue, 1, 0);
            ModalBody.Children.Add(offsetLabels);
            var offsetSlider = new Slider
            {
                Minimum = 0, Maximum = 40, Value = candidateOffset * 100d + 20d,
                MinimumTrackColor = Color.FromArgb("#8875E8"),
                MaximumTrackColor = PopupColor("#DAD3E9", "#494257"),
                ThumbColor = Color.FromArgb("#8A76EB"),
                HorizontalOptions = LayoutOptions.Fill
            };
            var offsetUpLabel = new Label
            {
                Text = FormatUpOffsetLabel(candidateOffset),
                FontSize = 9,
                TextColor = PopupColor("#7E8599", "#9FA5BC"),
                VerticalTextAlignment = TextAlignment.Center
            };
            var offsetDownLabel = new Label
            {
                Text = FormatDownOffsetLabel(candidateOffset),
                FontSize = 9,
                TextColor = PopupColor("#7E8599", "#9FA5BC"),
                HorizontalTextAlignment = Microsoft.Maui.TextAlignment.End,
                VerticalTextAlignment = TextAlignment.Center
            };
            var offsetUpContent = new HorizontalStackLayout
            {
                Spacing = 3,
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Image { Source = "icon_arrow_up.svg", WidthRequest = 22, HeightRequest = 22, Aspect = Aspect.AspectFit, VerticalOptions = LayoutOptions.Center },
                    offsetUpLabel
                }
            };
            var offsetDownContent = new HorizontalStackLayout
            {
                Spacing = 3,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center,
                FlowDirection = Microsoft.Maui.FlowDirection.RightToLeft,
                Children =
                {
                    new Image { Source = "icon_arrow_down.svg", WidthRequest = 22, HeightRequest = 22, Aspect = Aspect.AspectFit, VerticalOptions = LayoutOptions.Center },
                    offsetDownLabel
                }
            };
            offsetSlider.ValueChanged += (_, args) =>
            {
                candidateOffset = (float)((args.NewValue - 20d) / 100d);
                offsetValue.Text = FormatOffsetLabel(candidateOffset);
                offsetUpLabel.Text = FormatUpOffsetLabel(candidateOffset);
                offsetDownLabel.Text = FormatDownOffsetLabel(candidateOffset);
            };
            ModalBody.Children.Add(offsetSlider);

            var offsetRange = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
            offsetRange.Add(offsetUpContent, 0, 0);
            offsetRange.Add(offsetDownContent, 1, 0);
            ModalBody.Children.Add(offsetRange);

            var clearanceTitle = new Label
            {
                Text = LocalizationService.Translate("Empty space around clock"),
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = PopupColor("#3F4559", "#E5E7F5"),
                Margin = new Thickness(1, 4, 1, 0)
            };
            var clearanceValue = new Label
            {
                Text = FormatClearanceLabel(candidateClearance),
                FontSize = 11,
                TextColor = Color.FromArgb("#8A76EB"),
                HorizontalTextAlignment = Microsoft.Maui.TextAlignment.End
            };
            var clearanceLabels = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            clearanceLabels.Add(clearanceTitle, 0, 0);
            clearanceLabels.Add(clearanceValue, 1, 0);
            ModalBody.Children.Add(clearanceLabels);
            var clearanceSlider = new Slider
            {
                Minimum = 8, Maximum = 30, Value = candidateClearance * 100d,
                MinimumTrackColor = Color.FromArgb("#8875E8"),
                MaximumTrackColor = PopupColor("#DAD3E9", "#494257"),
                ThumbColor = Color.FromArgb("#8A76EB"),
                HorizontalOptions = LayoutOptions.Fill
            };
            clearanceSlider.ValueChanged += (_, args) =>
            {
                candidateClearance = (float)(args.NewValue / 100d);
                clearanceValue.Text = FormatClearanceLabel(candidateClearance);
            };
            ModalBody.Children.Add(clearanceSlider);
            ModalBody.Children.Add(new Label
            {
                Text = LocalizationService.Translate("Clock spacing applies mainly to Split around center clock. Offset moves the wallpaper content and its clear area together."),
                FontSize = 10,
                LineBreakMode = LineBreakMode.WordWrap,
                TextColor = PopupColor("#7E8599", "#9FA5BC")
            });

            ModalButtons.Children.Add(CreatePopupAction(
                "Preview layout", null, primary: false, destructive: false,
                customAction: () => CompletePopupInPlace("PREVIEW_LAYOUT")));

            var actions = new Grid { ColumnSpacing = 9 };
            actions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            actions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            actions.Add(CreatePopupAction("Cancel", null, primary: false, destructive: false), 0, 0);
            actions.Add(CreatePopupAction("Save", "__SAVE_LAYOUT__", primary: true, destructive: false, customAction: () => CompletePopup("SAVE_LAYOUT_SETTINGS")), 1, 0);
            ModalButtons.Children.Add(actions);

            var result = await completion.Task;
            if (result == "PREVIEW_LAYOUT")
            {
                await ShowWallpaperLayoutPreviewAsync(candidateLayoutIndex, candidateOffset, candidateClearance);
                if (_dismissLayoutPopupFlowRequested)
                {
                    _dismissLayoutPopupFlowRequested = false;
                    return;
                }
                continue;
            }
            if (result != "SAVE_LAYOUT_SETTINGS") return;

            _wallpaperViewModel.SaveLayoutSettings(candidateLayoutIndex, candidateOffset, candidateClearance);
            return;
        }
    }
    private async Task ShowWallpaperLayoutPreviewAsync(int layoutValue, float verticalOffset, float clockClearance)
    {
        _layoutPreviewActive = true;
        try
        {
            var completion = BeginPopup(
                "Layout preview",
                "Schematic only. CLOCK marks the expected clock area; the sample task cards are not added to the final wallpaper.",
                "preview");
            ModalBodyScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Never;
            ModalSectionLabel.Text = LocalizedDisplayText("LOCKSY · TASKS LAYOUT");

            var caption = new Label
            {
                Text = LocalizedDisplayText("SCHEMATIC PREVIEW"),
                FontSize = 10,
                CharacterSpacing = 1.1,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#8A76EB"),
                HorizontalTextAlignment = TextAlignment.Center,
                FlowDirection = LocalizationService.CurrentFlowDirection,
                Margin = new Thickness(0, 0, 0, 2)
            };
            ModalBody.Children.Add(caption);

            // Portrait phone-like canvas sized independently from Android's real wallpaper
            // output. These cards are drawn as placeholder views only and never enter the
            // TodoItem list or AndroidWallpaperService.
            const double canvasWidth = 142;
            const double canvasHeight = 306;
            const double taskWidth = 122;
            const double taskHeight = 22;
            const double taskGap = 7;
            const int sampleTaskCount = 4;
            var content = new AbsoluteLayout
            {
                WidthRequest = canvasWidth,
                HeightRequest = canvasHeight,
                BackgroundColor = PopupColor("#F2F0FF", "#171622"),
                InputTransparent = true
            };

            // Subtle schematic grid, intentionally different from the generated wallpaper.
            for (var i = 1; i <= 5; i++)
            {
                var gridline = new BoxView
                {
                    BackgroundColor = PopupColor("#E3DFFC", "#29263A"),
                    Opacity = 0.72
                };
                AddLayoutPreviewElement(content, gridline, 0, i * canvasHeight / 6d, canvasWidth, 1);
            }

            var selectedLayout = (WallpaperLayout)Math.Clamp(layoutValue, 0, (int)WallpaperLayout.TasksBottom);
            var layout = selectedLayout;
            // The schematic is a tall portrait phone, so Screen-aware (auto) previews the
            // tall-screen profile. Actual wallpaper generation still uses the real device ratio.
            if (layout == WallpaperLayout.ScreenAware)
                layout = WallpaperLayout.BelowTopClock;

            var offsetPx = Math.Clamp(verticalOffset, -0.20f, 0.20f) * canvasHeight;
            var totalGroupHeight = sampleTaskCount * taskHeight + (sampleTaskCount - 1) * taskGap;
            // Make the clock target easier to recognize in the schematic preview.
            var clockWidth = 72d;
            var clockHeight = 28d;
            double clockY;
            var taskPositions = new List<double>(sampleTaskCount);

            switch (layout)
            {
                case WallpaperLayout.TasksBottom:
                {
                    clockY = 8;
                    var start = Math.Clamp(canvasHeight * 0.925d + offsetPx - totalGroupHeight,
                        canvasHeight * 0.34d, canvasHeight - totalGroupHeight - 6d);
                    for (var i = 0; i < sampleTaskCount; i++) taskPositions.Add(start + i * (taskHeight + taskGap));
                    break;
                }
                case WallpaperLayout.AboveBottomClock:
                {
                    clockY = canvasHeight - clockHeight - 8;
                    var start = Math.Clamp(8d + offsetPx, 5d, canvasHeight * 0.30d);
                    for (var i = 0; i < sampleTaskCount; i++) taskPositions.Add(start + i * (taskHeight + taskGap));
                    break;
                }
                case WallpaperLayout.CenteredList:
                {
                    clockY = 8;
                    var start = Math.Clamp((canvasHeight - totalGroupHeight) / 2d + offsetPx,
                        6d, canvasHeight - totalGroupHeight - 6d);
                    for (var i = 0; i < sampleTaskCount; i++) taskPositions.Add(start + i * (taskHeight + taskGap));
                    break;
                }
                case WallpaperLayout.SplitAroundCenterClock:
                {
                    var gapHalf = canvasHeight * (Math.Clamp(clockClearance, 0.08f, 0.30f) / 2d);
                    var center = canvasHeight * 0.5d + offsetPx;
                    center = Math.Clamp(center,
                        2 * taskHeight + taskGap + gapHalf + 12d,
                        canvasHeight - (2 * taskHeight + taskGap + gapHalf + 12d));
                    clockY = center - clockHeight / 2d;
                    var upperStart = center - gapHalf - 12d - (2 * taskHeight + taskGap);
                    var lowerStart = center + gapHalf + 12d;
                    taskPositions.Add(upperStart);
                    taskPositions.Add(upperStart + taskHeight + taskGap);
                    taskPositions.Add(lowerStart);
                    taskPositions.Add(lowerStart + taskHeight + taskGap);
                    break;
                }
                default: // tall-screen Screen-aware profile: tasks below the top clock
                {
                    clockY = 8;
                    var start = Math.Clamp(canvasHeight * 0.205d + offsetPx,
                        canvasHeight * 0.15d, canvasHeight * 0.42d);
                    for (var i = 0; i < sampleTaskCount; i++) taskPositions.Add(start + i * (taskHeight + taskGap));
                    break;
                }
            }

            var clock = new Border
            {
                WidthRequest = clockWidth,
                HeightRequest = clockHeight,
                Padding = new Thickness(2, 0),
                BackgroundColor = Color.FromArgb("#88765DDF"),
                Stroke = new SolidColorBrush(Color.FromArgb("#B8A7FF")),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8) },
                Content = new Label
                {
                    Text = "CLOCK",
                    FontSize = 10,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                    FlowDirection = Microsoft.Maui.FlowDirection.LeftToRight
                }
            };
            AddLayoutPreviewElement(content, clock, (canvasWidth - clockWidth) / 2d, Math.Clamp(clockY, 4, canvasHeight - clockHeight - 4), clockWidth, clockHeight);

            for (var i = 0; i < taskPositions.Count; i++)
            {
                var card = CreateLayoutPreviewTaskCard(i + 1, taskWidth, taskHeight);
                var y = Math.Clamp(taskPositions[i], 4d, canvasHeight - taskHeight - 4d);
                AddLayoutPreviewElement(content, card, (canvasWidth - taskWidth) / 2d, y, taskWidth, taskHeight);
            }

            var phoneFrame = new Border
            {
                WidthRequest = 162,
                HeightRequest = 326,
                Padding = new Thickness(9),
                HorizontalOptions = LayoutOptions.Center,
                BackgroundColor = PopupColor("#E4E0F7", "#222031"),
                Stroke = new SolidColorBrush(PopupColor("#CFC8EB", "#48415F")),
                StrokeThickness = 1.2,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(21) },
                Content = content
            };
            var previewContainer = new Grid
            {
                HorizontalOptions = LayoutOptions.Center,
                Padding = new Thickness(0, 1, 0, 1)
            };
            previewContainer.Add(phoneFrame);
            ModalBody.Children.Add(previewContainer);

            ModalBody.Children.Add(new Label
            {
                Text = LocalizedDisplayText("The four sample cards are visual placeholders only. They will not appear on your wallpaper."),
                FontSize = 10,
                LineBreakMode = LineBreakMode.WordWrap,
                HorizontalTextAlignment = TextAlignment.Center,
                FlowDirection = LocalizationService.CurrentFlowDirection,
                TextColor = PopupColor("#7E8599", "#9FA5BC"),
                Margin = new Thickness(4, 1, 4, 0)
            });
            if (selectedLayout == WallpaperLayout.ScreenAware)
            {
                ModalBody.Children.Add(new Label
                {
                    Text = LocalizedDisplayText("Screen-aware preview assumes a tall portrait phone; the final layout adapts to your device."),
                    FontSize = 10,
                    LineBreakMode = LineBreakMode.WordWrap,
                    HorizontalTextAlignment = TextAlignment.Center,
                    FlowDirection = LocalizationService.CurrentFlowDirection,
                    TextColor = PopupColor("#7E8599", "#9FA5BC"),
                    Margin = new Thickness(4, 0, 4, 0)
                });
            }

            ModalButtons.Children.Add(CreatePopupAction(
                "Back to layout", null, primary: true, destructive: false,
                customAction: () => CompletePopupInPlace("BACK_TO_LAYOUT")));
            await completion.Task;
        }
        finally
        {
            _layoutPreviewActive = false;
            ModalBodyScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Default;
        }
    }
    private Border CreateLayoutPreviewTaskCard(int number, double width, double height)
    {
        var backgrounds = new[] { "#F0D17C", "#7DCDEB", "#754252", "#B9A7F5" };
        var lineColors = new[] { "#5C4725", "#20536A", "#F8EAF0", "#4E427B" };
        var index = (number - 1) % backgrounds.Length;
        var lineColor = Color.FromArgb(lineColors[index]);

        var numberBadge = new Border
        {
            WidthRequest = 14,
            HeightRequest = 14,
            BackgroundColor = Color.FromArgb("#FFF6E2"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(7) },
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = LocalizationService.LocalizeDigits(number.ToString("00", CultureInfo.InvariantCulture)),
                FontSize = 6,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#30283E"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
        var lines = new VerticalStackLayout
        {
            Spacing = 3,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill
        };
        lines.Children.Add(new BoxView
        {
            HeightRequest = 3,
            WidthRequest = width * (number % 2 == 0 ? 0.48 : 0.61),
            BackgroundColor = lineColor,
            HorizontalOptions = LayoutOptions.Start,
            Opacity = 0.92
        });
        lines.Children.Add(new BoxView
        {
            HeightRequest = 2,
            WidthRequest = width * 0.31,
            BackgroundColor = lineColor,
            HorizontalOptions = LayoutOptions.Start,
            Opacity = 0.58
        });
        var row = new Grid
        {
            Padding = new Thickness(4, 2),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 6,
            FlowDirection = Microsoft.Maui.FlowDirection.LeftToRight
        };
        row.Add(numberBadge, 0, 0);
        row.Add(lines, 1, 0);

        return new Border
        {
            WidthRequest = width,
            HeightRequest = height,
            Padding = new Thickness(1),
            BackgroundColor = Color.FromArgb(backgrounds[index]),
            Stroke = new SolidColorBrush(Color.FromArgb("#558A76EB")),
            StrokeThickness = 0.7,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) },
            Content = row
        };
    }
    private static void AddLayoutPreviewElement(AbsoluteLayout canvas, View view, double x, double y, double width, double height)
    {
        AbsoluteLayout.SetLayoutFlags(view, AbsoluteLayoutFlags.None);
        AbsoluteLayout.SetLayoutBounds(view, new Microsoft.Maui.Graphics.Rect(x, y, width, height));
        canvas.Children.Add(view);
    }
    private static string FormatOffsetLabel(float offset)
    {
        var percent = (int)Math.Round(offset * 100f);
        if (LocalizationService.IsPersian)
            return LocalizationService.LocalizeDigits(percent switch { < 0 => $"{Math.Abs(percent)}٪ به بالا", > 0 => $"{percent}٪ به پایین", _ => "بدون جابه‌جایی" });
        return percent switch { < 0 => $"{Math.Abs(percent)}% up", > 0 => $"{percent}% down", _ => "No offset" };
    }
    private static string FormatUpOffsetLabel(float offset) => LocalizationService.IsPersian
        ? LocalizationService.LocalizeDigits($"{Math.Max(0, (int)Math.Round(-offset * 100f))}٪ بالا")
        : $"Up {Math.Max(0, (int)Math.Round(-offset * 100f))}%";
    private static string FormatDownOffsetLabel(float offset) => LocalizationService.IsPersian
        ? LocalizationService.LocalizeDigits($"{Math.Max(0, (int)Math.Round(offset * 100f))}٪ پایین")
        : $"Down {Math.Max(0, (int)Math.Round(offset * 100f))}%";
    private static string FormatClearanceLabel(float clearance) => LocalizationService.IsPersian
        ? LocalizationService.LocalizeDigits($"{clearance * 100:0}٪ از ارتفاع صفحه")
        : $"{clearance * 100:0}% of screen height";
    private void AutoApplySwitch_Toggled(object? sender, ToggledEventArgs e) => UpdateAutoApplyVisual(e.Value);
}
