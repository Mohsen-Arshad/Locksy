using Android.App;
using Android.Graphics;
using AndroidColor = Android.Graphics.Color;
using AndroidStaticLayout = Android.Text.StaticLayout;
using AndroidText = Android.Text;
using AndroidTextPaint = Android.Text.TextPaint;
using AndroidTextDirectionHeuristics = Android.Text.TextDirectionHeuristics;
using AndroidTextLayout = Android.Text.Layout;
using AndroidPaint = Android.Graphics.Paint;
using Android.OS;
using AndroidX.Core.Content;
using Locksy.Helpers;
using Locksy.Models;
using Locksy.Services;
using Locksy.Services.Abstractions;
using System.Globalization;

namespace Locksy.Platforms.Android;

public sealed class AndroidWallpaperService : IWallpaperService
{
    private const int MaxRows = 8;

    public Task<byte[]> ApplyLockScreenAsync(
        IReadOnlyList<TodoItem> todos,
        bool darkMode,
        WallpaperLayoutSettings settings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Empty is intentional: DrawTodos skips task cards, but DrawBackground still
        // creates a fresh wallpaper bitmap that clears any previously rendered tasks.

        var context = global::Android.App.Application.Context
            ?? throw new InvalidOperationException("Android application context is not available.");
        var metrics = context.Resources?.DisplayMetrics;
        var sourceWidth = Math.Max(1, metrics?.WidthPixels ?? 1080);
        var sourceHeight = Math.Max(1, metrics?.HeightPixels ?? 1920);
        // Scale by width only and preserve the actual device aspect ratio.
        // Independent minimums distort the bitmap on tablets and compact displays.
        var scale = Math.Min(1.0f, 1440f / sourceWidth);
        var bitmapWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var bitmapHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));

        using var bitmap = Bitmap.CreateBitmap(bitmapWidth, bitmapHeight, Bitmap.Config.Argb8888!);
        using var canvas = new Canvas(bitmap);

        DrawBackground(canvas, bitmapWidth, bitmapHeight, darkMode, CustomWallpaperBackgroundService.GetSelectionForRendering());
        DrawTodos(canvas, bitmapWidth, bitmapHeight, todos.Take(MaxRows).ToList(), darkMode, settings);

        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream();
        bitmap.Compress(Bitmap.CompressFormat.Png!, 100, stream);
        var previewBytes = stream.ToArray();

        var wallpaperManager = WallpaperManager.GetInstance(context)
            ?? throw new InvalidOperationException("Android WallpaperManager is not available.");
        if (Build.VERSION.SdkInt < BuildVersionCodes.N)
            throw new NotSupportedException("Lock-screen-only wallpaper updates require Android 7.0 (API 24) or later.");

        // Keep the destination explicitly lock-only. Prefer the Bitmap API on newer Android
        // builds; a few vendor WallpaperManager implementations handle this path differently
        // from SetStream. The fallback also uses LOCK only and never requests FLAG_SYSTEM.
        var wallpaperId = 0;
        try
        {
            wallpaperId = wallpaperManager.SetBitmap(bitmap, null, true, WallpaperManagerFlags.Lock);
        }
        catch (Exception)
        {
            // Try the PNG stream path below on vendor builds that reject SetBitmap.
        }

        if (wallpaperId == 0)
        {
            using var wallpaperStream = new MemoryStream(previewBytes, writable: false);
            wallpaperId = wallpaperManager.SetStream(wallpaperStream, null, true, WallpaperManagerFlags.Lock);
        }

        if (wallpaperId == 0)
        {
            throw new InvalidOperationException(
                "Android did not confirm the lock-screen wallpaper update. Retry once. If this phone uses a custom Xiaomi/POCO lock-screen theme, switch its lock-screen style to Classic/Default and retry; regular apps cannot override a theme-owned lock screen.");
        }

        // A slideshow only advances after the bitmap was accepted by WallpaperManager.
        // Its receiver re-renders the next slide and schedules the following interval.
        WallpaperSlideshowAlarm.SyncAfterWallpaperApplied();

        // The same PNG bytes are returned to the UI for the success-dialog preview,
        // ensuring the preview matches the exact bitmap sent to WallpaperManager.
        return Task.FromResult(previewBytes);
    }

    private static bool TryDrawCustomBackground(Canvas canvas, int width, int height, bool dark, string imagePath)
    {
        try
        {
            var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeFile(imagePath, bounds);
            if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0)
                return false;

            var sampleSize = 1;
            var sourceAspect = bounds.OutWidth / (float)bounds.OutHeight;
            var targetAspect = width / (float)height;
            // Choose the sample size from the portion that will actually remain after
            // center-cropping. Using the whole image here would over-downsample panoramic
            // pictures and make the central portrait crop look blurry.
            var cropWidthAtSource = sourceAspect > targetAspect
                ? bounds.OutHeight * targetAspect
                : bounds.OutWidth;
            var cropHeightAtSource = sourceAspect > targetAspect
                ? bounds.OutHeight
                : bounds.OutWidth / targetAspect;
            while (sampleSize < 32 &&
                   cropWidthAtSource / (sampleSize * 2) >= width &&
                   cropHeightAtSource / (sampleSize * 2) >= height)
                sampleSize *= 2;

            var options = new BitmapFactory.Options { InSampleSize = sampleSize, InScaled = false };
            using var image = BitmapFactory.DecodeFile(imagePath, options);
            if (image is null || image.Width <= 0 || image.Height <= 0)
                return false;

            // Center-crop the selected image so it fills the lock screen without stretching.
            var decodedImageAspect = image.Width / (float)image.Height;
            var canvasAspect = width / (float)height;
            int cropWidth;
            int cropHeight;
            int left;
            int top;
            if (decodedImageAspect > canvasAspect)
            {
                cropHeight = image.Height;
                cropWidth = Math.Max(1, (int)Math.Round(image.Height * canvasAspect));
                left = (image.Width - cropWidth) / 2;
                top = 0;
            }
            else
            {
                cropWidth = image.Width;
                cropHeight = Math.Max(1, (int)Math.Round(image.Width / canvasAspect));
                left = 0;
                top = (image.Height - cropHeight) / 2;
            }

            using (var imagePaint = new AndroidPaint { AntiAlias = true, FilterBitmap = true, Color = AndroidColor.White })
            {
                canvas.DrawBitmap(image,
                    new global::Android.Graphics.Rect(left, top, left + cropWidth, top + cropHeight),
                    new global::Android.Graphics.Rect(0, 0, width, height), imagePaint);
            }

            // A subtle scrim keeps un-tinted task text readable over photographs without hiding the image.
            using var scrim = new AndroidPaint
            {
                AntiAlias = false,
                Color = dark ? AndroidColor.Argb(38, 0, 0, 0) : AndroidColor.Argb(28, 255, 255, 255)
            };
            canvas.DrawRect(0, 0, width, height, scrim);
            return true;
        }
        catch
        {
            // If the image was deleted or its format is unsupported, fall back to Locksy's built-in background.
            return false;
        }
    }

    private static void DrawBackground(Canvas canvas, int width, int height, bool dark, WallpaperBackgroundSelection selection)
    {
        if (selection.Mode == WallpaperBackgroundMode.CustomImage &&
            !string.IsNullOrWhiteSpace(selection.ImagePath) &&
            TryDrawCustomBackground(canvas, width, height, dark, selection.ImagePath))
            return;

        if (selection.Mode == WallpaperBackgroundMode.Preset &&
            WallpaperBackgroundCatalog.Find(selection.PresetKey) is { } preset)
        {
            DrawPresetBackground(canvas, width, height, preset);
            return;
        }

        // Original LockTodo treatment remains the selected Default background: soft gradient,
        // quiet glow, fine grid, and circular accents.
        var bgTop = dark ? AndroidColor.Rgb(12, 13, 22) : AndroidColor.Rgb(247, 245, 251);
        var bgBottom = dark ? AndroidColor.Rgb(31, 20, 48) : AndroidColor.Rgb(223, 214, 246);
        using (var gradient = new LinearGradient(0, 0, width, height, bgTop, bgBottom, Shader.TileMode.Clamp!))
        using (var paint = new AndroidPaint { AntiAlias = true })
        {
            paint.SetShader(gradient);
            canvas.DrawRect(0, 0, width, height, paint);
        }

        DrawGlow(canvas, width * 0.82f, height * 0.16f, width * 0.28f,
            dark ? AndroidColor.Argb(65, 176, 135, 255) : AndroidColor.Argb(48, 103, 76, 180));
        DrawGlow(canvas, width * 0.12f, height * 0.82f, width * 0.34f,
            dark ? AndroidColor.Argb(75, 132, 92, 255) : AndroidColor.Argb(55, 103, 156, 170));

        using var gridPaint = new AndroidPaint
        {
            AntiAlias = false,
            Color = dark ? AndroidColor.Argb(18, 255, 255, 255) : AndroidColor.Argb(18, 45, 35, 60)
        };
        gridPaint.StrokeWidth = Math.Max(1, width / 900f);
        var step = Math.Max(46, width / 12f);
        for (var x = 0f; x <= width; x += step) canvas.DrawLine(x, 0, x, height, gridPaint);
        for (var y = 0f; y <= height; y += step) canvas.DrawLine(0, y, width, y, gridPaint);

        using var ringPaint = new AndroidPaint
        {
            AntiAlias = true,
            Color = dark ? AndroidColor.Argb(40, 208, 170, 255) : AndroidColor.Argb(50, 81, 58, 180)
        };
        ringPaint.SetStyle(AndroidPaint.Style.Stroke);
        ringPaint.StrokeWidth = Math.Max(2, width / 180f);
        canvas.DrawCircle(width * 0.9f, height * 0.11f, width * 0.18f, ringPaint);
        canvas.DrawCircle(width * 0.9f, height * 0.11f, width * 0.13f, ringPaint);
    }

    private static void DrawPresetBackground(Canvas canvas, int width, int height, WallpaperBackgroundPreset preset)
    {
        var top = AndroidColor.ParseColor(preset.TopColor);
        var bottom = AndroidColor.ParseColor(preset.BottomColor);
        var accent = AndroidColor.ParseColor(preset.AccentColor);
        var secondary = AndroidColor.ParseColor(preset.SecondaryAccentColor);

        using (var gradient = new LinearGradient(0, 0, width * 0.72f, height, top, bottom, Shader.TileMode.Clamp!))
        using (var paint = new AndroidPaint { AntiAlias = true })
        {
            paint.SetShader(gradient);
            canvas.DrawRect(0, 0, width, height, paint);
        }

        DrawGlow(canvas, width * 0.83f, height * 0.16f, width * 0.31f, AndroidColor.Argb(72, accent.R, accent.G, accent.B));
        DrawGlow(canvas, width * 0.12f, height * 0.82f, width * 0.35f, AndroidColor.Argb(64, secondary.R, secondary.G, secondary.B));

        using var gridPaint = new AndroidPaint
        {
            AntiAlias = false,
            Color = AndroidColor.Argb(15, 255, 255, 255)
        };
        gridPaint.StrokeWidth = Math.Max(1, width / 900f);
        var step = Math.Max(46, width / 12f);
        for (var x = 0f; x <= width; x += step) canvas.DrawLine(x, 0, x, height, gridPaint);
        for (var y = 0f; y <= height; y += step) canvas.DrawLine(0, y, width, y, gridPaint);

        using var ringPaint = new AndroidPaint { AntiAlias = true, Color = AndroidColor.Argb(52, accent.R, accent.G, accent.B) };
        ringPaint.SetStyle(AndroidPaint.Style.Stroke);
        ringPaint.StrokeWidth = Math.Max(2, width / 180f);
        canvas.DrawCircle(width * 0.9f, height * 0.11f, width * 0.18f, ringPaint);
        canvas.DrawCircle(width * 0.9f, height * 0.11f, width * 0.13f, ringPaint);
    }

    private static void DrawGlow(Canvas canvas, float cx, float cy, float radius, AndroidColor color)
    {
        using var shader = new RadialGradient(cx, cy, radius, color, AndroidColor.Transparent, Shader.TileMode.Clamp!);
        using var paint = new AndroidPaint { AntiAlias = true };
        paint.SetShader(shader);
        canvas.DrawCircle(cx, cy, radius, paint);
    }

    private static void DrawTodos(Canvas canvas, int width, int height, IReadOnlyList<TodoItem> todos, bool dark, WallpaperLayoutSettings settings)
    {
        // All left/right-anchored content stays inside a 10% horizontal safe margin.
        var margin = width * 0.10f;
        var cardWidth = width - 2 * margin;
        var titleSize = Math.Clamp(width * 0.071f, 42f, 82f);
        var subtitleSize = Math.Clamp(width * 0.029f, 20f, 34f);
        // Keep Creation Date typography consistent across every wallpaper layout.
        // Persian glyphs have a smaller visual x-height than the uppercase English label,
        // so give the Persian label a slightly larger em size to achieve similar perceived weight.
        var creationLabelSize = titleSize * (LocalizationService.IsPersian ? 0.80f : 0.62f);
        var creationDateSize = subtitleSize * 0.72f;
        var creationDate = LocalizationService.IsPersian
            ? LocalizationService.LocalizeDigits(DateTime.Now.ToString("dddd، dd MMMM", CultureInfo.GetCultureInfo("fa-IR")))
            : DateTime.Now.ToString("dddd, dd MMMM", CultureInfo.InvariantCulture).ToUpperInvariant();
        var creationLabel = LocalizationService.IsPersian ? "تاریخ ایجاد" : "CREATION DATE:";
        var footerColor = dark ? AndroidColor.Rgb(140, 155, 165) : AndroidColor.Rgb(95, 99, 106);
        var layout = settings.Layout;
        if (layout == WallpaperLayout.ScreenAware)
        {
            // Android doesn't expose the user's customized clock coordinates. Use the real
            // screen aspect ratio to choose a starting profile and allow manual offset tuning.
            var heightToWidth = height / (float)Math.Max(1, width);
            layout = heightToWidth >= 2.15f
                ? WallpaperLayout.BelowTopClock
                : heightToWidth >= 1.78f
                    ? WallpaperLayout.CenteredList
                    : WallpaperLayout.SplitAroundCenterClock;
        }

        var offsetY = Math.Clamp(settings.VerticalOffset, -0.20f, 0.20f) * height;
        switch (layout)
        {
            case WallpaperLayout.SplitAroundCenterClock:
            {
                // Reserve a clear center band for the Android system clock. The creation
                // date is rendered as part of the upper task group, never detached at page top.
                var clockGapHalf = height * (Math.Clamp(settings.ClockClearance, 0.08f, 0.30f) / 2f);
                var gapCenter = Math.Clamp(height * 0.50f + offsetY, height * 0.34f, height * 0.70f);
                var upperRegionBottom = Math.Max(height * 0.28f, gapCenter - clockGapHalf);
                var lowerRegionTop = Math.Min(height * 0.84f, gapCenter + clockGapHalf);
                var upperRegionTop = Math.Clamp(height * 0.095f + offsetY, height * 0.02f, Math.Max(height * 0.02f, upperRegionBottom - height * 0.08f));
                var upperSpace = Math.Max(1f, upperRegionBottom - upperRegionTop);
                var lowerSpace = Math.Max(1f, height * 0.925f - lowerRegionTop);
                var split = todos.Count <= 1
                    ? todos.Count
                    : Math.Clamp((int)Math.Round(todos.Count * upperSpace / (upperSpace + lowerSpace)), 1, todos.Count - 1);
                DrawTaskGroup(canvas, width, height, todos.Take(split).ToList(), 0, margin, cardWidth,
                    upperRegionTop, upperRegionBottom, dark,
                    headingTitle: creationLabel, headingSubtitle: creationDate,
                    headingTitleSize: creationLabelSize, headingSubtitleSize: creationDateSize);
                DrawTaskGroup(canvas, width, height, todos.Skip(split).ToList(), split, margin, cardWidth,
                    lowerRegionTop, height * 0.925f, dark);
                break;
            }
            case WallpaperLayout.TasksBottom:
            {
                // Bottom anchoring applies to the heading and all cards as one combined block.
                // The last card sits near the screen bottom, above the fixed footer.
                var bottomAnchor = Math.Clamp(height * 0.925f + offsetY, height * 0.70f, height * 0.94f);
                DrawTaskGroup(canvas, width, height, todos, 0, margin, cardWidth,
                    height * 0.055f, bottomAnchor, dark,
                    alignToBottom: true,
                    headingTitle: creationLabel, headingSubtitle: creationDate,
                    headingTitleSize: creationLabelSize, headingSubtitleSize: creationDateSize);
                break;
            }
            case WallpaperLayout.AboveBottomClock:
            {
                // This is the Tasks top profile. The creation-date label moves with the list.
                DrawTaskGroup(canvas, width, height, todos, 0, margin, cardWidth,
                    Math.Clamp(height * 0.025f + offsetY, height * 0.01f, height * 0.42f),
                    Math.Clamp(height * 0.685f + offsetY, height * 0.28f, height * 0.92f), dark,
                    headingTitle: creationLabel, headingSubtitle: creationDate,
                    headingTitleSize: creationLabelSize, headingSubtitleSize: creationDateSize);
                break;
            }
            case WallpaperLayout.CenteredList:
            {
                // Center the heading and rows together, so the date stays attached to the tasks.
                DrawTaskGroup(canvas, width, height, todos, 0, margin, cardWidth,
                    Math.Clamp(height * 0.035f + offsetY, height * 0.02f, height * 0.32f),
                    Math.Clamp(height * 0.935f + offsetY, height * 0.65f, height * 0.96f), dark,
                    centerVertically: true,
                    headingTitle: creationLabel, headingSubtitle: creationDate,
                    headingTitleSize: creationLabelSize, headingSubtitleSize: creationDateSize);
                break;
            }
            default:
            {
                // Legacy "below top clock" profile (used by screen-aware on tall phones).
                DrawTaskGroup(canvas, width, height, todos, 0, margin, cardWidth,
                    Math.Clamp(height * 0.205f + offsetY, height * 0.08f, height * 0.46f), height * 0.925f, dark,
                    headingTitle: creationLabel, headingSubtitle: creationDate,
                    headingTitleSize: creationLabelSize, headingSubtitleSize: creationDateSize);
                break;
            }
        }

        using var footer = new AndroidPaint { AntiAlias = true, Color = footerColor };
        footer.SetTypeface(Typeface.Create("sans-serif", TypefaceStyle.Normal)!);
        footer.TextSize = Math.Clamp(width * 0.025f, 18f, 29f);
        footer.TextAlign = AndroidPaint.Align.Center;
        var footerText = LocalizationService.IsPersian
            ? "کارهایت، مستقیم روی صفحه قفل"
            : "LOCKSY  ·  FOCUS ON WHAT MATTERS";
        DrawLocalizedText(canvas, footerText, width / 2f, height * 0.965f, footer);
    }

    private static void DrawTitleAndDate(
        Canvas canvas,
        float margin,
        float right,
        float top,
        float titleSize,
        float subtitleSize,
        bool dark,
        string title,
        string subtitle,
        bool drawDate = true)
    {
        using var titlePaint = new AndroidPaint
        {
            AntiAlias = true,
            Color = dark ? AndroidColor.White : AndroidColor.Rgb(31, 29, 38)
        };
        titlePaint.SetTypeface(Typeface.Create("sans-serif", TypefaceStyle.Bold)!);
        titlePaint.TextSize = titleSize;
        titlePaint.TextAlign = LocalizationService.IsPersian ? AndroidPaint.Align.Right : AndroidPaint.Align.Left;
        DrawLocalizedText(canvas, title, LocalizationService.IsPersian ? right : margin, top + titleSize, titlePaint);

        if (!drawDate) return;
        using var subtitlePaint = new AndroidPaint
        {
            AntiAlias = true,
            Color = dark ? AndroidColor.Rgb(185, 225, 214) : AndroidColor.Rgb(69, 100, 87)
        };
        subtitlePaint.SetTypeface(Typeface.Create("sans-serif", TypefaceStyle.Normal)!);
        subtitlePaint.TextSize = subtitleSize;
        subtitlePaint.TextAlign = LocalizationService.IsPersian ? AndroidPaint.Align.Right : AndroidPaint.Align.Left;
        DrawLocalizedText(canvas, subtitle, LocalizationService.IsPersian ? right : margin,
            top + titleSize + subtitleSize * 1.55f, subtitlePaint);
    }

    private static void DrawLocalizedText(Canvas canvas, string text, float x, float baseline, AndroidPaint paint)
    {
        if (string.IsNullOrEmpty(text)) return;

        // StaticLayout runs Android's full bidirectional algorithm. For a Persian
        // paragraph, force RTL as the paragraph base direction while preserving Latin
        // runs (Locksy, URLs, acronyms, numbers) as natural left-to-right islands.
        using var textPaint = new AndroidTextPaint
        {
            AntiAlias = true,
            Color = paint.Color,
            TextSize = paint.TextSize
        };
        textPaint.SetTypeface(paint.Typeface);
        var displayText = LocalizationService.PrepareTextForDisplay(text);
        var layoutWidth = Math.Max(1, (int)Math.Ceiling(paint.MeasureText(text) + paint.TextSize * 2f + 12f));
        using var layout = CreateTextLayout(displayText, textPaint, layoutWidth, 1f, justify: false);
        var layoutX = LocalizationService.IsPersian ? x - layoutWidth : x;
        var saveCount = canvas.Save();
        try
        {
            canvas.Translate(layoutX, baseline - layout.GetLineBaseline(0));
            layout.Draw(canvas);
        }
        finally
        {
            canvas.RestoreToCount(saveCount);
        }
    }

    private static AndroidStaticLayout CreateTextLayout(
        string text,
        AndroidTextPaint paint,
        int width,
        float lineSpacingMultiplier,
        bool justify,
        TaskTextDirection? textDirection = null)
    {
        var safeText = text ?? string.Empty;
        var effectiveDirection = textDirection ?? (LocalizationService.IsPersian
            ? TaskTextDirection.RightToLeft
            : TaskTextDirection.LeftToRight);
        var builder = AndroidStaticLayout.Builder.Obtain(safeText, 0, safeText.Length, paint, Math.Max(1, width))
            .SetAlignment(AndroidTextLayout.Alignment.AlignNormal!)
            .SetTextDirection(effectiveDirection == TaskTextDirection.RightToLeft
                ? AndroidTextDirectionHeuristics.Rtl!
                : AndroidTextDirectionHeuristics.Ltr!)
            .SetIncludePad(false)
            .SetLineSpacing(0f, lineSpacingMultiplier)
            .SetBreakStrategy(AndroidText.BreakStrategy.HighQuality)
            .SetHyphenationFrequency(AndroidText.HyphenationFrequency.None);

        // Inter-word justification is supported from Android 8 (API 26). Bidi-aware
        // shaping and wrapping remain active on the minimum supported API (24) regardless.
        if (justify && OperatingSystem.IsAndroidVersionAtLeast(26))
            builder.SetJustificationMode(AndroidText.JustificationMode.InterWord);

        return builder.Build();
    }

    private static void DrawTaskGroup(
        Canvas canvas,
        int width,
        int height,
        IReadOnlyList<TodoItem> tasks,
        int startingNumber,
        float margin,
        float cardWidth,
        float regionTop,
        float regionBottom,
        bool dark,
        bool centerVertically = false,
        bool alignToBottom = false,
        string? headingTitle = null,
        string? headingSubtitle = null,
        float headingTitleSize = 0f,
        float headingSubtitleSize = 0f)
    {
        if (tasks.Count == 0) return;

        var availableHeight = Math.Max(0f, regionBottom - regionTop);
        var hasHeading = !string.IsNullOrWhiteSpace(headingTitle);
        var headingGap = hasHeading ? Math.Max(8f, height * 0.008f) : 0f;
        var headingHeight = hasHeading
            ? headingTitleSize + headingSubtitleSize * 1.55f + headingGap
            : 0f;
        var baseFontSize = Math.Clamp(width * 0.039f, 23f, 43f);
        var bodySize = baseFontSize;
        var isPersian = LocalizationService.IsPersian;
        var textX = margin + cardWidth * (isPersian ? 0.04f : 0.205f);
        var maxTextWidth = cardWidth * (isPersian ? 0.72f : 0.735f);
        var baseGap = Math.Clamp(height * 0.011f, 14f, 24f);
        var gap = baseGap;
        List<PreparedRow> rows = new();
        float totalHeight = 0f;

        // Preserve the original font sizing unless the selected region cannot fit.
        // Dispose layouts from rejected font-size attempts to avoid accumulating JNI peers.
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var candidateRows = PrepareRows(tasks, bodySize, maxTextWidth, height, baseFontSize);
            var rowsHeight = candidateRows.Sum(row => row.Height);
            var candidateTotalHeight = rowsHeight + Math.Max(0, candidateRows.Count - 1) * gap;
            if (candidateTotalHeight + headingHeight <= availableHeight || bodySize <= 12f || attempt == 31)
            {
                rows = candidateRows;
                totalHeight = candidateTotalHeight;
                break;
            }

            DisposeRows(candidateRows);
            bodySize *= 0.94f;
            gap = Math.Min(baseGap, Math.Max(4f, (availableHeight - rowsHeight) / Math.Max(1, candidateRows.Count - 1)));
        }

        if (totalHeight + headingHeight > availableHeight)
        {
            gap = 2f;
            while (totalHeight + headingHeight > availableHeight && bodySize > 9f)
            {
                bodySize *= 0.92f;
                DisposeRows(rows);
                rows = PrepareRows(tasks, bodySize, maxTextWidth, height, baseFontSize);
                totalHeight = rows.Sum(row => row.Height) + Math.Max(0, rows.Count - 1) * gap;
            }
        }

        var combinedHeight = totalHeight + headingHeight;
        var contentTop = alignToBottom
            ? regionBottom - Math.Min(availableHeight, combinedHeight)
            : centerVertically
                ? regionTop + Math.Max(0f, (availableHeight - combinedHeight) / 2f)
                : regionTop;

        if (hasHeading)
        {
            DrawTitleAndDate(canvas, margin, width - margin, contentTop,
                headingTitleSize, headingSubtitleSize, dark,
                headingTitle!, headingSubtitle ?? string.Empty);
        }

        var y = contentTop + headingHeight;
        var radius = Math.Clamp(width * 0.035f, 22f, 46f);
        try
        {
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (y + row.Height > regionBottom + 1f && i > 0) break;
                var cardBottom = Math.Min(regionBottom, y + row.Height);
                // The "No color" task deliberately shows the selected background image through its card.
                if (row.Task.ColorKey != TodoPalette.NoColorKey)
                {
                    var paletteColor = AndroidColor.ParseColor(
                        TodoPalette.GetHex(row.Task.ColorKey, row.Task.ColorPaletteKey));
                    var background = AndroidColor.Argb(255, paletteColor.R, paletteColor.G, paletteColor.B);
                    using var cardPaint = new AndroidPaint { AntiAlias = true, Color = background };
                    canvas.DrawRoundRect(margin, y, margin + cardWidth, cardBottom, radius, radius, cardPaint);
                }

                using (var stroke = new AndroidPaint
                {
                    AntiAlias = true,
                    Color = dark ? AndroidColor.Argb(82, 220, 202, 255) : AndroidColor.Argb(70, 70, 90, 120)
                })
                {
                    stroke.SetStyle(AndroidPaint.Style.Stroke);
                    stroke.StrokeWidth = Math.Max(1f, width / 800f);
                    canvas.DrawRoundRect(margin, y, margin + cardWidth, cardBottom, radius, radius, stroke);
                }

                var centerY = y + row.Height / 2f;
                var badgeCenterX = isPersian
                    ? margin + cardWidth * 0.925f
                    : margin + cardWidth * 0.075f;
                var badgeRadius = Math.Clamp(bodySize * 0.72f, 18f, 31f);
                using (var badgePaint = new AndroidPaint { AntiAlias = true, Color = AndroidColor.Rgb(255, 243, 220) })
                    canvas.DrawCircle(badgeCenterX, centerY, badgeRadius, badgePaint);

                if (row.Task.IsCompleted)
                {
                    // Use the same accent check shape as the app's SVG icon set instead of
                    // relying on a font glyph whose size and baseline vary across devices.
                    using var completePaint = new AndroidPaint
                    {
                        AntiAlias = true,
                        Color = AndroidColor.ParseColor("#8875E8")
                    };
                    completePaint.SetStyle(AndroidPaint.Style.Stroke);
                    completePaint.StrokeWidth = Math.Clamp(bodySize * 0.12f, 2.8f, 6.2f);
                    completePaint.StrokeCap = AndroidPaint.Cap.Round;
                    completePaint.StrokeJoin = AndroidPaint.Join.Round;
                    var checkWidth = badgeRadius * 0.62f;
                    var checkStartX = badgeCenterX - checkWidth * 0.50f;
                    var checkMidX = badgeCenterX - checkWidth * 0.08f;
                    var checkEndX = badgeCenterX + checkWidth * 0.50f;
                    canvas.DrawLine(checkStartX, centerY + badgeRadius * 0.01f,
                        checkMidX, centerY + badgeRadius * 0.36f, completePaint);
                    canvas.DrawLine(checkMidX, centerY + badgeRadius * 0.36f,
                        checkEndX, centerY - badgeRadius * 0.40f, completePaint);
                }
                else
                {
                    using var numberPaint = new AndroidPaint { AntiAlias = true, Color = AndroidColor.Rgb(18, 17, 20) };
                    numberPaint.SetTypeface(Typeface.Create("sans-serif", TypefaceStyle.Bold)!);
                    numberPaint.TextSize = Math.Clamp(bodySize * 0.62f, 15f, 27f);
                    numberPaint.TextAlign = AndroidPaint.Align.Center;
                    var numberText = LocalizationService.LocalizeDigits((startingNumber + i + 1).ToString("00", CultureInfo.InvariantCulture));
                    canvas.DrawText(numberText, badgeCenterX, centerY + numberPaint.TextSize * 0.34f, numberPaint);
                }

                // Reserve an alarm slot for all rows so titles remain aligned.
                var clockBadgeRadius = badgeRadius;
                var clockGap = Math.Max(5f, bodySize * 0.12f);
                var clockCenterX = isPersian
                    ? badgeCenterX - badgeRadius - clockBadgeRadius - clockGap
                    : badgeCenterX + badgeRadius + clockBadgeRadius + clockGap;
                clockCenterX = isPersian
                    ? Math.Max(clockCenterX, textX + maxTextWidth + clockBadgeRadius + 4f)
                    : Math.Min(clockCenterX, textX - clockBadgeRadius - 4f);
                if (row.Task.IsReminderActive)
                {
                    using (var clockBadgePaint = new AndroidPaint { AntiAlias = true, Color = AndroidColor.Rgb(255, 243, 220) })
                        canvas.DrawCircle(clockCenterX, centerY, clockBadgeRadius, clockBadgePaint);

                    var clockIconRadius = Math.Clamp(bodySize * 0.38f, 11f, 18f);
                    var clockColor = AndroidColor.ParseColor("#8875E8");
                    using var clockPaint = new AndroidPaint { AntiAlias = true, Color = clockColor };
                    clockPaint.SetStyle(AndroidPaint.Style.Stroke);
                    clockPaint.StrokeWidth = Math.Max(2.4f, bodySize * 0.052f);
                    canvas.DrawCircle(clockCenterX, centerY, clockIconRadius, clockPaint);
                    canvas.DrawLine(clockCenterX, centerY, clockCenterX, centerY - clockIconRadius * 0.50f, clockPaint);
                    canvas.DrawLine(clockCenterX, centerY, clockCenterX + clockIconRadius * 0.38f, centerY + clockIconRadius * 0.22f, clockPaint);
                }

                var textTop = y + (row.Height - row.Layout.Height) / 2f;
                var saveCount = canvas.Save();
                try
                {
                    canvas.Translate(textX, textTop);
                    row.Layout.Draw(canvas);

                    if (row.Task.IsCompleted)
                    {
                        var foreground = AndroidColor.ParseColor(
                            TodoPalette.GetTextHex(row.Task.ColorKey, row.Task.ColorPaletteKey));
                        using var strike = new AndroidPaint { AntiAlias = true, Color = foreground };
                        strike.StrokeWidth = Math.Max(1.5f, bodySize * 0.045f);
                        for (var lineIndex = 0; lineIndex < row.Layout.LineCount; lineIndex++)
                        {
                            var lineLeft = row.Layout.GetLineLeft(lineIndex);
                            var lineRight = row.Layout.GetLineRight(lineIndex);
                            var strikeY = row.Layout.GetLineBaseline(lineIndex) - bodySize * 0.32f;
                            canvas.DrawLine(lineLeft, strikeY, lineRight, strikeY, strike);
                        }
                    }
                }
                finally
                {
                    canvas.RestoreToCount(saveCount);
                }

                y = cardBottom + gap;
            }
        }
        finally
        {
            DisposeRows(rows);
        }
    }

    private static List<PreparedRow> PrepareRows(
        IReadOnlyList<TodoItem> tasks,
        float fontSize,
        float maxTextWidth,
        int height,
        float baseFontSize)
    {
        var rows = new List<PreparedRow>(tasks.Count);
        var baseCardHeight = Math.Clamp(height * 0.072f, 88f, 142f) * (fontSize / Math.Max(1f, baseFontSize));
        foreach (var task in tasks)
        {
            var textColor = AndroidColor.ParseColor(TodoPalette.GetTextHex(task.ColorKey, task.ColorPaletteKey));
            var textPaint = new AndroidTextPaint { AntiAlias = true, Color = textColor, TextSize = fontSize };
            textPaint.SetTypeface(Typeface.Create("sans-serif", TypefaceStyle.Normal)!);
            AndroidStaticLayout? layout = null;
            try
            {
                // Use the same display-only RTL/LTR embedding helper as the MAUI task labels.
                // Do not render the raw stored title: classic RLE/LRE marks keep Latin runs
                // (such as Locksy or a URL) in natural character order on the Android lock screen.
                var effectiveDirection = TaskTextDirectionHelper.Resolve(
                    task.Title, task.TextDirection, LocalizationService.IsPersian);
                var displayTitle = TaskTextDirectionHelper.PrepareForDisplay(
                    task.Title, task.TextDirection, LocalizationService.IsPersian);
                // Only task text direction changes here. Number badges, color dots, card
                // geometry, and their locale-based placement remain unchanged.
                layout = CreateTextLayout(displayTitle, textPaint,
                    Math.Max(1, (int)Math.Ceiling(maxTextWidth)), 1.22f, justify: true,
                    textDirection: effectiveDirection);
                var cardHeight = Math.Max(baseCardHeight, layout.Height + fontSize * 0.82f);
                rows.Add(new PreparedRow(task, textPaint, layout, cardHeight));
            }
            catch
            {
                layout?.Dispose();
                textPaint.Dispose();
                throw;
            }
        }
        return rows;
    }

    private static void DisposeRows(IEnumerable<PreparedRow> rows)
    {
        foreach (var row in rows) row.Dispose();
    }

    private sealed class PreparedRow : IDisposable
    {
        public TodoItem Task { get; }
        public AndroidTextPaint Paint { get; }
        public AndroidStaticLayout Layout { get; }
        public float Height { get; }

        public PreparedRow(TodoItem task, AndroidTextPaint paint, AndroidStaticLayout layout, float height)
        {
            Task = task;
            Paint = paint;
            Layout = layout;
            Height = height;
        }

        public void Dispose()
        {
            Layout.Dispose();
            Paint.Dispose();
        }
    }

}
