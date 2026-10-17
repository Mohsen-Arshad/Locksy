using Android.App;
using Android.Graphics;
using AndroidColor = Android.Graphics.Color;
using AndroidPaint = Android.Graphics.Paint;
using Android.OS;
using AndroidX.Core.Content;
using Locksy.Models;
using Locksy.Services;
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

        DrawBackground(canvas, bitmapWidth, bitmapHeight, darkMode);
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

        // The same PNG bytes are returned to the UI for the success-dialog preview,
        // ensuring the preview matches the exact bitmap sent to WallpaperManager.
        return Task.FromResult(previewBytes);
    }

    private static void DrawBackground(Canvas canvas, int width, int height, bool dark)
    {
        // Original LockTodo treatment: soft gradient, quiet glow, fine grid, and circular accents.
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
        // The values remain responsive to screen width, but no longer vary by layout.
        var creationLabelSize = titleSize * 0.62f;
        var creationDateSize = subtitleSize * 0.72f;
        var creationDate = DateTime.Now.ToString("dddd, dd MMMM", CultureInfo.InvariantCulture).ToUpperInvariant();
        const string creationLabel = "CREATION DATE:";
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
        canvas.DrawText("LOCKSY  ·  FOCUS ON WHAT MATTERS", width / 2f, height * 0.965f, footer);
    }

    private static void DrawTitleAndDate(
        Canvas canvas,
        float margin,
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
        titlePaint.TextAlign = AndroidPaint.Align.Left;
        canvas.DrawText(title, margin, top + titleSize, titlePaint);

        if (!drawDate) return;
        using var subtitlePaint = new AndroidPaint
        {
            AntiAlias = true,
            Color = dark ? AndroidColor.Rgb(185, 225, 214) : AndroidColor.Rgb(69, 100, 87)
        };
        subtitlePaint.SetTypeface(Typeface.Create("sans-serif", TypefaceStyle.Normal)!);
        subtitlePaint.TextSize = subtitleSize;
        canvas.DrawText(subtitle, margin, top + titleSize + subtitleSize * 1.55f, subtitlePaint);
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
        var textX = margin + cardWidth * 0.16f;
        var maxTextWidth = cardWidth * 0.79f;
        var baseGap = Math.Clamp(height * 0.011f, 14f, 24f);
        var gap = baseGap;
        List<PreparedRow> rows = new();
        float totalHeight = 0f;

        // Preserve the original LockTodo font sizing unless this selected region cannot fit it.
        for (var attempt = 0; attempt < 32; attempt++)
        {
            rows = PrepareRows(tasks, bodySize, maxTextWidth, height, baseFontSize);
            var rowsHeight = rows.Sum(row => row.Height);
            totalHeight = rowsHeight + Math.Max(0, rows.Count - 1) * gap;
            if (totalHeight + headingHeight <= availableHeight || bodySize <= 12f) break;
            bodySize *= 0.94f;
            gap = Math.Min(baseGap, Math.Max(4f, (availableHeight - rowsHeight) / Math.Max(1, rows.Count - 1)));
        }

        if (totalHeight + headingHeight > availableHeight)
        {
            gap = 2f;
            while (totalHeight + headingHeight > availableHeight && bodySize > 9f)
            {
                bodySize *= 0.92f;
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
            DrawTitleAndDate(canvas, margin, contentTop,
                headingTitleSize, headingSubtitleSize, dark,
                headingTitle!, headingSubtitle ?? string.Empty);
        }

        var y = contentTop + headingHeight;
        var radius = Math.Clamp(width * 0.035f, 22f, 46f);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (y + row.Height > regionBottom + 1f && i > 0) break;
            var cardBottom = Math.Min(regionBottom, y + row.Height);
            // A task keeps its chosen palette variant independently of app appearance.
            // Use an opaque fill so the same selection looks consistent in Light/Dark mode.
            var paletteColor = AndroidColor.ParseColor(
                TodoPalette.GetHex(row.Task.ColorKey, row.Task.ColorPaletteKey));
            var background = AndroidColor.Argb(255, paletteColor.R, paletteColor.G, paletteColor.B);
            using (var cardPaint = new AndroidPaint { AntiAlias = true, Color = background })
                canvas.DrawRoundRect(margin, y, margin + cardWidth, cardBottom, radius, radius, cardPaint);

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

            // The task number sits in its own warm-ivory badge, above every custom row tint.
            // This prevents strong background colors from visually swallowing the number.
            var centerY = y + row.Height / 2f;
            var badgeCenterX = margin + cardWidth * 0.075f;
            var badgeRadius = Math.Clamp(bodySize * 0.72f, 18f, 31f);
            using (var badgePaint = new AndroidPaint { AntiAlias = true, Color = AndroidColor.Rgb(255, 243, 220) })
                canvas.DrawCircle(badgeCenterX, centerY, badgeRadius, badgePaint);

            using var numberPaint = new AndroidPaint { AntiAlias = true, Color = AndroidColor.Rgb(18, 17, 20) };
            numberPaint.SetTypeface(Typeface.Create("sans-serif", TypefaceStyle.Bold)!);
            numberPaint.TextSize = Math.Clamp(bodySize * 0.62f, 15f, 27f);
            numberPaint.TextAlign = AndroidPaint.Align.Center;
            var numberText = row.Task.IsCompleted ? "✓" : (startingNumber + i + 1).ToString("00", CultureInfo.InvariantCulture);
            canvas.DrawText(numberText, badgeCenterX, centerY + numberPaint.TextSize * 0.34f, numberPaint);

            // Reserve the clock's horizontal slot for every row so task titles remain
            // aligned, but draw the symbol only when this task currently has an active,
            // future reminder. Disabling/removing an alarm therefore removes the icon
            // from the next generated wallpaper instead of leaving a smoky stale clock.
            // Larger clock indicator: keep it legible on phone lock screens while
            // the reserved text slot below continues to protect title alignment.
            var clockRadius = Math.Clamp(bodySize * 0.31f, 12f, 19f);
            var clockCenterX = badgeCenterX + badgeRadius + clockRadius + Math.Max(5f, bodySize * 0.12f);
            clockCenterX = Math.Min(clockCenterX, textX - clockRadius - 5f);
            if (row.Task.IsReminderActive)
            {
                var clockColor = dark
                    ? AndroidColor.Rgb(197, 176, 255)
                    : AndroidColor.Rgb(112, 76, 211);
                using var clockPaint = new AndroidPaint { AntiAlias = true, Color = clockColor };
                clockPaint.SetStyle(AndroidPaint.Style.Stroke);
                clockPaint.StrokeWidth = Math.Max(3.2f, bodySize * 0.072f);
                canvas.DrawCircle(clockCenterX, centerY, clockRadius, clockPaint);
                canvas.DrawLine(clockCenterX, centerY, clockCenterX, centerY - clockRadius * 0.48f, clockPaint);
                canvas.DrawLine(clockCenterX, centerY, clockCenterX + clockRadius * 0.36f, centerY + clockRadius * 0.20f, clockPaint);
            }

            var foreground = AndroidColor.ParseColor(
                TodoPalette.GetTextHex(row.Task.ColorKey, row.Task.ColorPaletteKey));
            using var textPaint = new AndroidPaint { AntiAlias = true, Color = foreground };
            textPaint.SetTypeface(Typeface.Create("sans-serif", TypefaceStyle.Normal)!);
            textPaint.TextSize = bodySize;
            var lineHeight = bodySize * 1.22f;
            var textHeight = row.Lines.Count * lineHeight;
            var baseline = y + (row.Height - textHeight) / 2f + bodySize * 0.82f;
            foreach (var line in row.Lines)
            {
                canvas.DrawText(line, textX, baseline, textPaint);
                if (row.Task.IsCompleted)
                {
                    using var strike = new AndroidPaint { AntiAlias = true, Color = foreground };
                    strike.StrokeWidth = Math.Max(1.5f, bodySize * 0.045f);
                    var measured = textPaint.MeasureText(line);
                    canvas.DrawLine(textX, baseline - bodySize * 0.32f,
                        Math.Min(textX + measured, textX + maxTextWidth), baseline - bodySize * 0.32f, strike);
                }
                baseline += lineHeight;
            }
            y = cardBottom + gap;
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
        var lineHeight = fontSize * 1.22f;
        var baseCardHeight = Math.Clamp(height * 0.072f, 88f, 142f) * (fontSize / Math.Max(1f, baseFontSize));
        foreach (var task in tasks)
        {
            using var measurePaint = new AndroidPaint { AntiAlias = true };
            measurePaint.SetTypeface(Typeface.Create("sans-serif", TypefaceStyle.Normal)!);
            measurePaint.TextSize = fontSize;
            var lines = WrapText(task.Title, measurePaint, maxTextWidth);
            var textBlockHeight = lines.Count * lineHeight;
            var cardHeight = Math.Max(baseCardHeight, textBlockHeight + fontSize * 0.82f);
            rows.Add(new PreparedRow(task, lines, cardHeight));
        }
        return rows;
    }

    private static List<string> WrapText(string text, AndroidPaint paint, float maxWidth)
    {
        var result = new List<string>();
        var paragraphs = (text ?? string.Empty).Replace("\r", string.Empty).Split('\n');
        foreach (var paragraph in paragraphs)
        {
            var current = string.Empty;
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = current.Length == 0 ? word : $"{current} {word}";
                if (paint.MeasureText(candidate) <= maxWidth)
                {
                    current = candidate;
                    continue;
                }

                if (current.Length > 0)
                {
                    result.Add(current);
                    current = string.Empty;
                }

                if (paint.MeasureText(word) <= maxWidth)
                {
                    current = word;
                    continue;
                }

                // Even a very long unbroken word is wrapped character by character.
                foreach (var character in word)
                {
                    var next = current + character;
                    if (current.Length > 0 && paint.MeasureText(next) > maxWidth)
                    {
                        result.Add(current);
                        current = character.ToString();
                    }
                    else
                    {
                        current = next;
                    }
                }
            }

            if (current.Length > 0 || paragraph.Length == 0)
                result.Add(current);
        }

        if (result.Count == 0) result.Add(string.Empty);
        return result;
    }

    private sealed record PreparedRow(TodoItem Task, List<string> Lines, float Height);
}
