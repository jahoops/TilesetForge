using System.Text.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace TilesetForge.Core;

public static class SheetSplit
{
    public sealed record SheetFormat(
        int TileWidth,
        int TileHeight,
        int Spacing,
        int Margin,
        int Columns,
        int Rows);

    /// <summary>
    /// Tries to detect a grid layout using only image dimensions.
    /// Works when sheets are "cleanly packed" (like your 968x526 example).
    /// </summary>
    public static SheetFormat DetectFormatByDimensions(int sheetW, int sheetH)
    {
        // Common tile sizes to try first (extend later if needed)
        int[] sizes = [8, 16, 24, 32, 48, 64, 96, 128, 256];
        int[] spacings = [0, 1, 2, 3, 4];
        int[] margins = [0, 1, 2, 3, 4];

        SheetFormat? best = null;
        int bestScore = int.MinValue;

        foreach (var tw in sizes)
            foreach (var th in sizes)
                foreach (var space in spacings)
                    foreach (var margin in margins)
                    {
                        var cols = ComputeCount(sheetW, tw, space, margin);
                        var rows = ComputeCount(sheetH, th, space, margin);
                        if (cols <= 0 || rows <= 0) continue;

                        // Must pack exactly with no trailing partial cell
                        int usedW = margin + cols * tw + Math.Max(0, cols - 1) * space;
                        int usedH = margin + rows * th + Math.Max(0, rows - 1) * space;
                        if (usedW != sheetW || usedH != sheetH) continue;

                        // Score heuristic: prefer larger tiles, smaller spacing/margin
                        // and more tiles (useful sheets).
                        int score = 0;
                        score += (tw + th) * 10;
                        score -= (space * 50 + margin * 50);
                        score += (cols * rows);

                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = new SheetFormat(tw, th, space, margin, cols, rows);
                        }
                    }

        if (best is null)
            throw new InvalidOperationException(
                $"Could not detect sheet format from dimensions {sheetW}x{sheetH}. " +
                $"Try providing --tw/--th and --space/--margin explicitly.");

        return best;
    }

    public static AtlasBuilder.TilesetManifest SplitSheetToFolder(
        string sheetPath,
        string outputFolder,
        SheetFormat fmt,
        bool writeTiles = true,
        string atlasFileName = "atlas.png",
        string manifestFileName = "tileset.json")
    {
        if (!File.Exists(sheetPath))
            throw new FileNotFoundException("Sheet not found", sheetPath);

        Directory.CreateDirectory(outputFolder);

        // Copy sheet into output folder as atlas.png (self-contained output)
        var atlasPath = Path.Combine(outputFolder, atlasFileName);
        File.Copy(sheetPath, atlasPath, overwrite: true);

        // Where extracted tiles go
        var tilesDir = Path.Combine(outputFolder, "tiles");
        if (writeTiles) Directory.CreateDirectory(tilesDir);

        using var sheet = Image.Load<Rgba32>(sheetPath);

        var tiles = new List<AtlasBuilder.TileEntry>(fmt.Columns * fmt.Rows);

        int id = 0;
        for (int r = 0; r < fmt.Rows; r++)
        {
            for (int c = 0; c < fmt.Columns; c++)
            {
                int x = fmt.Margin + c * (fmt.TileWidth + fmt.Spacing);
                int y = fmt.Margin + r * (fmt.TileHeight + fmt.Spacing);

                var name = $"tile_r{r:D2}_c{c:D2}";
                tiles.Add(new AtlasBuilder.TileEntry(id, name, x, y, fmt.TileWidth, fmt.TileHeight));

                if (writeTiles)
                {
                    var rect = new Rectangle(x, y, fmt.TileWidth, fmt.TileHeight);
                    using var tileImg = sheet.Clone(ctx => ctx.Crop(rect));
                    tileImg.Save(Path.Combine(tilesDir, $"{id:D4}_{name}.png"));
                }

                id++;
            }
        }

        // Border in our manifest maps well to "Margin" for sheet grids.
        var manifest = new AtlasBuilder.TilesetManifest(
            TileWidth: fmt.TileWidth,
            TileHeight: fmt.TileHeight,
            Columns: fmt.Columns,
            Spacing: fmt.Spacing,
            Border: fmt.Margin,
            Atlas: atlasFileName,
            Tiles: tiles
        );

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(outputFolder, manifestFileName), json);

        return manifest;
    }

    private static int ComputeCount(int sheetSize, int tileSize, int spacing, int margin)
    {
        // We assume margin only before the first tile (top/left),
        // and no trailing margin after the last tile (matches your sheet).
        int usable = sheetSize - margin;
        if (usable < tileSize) return 0;

        // Count positions i where: margin + i*(tile+spacing) + tile <= sheetSize
        return 1 + (usable - tileSize) / (tileSize + spacing);
    }
}
