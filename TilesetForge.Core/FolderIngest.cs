using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace TilesetForge.Core;

public static class FolderIngest
{
    public sealed record IngestResult(int SourceFiles, int ProducedTiles);

    public static IngestResult SplitFolderToTiles(
        string inputFolder,
        string outputTilesFolder,
        int tileWidth,
        int tileHeight,
        bool includeSingles = true)
    {
        if (!Directory.Exists(inputFolder))
            throw new DirectoryNotFoundException($"Input folder not found: {inputFolder}");

        Directory.CreateDirectory(outputTilesFolder);

        int sourceFiles = 0;
        int produced = 0;

        foreach (var file in Directory.EnumerateFiles(inputFolder, "*.png", SearchOption.TopDirectoryOnly))
        {
            sourceFiles++;

            var baseName = Path.GetFileNameWithoutExtension(file);

            using var img = Image.Load<Rgba32>(file);

            int w = img.Width;
            int h = img.Height;

            // Single tile
            if (w == tileWidth && h == tileHeight)
            {
                if (!includeSingles) continue;

                var outPath = UniqueFile(Path.Combine(outputTilesFolder, $"{Sanitize(baseName)}.png"));
                img.Save(outPath);
                produced++;
                continue;
            }

            // Must be an exact grid of tiles
            if (w % tileWidth != 0 || h % tileHeight != 0)
            {
                throw new InvalidOperationException(
                    $"PNG '{Path.GetFileName(file)}' is {w}x{h} which is not divisible by tile size {tileWidth}x{tileHeight}. " +
                    $"Fix the asset, or move it out of the ingest folder.");
            }

            int cols = w / tileWidth;
            int rows = h / tileHeight;

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int x = c * tileWidth;
                    int y = r * tileHeight;

                    var rect = new Rectangle(x, y, tileWidth, tileHeight);
                    using var tile = img.Clone(ctx => ctx.Crop(rect));

                    var name = $"{Sanitize(baseName)}_r{r:D2}_c{c:D2}.png";
                    var outPath = UniqueFile(Path.Combine(outputTilesFolder, name));
                    tile.Save(outPath);
                    produced++;
                }
            }
        }

        return new IngestResult(sourceFiles, produced);
    }

    private static string Sanitize(string name)
    {
        // keep it simple: remove characters illegal in filenames
        foreach (var ch in Path.GetInvalidFileNameChars())
            name = name.Replace(ch, '_');
        return name.Trim();
    }

    private static string UniqueFile(string path)
    {
        if (!File.Exists(path)) return path;

        var dir = Path.GetDirectoryName(path)!;
        var baseName = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        for (int i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, $"{baseName}_{i:D2}{ext}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }
}