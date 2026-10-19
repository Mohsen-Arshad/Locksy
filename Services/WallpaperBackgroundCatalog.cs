using Locksy.Models;

namespace Locksy.Services;

/// <summary>Provides the built-in wallpaper background palette.</summary>
public static class WallpaperBackgroundCatalog
{
    public static IReadOnlyList<WallpaperBackgroundPreset> Presets { get; } =
    [
        new("ocean", "Ocean", "Cool blue gradients", "#071B35", "#246B83", "#43D5F2", "#5478F2"),
        new("sunset", "Sunset", "Warm evening colors", "#21143F", "#B84F72", "#FFBD78", "#EE7BA6"),
        new("forest", "Forest", "Deep greens", "#071E20", "#28654C", "#92D9A7", "#4EB7A0"),
        new("rose", "Rose", "Soft rose and plum", "#2C1429", "#9F416D", "#FFC0D7", "#D98EBB"),
        new("lavender", "Lavender", "Violet and lilac", "#1B1538", "#6955AD", "#D1B7FF", "#8E85FF"),
        new("amber", "Amber", "Golden warm tones", "#321B0A", "#A9571C", "#FFCF75", "#F18A56"),
        new("slate", "Slate", "Quiet steel and graphite", "#101522", "#414E65", "#B7C4DB", "#7B91B7"),
        new("aurora", "Aurora", "Teal light over midnight", "#071A2B", "#155A68", "#75F0D1", "#8A8DFF")
    ];

    public static WallpaperBackgroundPreset? Find(string? key) =>
        Presets.FirstOrDefault(preset => string.Equals(preset.Key, key, StringComparison.OrdinalIgnoreCase));
}
