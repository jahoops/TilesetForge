using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace TilesetForge.Core;

public static class AtlasSplitter
{
    public enum NameMode
    {
        ManifestName,   // use Name (fallback to Id if empty)
        IdOnly,         // always tile_0000.png
        IdAndName       // 0000_name.png (fallback to id only)
    }

    public static void SplitFromManifest(
        string manifestPath,
        string atlasPath,
        string outputFolder,
        NameMode nameMode = NameMode.IdAndName)
    {
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("Manifest not found", manifestPath);

        if (!File.Exists(atlasPath))
            throw new FileNotFoundException("Atlas not found", atlasPath);

        Directory.CreateDirectory(outputFolder);

        var manifestJson = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<AtlasBuilder.TilesetManifest>(manifestJson)
            ?? throw new InvalidOperationException("Failed to parse manifest JSON.");

        using var atlas = Image.Load<Rgba32>(atlasPath);

        foreach (var t in manifest.Tiles)
        {
            // Bounds check
            if (t.X < 0 || t.Y < 0 || t.W <= 0 || t.H <= 0 ||
                t.X + t.W > atlas.Width || t.Y + t.H > atlas.Height)
            {
                throw new InvalidOperationException(
                    $"Tile Id={t.Id} Name='{t.Name}' rect out of bounds: ({t.X},{t.Y},{t.W},{t.H}) in atlas {atlas.Width}x{atlas.Height}");
            }

            var rect = new Rectangle(t.X, t.Y, t.W, t.H);
            using var tile = atlas.Clone(ctx => ctx.Crop(rect));

            var outName = BuildFileName(t.Id, t.Name, nameMode);
            var outPath = Path.Combine(outputFolder, outName);

            tile.Save(outPath);
        }
    }

    private static string BuildFileName(int id, string? name, NameMode mode)
    {
        string idPart = $"tile_{id:D4}";
        string safeName = MakeSafeFileName(name ?? "");

        return mode switch
        {
            NameMode.IdOnly => $"{idPart}.png",

            NameMode.ManifestName =>
                string.IsNullOrWhiteSpace(safeName)
                    ? $"{idPart}.png"
                    : $"{safeName}.png",

            NameMode.IdAndName =>
                string.IsNullOrWhiteSpace(safeName)
                    ? $"{idPart}.png"
                    : $"{idPart}_{safeName}.png",

            _ => $"{idPart}.png"
        };
    }

    private static string MakeSafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }
}
