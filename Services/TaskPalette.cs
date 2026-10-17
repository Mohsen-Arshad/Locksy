namespace Locksy.Services;

public sealed record TaskColorOption(
    string Key,
    string Name,
    string Swatch,
    string LightHex,
    string DarkHex,
    string LightTextHex,
    string DarkTextHex,
    string VibrantHex,
    string VibrantTextHex);

public static class TodoPalette
{
    public const string DefaultKey = "default";
    public const string NoColorKey = "none";

    public const string LightPaletteKey = "light";
    public const string DarkPaletteKey = "dark";
    public const string VibrantPaletteKey = "vibrant";

    public static IReadOnlyList<(string Key, string Name)> Palettes { get; } = new (string Key, string Name)[]
    {
        (LightPaletteKey, "Soft Light"),
        (DarkPaletteKey, "Deep Dark"),
        (VibrantPaletteKey, "Vibrant")
    };

    public static IReadOnlyList<TaskColorOption> Options { get; } = new[]
    {
        new TaskColorOption(NoColorKey, "No color", "○", "#00000000", "#00000000", "#34394B", "#F1F2FA", "#00000000", "#252F48"),
        new TaskColorOption(DefaultKey, "Default", "⚪", "#F0F2F8", "#252736", "#34394B", "#F1F2FA", "#AEBAD1", "#252F48"),
        new TaskColorOption("green", "Green", "🟩", "#DDF7E7", "#285A42", "#173A28", "#F0FFF4", "#8ECFA2", "#164629"),
        new TaskColorOption("blue", "Blue", "🟦", "#DCEBFF", "#2A4D70", "#1A3354", "#EEF7FF", "#8DB9ED", "#16385F"),
        new TaskColorOption("amber", "Amber", "🟨", "#FFF0C2", "#695522", "#4B3810", "#FFF7D6", "#E9C25D", "#543806"),
        new TaskColorOption("rose", "Rose", "🟥", "#FFE0E7", "#683544", "#572431", "#FFF0F4", "#DE91A6", "#581A30"),
        new TaskColorOption("violet", "Violet", "🟪", "#EAE1FF", "#514073", "#38265B", "#F6F0FF", "#A994E5", "#35225D"),
        new TaskColorOption("teal", "Teal", "🔹", "#D7F8F4", "#245B58", "#154847", "#E7FFFC", "#7BC9C2", "#104A45"),
        new TaskColorOption("orange", "Orange", "🟧", "#FFE4CF", "#6A4228", "#593015", "#FFF1E4", "#EEA16F", "#582B11"),
        new TaskColorOption("gray", "Slate", "⚫", "#E8EAF0", "#414858", "#343947", "#F5F6FC", "#AEB8C9", "#2A3346"),
        new TaskColorOption("mint", "Mint", "🟢", "#C9FBE7", "#28624F", "#14533F", "#E7FFF5", "#7ED8B6", "#14503A"),
        new TaskColorOption("sky", "Sky Blue", "🔵", "#CDEEFF", "#285778", "#174B6D", "#EAF8FF", "#77C9EE", "#12465F"),
        new TaskColorOption("lilac", "Lilac", "🟣", "#F0DFFF", "#5B3F6D", "#58376E", "#FAF0FF", "#C29CE1", "#4C2E62"),
        new TaskColorOption("lemon", "Lemon", "🟡", "#FFF8B8", "#675B1F", "#5F4F00", "#FFFBD8", "#E4D765", "#514800"),
        new TaskColorOption("coral", "Coral", "🪸", "#FFDCD5", "#714137", "#7A3327", "#FFF0EB", "#F28E7E", "#60251D"),
        new TaskColorOption("periwinkle", "Periwinkle", "🔷", "#DFE2FF", "#394B7B", "#343F80", "#F0F2FF", "#98A7EB", "#283778"),
        new TaskColorOption("peach", "Peach", "🍑", "#FFE1C9", "#714C30", "#70401D", "#FFF3E7", "#E8B07B", "#58300F"),
        new TaskColorOption("seafoam", "Seafoam", "🩵", "#CEF8F5", "#285B60", "#175257", "#EAFFFF", "#79CFD0", "#11494E")
    };

    public static bool IsValidKey(string? key) => Options.Any(option => option.Key == key);

    public static bool IsValidPaletteKey(string? key) => Palettes.Any(palette => palette.Key == key);

    public static string GetPaletteName(string? key)
    {
        foreach (var palette in Palettes)
        {
            if (palette.Key == key) return palette.Name;
        }
        return "Vibrant";
    }

    public static TaskColorOption GetOption(string? key) =>
        Options.FirstOrDefault(option => option.Key == key) ?? Options[0];

    public static string GetHex(string? key, string? paletteKey)
    {
        var option = GetOption(key);
        return paletteKey switch
        {
            LightPaletteKey => option.LightHex,
            DarkPaletteKey => option.DarkHex,
            _ => option.VibrantHex
        };
    }

    public static string GetTextHex(string? key, string? paletteKey)
    {
        var option = GetOption(key);
        return paletteKey switch
        {
            LightPaletteKey => option.LightTextHex,
            DarkPaletteKey => option.DarkTextHex,
            _ => option.VibrantTextHex
        };
    }

    // Legacy overloads remain available to avoid breaking any old call sites.
    public static string GetHex(string? key, bool darkMode) =>
        GetHex(key, darkMode ? DarkPaletteKey : LightPaletteKey);

    public static string GetTextHex(string? key, bool darkMode) =>
        GetTextHex(key, darkMode ? DarkPaletteKey : LightPaletteKey);

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
