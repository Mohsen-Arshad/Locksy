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
    private async void SettingsButton_Clicked(object? sender, EventArgs e)
    {
        _settingsFlowActive = true;
        _currentSettingsPage = "Settings";
        var completion = BeginPopup(
            "Settings",
            "Manage your app theme, language, custom wallpaper and app information.",
            "settings");

        // Settings is a single persistent modal. Sub-pages replace its content instead
        // of completing this popup and opening another one on top of it. It stays content-sized.
        await NavigateSettingsPageAsync("Settings", animate: false);
        await completion.Task;
        _settingsFlowActive = false;
        _currentSettingsPage = "Settings";
    }
    private async Task NavigateSettingsPageAsync(
        string page,
        bool animate = true,
        bool reverse = false,
        Action? beforeRender = null)
    {
        if (_popupCompletionSource is null || _settingsTransitionRunning)
            return;

        _settingsTransitionRunning = true;
        try
        {
            var rtlSign = LocalizationService.IsPersian ? -1d : 1d;
            var exitX = (reverse ? 1d : -1d) * rtlSign * 18d;
            var enterX = -exitX;

            if (animate)
            {
                await Task.WhenAll(
                    ModalBodyScroll.FadeTo(0, 90, Easing.CubicIn),
                    ModalButtons.FadeTo(0, 90, Easing.CubicIn),
                    ModalBodyScroll.TranslateTo(exitX, 0, 90, Easing.CubicIn),
                    ModalButtons.TranslateTo(exitX, 0, 90, Easing.CubicIn),
                    AnimatePopupHeaderAsync(0, exitX, 90, Easing.CubicIn));

                // The user may close the modal during the short transition.
                if (_popupCompletionSource is null)
                    return;
            }

            beforeRender?.Invoke();
            ModalBody.Children.Clear();
            ModalButtons.Children.Clear();
            ModalBody.FlowDirection = LocalizationService.CurrentFlowDirection;
            ModalBodyScroll.FlowDirection = LocalizationService.CurrentFlowDirection;
            ModalButtons.FlowDirection = LocalizationService.CurrentFlowDirection;

            ConfigureSettingsPopupHeader(page);
            switch (page)
            {
                case "Settings":
                    RenderSettingsHome();
                    break;
                case "Language":
                    RenderLanguageSettings();
                    break;
                case "Background image":
                    RenderWallpaperBackgroundSettings();
                    break;
                case "About":
                    RenderAboutSettings();
                    break;
                default:
                    page = "Settings";
                    RenderSettingsHome();
                    ConfigureSettingsPopupHeader("Settings");
                    break;
            }
            _currentSettingsPage = page;

            _ = ModalBodyScroll.ScrollToAsync(0, 0, false);
            if (!animate)
            {
                ModalBodyScroll.Opacity = 1;
                ModalButtons.Opacity = 1;
                ModalBodyScroll.TranslationX = 0;
                ModalButtons.TranslationX = 0;
                SetPopupHeaderVisualState(1, 0);
                return;
            }

            ModalBodyScroll.Opacity = 0;
            ModalButtons.Opacity = 0;
            ModalBodyScroll.TranslationX = enterX;
            ModalButtons.TranslationX = enterX;
            SetPopupHeaderVisualState(0, enterX);
            await Task.WhenAll(
                ModalBodyScroll.FadeTo(1, 170, Easing.CubicOut),
                ModalButtons.FadeTo(1, 170, Easing.CubicOut),
                ModalBodyScroll.TranslateTo(0, 0, 170, Easing.CubicOut),
                ModalButtons.TranslateTo(0, 0, 170, Easing.CubicOut),
                AnimatePopupHeaderAsync(1, 0, 170, Easing.CubicOut));
        }
        finally
        {
            _settingsTransitionRunning = false;
        }
    }
    private void ConfigureSettingsPopupHeader(string page)
    {
        var title = page switch
        {
            "Settings" => "Settings",
            "Language" => "Language",
            "Background image" => "Background image",
            "About" => "About",
            _ => "Settings"
        };
        var message = page switch
        {
            "Settings" => "Manage your app theme, language, custom wallpaper and app information.",
            "Language" => "Choose your preferred language. English is the default.",
            "Background image" => _viewModel.Settings?.HasCustomBackground == true
                ? "A custom background is currently active."
                : "The default Locksy background is currently active.",
            "About" => "Locksy keeps important tasks visible directly on your Android lock screen. Customize task colors, reminders, theme, layout and background image. Your task list is stored locally on this device.",
            _ => string.Empty
        };
        var icon = page switch
        {
            "Settings" => "settings",
            "Language" => "Aa",
            "Background image" => "▧",
            "About" => "!",
            _ => "settings"
        };

        ModalSectionLabel.Text = LocalizedDisplayText("LOCKSY · SETTINGS");
        // Theme changes happen while this popup remains open, so refresh its manually
        // assigned surface colors as well as the dynamically-themed page beneath it.
        ModalCard.BackgroundColor = PopupColor("#F0F2F8", "#252736");
        ModalCard.Stroke = new SolidColorBrush(PopupColor("#CDD5E2", "#383A4D"));
        ModalTitle.FlowDirection = LocalizationService.CurrentFlowDirection;
        ModalSectionLabel.FlowDirection = LocalizationService.CurrentFlowDirection;
        ModalTitle.Text = LocalizedDisplayText(title);
        SetModalMessageText(message);
        ModalMessage.TextColor = PopupColor("#6F7589", "#ADB2C8");
        ModalMessage.IsVisible = !string.IsNullOrWhiteSpace(message);

        var useSettingsIcon = string.Equals(icon, "settings", StringComparison.OrdinalIgnoreCase);
        ModalIconImage.IsVisible = useSettingsIcon;
        ModalIconLabel.IsVisible = !useSettingsIcon;
        if (useSettingsIcon)
        {
            ModalIconImage.Source = ImageSource.FromFile("settings_gear.svg");
            ModalIconImage.WidthRequest = 28;
            ModalIconImage.HeightRequest = 28;
        }
        else
        {
            ModalIconLabel.Text = icon;
            ModalIconLabel.FontSize = icon == "Aa" ? 17 : 22;
        }
    }
    private void RenderSettingsHome()
    {
        AddThemeToggleRow();

        AddSettingsOption("Language", "Choose English or Persian for the app.", "Aa",
            () => NavigateSettingsPageAsync("Language"));

        var backgroundSubtitle = _viewModel.Settings?.HasCustomBackground == true
            ? "A custom background is currently active."
            : "The default Locksy background is currently active.";
        AddSettingsOption("Background image", backgroundSubtitle, "▧",
            () => NavigateSettingsPageAsync("Background image"));
        AddSettingsOption("About", "App information and project source code.", "!",
            () => NavigateSettingsPageAsync("About"));
        BuildSettingsFooter(isHome: true);
    }
    private void AddThemeToggleRow()
    {
        var isDark = (_viewModel.Settings?.IsDarkMode ?? false);
        const double switchWidth = 94;
        const double switchHeight = 50;
        const double thumbSize = 42;
        const double thumbTravel = switchWidth - thumbSize - 8;

        var themeSwitch = new Grid
        {
            WidthRequest = switchWidth,
            HeightRequest = switchHeight,
            FlowDirection = Microsoft.Maui.FlowDirection.LeftToRight,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            AutomationId = "LocksyThemeToggle"
        };

        var track = new Border
        {
            BackgroundColor = isDark ? Color.FromArgb("#29243D") : Color.FromArgb("#E8E1FF"),
            Stroke = new SolidColorBrush(isDark ? Color.FromArgb("#66568F") : Color.FromArgb("#C6B8FF")),
            StrokeThickness = 1.25,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(switchHeight / 2) }
        };
        themeSwitch.Add(track);

        // The inactive glyphs sit at the ends of the track; the moving violet thumb
        // carries a high-contrast white version of the selected sun/moon symbol.
        themeSwitch.Add(new Image
        {
            Source = "theme_sun.svg",
            WidthRequest = 20,
            HeightRequest = 20,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(14, 0, 0, 0),
            Opacity = isDark ? 0.24 : 0.10,
            InputTransparent = true
        });
        themeSwitch.Add(new Image
        {
            Source = "theme_moon.svg",
            WidthRequest = 20,
            HeightRequest = 20,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 14, 0),
            Opacity = isDark ? 0.10 : 0.24,
            InputTransparent = true
        });

        var activeGlyph = new Image
        {
            Source = isDark ? "theme_moon_active.svg" : "theme_sun_active.svg",
            WidthRequest = 23,
            HeightRequest = 23,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };
        var thumb = new Border
        {
            WidthRequest = thumbSize,
            HeightRequest = thumbSize,
            Margin = new Thickness(4, 0),
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb("#A18CFF"), 0f),
                    new GradientStop(Color.FromArgb("#7962E8"), 1f)
                }
            },
            Stroke = new SolidColorBrush(Color.FromArgb("#C3B6FF")),
            StrokeThickness = 0.8,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(thumbSize / 2) },
            Shadow = new Shadow
            {
                Brush = new SolidColorBrush(
                    isDark
                        ? Colors.Transparent
                        : Color.FromArgb("#6551C7")),
                Offset = isDark
                    ? Point.Zero
                    : new Point(0, 3),
                Radius = isDark ? 0 : 6,
                Opacity = isDark ? 0f : 0.42f
            },
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            TranslationX = isDark ? thumbTravel : 0,
            Content = activeGlyph,
            ZIndex = 2
        };
        themeSwitch.Add(thumb);
        SemanticProperties.SetDescription(themeSwitch, LocalizedDisplayText("Switch between dark and light theme"));
        AutomationProperties.SetName(themeSwitch, LocalizedDisplayText("Theme"));

        var labels = new VerticalStackLayout
        {
            Spacing = 3,
            VerticalOptions = LayoutOptions.Center,
            FlowDirection = LocalizationService.CurrentFlowDirection
        };
        labels.Children.Add(new Label
        {
            Text = LocalizedDisplayText("Theme"),
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            FlowDirection = LocalizationService.CurrentFlowDirection,
            TextColor = PopupColor("#363247", "#F0EDFF")
        });
        labels.Children.Add(new Label
        {
            Text = LocalizedDisplayText(isDark ? "Dark theme" : "Light theme"),
            FontSize = 11.5,
            FlowDirection = LocalizationService.CurrentFlowDirection,
            TextColor = PopupColor("#756E8E", "#B2AACB")
        });

        var row = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 14,
            Padding = new Thickness(16, 12),
            MinimumHeightRequest = 76,
            FlowDirection = LocalizationService.CurrentFlowDirection
        };
        row.Add(labels, 0, 0);
        row.Add(themeSwitch, 1, 0);

        var rowBorder = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(PopupColor("#F6F2FF", "#282438"), 0f),
                    new GradientStop(PopupColor("#EAE4FF", "#211E31"), 1f)
                }
            },
            Stroke = new SolidColorBrush(PopupColor("#D0C4FF", "#564879")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(19) },
            Shadow = CreateNeumorphicShadow(0.36f),
            Content = row
        };
        ModalBody.Children.Add(rowBorder);

        // Tapping either the switch or its whole settings row toggles the theme.
        // The guard inside ToggleThemeInlineAsync prevents double activation if a tap bubbles.
        var switchTap = new TapGestureRecognizer();
        switchTap.Tapped += async (_, _) =>
            await ToggleThemeInlineAsync(themeSwitch, thumb, activeGlyph, track, !(_viewModel.Settings?.IsDarkMode ?? false), thumbTravel);
        themeSwitch.GestureRecognizers.Add(switchTap);
        var rowTap = new TapGestureRecognizer();
        rowTap.Tapped += async (_, _) =>
            await ToggleThemeInlineAsync(themeSwitch, thumb, activeGlyph, track, !(_viewModel.Settings?.IsDarkMode ?? false), thumbTravel);
        rowBorder.GestureRecognizers.Add(rowTap);
    }
    private async Task ToggleThemeInlineAsync(
        Grid switchSurface,
        Border thumb,
        Image activeGlyph,
        Border track,
        bool isDark,
        double thumbTravel)
    {
        if (_settingsTransitionRunning || _popupCompletionSource is null || !switchSurface.IsEnabled)
            return;

        switchSurface.IsEnabled = false;
        try
        {
            track.BackgroundColor = isDark ? Color.FromArgb("#29243D") : Color.FromArgb("#E8E1FF");
            track.Stroke = new SolidColorBrush(isDark ? Color.FromArgb("#66568F") : Color.FromArgb("#C6B8FF"));
            activeGlyph.Source = isDark ? "theme_moon_active.svg" : "theme_sun_active.svg";
            // Keep the movement compact and deterministic. Previously the theme change
            // followed a long multi-stage animation and also refreshed every task row,
            // which caused a noticeable pause when switching from dark to light.
            await Task.WhenAll(
                thumb.TranslateTo(isDark ? thumbTravel : 0, 0, 125, Easing.CubicOut),
                thumb.ScaleTo(0.97, 55, Easing.CubicOut));
            await thumb.ScaleTo(1, 40, Easing.CubicOut);

            // Let the final thumb position paint before switching AppTheme. MAUI can
            // invalidate many AppThemeBindings at once, especially on dark-to-light
            // transitions; yielding a frame keeps the toggle responsive and visible.
            await Task.Delay(32);
            ApplyThemePreference(isDark ? ThemePreference.Dark : ThemePreference.Light);
            // Let AppThemeBindings paint their new values before reconstructing the
            // settings popup's manually-colored controls. This avoids doing both visual
            // updates in one UI turn, which was most noticeable on dark-to-light changes.
            await Task.Delay(48);
            // Recreate only the settings surface so its manually assigned popup colors
            // match the new theme. Task palette colors are theme-independent.
            await NavigateSettingsPageAsync("Settings", animate: false);
        }
        finally
        {
            switchSurface.IsEnabled = true;
        }
    }
    private void RenderLanguageSettings()
    {
        var isPersian = LocalizationService.IsPersian;
        AddSettingsOption("English", "Use the default English interface.", "EN",
            async () =>
            {
                if (LocalizationService.IsPersian)
                    await NavigateSettingsPageAsync("Language", animate: true, reverse: false, beforeRender: () =>
                    {
                        _viewModel.Settings?.SetLanguageCommand.Execute("en");
                    });
            }, isSelected: !isPersian);
        AddSettingsOption("فارسی", "Use the Persian right-to-left interface.", "فا",
            async () =>
            {
                if (!LocalizationService.IsPersian)
                    await NavigateSettingsPageAsync("Language", animate: true, reverse: false, beforeRender: () =>
                    {
                        _viewModel.Settings?.SetLanguageCommand.Execute("fa");
                    });
            }, isSelected: isPersian);
        BuildSettingsFooter(isHome: false);
    }
    private void RenderWallpaperBackgroundSettings()
    {
        AddSettingsOption("Choose image", "Select a personal image from this device.", "▧",
            PickCustomBackgroundWithinSettingsAsync);
        AddSettingsOption("Remove custom background", "Restore the built-in Locksy background.", "×",
            RemoveCustomBackgroundWithinSettingsAsync);
        BuildSettingsFooter(isHome: false);
    }
    private async Task PickCustomBackgroundWithinSettingsAsync()
    {
        var settings = _viewModel.Settings;
        if (settings is null) return;

        var result = await settings.ChooseCustomBackgroundAsync();
        if (result.Cancelled)
        {
            _wallpaperViewModel.StatusMessage = LocalizationService.Translate("No image was selected.");
            return;
        }

        if (!result.Succeeded)
        {
            SetModalMessageText(result.Message ?? LocalizationService.Translate("The selected image could not be saved."));
            ModalMessage.TextColor = Color.FromArgb("#D45C72");
            ModalMessage.IsVisible = true;
            return;
        }

        await NavigateSettingsPageAsync("Background image", animate: true);
    }
    private async Task RemoveCustomBackgroundWithinSettingsAsync()
    {
        if (_viewModel.Settings?.RemoveCustomBackground() != true)
            return;

        await NavigateSettingsPageAsync("Background image", animate: true);
    }
    private void RenderAboutSettings()
    {
        ModalBody.Children.Add(new Label
        {
            Text = $"{LocalizationService.Translate("Version")} {LocalizationService.LocalizeDigits(AppInfo.Current.VersionString)}",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = PopupColor("#625B7A", "#D9D4F0"),
            Margin = new Thickness(1, 2, 1, 2)
        });

        var repositoryLink = new Label
        {
            Text = ProjectGitHubUrl,
            FontSize = 11.5,
            TextColor = Color.FromArgb("#8A76EB"),
            TextDecorations = TextDecorations.Underline,
            LineBreakMode = LineBreakMode.WordWrap,
            FlowDirection = Microsoft.Maui.FlowDirection.LeftToRight,
            HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Start,
            Margin = new Thickness(1, 0, 1, 4)
        };
        var repositoryTap = new TapGestureRecognizer();
        repositoryTap.Tapped += async (_, _) => await OpenGitHubFromSettingsAsync();
        repositoryLink.GestureRecognizers.Add(repositoryTap);
        ModalBody.Children.Add(repositoryLink);
        AddSettingsOption("Open GitHub repository", "View the Locksy source code on GitHub.", "↗",
            OpenGitHubFromSettingsAsync);
        BuildSettingsFooter(isHome: false);
    }
    private async Task OpenGitHubFromSettingsAsync()
    {
        try
        {
            await (_viewModel.Settings?.OpenRepositoryAsync() ?? Task.CompletedTask);
        }
        catch
        {
            SetModalMessageText(LocalizationService.Translate("Could not open GitHub") + "\n" +
                                LocalizationService.Translate("Please check your internet connection and try again."));
            ModalMessage.TextColor = Color.FromArgb("#D45C72");
            ModalMessage.IsVisible = true;
        }
    }
    private void AddSettingsOption(
        string title,
        string subtitle,
        string icon,
        Func<Task> onSelected,
        bool isSelected = false)
    {
        var row = new Grid
        {
            ColumnSpacing = 11,
            Padding = new Thickness(10, 9),
            VerticalOptions = LayoutOptions.Fill
        };
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(38)));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(18)));

        var iconLabel = new Label
        {
            Text = icon,
            FontSize = icon is "EN" or "فا" or "Aa" ? 14 : 18,
            FontAttributes = icon == "!" ? FontAttributes.Bold : FontAttributes.None,
            TextColor = isSelected ? Colors.White : Color.FromArgb("#8A76EB"),
            HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center,
            VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center
        };
        row.Add(new Border
        {
            WidthRequest = 36,
            HeightRequest = 36,
            BackgroundColor = isSelected ? Color.FromArgb("#8875E8") : PopupColor("#E2E7F1", "#2A2C3D"),
            Stroke = new SolidColorBrush(isSelected ? Color.FromArgb("#8875E8") : PopupColor("#D1D9E5", "#383A4D")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            Content = iconLabel,
            VerticalOptions = LayoutOptions.Center
        }, 0, 0);

        var titleLabel = new Label
        {
            Text = LocalizedDisplayText(title),
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            FlowDirection = LocalizationService.CurrentFlowDirection,
            TextColor = PopupColor("#3F4559", "#E5E7F5"),
            LineBreakMode = LineBreakMode.WordWrap
        };
        var subtitleLabel = new Label
        {
            Text = LocalizedDisplayText(subtitle),
            FontSize = 10.5,
            FlowDirection = LocalizationService.CurrentFlowDirection,
            TextColor = PopupColor("#7E8599", "#9FA5BC"),
            HorizontalTextAlignment = TextAlignment.Justify,
            LineBreakMode = LineBreakMode.WordWrap
        };
        var labels = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center };
        labels.Children.Add(titleLabel);
        labels.Children.Add(subtitleLabel);
        row.Add(labels, 1, 0);
        row.Add(new Label
        {
            Text = isSelected ? "✓" : (LocalizationService.IsPersian ? "‹" : "›"),
            FontSize = isSelected ? 17 : 22,
            FontAttributes = isSelected ? FontAttributes.Bold : FontAttributes.None,
            TextColor = isSelected ? Color.FromArgb("#8A76EB") : PopupColor("#A29AB7", "#9189A8"),
            HorizontalTextAlignment = Microsoft.Maui.TextAlignment.Center,
            VerticalTextAlignment = Microsoft.Maui.TextAlignment.Center
        }, 2, 0);

        var border = new Border
        {
            BackgroundColor = isSelected ? PopupColor("#E6E0FF", "#332D49") : PopupColor("#F0F2F8", "#252736"),
            Stroke = new SolidColorBrush(isSelected ? Color.FromArgb("#AA9AF2") : PopupColor("#D1D9E5", "#383A4D")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(15) },
            Shadow = CreateNeumorphicShadow(isSelected ? 0.45f : 0.30f),
            Content = row
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            if (_settingsTransitionRunning || _popupCompletionSource is null)
                return;
            await onSelected();
        };
        border.GestureRecognizers.Add(tap);
        ModalBody.Children.Add(border);
    }
    private void BuildSettingsFooter(bool isHome)
    {
        if (isHome)
        {
            ModalButtons.Children.Add(CreatePopupAction("Close", null, primary: false, destructive: false));
            return;
        }

        var actions = new Grid { ColumnSpacing = 9 };
        actions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        actions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        actions.Add(CreatePopupAction("Back to settings", null, primary: false, destructive: false,
            customAction: async () => await NavigateSettingsPageAsync("Settings", animate: true, reverse: true)), 0, 0);
        actions.Add(CreatePopupAction("Close", null, primary: true, destructive: false), 1, 0);
        ModalButtons.Children.Add(actions);
    }
    private void ApplyThemePreference(ThemePreference preference) =>
        _viewModel.Settings?.SetThemePreferenceCommand.Execute(preference);
}
