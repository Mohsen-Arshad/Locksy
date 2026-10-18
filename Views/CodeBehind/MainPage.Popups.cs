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
    private void ApplyResponsivePopupBounds(double? availableWidth = null, double? availableHeight = null)
    {
        var viewportWidth = availableWidth ?? Width;
        var viewportHeight = availableHeight ?? Height;
        if (viewportWidth <= 0 || viewportHeight <= 0 || ModalCard is null || ModalBodyScroll is null)
            return;

        var cardWidth = Math.Min(_popupPreferredWidth, Math.Max(120d, viewportWidth - 28d));
        var cardMaxHeight = Math.Max(160d, viewportHeight - 40d);
        // Reserve room for header, optional message, card padding, row spacing and fixed footer.
        // All popup types use the same cap instead of maintaining fragile per-dialog constants.
        var bodyMaxHeight = Math.Clamp(viewportHeight - 300d, 72d, 520d);

        if (Math.Abs(ModalCard.WidthRequest - cardWidth) > 1d)
            ModalCard.WidthRequest = cardWidth;
        if (Math.Abs(ModalCard.MaximumWidthRequest - cardWidth) > 1d)
            ModalCard.MaximumWidthRequest = cardWidth;
        if (Math.Abs(ModalCard.MaximumHeightRequest - cardMaxHeight) > 1d)
            ModalCard.MaximumHeightRequest = cardMaxHeight;
        if (Math.Abs(ModalBodyScroll.MaximumHeightRequest - bodyMaxHeight) > 1d)
            ModalBodyScroll.MaximumHeightRequest = bodyMaxHeight;
    }
    private void SetModalMessageText(string? value)
    {
        ModalMessage.FlowDirection = LocalizationService.CurrentFlowDirection;
        ModalMessage.HorizontalTextAlignment = TextAlignment.Justify;
        ModalMessage.LineBreakMode = LineBreakMode.WordWrap;
        ModalMessage.Text = LocalizedDisplayText(value);
    }
    private void SetPopupHeaderVisualState(double opacity, double translationX)
    {
        ModalSectionLabel.Opacity = opacity;
        ModalTitle.Opacity = opacity;
        ModalMessage.Opacity = opacity;
        ModalIconImage.Opacity = opacity;
        ModalIconLabel.Opacity = opacity;
        ModalSectionLabel.TranslationX = translationX;
        ModalTitle.TranslationX = translationX;
        ModalMessage.TranslationX = translationX;
        ModalIconImage.TranslationX = translationX;
        ModalIconLabel.TranslationX = translationX;
    }
    private Task AnimatePopupHeaderAsync(double opacity, double translationX, uint duration, Easing easing)
    {
        return Task.WhenAll(
            ModalSectionLabel.FadeTo(opacity, duration, easing),
            ModalTitle.FadeTo(opacity, duration, easing),
            ModalMessage.FadeTo(opacity, duration, easing),
            ModalIconImage.FadeTo(opacity, duration, easing),
            ModalIconLabel.FadeTo(opacity, duration, easing),
            ModalSectionLabel.TranslateTo(translationX, 0, duration, easing),
            ModalTitle.TranslateTo(translationX, 0, duration, easing),
            ModalMessage.TranslateTo(translationX, 0, duration, easing),
            ModalIconImage.TranslateTo(translationX, 0, duration, easing),
            ModalIconLabel.TranslateTo(translationX, 0, duration, easing));
    }
    private TaskCompletionSource<string?> BeginPopup(string title, string message, string icon = "✦")
    {
        // A nested action can finish its result while keeping the modal surface open.
        // The next popup then replaces the content and animates inside the same card.
        var reuseVisibleModal = ModalOverlay.IsVisible && _popupCompletionSource is null;
        if (_popupCompletionSource is not null)
            CompletePopup(null);

        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _popupCompletionSource = completion;
        _popupPreferredWidth = 380d;
        ModalCard.MinimumHeightRequest = -1;
        ApplyResponsivePopupBounds();
        ModalBodyScroll.Opacity = reuseVisibleModal ? 0 : 1;
        ModalButtons.Opacity = reuseVisibleModal ? 0 : 1;
        var transitionStartX = (LocalizationService.IsPersian ? -1d : 1d) * 16d;
        ModalBodyScroll.TranslationX = reuseVisibleModal ? transitionStartX : 0;
        ModalButtons.TranslationX = reuseVisibleModal ? transitionStartX : 0;
        SetPopupHeaderVisualState(reuseVisibleModal ? 0 : 1, reuseVisibleModal ? transitionStartX : 0);
        ModalSectionLabel.Text = LocalizedDisplayText("LOCKSY · QUICK ACTION");
        ModalTitle.FlowDirection = LocalizationService.CurrentFlowDirection;
        ModalSectionLabel.FlowDirection = LocalizationService.CurrentFlowDirection;
        ModalTitle.Text = LocalizedDisplayText(title);
        SetModalMessageText(message);
        ModalMessage.TextColor = PopupColor("#6F7589", "#ADB2C8");
        ModalMessage.IsVisible = !string.IsNullOrWhiteSpace(message);
        var useOptionsIcon = string.Equals(icon, "options", StringComparison.OrdinalIgnoreCase);
        var useAlarmIcon = string.Equals(icon, "alarm", StringComparison.OrdinalIgnoreCase)
            || icon is "◷" or "⏰";
        var useSettingsIcon = string.Equals(icon, "settings", StringComparison.OrdinalIgnoreCase);
        var useVectorIcon = useOptionsIcon || useAlarmIcon || useSettingsIcon;
        ModalIconImage.IsVisible = useVectorIcon;
        ModalIconLabel.IsVisible = !useVectorIcon;
        if (useOptionsIcon)
        {
            ModalIconImage.Source = ImageSource.FromFile("locksy_options_icon.svg");
            ModalIconImage.WidthRequest = 23;
            ModalIconImage.HeightRequest = 23;
        }
        else if (useAlarmIcon)
        {
            ModalIconImage.Source = ImageSource.FromFile("locksy_alarm_icon.svg");
            ModalIconImage.WidthRequest = 23;
            ModalIconImage.HeightRequest = 23;
        }
        else if (useSettingsIcon)
        {
            ModalIconImage.Source = ImageSource.FromFile("settings_gear.svg");
            ModalIconImage.WidthRequest = 28;
            ModalIconImage.HeightRequest = 28;
        }
        else
        {
            ModalIconLabel.Text = icon;
            ModalIconImage.WidthRequest = 23;
            ModalIconImage.HeightRequest = 23;
        }
        ModalIconLabel.FontSize = 22;
        ModalBody.Children.Clear();
        ModalButtons.Children.Clear();
        _ = ModalBodyScroll.ScrollToAsync(0, 0, false);

        ModalCard.BackgroundColor = PopupColor("#F0F2F8", "#252736");
        ModalCard.Stroke = new SolidColorBrush(PopupColor("#CDD5E2", "#383A4D"));
        // Disable hit testing on the entire page beneath the modal. This is separate
        // from the backdrop's hit target so even platform-native controls cannot leak taps.
        PageContent.InputTransparent = true;
        if (reuseVisibleModal)
        {
            ModalOverlay.Opacity = 1;
            ModalCard.Scale = 1;
            ModalOverlay.IsVisible = true;
            _ = AnimatePopupContentInAsync();
        }
        else
        {
            ModalOverlay.Opacity = 0;
            ModalCard.Scale = 0.94;
            ModalOverlay.IsVisible = true;
            _ = ModalOverlay.FadeTo(1, 160, Easing.CubicOut);
            _ = ModalCard.ScaleTo(1, 180, Easing.CubicOut);
        }
        return completion;
    }
    private async Task AnimatePopupContentInAsync()
    {
        // BeginPopup callers build the new controls synchronously after it returns.
        try
        {
            await Task.Delay(20);
            await Task.WhenAll(
                ModalBodyScroll.FadeTo(1, 145, Easing.CubicOut),
                ModalButtons.FadeTo(1, 145, Easing.CubicOut),
                ModalBodyScroll.TranslateTo(0, 0, 145, Easing.CubicOut),
                ModalButtons.TranslateTo(0, 0, 145, Easing.CubicOut),
                AnimatePopupHeaderAsync(1, 0, 145, Easing.CubicOut));
        }
        catch (OperationCanceledException)
        {
            // A newer transition may cancel this one; it will own the final state.
        }
        finally
        {
            ModalCard.InputTransparent = false;
        }
    }
    private Color PopupColor(string light, string dark) =>
        Color.FromArgb((_viewModel.Settings?.IsDarkMode ?? false) ? dark : light);

    // Consistent raised-surface treatment used by dynamically-created popup controls.
    // A crisp highlight stroke supplies the second edge that a single MAUI Shadow cannot provide.
    private Shadow CreateNeumorphicShadow(float strength = 1f)
    {
        var dark = (_viewModel.Settings?.IsDarkMode ?? false);
        return new Shadow
        {
            Brush = new SolidColorBrush(Color.FromArgb(dark ? "#11121A" : "#B5BDCC")),
            Offset = new Microsoft.Maui.Graphics.Point(1, 2),
            Radius = 9,
            Opacity = Math.Clamp((dark ? 0.30f : 0.28f) * strength, 0.05f, 0.38f)
        };
    }
    private Task<string?> ShowChoicePopupAsync(
        string title,
        string message,
        IReadOnlyList<string> choices,
        string cancelLabel = "Cancel",
        IReadOnlyList<string>? icons = null,
        string popupIcon = "✦",
        bool keepOpenOnSelection = false)
    {
        var completion = BeginPopup(title, message, popupIcon);
        for (var i = 0; i < choices.Count; i++)
        {
            var choice = choices[i];
            var icon = icons is not null && i < icons.Count ? icons[i] : ChoiceIcon(choice, i);
            AddPopupChoice(choice, choice, icon, IsDestructive(choice), keepOpenOnSelection);
        }
        ModalButtons.Children.Add(CreatePopupAction(cancelLabel, null, primary: false, destructive: false));
        return completion.Task;
    }
    private Task<string?> ShowPromptPopupAsync(
        string title,
        string message,
        string placeholder,
        int maxLength,
        string? initialValue = null,
        bool keepModalOpenOnCompletion = false)
    {
        var completion = BeginPopup(title, message, "✎");
        // Keep task editing more compact than the generic dialogs, but provide a real
        // multi-line editor with its own scrolling for long task descriptions.
        _popupPreferredWidth = keepModalOpenOnCompletion ? 380d : 300d;
        ApplyResponsivePopupBounds();

        var input = new Editor
        {
            Text = initialValue ?? string.Empty,
            Placeholder = placeholder,
            MaxLength = maxLength,
            AutoSize = EditorAutoSizeOption.Disabled,
            BackgroundColor = Colors.Transparent,
            TextColor = PopupColor("#303545", "#F1F2FA"),
            PlaceholderColor = PopupColor("#7E8599", "#9FA5BC"),
            FontSize = 14,
            HeightRequest = 152,
            VerticalOptions = LayoutOptions.Fill,
            Margin = new Thickness(0)
        };
        input.HandlerChanged += RoundedControl_HandlerChanged;
        var inputBorder = new Border
        {
            Padding = new Thickness(12, 8),
            HeightRequest = 170,
            BackgroundColor = PopupColor("#E1E6F0", "#1A1B26"),
            Stroke = new SolidColorBrush(PopupColor("#D1D7E3", "#292B3A")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(15) },
            Shadow = CreateNeumorphicShadow(0.24f),
            Content = input
        };
        ModalBody.Children.Add(inputBorder);
        ModalBody.Children.Add(new Label
        {
            Text = LocalizationService.FormatCharacterCounter((initialValue ?? string.Empty).Length, maxLength),
            FontSize = 10,
            TextColor = PopupColor("#7E8599", "#9FA5BC")
        });
        input.TextChanged += (_, args) =>
        {
            if (ModalBody.Children.Count > 1 && ModalBody.Children[1] is Label counter)
                counter.Text = LocalizationService.FormatCharacterCounter((args.NewTextValue ?? string.Empty).Length, maxLength);
        };

        var actions = new Grid { ColumnSpacing = 9 };
        actions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        actions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        actions.Add(CreatePopupAction(keepModalOpenOnCompletion ? "Back to task options" : "Cancel", null, primary: false, destructive: false,
            customAction: () =>
            {
                if (keepModalOpenOnCompletion) CompletePopupInPlace(null);
                else CompletePopup(null);
            }), 0, 0);
        actions.Add(CreatePopupAction("Save changes", "__SAVE__", primary: true, destructive: false, customAction: () =>
        {
            if (keepModalOpenOnCompletion) CompletePopupInPlace(input.Text ?? string.Empty);
            else CompletePopup(input.Text ?? string.Empty);
        }), 1, 0);
        ModalButtons.Children.Add(actions);
        return completion.Task;
    }
    private async Task<bool> ShowConfirmationPopupAsync(string title, string message, string confirmLabel, bool destructive = false, bool keepModalOpenOnDismiss = false)
    {
        var completion = BeginPopup(title, message, destructive ? "⌫" : "?");
        var actions = new Grid { ColumnSpacing = 9 };
        actions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        actions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        actions.Add(CreatePopupAction(keepModalOpenOnDismiss ? "Back to task options" : "Cancel", null, primary: false, destructive: false,
            customAction: () =>
            {
                if (keepModalOpenOnDismiss) CompletePopupInPlace(null);
                else CompletePopup(null);
            }), 0, 0);
        actions.Add(CreatePopupAction(confirmLabel, confirmLabel, primary: !destructive, destructive: destructive,
            customAction: () =>
            {
                if (keepModalOpenOnDismiss) CompletePopupInPlace(confirmLabel);
                else CompletePopup(confirmLabel);
            }), 1, 0);
        ModalButtons.Children.Add(actions);
        return await completion.Task == confirmLabel;
    }
    private async Task ShowInfoPopupAsync(string title, string message, bool isError = false, byte[]? previewBytes = null, bool keepModalOpenOnDismiss = false)
    {
        var completion = BeginPopup(title, message, isError ? "!" : "✓");
        if (!isError && previewBytes is { Length: > 0 })
        {
            var previewImage = new Image
            {
                Source = ImageSource.FromStream(() => new MemoryStream(previewBytes)),
                Aspect = Aspect.AspectFit,
                HeightRequest = 190,
                HorizontalOptions = LayoutOptions.Fill,
                BackgroundColor = PopupColor("#E1E6F0", "#1A1B26")
            };
            ModalBody.Children.Add(new Border
            {
                Padding = new Thickness(5),
                HeightRequest = 200,
                BackgroundColor = PopupColor("#E1E6F0", "#1A1B26"),
                Stroke = new SolidColorBrush(PopupColor("#D1D9E5", "#383A4D")),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
                Shadow = CreateNeumorphicShadow(0.34f),
                Content = previewImage
            });
        }
        ModalButtons.Children.Add(CreatePopupAction("Got it", "OK", primary: true, destructive: false,
            customAction: () =>
            {
                if (keepModalOpenOnDismiss) CompletePopupInPlace("OK");
                else CompletePopup("OK");
            }));
        await completion.Task;
    }
    private Border CreatePopupAction(
        string text,
        string? result,
        bool primary,
        bool destructive,
        Action? customAction = null)
    {
        var background = destructive
            ? PopupColor("#FCE9ED", "#3A222E")
            : primary
                ? Color.FromArgb("#8875E8")
                : PopupColor("#F0F2F8", "#252736");
        var textColor = destructive
            ? PopupColor("#A83E59", "#FFC1D0")
            : primary
                ? Colors.White
                : PopupColor("#625A78", "#D8D0ED");
        var border = new Border
        {
            Padding = new Thickness(14, 12),
            MinimumHeightRequest = 46,
            BackgroundColor = background,
            Stroke = new SolidColorBrush(destructive ? PopupColor("#F0CDD5", "#573040") : primary ? Color.FromArgb("#8875E8") : PopupColor("#D1D9E5", "#383A4D")),
            StrokeThickness = primary ? 0 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Shadow = CreateNeumorphicShadow(primary ? 0.55f : 0.34f),
            HorizontalOptions = LayoutOptions.Fill
        };
        border.Content = new Label
        {
            Text = LocalizationService.Translate(text),
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = textColor,
            HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center,
            VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (customAction is not null) customAction();
            else CompletePopup(result);
        };
        border.GestureRecognizers.Add(tap);
        return border;
    }
    private void AddPopupChoice(string label, string result, string icon, bool destructive, bool keepOpenOnSelection = false)
    {
        var row = new Grid { ColumnSpacing = 11, Padding = new Thickness(11, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(34)));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(14)));

        View iconContent;
        var vectorIconSource = TaskActionIconSource(icon);
        if (vectorIconSource is not null)
        {
            var iconSize = 21d; // Keep the alarm glyph visually aligned with edit, palette, and delete icons.
            iconContent = new Image
            {
                Source = ImageSource.FromFile(vectorIconSource),
                WidthRequest = iconSize,
                HeightRequest = iconSize,
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
        }
        else
        {
            iconContent = new Label
            {
                Text = icon,
                FontSize = 16,
                TextColor = destructive ? PopupColor("#A83E59", "#FFC1D0") : Color.FromArgb("#8A76EB"),
                HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center,
                VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center
            };
        }

        var iconBack = new Border
        {
            WidthRequest = 34,
            HeightRequest = 34,
            BackgroundColor = destructive ? PopupColor("#FCE9ED", "#3A222E") : PopupColor("#E2E7F1", "#2A2C3D"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(11) },
            Stroke = new SolidColorBrush(PopupColor("#D1D9E5", "#383A4D")),
            StrokeThickness = 1,
            Shadow = CreateNeumorphicShadow(0.30f),
            VerticalOptions = LayoutOptions.Center,
            Content = iconContent
        };
        row.Add(iconBack, 0, 0);
        row.Add(new Label
        {
            Text = LocalizationService.Translate(label),
            FontSize = 13,
            FontAttributes = FontAttributes.None,
            TextColor = destructive ? PopupColor("#A83E59", "#FFC1D0") : PopupColor("#3F4559", "#E5E7F5"),
            VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap
        }, 1, 0);
        row.Add(new Label
        {
            Text = LocalizationService.IsPersian ? "‹" : "›",
            FontSize = 22,
            TextColor = PopupColor("#A29AB7", "#9189A8"),
            VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center,
            HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center
        }, 2, 0);

        var border = new Border
        {
            BackgroundColor = PopupColor("#F0F2F8", "#252736"),
            Stroke = new SolidColorBrush(PopupColor("#D1D9E5", "#383A4D")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(15) },
            Shadow = CreateNeumorphicShadow(0.38f),
            Content = row
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (keepOpenOnSelection) CompletePopupInPlace(result);
            else CompletePopup(result);
        };
        border.GestureRecognizers.Add(tap);
        ModalBody.Children.Add(border);
    }
    private static bool IsDestructive(string value) =>
        value.Contains("delete", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("remove", StringComparison.OrdinalIgnoreCase);
    private static string ChoiceIcon(string value, int index)
    {
        if (value.Contains("edit", StringComparison.OrdinalIgnoreCase)) return "edit";
        if (value.Contains("color", StringComparison.OrdinalIgnoreCase) || value.Contains("palette", StringComparison.OrdinalIgnoreCase)) return "palette";
        if (value.Contains("delete", StringComparison.OrdinalIgnoreCase) || value.Contains("remove", StringComparison.OrdinalIgnoreCase)) return "trash";
        return index switch { 0 => "✦", 1 => "◈", 2 => "○", _ => "•" };
    }
    private static string? TaskActionIconSource(string icon) => icon switch
    {
        "edit" => "locksy_edit_icon.svg",
        "palette" => "locksy_palette_icon.svg",
        "alarm" => "locksy_alarm_icon.svg",
        "trash" => "locksy_trash_icon.svg",
        _ => null
    };
    private void ModalBackdrop_Tapped(object? sender, TappedEventArgs e)
    {
        // Backdrop taps follow the same parent-navigation rules as Android Back:
        // connected settings/task subpages return to their parent menu; root pages close.
        // Taps inside ModalCard are handled by its child controls instead.
        if (_popupCompletionSource is not null)
        {
            if (_layoutPreviewActive)
            {
                CompletePopupInPlace("BACK_TO_LAYOUT");
            }
            else if (_settingsFlowActive && !string.Equals(_currentSettingsPage, "Settings", StringComparison.Ordinal))
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                    await NavigateSettingsPageAsync("Settings", animate: true, reverse: true));
            }
            else if (_taskPopupFlowActive && _taskPopupSubpageActive)
            {
                CompletePopupInPlace(null);
            }
            else
            {
                CompletePopup(null);
            }
        }
    }
    private void CompletePopupInPlace(string? result)
    {
        // Fade the current content out before releasing the awaiting flow. This keeps
        // the modal card and dimmed backdrop steady while the next page is prepared.
        var completion = _popupCompletionSource;
        _popupCompletionSource = null;
        if (completion is not null)
        {
            ModalCard.InputTransparent = true;
            _ = CompletePopupInPlaceAsync(completion, result);
        }
    }
    private async Task CompletePopupInPlaceAsync(TaskCompletionSource<string?> completion, string? result)
    {
        try
        {
            var exitX = LocalizationService.IsPersian ? -12d : 12d;
            await Task.WhenAll(
                ModalBodyScroll.FadeTo(0, 85, Easing.CubicIn),
                ModalButtons.FadeTo(0, 85, Easing.CubicIn),
                ModalBodyScroll.TranslateTo(exitX, 0, 85, Easing.CubicIn),
                ModalButtons.TranslateTo(exitX, 0, 85, Easing.CubicIn),
                AnimatePopupHeaderAsync(0, exitX, 85, Easing.CubicIn));
        }
        catch (OperationCanceledException)
        {
            // The replacement page can take over if another transition is requested.
        }
        finally
        {
            completion.TrySetResult(result);
        }
    }
    private void CompletePopup(string? result)
    {
        ModalOverlay.IsVisible = false;
        ModalOverlay.Opacity = 0;
        ModalCard.InputTransparent = false;
        PageContent.InputTransparent = false;
        ModalCard.Scale = 0.96;
        ModalCard.MinimumHeightRequest = -1;
        _popupPreferredWidth = 380d;
        ApplyResponsivePopupBounds();
        ModalBodyScroll.Opacity = 1;
        ModalButtons.Opacity = 1;
        ModalBodyScroll.TranslationX = 0;
        ModalButtons.TranslationX = 0;
        SetPopupHeaderVisualState(1, 0);
        ModalBody.Children.Clear();
        ModalButtons.Children.Clear();
        var completion = _popupCompletionSource;
        _popupCompletionSource = null;
        completion?.TrySetResult(result);
    }
}
