namespace Locksy.Services;

public sealed record TaskColorOption(
    string Key,
    string Name,
    string Swatch,
    string LightHex,
    string DarkHex,
    string LightTextHex,
    string DarkTextHex);

public static class TodoPalette
{
    public const string DefaultKey = "default";

    public static IReadOnlyList<TaskColorOption> Options { get; } = new[]
    {
        new TaskColorOption(DefaultKey, "Default", "⚪", "#F0F2F8", "#252736", "#34394B", "#F1F2FA"),
        new TaskColorOption("green", "Green", "🟩", "#DDF7E7", "#285A42", "#173A28", "#F0FFF4"),
        new TaskColorOption("blue", "Blue", "🟦", "#DCEBFF", "#2A4D70", "#1A3354", "#EEF7FF"),
        new TaskColorOption("amber", "Amber", "🟨", "#FFF0C2", "#695522", "#4B3810", "#FFF7D6"),
        new TaskColorOption("rose", "Rose", "🟥", "#FFE0E7", "#683544", "#572431", "#FFF0F4"),
        new TaskColorOption("violet", "Violet", "🟪", "#EAE1FF", "#514073", "#38265B", "#F6F0FF"),
        new TaskColorOption("teal", "Teal", "🔹", "#D7F8F4", "#245B58", "#154847", "#E7FFFC"),
        new TaskColorOption("orange", "Orange", "🟧", "#FFE4CF", "#6A4228", "#593015", "#FFF1E4"),
        new TaskColorOption("gray", "Slate", "⚫", "#E8EAF0", "#414858", "#343947", "#F5F6FC"),
        new TaskColorOption("mint", "Mint", "🟢", "#C9FBE7", "#28624F", "#14533F", "#E7FFF5"),
        new TaskColorOption("sky", "Sky Blue", "🔵", "#CDEEFF", "#285778", "#174B6D", "#EAF8FF"),
        new TaskColorOption("lilac", "Lilac", "🟣", "#F0DFFF", "#5B3F6D", "#58376E", "#FAF0FF"),
        new TaskColorOption("lemon", "Lemon", "🟡", "#FFF8B8", "#675B1F", "#5F4F00", "#FFFBD8"),
        new TaskColorOption("coral", "Coral", "🪸", "#FFDCD5", "#714137", "#7A3327", "#FFF0EB"),
        new TaskColorOption("periwinkle", "Periwinkle", "🔷", "#DFE2FF", "#394B7B", "#343F80", "#F0F2FF"),
        new TaskColorOption("peach", "Peach", "🍑", "#FFE1C9", "#714C30", "#70401D", "#FFF3E7"),
        new TaskColorOption("seafoam", "Seafoam", "🩵", "#CEF8F5", "#285B60", "#175257", "#EAFFFF")
    };

    public static bool IsValidKey(string? key) => Options.Any(option => option.Key == key);

    public static TaskColorOption GetOption(string? key) =>
        Options.FirstOrDefault(option => option.Key == key) ?? Options[0];

    public static string GetHex(string? key, bool darkMode)
    {
        var option = GetOption(key);
        return darkMode ? option.DarkHex : option.LightHex;
    }

    public static string GetTextHex(string? key, bool darkMode)
    {
        var option = GetOption(key);
        return darkMode ? option.DarkTextHex : option.LightTextHex;
    }
}

public enum WallpaperLayout
{
    BelowTopClock = 0,
    SplitAroundCenterClock = 1,
    AboveBottomClock = 2,
    CenteredList = 3,
    ScreenAware = 4,
    TasksBottom = 5
}

public enum ThemePreference
{
    System = 0,
    Light = 1,
    Dark = 2
}
