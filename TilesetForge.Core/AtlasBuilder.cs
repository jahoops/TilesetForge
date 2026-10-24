using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace TilesetForge.Core;

public static class AtlasBuilder
{
    public sealed record TileEntry(int Id, string Name, int X, int Y, int W, int H);

    public sealed record TilesetManifest(
        int TileWidth,
        int TileHeight,
        int Columns,
        int Spacing,
        int Border,
        string Atlas,
        List<TileEntry> Tiles);

    /// <summary>
    /// Builds a simple grid atlas from a folder of PNG tiles (all same size).
    /// </summary>
    public static TilesetManifest BuildGridAtlas(
    string inputFolder,
    string outputFolder,
    int tileWidth,
    int tileHeight,
    int columns,
    int spacing = 2,
    int border = 2,
    string atlasFileName = "atlas.png",
    string manifestFileName = "tileset.json")
    {
        if (!Directory.Exists(inputFolder))
            throw new DirectoryNotFoundException($"Input folder not found: {inputFolder}");

        Directory.CreateDirectory(outputFolder);

        var files = Directory.GetFiles(inputFolder, "*.png", SearchOption.TopDirectoryOnly)
                             .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                             .ToList();

        if (files.Count == 0)
            throw new InvalidOperationException($"No .png files found in: {inputFolder}");

        // Validate all sizes
        foreach (var f in files)
        {
            using var img = Image.Load<Rgba32>(f);
            if (img.Width != tileWidth || img.Height != tileHeight)
                throw new InvalidOperationException(
                    $"Tile '{Path.GetFileName(f)}' is {img.Width}x{img.Height}, expected {tileWidth}x{tileHeight}.");
        }

        if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
        if (spacing < 0) throw new ArgumentOutOfRangeException(nameof(spacing));
        if (border < 0) throw new ArgumentOutOfRangeException(nameof(border));

        var rows = (int)Math.Ceiling(files.Count / (double)columns);

        int cellW = tileWidth + spacing;
        int cellH = tileHeight + spacing;

        // Atlas includes outer border, plus spacing between cells (not after last cell)
        var atlasW = border * 2 + columns * tileWidth + Math.Max(0, columns - 1) * spacing;
        var atlasH = border * 2 + rows * tileHeight + Math.Max(0, rows - 1) * spacing;

        var atlasPath = Path.Combine(outputFolder, atlasFileName);
        var manifestPath = Path.Combine(outputFolder, manifestFileName);

        var tiles = new List<TileEntry>(files.Count);

        using (var atlas = new Image<Rgba32>(atlasW, atlasH))
        {
            // Your ImageSharp version supports BackgroundColor; keep using it.
            atlas.Mutate(ctx => ctx.BackgroundColor(Color.Transparent));

            for (int i = 0; i < files.Count; i++)
            {
                int col = i % columns;
                int row = i / columns;

                int x = border + col * (tileWidth + spacing);
                int y = border + row * (tileHeight + spacing);

                var file = files[i];
                var name = Path.GetFileNameWithoutExtension(file);

                using var tile = Image.Load<Rgba32>(file);

                // Draw the tile
                atlas.Mutate(ctx => ctx.DrawImage(tile, new Point(x, y), 1f));

                // Edge bleed into spacing/border area around the tile:
                // We "extrude" the tile's edge pixels outward by 'bleed' pixels.
                int bleed = Math.Max(border, spacing); // simple choice: bleed enough to cover both
                BleedEdges(atlas, tile, x, y, bleed, atlasW, atlasH);

                tiles.Add(new TileEntry(i, name, x, y, tileWidth, tileHeight));
            }

            atlas.Save(atlasPath);
        }

        var manifest = new TilesetManifest(
            TileWidth: tileWidth,
            TileHeight: tileHeight,
            Columns: columns,
            Spacing: spacing,
            Border: border,
            Atlas: atlasFileName,
            Tiles: tiles
        );

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(manifestPath, json);

        return manifest;
    }

    /// <summary>
    /// Copies edge pixels from 'tile' into the surrounding area in 'atlas' to reduce texture bleeding.
    /// </summary>
    private static void BleedEdges(
        Image<Rgba32> atlas,
        Image<Rgba32> tile,
        int dstX,
        int dstY,
        int bleed,
        int atlasW,
        int atlasH)
    {
        if (bleed <= 0) return;

        int w = tile.Width;
        int h = tile.Height;

        // Clamp bleed so we don't write outside atlas (extra safety)
        int leftMax = Math.Min(bleed, dstX);
        int topMax = Math.Min(bleed, dstY);
        int rightMax = Math.Min(bleed, atlasW - (dstX + w));
        int bottomMax = Math.Min(bleed, atlasH - (dstY + h));

        // Left / Right strips
        for (int y = 0; y < h; y++)
        {
            var leftPixel = tile[0, y];
            for (int dx = 1; dx <= leftMax; dx++)
                atlas[dstX - dx, dstY + y] = leftPixel;

            var rightPixel = tile[w - 1, y];
            for (int dx = 0; dx < rightMax; dx++)
                atlas[dstX + w + dx, dstY + y] = rightPixel;
        }

        // Top / Bottom strips (including corners)
        for (int x = -leftMax; x < w + rightMax; x++)
        {
            int srcX = Math.Clamp(x, 0, w - 1);

            var topPixel = tile[srcX, 0];
            for (int dy = 1; dy <= topMax; dy++)
                atlas[dstX + x, dstY - dy] = topPixel;

            var bottomPixel = tile[srcX, h - 1];
            for (int dy = 0; dy < bottomMax; dy++)
                atlas[dstX + x, dstY + h + dy] = bottomPixel;
        }
    }

}
