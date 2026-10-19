using System.Globalization;
using System.Diagnostics;
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
    // Keep each settings page as a complete, detached MAUI layout tree. A cache hit swaps
    // two ContentView roots instead of allocating controls or reattaching dozens of children.
    private sealed class SettingsPageVisualSnapshot
    {
        public SettingsPageVisualSnapshot(VerticalStackLayout bodyRoot, VerticalStackLayout footerRoot)
        {
            BodyRoot = bodyRoot;
            FooterRoot = footerRoot;
        }

        public VerticalStackLayout BodyRoot { get; }
        public VerticalStackLayout FooterRoot { get; }
    }

    private readonly Dictionary<string, SettingsPageVisualSnapshot> _settingsPageVisualCache =
        new(StringComparer.Ordinal);

    // Warm background metadata while the Settings home is on screen, instead of making
    // the Background button wait for preferences and slideshow-file validation.
    private Task<WallpaperBackgroundSettingsSnapshot>? _backgroundSettingsSnapshotPrefetchTask;

    // Build dynamic popup/page trees while detached, then attach each root once. This avoids
    // repeated native measure/layout passes as dozens of child controls are appended.
    private VerticalStackLayout? _stagedModalBodyRoot;
    private VerticalStackLayout? _stagedModalButtonsRoot;
    private bool _deferredPopupPresentation;
    // Prevent caching a temporary background-loading shell as if it were a real settings page.
    private bool _settingsTransientLoadingVisible;

    private void BeginStagedModalContentBuild()
    {
        _stagedModalBodyRoot ??= new VerticalStackLayout
        {
            Spacing = 8,
            FlowDirection = LocalizationService.CurrentFlowDirection
        };
        _stagedModalButtonsRoot ??= new VerticalStackLayout
        {
            Spacing = 8,
            FlowDirection = LocalizationService.CurrentFlowDirection
        };
    }

    private void DiscardStagedModalContentBuild()
    {
        _stagedModalBodyRoot = null;
        _stagedModalButtonsRoot = null;
    }

    private void CommitStagedModalContentBuild()
    {
        if (_stagedModalBodyRoot is { } bodyRoot)
            ModalBodyHost.Content = bodyRoot;
        if (_stagedModalButtonsRoot is { } buttonsRoot)
            ModalButtonsHost.Content = buttonsRoot;

        DiscardStagedModalContentBuild();

        if (!_deferredPopupPresentation)
            return;

        _deferredPopupPresentation = false;
        ModalOverlay.Opacity = 0;
        ModalCard.Scale = 0.94;
        ModalOverlay.IsVisible = true;
        _ = ModalOverlay.FadeToAsync(1, 160, Easing.CubicOut);
        _ = ModalCard.ScaleToAsync(1, 180, Easing.CubicOut);
    }

    // The roots are created lazily because a popup may be closed and detached entirely.
    // Keeping the historic property names avoids changing every render helper.
    private VerticalStackLayout ModalBody
    {
        get
        {
            if (_stagedModalBodyRoot is { } stagedBody)
                return stagedBody;

            if (ModalBodyHost.Content is VerticalStackLayout body)
                return body;

            body = new VerticalStackLayout
            {
                Spacing = 8,
                FlowDirection = LocalizationService.CurrentFlowDirection
            };
            ModalBodyHost.Content = body;
            return body;
        }
    }

    private VerticalStackLayout ModalButtons
    {
        get
        {
            if (_stagedModalButtonsRoot is { } stagedButtons)
                return stagedButtons;

            if (ModalButtonsHost.Content is VerticalStackLayout footer)
                return footer;

            footer = new VerticalStackLayout
            {
                Spacing = 8,
                FlowDirection = LocalizationService.CurrentFlowDirection
            };
            ModalButtonsHost.Content = footer;
            return footer;
        }
    }

    private void CacheCurrentSettingsPageVisuals()
    {
        if (ModalBodyHost.Content is not VerticalStackLayout bodyRoot ||
            ModalButtonsHost.Content is not VerticalStackLayout footerRoot ||
            (bodyRoot.Children.Count == 0 && footerRoot.Children.Count == 0))
            return;

        _settingsPageVisualCache[_currentSettingsPage] =
            new SettingsPageVisualSnapshot(bodyRoot, footerRoot);
        ModalBodyHost.Content = null;
        ModalButtonsHost.Content = null;
    }

    private bool TryRestoreSettingsPageVisuals(string page)
    {
        if (!_settingsPageVisualCache.TryGetValue(page, out var snapshot))
            return false;

        ModalBodyHost.Content = snapshot.BodyRoot;
        ModalButtonsHost.Content = snapshot.FooterRoot;
        return true;
    }

    private void InvalidateSettingsPageVisualCache(params string[] pages)
    {
        foreach (var page in pages)
            _settingsPageVisualCache.Remove(page);
    }

    private void ClearSettingsPageVisualCache() => _settingsPageVisualCache.Clear();

    private void InvalidateBackgroundSettingsVisualCache() =>
        InvalidateSettingsPageVisualCache("Settings", "Background image", "Slideshow interval");

    private static Task<WallpaperBackgroundSettingsSnapshot> CreateBackgroundSettingsSnapshotTask(
        SettingsViewModel settings)
    {
        var task = Task.Run(() =>
        {
#if DEBUG
            var started = Stopwatch.GetTimestamp();
#endif
            var snapshot = settings.GetBackgroundSettingsSnapshot();
#if DEBUG
            Debug.WriteLine($"[Locksy.Perf] Background snapshot workerMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1}; slideshowItems={snapshot.SlideshowItems.Count}");
#endif
            return snapshot;
        });

        // Observe a prefetch fault even if the user closes Settings without visiting Background.
        _ = task.ContinueWith(
            failedTask => Debug.WriteLine($"[Locksy.Perf] Background snapshot prefetch failed: {failedTask.Exception}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return task;
    }

    private async void SettingsButton_Clicked(object? sender, EventArgs e)
    {
        _settingsFlowActive = true;
        _currentSettingsPage = "Settings";
        var completion = BeginPopup(
            "Settings",
            "Manage your app theme, language, custom wallpaper and app information.",
            "settings",
            deferPresentationUntilReady: true);

        var settingsToPrime = _viewModel.Settings;
        _backgroundSettingsSnapshotPrefetchTask = null;

        // Let the Settings view commit and paint before starting filesystem/JSON work. The
        // snapshot is still prefetched during idle time, but no longer competes with first paint.
        await NavigateSettingsPageAsync("Settings", animate: false);
        if (ReferenceEquals(_popupCompletionSource, completion))
        {
            await Task.Delay(100);
            if (ReferenceEquals(_popupCompletionSource, completion) &&
                !_settingsTransitionRunning &&
                string.Equals(_currentSettingsPage, "Settings", StringComparison.Ordinal) &&
                _backgroundSettingsSnapshotPrefetchTask is null &&
                settingsToPrime is not null)
            {
                _backgroundSettingsSnapshotPrefetchTask = CreateBackgroundSettingsSnapshotTask(settingsToPrime);
            }
        }

        await completion.Task;
        _backgroundSettingsSnapshotPrefetchTask = null;
        _settingsFlowActive = false;
        _currentSettingsPage = "Settings";
    }

    private async Task NavigateSettingsPageAsync(
        string page,
        bool animate = true,
        bool reverse = false,
        Action? beforeRender = null,
        bool cacheCurrentPage = true)
    {
        if (_popupCompletionSource is null || _settingsTransitionRunning)
            return;

        // Non-animated Settings is also used for the initial/reopen path. Keep its warm tree
        // unless the caller explicitly invalidated it (theme/language changes already do so).
        // Background refreshes after data mutations still rebuild the target page.
        if (!animate && !string.Equals(page, "Settings", StringComparison.Ordinal))
        {
            InvalidateSettingsPageVisualCache(page);
            if (string.Equals(page, "Background image", StringComparison.Ordinal))
                InvalidateSettingsPageVisualCache("Settings");
        }

        Task<WallpaperBackgroundSettingsSnapshot>? backgroundSnapshotTask = null;
        if (string.Equals(page, "Background image", StringComparison.Ordinal) &&
            !_settingsPageVisualCache.ContainsKey(page) && _viewModel.Settings is { } settingsForSnapshot)
        {
            if (string.Equals(_currentSettingsPage, "Settings", StringComparison.Ordinal) &&
                _backgroundSettingsSnapshotPrefetchTask is { } prefetchedSnapshot)
            {
                backgroundSnapshotTask = prefetchedSnapshot;
                _backgroundSettingsSnapshotPrefetchTask = null;
            }
            else
            {
                backgroundSnapshotTask = CreateBackgroundSettingsSnapshotTask(settingsForSnapshot);
            }
        }

#if DEBUG
        var transitionStarted = Stopwatch.GetTimestamp();
#endif
        _settingsTransitionRunning = true;
        // Disable taps while a transition or progressive background render is in flight.
        ModalBodyScroll.InputTransparent = true;
        ModalButtonsHost.InputTransparent = true;
        Task? progressiveBackgroundRenderTask = null;

        try
        {
            var needsFreshBackgroundPage =
                string.Equals(page, "Background image", StringComparison.Ordinal) &&
                !_settingsPageVisualCache.ContainsKey(page) &&
                beforeRender is null;

            // For an animated first visit, move to a purposeful loading surface immediately.
            // Build the real page off-tree while the loading surface is visible, then swap roots
            // once. This avoids both a long frozen old page and the empty gray modal.
            if (needsFreshBackgroundPage && animate)
            {
                await NavigateFreshBackgroundPageWithLoadingSurfaceAsync(
                    reverse, cacheCurrentPage, backgroundSnapshotTask);
                return;
            }

            WallpaperBackgroundSettingsSnapshot? preparedBackgroundSnapshot = null;
            if (backgroundSnapshotTask is not null)
            {
                try
                {
                    preparedBackgroundSnapshot = await backgroundSnapshotTask;
                }
                catch (Exception snapshotException)
                {
                    Debug.WriteLine($"[Locksy.Perf] Background snapshot failed; renderer will retry: {snapshotException}");
                }

                if (_popupCompletionSource is null)
                    return;
            }

            // For explicit no-animation refreshes, keep the currently visible page in place
            // until the detached replacement tree has been populated.
            var backgroundPagePrebuilt = false;
            if (needsFreshBackgroundPage)
            {
                BeginStagedModalContentBuild();
                await RenderWallpaperBackgroundSettingsAsync(preparedBackgroundSnapshot);
                if (_popupCompletionSource is null)
                {
                    DiscardStagedModalContentBuild();
                    return;
                }
                backgroundPagePrebuilt = true;
            }

            var rtlSign = LocalizationService.IsPersian ? -1d : 1d;
            var exitX = (reverse ? 1d : -1d) * rtlSign * 18d;
            var enterX = -exitX;

            if (animate)
            {
                await Task.WhenAll(
                    ModalBodyScroll.FadeToAsync(0, 90, Easing.CubicIn),
                    ModalButtons.FadeToAsync(0, 90, Easing.CubicIn),
                    ModalBodyScroll.TranslateToAsync(exitX, 0, 90, Easing.CubicIn),
                    ModalButtons.TranslateToAsync(exitX, 0, 90, Easing.CubicIn),
                    AnimatePopupHeaderAsync(0, exitX, 90, Easing.CubicIn));

                if (_popupCompletionSource is null)
                    return;
            }

            // A pre-render action currently means the language is changing. Its text and
            // FlowDirection affect every cached page, so discard all retained visual trees.
            if (beforeRender is not null)
                ClearSettingsPageVisualCache();
            else if (animate && cacheCurrentPage)
                CacheCurrentSettingsPageVisuals();

            beforeRender?.Invoke();
            ModalBodyHost.Content = null;
            ModalButtonsHost.Content = null;
            ModalBodyScroll.FlowDirection = LocalizationService.CurrentFlowDirection;
            ConfigureSettingsPopupHeader(page);

#if DEBUG
            var pageBuildStarted = Stopwatch.GetTimestamp();
#endif
            var restoredFromCache = TryRestoreSettingsPageVisuals(page);
            if (restoredFromCache)
            {
                // The cached tree is already attached by TryRestore; discard any empty staging
                // roots left by an initial deferred popup open.
                DiscardStagedModalContentBuild();
                ModalBody.FlowDirection = LocalizationService.CurrentFlowDirection;
                ModalButtons.FlowDirection = LocalizationService.CurrentFlowDirection;
            }
            else
            {
                BeginStagedModalContentBuild();
                ModalBody.FlowDirection = LocalizationService.CurrentFlowDirection;
                ModalButtons.FlowDirection = LocalizationService.CurrentFlowDirection;
                if (!backgroundPagePrebuilt)
                switch (page)
                {
                    case "Settings":
                        RenderSettingsHome();
                        break;
                    case "Language":
                        RenderLanguageSettings();
                        break;
                    case "Background image":
                        // The renderer adds the immediately visible content synchronously,
                        // then yields between card batches. Run it alongside the entry animation.
                        progressiveBackgroundRenderTask =
                            RenderWallpaperBackgroundSettingsAsync(preparedBackgroundSnapshot);
                        break;
                    case "Slideshow interval":
                        RenderSlideshowIntervalSettings();
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
            }

            // Attach a complete page tree once rather than invalidating the live native layout
            // for each child that the render helpers add. This also presents a deferred first
            // popup only after its actual contents exist.
            CommitStagedModalContentBuild();

            _currentSettingsPage = page;
            _ = ModalBodyScroll.ScrollToAsync(0, 0, false);

            if (!animate)
            {
                ModalBodyScroll.Opacity = 1;
                ModalButtons.Opacity = 1;
                ModalBodyScroll.TranslationX = 0;
                ModalButtons.TranslationX = 0;
                SetPopupHeaderVisualState(1, 0);

                if (progressiveBackgroundRenderTask is not null)
                    await progressiveBackgroundRenderTask;

#if DEBUG
                if (progressiveBackgroundRenderTask is null)
                    Debug.WriteLine($"[Locksy.Perf] Settings page={page}; source={(restoredFromCache ? "cache" : "fresh-render")}; viewBuildMs={Stopwatch.GetElapsedTime(pageBuildStarted).TotalMilliseconds:F1}; bodyViews={ModalBody.Children.Count}");
#endif
                return;
            }

            ModalBodyScroll.Opacity = 0;
            ModalButtons.Opacity = 0;
            ModalBodyScroll.TranslationX = enterX;
            ModalButtons.TranslationX = enterX;
            SetPopupHeaderVisualState(0, enterX);

            var entryAnimationTask = Task.WhenAll(
                ModalBodyScroll.FadeToAsync(1, 170, Easing.CubicOut),
                ModalButtons.FadeToAsync(1, 170, Easing.CubicOut),
                ModalBodyScroll.TranslateToAsync(0, 0, 170, Easing.CubicOut),
                ModalButtons.TranslateToAsync(0, 0, 170, Easing.CubicOut),
                AnimatePopupHeaderAsync(1, 0, 170, Easing.CubicOut));

            if (progressiveBackgroundRenderTask is not null)
                await Task.WhenAll(entryAnimationTask, progressiveBackgroundRenderTask);
            else
                await entryAnimationTask;

            if (_popupCompletionSource is null)
                return;

#if DEBUG
            Debug.WriteLine($"[Locksy.Perf] Settings page={page}; source={(restoredFromCache ? "cache" : "fresh-render")}; viewBuildMs={Stopwatch.GetElapsedTime(pageBuildStarted).TotalMilliseconds:F1}; bodyViews={ModalBody.Children.Count}");
#endif
        }
        finally
        {
            ModalBodyScroll.InputTransparent = false;
            ModalButtonsHost.InputTransparent = false;
#if DEBUG
            Debug.WriteLine($"[Locksy.Perf] Settings transition page={page}; totalMs={Stopwatch.GetElapsedTime(transitionStarted).TotalMilliseconds:F1}");
#endif
            _settingsTransitionRunning = false;
        }
    }
    private async Task NavigateFreshBackgroundPageWithLoadingSurfaceAsync(
        bool reverse,
        bool cacheCurrentPage,
        Task<WallpaperBackgroundSettingsSnapshot>? snapshotTask)
    {
#if DEBUG
        var loadStarted = Stopwatch.GetTimestamp();
#endif
        var rtlSign = LocalizationService.IsPersian ? -1d : 1d;
        var exitX = (reverse ? 1d : -1d) * rtlSign * 18d;
        var enterX = -exitX;

        await Task.WhenAll(
            ModalBodyScroll.FadeToAsync(0, 90, Easing.CubicIn),
            ModalButtons.FadeToAsync(0, 90, Easing.CubicIn),
            ModalBodyScroll.TranslateToAsync(exitX, 0, 90, Easing.CubicIn),
            ModalButtons.TranslateToAsync(exitX, 0, 90, Easing.CubicIn),
            AnimatePopupHeaderAsync(0, exitX, 90, Easing.CubicIn));

        if (_popupCompletionSource is null)
            return;

        if (cacheCurrentPage)
            CacheCurrentSettingsPageVisuals();
        else
        {
            ModalBodyHost.Content = null;
            ModalButtonsHost.Content = null;
        }

        ModalBodyHost.Content = null;
        ModalButtonsHost.Content = null;
        ConfigureSettingsPopupHeader("Background image");
        _currentSettingsPage = "Background image";
        _settingsTransientLoadingVisible = true;

        // This is a real, themed loading surface rather than an empty placeholder. It paints
        // quickly and gives the UI thread a chance to render while the background content builds.
        var loadingBody = new VerticalStackLayout
        {
            Spacing = 14,
            Padding = new Thickness(12, 24),
            MinimumHeightRequest = 250,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
            FlowDirection = LocalizationService.CurrentFlowDirection
        };
        loadingBody.Children.Add(new ActivityIndicator
        {
            IsRunning = true,
            Color = Color.FromArgb("#8875E8"),
            WidthRequest = 30,
            HeightRequest = 30,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        });
        loadingBody.Children.Add(new Label
        {
            Text = LocalizedDisplayText("Preparing background options..."),
            FontSize = 12,
            TextColor = PopupColor("#6F7589", "#ADB2C8"),
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            FlowDirection = LocalizationService.CurrentFlowDirection
        });
        var loadingFooter = new VerticalStackLayout { Spacing = 8, FlowDirection = LocalizationService.CurrentFlowDirection };
        ModalBodyHost.Content = loadingBody;
        ModalButtonsHost.Content = loadingFooter;

        ModalBodyScroll.Opacity = 0;
        ModalButtons.Opacity = 0;
        ModalBodyScroll.TranslationX = enterX;
        ModalButtons.TranslationX = enterX;
        SetPopupHeaderVisualState(0, enterX);
        await Task.WhenAll(
            ModalBodyScroll.FadeToAsync(1, 170, Easing.CubicOut),
            ModalButtons.FadeToAsync(1, 170, Easing.CubicOut),
            ModalBodyScroll.TranslateToAsync(0, 0, 170, Easing.CubicOut),
            ModalButtons.TranslateToAsync(0, 0, 170, Easing.CubicOut),
            AnimatePopupHeaderAsync(1, 0, 170, Easing.CubicOut));

        if (_popupCompletionSource is null)
            return;

        // Yield after the loader animation so Android can actually paint it before parsing the
        // snapshot and constructing all preset/photo controls.
        await Task.Yield();
        WallpaperBackgroundSettingsSnapshot? snapshot = null;
        if (snapshotTask is not null)
        {
            try
            {
                snapshot = await snapshotTask;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Locksy.Perf] Background snapshot failed after loader display: {ex}");
            }
        }

        if (_popupCompletionSource is null)
            return;

        try
        {
            BeginStagedModalContentBuild();
            await RenderWallpaperBackgroundSettingsAsync(snapshot);
            if (_popupCompletionSource is null)
            {
                DiscardStagedModalContentBuild();
                return;
            }

            await Task.WhenAll(
                ModalBodyScroll.FadeToAsync(0, 75, Easing.CubicIn),
                ModalButtons.FadeToAsync(0, 75, Easing.CubicIn));

            ModalBodyHost.Content = null;
            ModalButtonsHost.Content = null;
            CommitStagedModalContentBuild();
            _settingsTransientLoadingVisible = false;
            _currentSettingsPage = "Background image";
            _ = ModalBodyScroll.ScrollToAsync(0, 0, false);

            ModalBodyScroll.Opacity = 0;
            ModalButtons.Opacity = 0;
            ModalBodyScroll.TranslationX = enterX * 0.25;
            ModalButtons.TranslationX = enterX * 0.25;
            await Task.WhenAll(
                ModalBodyScroll.FadeToAsync(1, 115, Easing.CubicOut),
                ModalButtons.FadeToAsync(1, 115, Easing.CubicOut),
                ModalBodyScroll.TranslateToAsync(0, 0, 115, Easing.CubicOut),
                ModalButtons.TranslateToAsync(0, 0, 115, Easing.CubicOut));
#if DEBUG
            Debug.WriteLine($"[Locksy.Perf] Background loading surface totalMs={Stopwatch.GetElapsedTime(loadStarted).TotalMilliseconds:F1}; items={snapshot?.SlideshowItems.Count ?? 0}; bodyViews={ModalBody.Children.Count}");
#endif
        }
        catch (Exception ex)
        {
            DiscardStagedModalContentBuild();
            Debug.WriteLine($"[Locksy.Perf] Background rendering failed: {ex}");
            if (_popupCompletionSource is null)
                return;

            // If failure happened after the real tree was attached (for example during a
            // cancellable fade), do not mutate the detached loading root or replace valid content.
            if (!ReferenceEquals(ModalBodyHost.Content, loadingBody) || !_settingsTransientLoadingVisible)
                return;

            loadingBody.Children.Clear();
            loadingBody.Children.Add(new Label
            {
                Text = LocalizedDisplayText("Background options could not be loaded."),
                FontSize = 12,
                TextColor = PopupColor("#D45C72", "#FF9EAF"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            });
            loadingFooter.Children.Clear();
            loadingFooter.Children.Add(CreatePopupAction(
                "Retry", null, primary: true, destructive: false,
                customAction: () => _ = NavigateSettingsPageAsync("Background image", animate: true, cacheCurrentPage: false)));
        }
    }

    private void ConfigureSettingsPopupHeader(string page)
    {
        var title = page switch
        {
            "Settings" => "Settings",
            "Language" => "Language",
            "Background image" => "Background image",
            "Slideshow interval" => "Slideshow interval",
            "About" => "About",
            _ => "Settings"
        };
        var message = page switch
        {
            "Settings" => "Manage your app theme, language, custom wallpaper and app information.",
            "Language" => "Choose your preferred language. English is the default.",
            "Background image" => _viewModel.Settings?.BackgroundSummary ?? "The default Locksy background is currently active.",
            "Slideshow interval" => "Choose how often the background changes. Select an interval below.",
            "About" => "Locksy keeps important tasks visible directly on your Android lock screen. Customize task colors, reminders, theme, layout and background image. Your task list is stored locally on this device.",
            _ => string.Empty
        };
        var icon = page switch
        {
            "Settings" => "settings",
            "Language" => "language",
            "Background image" => "background",
            "Slideshow interval" => "slideshow",
            "About" => "about",
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

        ConfigureModalIcon(icon);
    }
    private void RenderSettingsHome()
    {
        AddThemeToggleRow();

        AddSettingsOption("Language", "Choose English or Persian for the app.", "language",
            () => NavigateSettingsPageAsync("Language"));

        var backgroundSubtitle = _viewModel.Settings?.BackgroundSummary
            ?? "The default Locksy background is currently active.";
        AddSettingsOption("Background image", backgroundSubtitle, "background",
            () => NavigateSettingsPageAsync("Background image"));
        AddSettingsOption("About", "App information and project source code.", "about",
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
            WidthRequest = StandardIconSize,
            HeightRequest = StandardIconSize,
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
            WidthRequest = StandardIconSize,
            HeightRequest = StandardIconSize,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 14, 0),
            Opacity = isDark ? 0.10 : 0.24,
            InputTransparent = true
        });

        var activeGlyph = new Image
        {
            Source = isDark ? "theme_moon.svg" : "theme_sun.svg",
            WidthRequest = StandardIconSize,
            HeightRequest = StandardIconSize,
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
            BackgroundColor = PopupColor("#F8F9FD", "#222333"),
            Stroke = new SolidColorBrush(Color.FromArgb("#8875E8")),
            StrokeThickness = 0.8,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(thumbSize / 2) },
            Shadow = new Shadow
            {
                Brush = new SolidColorBrush(isDark ? Colors.Transparent : Color.FromArgb("#8875E8")),
                Offset = isDark ? Point.Zero : new Point(0, 2),
                Radius = isDark ? 0 : 4,
                Opacity = isDark ? 0f : 0.22f
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
            activeGlyph.Source = isDark ? "theme_moon.svg" : "theme_sun.svg";
            // Keep the movement compact and deterministic. Previously the theme change
            // followed a long multi-stage animation and also refreshed every task row,
            // which caused a noticeable pause when switching from dark to light.
            await Task.WhenAll(
                thumb.TranslateToAsync(isDark ? thumbTravel : 0, 0, 125, Easing.CubicOut),
                thumb.ScaleToAsync(0.97, 55, Easing.CubicOut));
            await thumb.ScaleToAsync(1, 40, Easing.CubicOut);

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
            ClearSettingsPageVisualCache();
            await NavigateSettingsPageAsync("Settings", animate: false, cacheCurrentPage: false);
        }
        finally
        {
            switchSurface.IsEnabled = true;
        }
    }
    private void RenderLanguageSettings()
    {
        var isPersian = LocalizationService.IsPersian;
        AddSettingsOption("English", "Use the default English interface.", "language-en",
            async () =>
            {
                if (LocalizationService.IsPersian)
                    await NavigateSettingsPageAsync("Language", animate: true, reverse: false, beforeRender: () =>
                    {
                        _viewModel.Settings?.SetLanguageCommand.Execute("en");
                    });
            }, isSelected: !isPersian);
        AddSettingsOption("فارسی", "Use the Persian right-to-left interface.", "language-fa",
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
    private async Task RenderWallpaperBackgroundSettingsAsync(WallpaperBackgroundSettingsSnapshot? snapshot)
    {
        var settings = _viewModel.Settings;
        if (settings is null)
        {
            BuildSettingsFooter(isHome: false);
            return;
        }

#if DEBUG
        var renderStarted = Stopwatch.GetTimestamp();
#endif
        // Never fall back to reading preferences or checking image paths synchronously on the UI thread.
        snapshot ??= await Task.Run(() => settings.GetBackgroundSettingsSnapshot());
        if (_popupCompletionSource is null)
            return;

        var backgroundMode = snapshot.Mode;
        var selectedPresetKey = snapshot.SelectedPresetKey;
        var currentBackgroundPresetKey = snapshot.CurrentPreviewPresetKey;
        var currentPreviewPath = snapshot.CurrentPreviewImagePath;
        var isSlideshowSelected = backgroundMode == WallpaperBackgroundMode.Slideshow;
        var slideshowItems = snapshot.SlideshowItems;

        // Add above-the-fold actions and footer first so the target page has real content
        // before the entrance transition begins; don't build all preset and photo controls at once.
        AddSettingsOption("Default Locksy background", "Use the original Locksy gradient and grid.", "default-background",
            async () =>
            {
                settings.SelectDefaultBackground();
                await NavigateSettingsPageAsync("Background image", animate: false);
            }, isSelected: backgroundMode == WallpaperBackgroundMode.Default,
            iconImageSize: 30, iconContainerSize: 48);
        if (backgroundMode == WallpaperBackgroundMode.Default)
            AddCurrentWallpaperBackgroundPreview(backgroundMode, selectedPresetKey, currentBackgroundPresetKey, currentPreviewPath);

        AddSettingsOption("Choose one photo", "Use one photo and crop it to fill the lock screen.", "photo-add",
            PickCustomBackgroundWithinSettingsAsync,
            isSelected: backgroundMode == WallpaperBackgroundMode.CustomImage,
            iconImageSize: 30, iconContainerSize: 48);
        if (backgroundMode == WallpaperBackgroundMode.CustomImage)
            AddCurrentWallpaperBackgroundPreview(backgroundMode, selectedPresetKey, currentBackgroundPresetKey, currentPreviewPath);

        BuildSettingsFooter(isHome: false);
        await Task.Delay(16);
        if (_popupCompletionSource is null)
            return;

        ModalBody.Children.Add(CreateSettingsSectionLabel("Built-in backgrounds"));
        var presets = settings.BackgroundPresets;
        for (var index = 0; index < presets.Count; index++)
        {
            var preset = presets[index];
            AddWallpaperPresetCard(preset, backgroundMode == WallpaperBackgroundMode.Preset &&
                                           string.Equals(selectedPresetKey, preset.Key, StringComparison.OrdinalIgnoreCase));

            // One preset per UI turn: lets Android lay out/paint between relatively complex cards.
            await Task.Delay(16);
            if (_popupCompletionSource is null)
                return;
        }

        if (backgroundMode == WallpaperBackgroundMode.Preset)
            AddCurrentWallpaperBackgroundPreview(backgroundMode, selectedPresetKey, currentBackgroundPresetKey, currentPreviewPath);

        ModalBody.Children.Add(CreateSettingsSectionLabel("Slideshow"));
        AddSettingsOption("Choose multiple photos", "Add several photos from your device to the slideshow.", "multiple-photos",
            ChooseSlideshowImagesWithinSettingsAsync,
            iconImageSize: 30, iconContainerSize: 48);
        AddSettingsOption("Use slideshow", "Cycle through your selected photos and built-in backgrounds.", "slideshow",
            async () =>
            {
                if (settings.ActivateSlideshow())
                {
                    await NavigateSettingsPageAsync("Background image", animate: false);
                    return;
                }

                SetModalMessageText(LocalizationService.Translate("Add at least two backgrounds to start a slideshow."));
                ModalMessage.TextColor = Color.FromArgb("#D45C72");
                ModalMessage.IsVisible = true;
            }, isSelected: isSlideshowSelected,
            iconImageSize: 30, iconContainerSize: 48);
        if (isSlideshowSelected)
            AddCurrentWallpaperBackgroundPreview(backgroundMode, selectedPresetKey, currentBackgroundPresetKey, currentPreviewPath);

        AddSlideshowIntervalPicker(settings, snapshot.SlideshowIntervalMinutes);
        await Task.Delay(16);
        if (_popupCompletionSource is null)
            return;

        await AddSlideshowItemsAsync(slideshowItems);

#if DEBUG
        Debug.WriteLine($"[Locksy.Perf] Background progressiveRenderMs={Stopwatch.GetElapsedTime(renderStarted).TotalMilliseconds:F1}; presets={presets.Count}; slideshowItems={slideshowItems.Count}; bodyViews={ModalBody.Children.Count}");
#endif
    }

    private Label CreateSettingsSectionLabel(string text) => new()
    {
        Text = LocalizedDisplayText(text),
        FontSize = 12,
        FontAttributes = FontAttributes.Bold,
        TextColor = PopupColor("#77718D", "#B5ADC9"),
        Margin = new Thickness(2, 8, 2, 1),
        FlowDirection = LocalizationService.CurrentFlowDirection
    };

    private void AddCurrentWallpaperBackgroundPreview(
        WallpaperBackgroundMode backgroundMode,
        string selectedPresetKey,
        string? currentBackgroundPresetKey,
        string? previewPath)
    {
        var modeTitle = backgroundMode switch
        {
            WallpaperBackgroundMode.Preset => WallpaperBackgroundCatalog.Find(selectedPresetKey)?.Title ?? "Built-in background",
            WallpaperBackgroundMode.CustomImage => "Custom photo",
            WallpaperBackgroundMode.Slideshow => "Slideshow",
            _ => "Default Locksy background"
        };

        var previewFrame = new Border
        {
            HeightRequest = 104,
            Padding = 0,
            Stroke = new SolidColorBrush(PopupColor("#CFC5F7", "#554A75")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(15) },
            Background = CreateBackgroundPreviewBrush(currentBackgroundPresetKey),
            HorizontalOptions = LayoutOptions.Fill
        };

        // Snapshot construction validates the path off the UI thread; avoid repeating File.Exists
        // while the page is being laid out. Image loading itself remains delegated to MAUI.
        if (!string.IsNullOrWhiteSpace(previewPath))
        {
            previewFrame.Content = new Image
            {
                Source = ImageSource.FromFile(previewPath),
                Aspect = Aspect.AspectFill,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill
            };
        }
        else
        {
            var decoration = new Grid { InputTransparent = true };
            decoration.Children.Add(new Ellipse
            {
                WidthRequest = 82,
                HeightRequest = 82,
                Stroke = new SolidColorBrush(Color.FromArgb("#70FFFFFF")),
                StrokeThickness = 2,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, -20, 16, 0)
            });
            decoration.Children.Add(new Label
            {
                Text = "LOCKSY",
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                CharacterSpacing = 3,
                TextColor = Color.FromArgb("#EFFFFFFF"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            });
            previewFrame.Content = decoration;
        }

        var summary = new Label
        {
            Text = LocalizedDisplayText(modeTitle),
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = PopupColor("#3F4559", "#E5E7F5"),
            FlowDirection = LocalizationService.CurrentFlowDirection
        };
        var caption = new Label
        {
            Text = LocalizedDisplayText("Current background preview · images fill the screen with center-crop."),
            FontSize = 10.5,
            TextColor = PopupColor("#7E8599", "#9FA5BC"),
            FlowDirection = LocalizationService.CurrentFlowDirection,
            LineBreakMode = LineBreakMode.WordWrap
        };
        ModalBody.Children.Add(new VerticalStackLayout
        {
            Spacing = 7,
            Margin = new Thickness(0, 0, 0, 5),
            Children = { summary, previewFrame, caption }
        });
    }

    private static Brush CreateBackgroundPreviewBrush(string? presetKey)
    {
        var preset = WallpaperBackgroundCatalog.Find(presetKey);
        var top = Color.FromArgb(preset?.TopColor ?? "#0C0D16");
        var bottom = Color.FromArgb(preset?.BottomColor ?? "#1F1430");
        return new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop(top, 0f),
                new GradientStop(bottom, 1f)
            }
        };
    }

    private void AddWallpaperPresetCard(WallpaperBackgroundPreset preset, bool isSelected)
    {
        var row = new Grid
        {
            ColumnSpacing = 9,
            Padding = new Thickness(9, 8),
            VerticalOptions = LayoutOptions.Center,
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(48)),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(36))
            }
        };
        row.Add(new Border
        {
            WidthRequest = 44,
            HeightRequest = 44,
            Background = CreateBackgroundPreviewBrush(preset.Key),
            Stroke = new SolidColorBrush(Color.FromArgb(preset.AccentColor)),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            VerticalOptions = LayoutOptions.Center
        }, 0, 0);

        var details = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        details.Children.Add(new Label
        {
            Text = LocalizedDisplayText(preset.Title),
            FontSize = 12.5,
            FontAttributes = FontAttributes.Bold,
            TextColor = PopupColor("#3F4559", "#E5E7F5"),
            FlowDirection = LocalizationService.CurrentFlowDirection
        });
        details.Children.Add(new Label
        {
            Text = LocalizedDisplayText(preset.Subtitle),
            FontSize = 10,
            TextColor = PopupColor("#7E8599", "#9FA5BC"),
            FlowDirection = LocalizationService.CurrentFlowDirection,
            LineBreakMode = LineBreakMode.WordWrap
        });
        row.Add(details, 1, 0);

        var useButton = new Button
        {
            Text = LocalizedDisplayText("Use"),
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            Padding = new Thickness(9, 0),
            HeightRequest = 34,
            MinimumWidthRequest = 54,
            BackgroundColor = isSelected ? Color.FromArgb("#8875E8") : PopupColor("#E2E7F1", "#303042"),
            TextColor = isSelected ? Colors.White : PopupColor("#625A78", "#E5E1F5"),
            CornerRadius = 10,
            VerticalOptions = LayoutOptions.Center
        };
        useButton.Clicked += async (_, _) =>
        {
            settingsPreset(preset.Key);
            await NavigateSettingsPageAsync("Background image", animate: false);
        };
        row.Add(useButton, 2, 0);

        var addButton = new ImageButton
        {
            Source = "icon_plus.svg",
            Aspect = Aspect.AspectFit,
            Padding = 6,
            WidthRequest = 34,
            HeightRequest = 34,
            BackgroundColor = PopupColor("#E2E7F1", "#303042"),
            CornerRadius = 10,
            VerticalOptions = LayoutOptions.Center
        };
        SemanticProperties.SetDescription(addButton, LocalizationService.Translate("Add preset to slideshow"));
        addButton.Clicked += async (_, _) =>
        {
            _viewModel.Settings?.AddPresetToSlideshow(preset.Key);
            await NavigateSettingsPageAsync("Background image", animate: false);
        };
        row.Add(addButton, 3, 0);

        ModalBody.Children.Add(new Border
        {
            BackgroundColor = isSelected ? PopupColor("#E9E2FF", "#332D49") : PopupColor("#F0F2F8", "#252736"),
            Stroke = new SolidColorBrush(isSelected ? Color.FromArgb("#9C8AF0") : PopupColor("#D1D9E5", "#383A4D")),
            StrokeThickness = isSelected ? 1.4 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Content = row
        });

        void settingsPreset(string key) => _viewModel.Settings?.SelectPresetBackground(key);
    }

    private void AddSlideshowIntervalPicker(SettingsViewModel settings, int intervalMinutes)
    {
        var currentInterval = LocalizedDisplayText($"Every {intervalMinutes} minutes");
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(24))
            },
            ColumnSpacing = 8,
            Padding = new Thickness(12, 10),
            FlowDirection = LocalizationService.CurrentFlowDirection
        };
        row.Add(new Label
        {
            Text = LocalizedDisplayText("Change every"),
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = PopupColor("#626A80", "#BFC3D6"),
            VerticalTextAlignment = TextAlignment.Center,
            FlowDirection = LocalizationService.CurrentFlowDirection
        }, 0, 0);
        row.Add(new Label
        {
            Text = currentInterval,
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = PopupColor("#8875E8", "#B7A9FF"),
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.End,
            FlowDirection = LocalizationService.CurrentFlowDirection
        }, 1, 0);
        row.Add(new Image
        {
            Source = ImageSource.FromFile("icon_chevron_down.svg"),
            WidthRequest = 22,
            HeightRequest = 22,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        }, 2, 0);

        var card = new Border
        {
            Padding = 0,
            BackgroundColor = PopupColor("#E9EAF2", "#20212E"),
            Stroke = new SolidColorBrush(PopupColor("#D1D6E3", "#38394C")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Shadow = CreateNeumorphicShadow(0.24f),
            Content = row
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            if (_settingsTransitionRunning || _popupCompletionSource is null)
                return;
            await NavigateSettingsPageAsync("Slideshow interval", animate: true);
        };
        card.GestureRecognizers.Add(tap);
        SemanticProperties.SetDescription(card, LocalizedDisplayText("Change slideshow interval"));
        ModalBody.Children.Add(card);
    }

    private void RenderSlideshowIntervalSettings()
    {
        var settings = _viewModel.Settings;
        if (settings is null)
        {
            BuildSettingsFooter(isHome: false, backLabelOverride: "Back to background", backPageOverride: "Background image");
            return;
        }

        ModalBody.Children.Add(new Label
        {
            Text = LocalizedDisplayText("Tap an interval to select it."),
            FontSize = 12,
            TextColor = PopupColor("#77718D", "#B5ADC9"),
            Margin = new Thickness(2, 0, 2, 6),
            FlowDirection = LocalizationService.CurrentFlowDirection
        });

        foreach (var interval in new[] { 5, 10, 15, 30, 60 })
            AddSlideshowIntervalOption(settings, interval);

        BuildSettingsFooter(isHome: false, backLabelOverride: "Back to background", backPageOverride: "Background image");
    }

    private void AddSlideshowIntervalOption(SettingsViewModel settings, int minutes)
    {
        var isSelected = settings.SlideshowIntervalMinutes == minutes;
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(30)),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(24))
            },
            ColumnSpacing = 10,
            Padding = new Thickness(13, 10),
            FlowDirection = LocalizationService.CurrentFlowDirection
        };

        var bullet = new Border
        {
            WidthRequest = 20,
            HeightRequest = 20,
            Padding = 0,
            BackgroundColor = isSelected ? Color.FromArgb("#8875E8") : Colors.Transparent,
            Stroke = new SolidColorBrush(isSelected ? Color.FromArgb("#8875E8") : PopupColor("#AEB6C8", "#666B83")),
            StrokeThickness = 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        if (isSelected)
        {
            bullet.Content = new Border
            {
                WidthRequest = 6,
                HeightRequest = 6,
                BackgroundColor = Colors.White,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(3) },
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
        }
        row.Add(bullet, 0, 0);
        row.Add(new Label
        {
            Text = LocalizedDisplayText($"Every {minutes} minutes"),
            FontSize = 13,
            FontAttributes = isSelected ? FontAttributes.Bold : FontAttributes.None,
            TextColor = PopupColor(isSelected ? "#5946B2" : "#3F4559", isSelected ? "#C3B8FF" : "#E5E7F5"),
            VerticalTextAlignment = TextAlignment.Center,
            FlowDirection = LocalizationService.CurrentFlowDirection
        }, 1, 0);
        if (isSelected)
        {
            row.Add(new Image
            {
                Source = ImageSource.FromFile("icon_check.svg"),
                WidthRequest = 22,
                HeightRequest = 22,
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }, 2, 0);
        }

        var card = new Border
        {
            Padding = 0,
            BackgroundColor = isSelected ? PopupColor("#E6E0FF", "#332D49") : PopupColor("#F0F2F8", "#252736"),
            Stroke = new SolidColorBrush(isSelected ? PopupColor("#AA9AF2", "#65558F") : PopupColor("#D1D9E5", "#383A4D")),
            StrokeThickness = isSelected ? 1.3 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Shadow = CreateNeumorphicShadow(isSelected ? 0.40f : 0.24f),
            Content = row
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            if (_settingsTransitionRunning || _popupCompletionSource is null)
                return;
            if (!isSelected)
            {
                settings.SetSlideshowIntervalMinutes(minutes);
                InvalidateSettingsPageVisualCache("Slideshow interval", "Background image", "Settings");
            }
            await NavigateSettingsPageAsync(
                "Background image", animate: true, reverse: true, cacheCurrentPage: false);
        };
        card.GestureRecognizers.Add(tap);
        SemanticProperties.SetDescription(card, LocalizedDisplayText($"Every {minutes} minutes"));
        ModalBody.Children.Add(card);
    }

    private async Task AddSlideshowItemsAsync(IReadOnlyList<WallpaperSlideshowItem> items)
    {
        ModalBody.Children.Add(new Label
        {
            Text = $"{LocalizedDisplayText("Slideshow items:")} {LocalizationService.LocalizeDigits($"{items.Count}/20")}",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = PopupColor("#77718D", "#B5ADC9"),
            Margin = new Thickness(2, 4, 2, 0),
            FlowDirection = LocalizationService.CurrentFlowDirection
        });

        if (items.Count == 0)
        {
            ModalBody.Children.Add(new Label
            {
                Text = LocalizedDisplayText("No slideshow items yet. Add photos or use the + button on a built-in background."),
                FontSize = 11,
                TextColor = PopupColor("#7E8599", "#9FA5BC"),
                LineBreakMode = LineBreakMode.WordWrap,
                Margin = new Thickness(2, 0, 2, 4),
                FlowDirection = LocalizationService.CurrentFlowDirection
            });
            return;
        }

        for (var index = 0; index < items.Count; index++)
        {
            if (_popupCompletionSource is null)
                return;

            var item = items[index];
            var row = new Grid
            {
                ColumnSpacing = 9,
                Padding = new Thickness(8, 6),
                ColumnDefinitions = { new ColumnDefinition(new GridLength(42)), new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(34)) }
            };
            View thumbnail;
            var preset = item.Kind == "preset" ? WallpaperBackgroundCatalog.Find(item.Key) : null;
            if (preset is not null)
            {
                thumbnail = new Border
                {
                    WidthRequest = 38,
                    HeightRequest = 38,
                    Background = CreateBackgroundPreviewBrush(preset.Key),
                    Stroke = new SolidColorBrush(Color.FromArgb(preset.AccentColor)),
                    StrokeThickness = 1,
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                    VerticalOptions = LayoutOptions.Center
                };
            }
            else
            {
                thumbnail = new Border
                {
                    WidthRequest = 38,
                    HeightRequest = 38,
                    Padding = 0,
                    Stroke = new SolidColorBrush(PopupColor("#D1D9E5", "#383A4D")),
                    StrokeThickness = 1,
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                    Content = new Image
                    {
                        Source = !string.IsNullOrWhiteSpace(item.ImagePath) ? ImageSource.FromFile(item.ImagePath) : null,
                        WidthRequest = 34,
                        HeightRequest = 34,
                        Aspect = Aspect.AspectFill
                    },
                    VerticalOptions = LayoutOptions.Center
                };
            }
            row.Add(thumbnail, 0, 0);
            row.Add(new Label
            {
                Text = LocalizedDisplayText(item.DisplayName),
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = PopupColor("#3F4559", "#E5E7F5"),
                VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.TailTruncation
            }, 1, 0);
            var remove = new ImageButton
            {
                Source = "locksy_trash_icon.svg",
                Aspect = Aspect.AspectFit,
                Padding = 5,
                WidthRequest = 32,
                HeightRequest = 32,
                BackgroundColor = PopupColor("#FCE9ED", "#3A222E"),
                CornerRadius = 10,
                VerticalOptions = LayoutOptions.Center
            };
            remove.Clicked += async (_, _) =>
            {
                _viewModel.Settings?.RemoveSlideshowItem(item.Id);
                await NavigateSettingsPageAsync("Background image", animate: false);
            };
            row.Add(remove, 2, 0);
            ModalBody.Children.Add(new Border
            {
                BackgroundColor = PopupColor("#F0F2F8", "#252736"),
                Stroke = new SolidColorBrush(PopupColor("#D1D9E5", "#383A4D")),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
                Content = row
            });

            // Avoid inflating every image row in the same UI frame on devices with many photos.
            if ((index + 1) % 2 == 0 && index + 1 < items.Count)
            {
                await Task.Delay(16);
                if (_popupCompletionSource is null)
                    return;
            }
        }
    }

    private async Task ChooseSlideshowImagesWithinSettingsAsync()
    {
        var settings = _viewModel.Settings;
        if (settings is null) return;
        var result = await settings.ChooseSlideshowImagesAsync();
        if (result.Cancelled) return;
        if (!result.Succeeded)
        {
            SetModalMessageText(result.Message ?? LocalizationService.Translate("The selected images could not be saved."));
            ModalMessage.TextColor = Color.FromArgb("#D45C72");
            ModalMessage.IsVisible = true;
            return;
        }
        await NavigateSettingsPageAsync("Background image", animate: false);
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

        InvalidateSettingsPageVisualCache("Background image", "Settings");
        await NavigateSettingsPageAsync("Background image", animate: true, cacheCurrentPage: false);
    }
    private async Task RemoveCustomBackgroundWithinSettingsAsync()
    {
        if (_viewModel.Settings?.RemoveCustomBackground() != true)
            return;

        InvalidateSettingsPageVisualCache("Background image", "Settings");
        await NavigateSettingsPageAsync("Background image", animate: true, cacheCurrentPage: false);
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
        AddSettingsOption("Open GitHub repository", "View the Locksy source code on GitHub.", "github",
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
        bool isSelected = false,
        double iconImageSize = StandardIconSize,
        double iconContainerSize = 42)
    {
        var row = new Grid
        {
            ColumnSpacing = 11,
            Padding = new Thickness(10, 9),
            VerticalOptions = LayoutOptions.Fill
        };
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(Math.Max(44d, iconContainerSize + 2d))));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        row.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(22)));

        Microsoft.Maui.Controls.View iconContent;
        if (IsLanguageBadgeIcon(icon))
        {
            iconContent = new Label
            {
                Text = LanguageBadgeText(icon),
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                CharacterSpacing = 0.25,
                TextColor = PopupColor("#8875E8", "#B7A9FF"),
                FlowDirection = Microsoft.Maui.FlowDirection.LeftToRight,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
        }
        else
        {
            iconContent = new Image
            {
                Source = ImageSource.FromFile(IconSourceFor(icon)),
                WidthRequest = iconImageSize,
                HeightRequest = iconImageSize,
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
        }
        row.Add(new Border
        {
            WidthRequest = iconContainerSize,
            HeightRequest = iconContainerSize,
            Padding = 0,
            BackgroundColor = PopupColor("#E2E7F1", "#2A2C3D"),
            Stroke = new SolidColorBrush(isSelected ? Color.FromArgb("#8875E8") : PopupColor("#D1D9E5", "#383A4D")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(iconContainerSize >= 46 ? 14 : 12) },
            Content = iconContent,
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
        row.Add(new Image
        {
            Source = ImageSource.FromFile(isSelected ? "icon_check.svg" : "icon_chevron_right.svg"),
            WidthRequest = StandardIconSize,
            HeightRequest = StandardIconSize,
            Aspect = Aspect.AspectFit,
            Rotation = !isSelected && LocalizationService.IsPersian ? 180 : 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
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
    private void BuildSettingsFooter(
        bool isHome,
        string? backLabelOverride = null,
        string? backPageOverride = null)
    {
        if (isHome)
        {
            ModalButtons.Children.Add(CreatePopupAction("Close", null, primary: false, destructive: false));
            return;
        }

        var actions = new Grid { ColumnSpacing = 9 };
        actions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        actions.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        var backLabel = backLabelOverride ?? "Back to settings";
        var backPage = backPageOverride ?? "Settings";
        actions.Add(CreatePopupAction(backLabel, null, primary: false, destructive: false,
            customAction: async () => await NavigateSettingsPageAsync(backPage, animate: true, reverse: true)), 0, 0);
        actions.Add(CreatePopupAction("Close", null, primary: true, destructive: false), 1, 0);
        ModalButtons.Children.Add(actions);
    }
    private void ApplyThemePreference(ThemePreference preference) =>
        _viewModel.Settings?.SetThemePreferenceCommand.Execute(preference);
}
