using System.Text;
using System.Text.RegularExpressions;
using Locksy.Models;

namespace Locksy.Helpers;

/// <summary>Resolves and embeds task-title direction without coupling it to the app's UI language.</summary>
public static class TaskTextDirectionHelper
{
    private static readonly Regex EmbeddedLeftToRightRun = new(
        @"(?i:https?://[^\s]+)|(?<![A-Za-z0-9])[A-Za-z0-9]+(?:[._:/?#&=%+@'-][A-Za-z0-9]+)*(?:\s+[A-Za-z0-9]+(?:[._:/?#&=%+@'-][A-Za-z0-9]+)*)*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EmbeddedRightToLeftRun = new(
        @"[\u0590-\u08FF\uFB1D-\uFDFF\uFE70-\uFEFF]+(?:[\s\u200C\u200D]+[\u0590-\u08FF\uFB1D-\uFDFF\uFE70-\uFEFF]+)*",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns the requested direction, or detects it from the first Unicode letter.
    /// Digits, punctuation and emoji are weak direction indicators and are skipped.
    /// </summary>
    public static TaskTextDirection Resolve(
        string? text,
        TaskTextDirection requestedDirection = TaskTextDirection.Auto,
        bool fallbackRightToLeft = false)
    {
        if (requestedDirection is TaskTextDirection.LeftToRight or TaskTextDirection.RightToLeft)
            return requestedDirection;

        if (!string.IsNullOrEmpty(text))
        {
            foreach (var rune in text.EnumerateRunes())
            {
                if (!Rune.IsLetter(rune))
                    continue;

                return IsRightToLeftCodePoint(rune.Value)
                    ? TaskTextDirection.RightToLeft
                    : TaskTextDirection.LeftToRight;
            }
        }

        return fallbackRightToLeft ? TaskTextDirection.RightToLeft : TaskTextDirection.LeftToRight;
    }

    /// <summary>
    /// Adds display-only Unicode embedding marks. The stored task title is never modified.
    /// The outer embedding establishes paragraph direction; opposite-direction runs keep
    /// their natural order inside mixed Persian/Latin titles.
    /// </summary>
    public static string PrepareForDisplay(
        string? value,
        TaskTextDirection requestedDirection,
        bool fallbackRightToLeft = false)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? string.Empty;

        var effectiveDirection = Resolve(value, requestedDirection, fallbackRightToLeft);
        var mixedText = effectiveDirection == TaskTextDirection.RightToLeft
            ? EmbeddedLeftToRightRun.Replace(value, match => "\u202A" + match.Value + "\u202C")
            : EmbeddedRightToLeftRun.Replace(value, match => "\u202B" + match.Value + "\u202C");

        return effectiveDirection == TaskTextDirection.RightToLeft
            ? "\u202B" + mixedText + "\u202C"
            : "\u202A" + mixedText + "\u202C";
    }

    public static bool IsRightToLeftCodePoint(int value) =>
        value is >= 0x0590 and <= 0x08FF
        or >= 0xFB1D and <= 0xFDFF
        or >= 0xFE70 and <= 0xFEFF
        or >= 0x10800 and <= 0x10FFF
        or >= 0x1E800 and <= 0x1EEFF;
}
