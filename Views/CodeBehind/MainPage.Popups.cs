using System.Globalization;
using Locksy.Helpers;
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
        // Animate the header as one native visual instead of starting separate animations for
        // each label and icon. ModalMessage remains independent because it occupies its own row.
        ModalHeaderContainer.Opacity = opacity;
        ModalHeaderContainer.TranslationX = translationX;
        ModalMessage.Opacity = opacity;
        ModalMessage.TranslationX = translationX;
    }
    private Task AnimatePopupHeaderAsync(double opacity, double translationX, uint duration, Easing easing)
    {
        // Same duration and same fade/slide effect, with four operations instead of ten.
        return Task.WhenAll(
            ModalHeaderContainer.FadeToAsync(opacity, duration, easing),
            ModalHeaderContainer.TranslateToAsync(translationX, 0, duration, easing),
            ModalMessage.FadeToAsync(opacity, duration, easing),
            ModalMessage.TranslateToAsync(translationX, 0, duration, easing));
    }
    private const double StandardIconSize = 30d;

    private static bool IsLanguageBadgeIcon(string key) => key.Trim().ToLowerInvariant() is
            "language-en" or "language-fa";

    private string LanguageBadgeText(string key) => key.Trim().ToLowerInvariant() switch
    {
        "language-en" => "EN",
        "language-fa" => "FA",
        _ => LocalizationService.IsPersian ? "FA" : "EN"
    };

    private void ConfigureModalIconContainer(string key)
    {
        var destructiveHeaderIcon = key.Equals("trash", StringComparison.OrdinalIgnoreCase)
            || key.Equals("delete", StringComparison.OrdinalIgnoreCase)
            || key.Equals("remove", StringComparison.OrdinalIgnoreCase);

        ModalIconContainer.WidthRequest = destructiveHeaderIcon ? 34 : 44;
        ModalIconContainer.HeightRequest = destructiveHeaderIcon ? 34 : 44;
        ModalIconContainer.BackgroundColor = destructiveHeaderIcon
            ? PopupColor("#FCE9ED", "#3A222E")
            : PopupColor("#F0F2F8", "#252736");
        ModalIconContainer.Stroke = new SolidColorBrush(
            destructiveHeaderIcon
                ? PopupColor("#D1D9E5", "#383A4D")
                : PopupColor("#D4DBE7", "#383A4D"));
        ModalIconContainer.StrokeThickness = 1;
        ModalIconContainer.StrokeShape = new RoundRectangle
        {
            CornerRadius = new CornerRadius(destructiveHeaderIcon ? 11 : 15)
        };
    }

    private void ConfigureModalIcon(string key)
    {
        if (IsLanguageBadgeIcon(key))
        {
            ModalIconImage.IsVisible = false;
            ModalIconTextLabel.Text = LanguageBadgeText(key);
            ModalIconTextLabel.TextColor = PopupColor("#8875E8", "#B7A9FF");
            ModalIconTextLabel.WidthRequest = 30;
            ModalIconTextLabel.HeightRequest = 30;
            ModalIconTextLabel.FontSize = 14;
            ModalIconTextLabel.FontAttributes = FontAttributes.Bold;
            ModalIconTextLabel.Opacity = ModalIconImage.Opacity;
            ModalIconTextLabel.TranslationX = ModalIconImage.TranslationX;
            ModalIconTextLabel.IsVisible = true;
            return;
        }

        ModalIconTextLabel.IsVisible = false;
        ModalIconImage.Source = ImageSource.FromFile(IconSourceFor(key));
        ModalIconImage.WidthRequest = StandardIconSize;
        ModalIconImage.HeightRequest = StandardIconSize;
        ModalIconImage.IsVisible = true;
        ModalIconImage.Aspect = Aspect.AspectFit;
        ModalIconImage.HorizontalOptions = LayoutOptions.Center;
        ModalIconImage.VerticalOptions = LayoutOptions.Center;
    }

    private static string IconSourceFor(string key) => key.Trim().ToLowerInvariant() switch
    {
        "settings" => "icon_settings.svg",
        "options" => "locksy_options_icon.svg",
        "language" => "icon_language.svg",
        "alarm" => "locksy_alarm_icon.svg",
        "edit" or "pencil" => "locksy_edit_icon.svg",
        "palette" or "color" => "locksy_palette_icon.svg",
        "trash" or "delete" or "remove" => "locksy_trash_icon.svg",
        "direction" => "locksy_text_direction_icon.svg",
        "direction-auto" => "icon_direction_auto.svg",
        "direction-ltr" => "icon_direction_ltr.svg",
        "direction-rtl" => "icon_direction_rtl.svg",
        "background" or "photo" => "icon_photo.svg",
        "photo-add" => "icon_photo_add.svg",
        "default-background" => "icon_default_background.svg",
        "multiple-photos" => "icon_multiple_photos.svg",
        "wallpaper" => "icon_wallpaper.svg",
        "about" or "info" => "icon_info.svg",
        "layout" or "placement" => "icon_layout.svg",
        "preview" => "icon_preview.svg",
        "check" or "success" => "icon_check.svg",
        "alert" or "error" or "warning" => "icon_alert.svg",
        "help" or "question" => "icon_help.svg",
        "plus" or "add" => "icon_plus.svg",
        "reorder" => "icon_reorder.svg",
        "more" => "icon_more.svg",
        "chevron-down" => "icon_chevron_down.svg",
        "chevron-right" => "icon_chevron_right.svg",
        "slideshow" => "icon_slideshow.svg",
        "slideshow-manage" => "icon_slideshow_manage.svg",
        "github" => "icon_github.svg",
        "task" => "icon_task.svg",
        _ => "icon_info.svg"
    };

    private TaskCompletionSource<string?> BeginPopup(
        string title,
        string message,
        string icon = "info",
        bool deferPresentationUntilReady = false)
    {
        // If another flow takes ownership of the modal, don't let a late wallpaper callback
        // overwrite the newly opened popup after the user leaves the progress dialog.
        if (_wallpaperProgressPopupActive)
        {
            _wallpaperProgressPopupActive = false;
            _wallpaperProgressPopupDismissed = true;
        }

        // A nested action can finish its result while keeping the modal surface open.
        // The next popup then replaces the content and animates inside the same card.
        var reuseVisibleModal = ModalOverlay.IsVisible && _popupCompletionSource is null;
        if (_popupCompletionSource is not null)
            CompletePopup(null);

        var deferFirstPresentation = deferPresentationUntilReady && !reuseVisibleModal;
        if (deferFirstPresentation)
            BeginStagedModalContentBuild();
        else
            DiscardStagedModalContentBuild();
        _deferredPopupPresentation = deferFirstPresentation;

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
        ConfigureModalIconContainer(icon);
        ConfigureModalIcon(icon);
        SetModalMessageText(message);
        ModalMessage.TextColor = PopupColor("#6F7589", "#ADB2C8");
        ModalMessage.IsVisible = !string.IsNullOrWhiteSpace(message);
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
        else if (!deferFirstPresentation)
        {
            ModalOverlay.Opacity = 0;
            ModalCard.Scale = 0.94;
            ModalOverlay.IsVisible = true;
            _ = ModalOverlay.FadeToAsync(1, 160, Easing.CubicOut);
            _ = ModalCard.ScaleToAsync(1, 180, Easing.CubicOut);
        }
        // For an expensive dynamic view, the caller builds its body/footer in detached roots
        // and CommitStagedModalContentBuild reveals the popup only after the roots are ready.
        return completion;
    }
    private async Task AnimatePopupContentInAsync()
    {
        // BeginPopup callers build the new controls synchronously after it returns.
        try
        {
            await Task.Delay(20);
            await Task.WhenAll(
                ModalBodyScroll.FadeToAsync(1, 145, Easing.CubicOut),
                ModalButtons.FadeToAsync(1, 145, Easing.CubicOut),
                ModalBodyScroll.TranslateToAsync(0, 0, 145, Easing.CubicOut),
                ModalButtons.TranslateToAsync(0, 0, 145, Easing.CubicOut),
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
        string popupIcon = "task",
        bool keepOpenOnSelection = false,
        bool keepOpenOnCancel = false)
    {
        var completion = BeginPopup(title, message, popupIcon);
        for (var i = 0; i < choices.Count; i++)
        {
            var choice = choices[i];
            var icon = icons is not null && i < icons.Count ? icons[i] : ChoiceIcon(choice, i);
            AddPopupChoice(choice, choice, icon, IsDestructive(choice), keepOpenOnSelection);
        }
        ModalButtons.Children.Add(CreatePopupAction(cancelLabel, null, primary: false, destructive: false,
            customAction: () =>
            {
                if (keepOpenOnCancel) CompletePopupInPlace(null);
                else CompletePopup(null);
            }));
        return completion.Task;
    }
    private Task<string?> ShowPromptPopupAsync(
        string title,
        string message,
        string placeholder,
        int maxLength,
        string? initialValue = null,
        bool keepModalOpenOnCompletion = false,
        TaskTextDirection textDirection = TaskTextDirection.Auto)
    {
        var completion = BeginPopup(title, message, "edit");
        // Keep task editing more compact than the generic dialogs, but provide a real
        // multi-line editor with its own scrolling for long task descriptions.
        _popupPreferredWidth = keepModalOpenOnCompletion ? 380d : 300d;
        ApplyResponsivePopupBounds();

        var input = new Editor
        {
            Text = initialValue ?? string.Empty,
            FlowDirection = ResolveTaskFlowDirection(initialValue, textDirection),
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
            if (textDirection == TaskTextDirection.Auto)
                input.FlowDirection = ResolveTaskFlowDirection(args.NewTextValue, TaskTextDirection.Auto);
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
        var completion = BeginPopup(title, message, destructive ? "trash" : "help");
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
        var completion = BeginPopup(title, message, isError ? "alert" : "check");
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
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(22)));

        var iconContent = new Image
        {
            Source = ImageSource.FromFile(IconSourceFor(destructive ? "trash" : icon)),
            WidthRequest = StandardIconSize,
            HeightRequest = StandardIconSize,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

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
        row.Add(new Image
        {
            Source = ImageSource.FromFile("icon_chevron_right.svg"),
            WidthRequest = StandardIconSize,
            HeightRequest = StandardIconSize,
            Aspect = Aspect.AspectFit,
            Rotation = LocalizationService.IsPersian ? 180 : 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
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
        if (value.Contains("alarm", StringComparison.OrdinalIgnoreCase) || value.Contains("reminder", StringComparison.OrdinalIgnoreCase)) return "alarm";
        if (value.Contains("direction", StringComparison.OrdinalIgnoreCase)) return "direction";
        if (value.Contains("language", StringComparison.OrdinalIgnoreCase)) return "language";
        if (value.Contains("photo", StringComparison.OrdinalIgnoreCase) || value.Contains("background", StringComparison.OrdinalIgnoreCase)) return "photo";
        if (value.Contains("delete", StringComparison.OrdinalIgnoreCase) || value.Contains("remove", StringComparison.OrdinalIgnoreCase)) return "trash";
        if (value.Contains("github", StringComparison.OrdinalIgnoreCase) || value.Contains("repository", StringComparison.OrdinalIgnoreCase)) return "github";
        return index switch { 0 => "task", 1 => "options", 2 => "palette", _ => "info" };
    }

    private void ModalBackdrop_Tapped(object? sender, TappedEventArgs e)
    {
        // A tap outside the modal dismisses the whole popup flow, not just its current
        // submenu. Mark looping popup flows so they cannot reopen themselves afterward.
        if (_popupCompletionSource is null)
            return;

        if (_taskPopupFlowActive)
            _dismissTaskPopupFlowRequested = true;
        if (_layoutPreviewActive)
            _dismissLayoutPopupFlowRequested = true;

        CompletePopup(null);
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
                ModalBodyScroll.FadeToAsync(0, 85, Easing.CubicIn),
                ModalButtons.FadeToAsync(0, 85, Easing.CubicIn),
                ModalBodyScroll.TranslateToAsync(exitX, 0, 85, Easing.CubicIn),
                ModalButtons.TranslateToAsync(exitX, 0, 85, Easing.CubicIn),
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
        if (_wallpaperProgressPopupActive)
        {
            _wallpaperProgressPopupActive = false;
            _wallpaperProgressPopupDismissed = true;
        }

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
        // Keep the active settings page detached in the bounded page cache. The current
        // SettingsViewModel invalidation hooks evict stale pages when theme, language or
        // background data changes, so reopening Settings does not need to rebuild every view.
        if (_settingsFlowActive &&
            ModalBodyHost.Content is VerticalStackLayout bodyRoot &&
            ModalButtonsHost.Content is VerticalStackLayout footerRoot &&
            (bodyRoot.Children.Count > 0 || footerRoot.Children.Count > 0))
        {
            _settingsPageVisualCache[_currentSettingsPage] =
                new SettingsPageVisualSnapshot(bodyRoot, footerRoot);
        }

        // Detach whole page roots instead of clearing child collections one item at a time.
        ModalBodyHost.Content = null;
        ModalButtonsHost.Content = null;
        DiscardStagedModalContentBuild();
        _deferredPopupPresentation = false;
        var completion = _popupCompletionSource;
        _popupCompletionSource = null;
        completion?.TrySetResult(result);
    }
}
