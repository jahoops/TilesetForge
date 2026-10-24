using System.Text.Json;

namespace TilesetForge.Core;

public sealed class AppSettings
{
    // Defaults used when nothing else is specified
    public int DefaultTileWidth { get; set; } = 16;
    public int DefaultTileHeight { get; set; } = 16;
    public int DefaultMaxWidth { get; set; } = 4096;
    public int DefaultColumns { get; set; } = 0; // 0 = auto
    public int DefaultSpacing { get; set; } = 2;
    public int DefaultBorder { get; set; } = 2;

    public string? LastInputFolder { get; set; }
    public string? LastOutputFolder { get; set; }

    public string? LastManifestPath { get; set; }
    public string? LastAtlasPath { get; set; }
    public string? LastSplitOutputFolder { get; set; }

    public static string DefaultSettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TilesetForge",
            "settings.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultSettingsPath;

        try
        {
            if (!File.Exists(path))
                return new AppSettings();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            // If settings are corrupted, fall back safely.
            return new AppSettings();
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultSettingsPath;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(path, json);
    }
}
