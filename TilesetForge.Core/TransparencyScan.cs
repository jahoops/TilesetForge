using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.PixelFormats;

namespace TilesetForge.Core;

public static class TransparencyScan
{
    public sealed record ScanResult(
        bool HasAlpha,
        int Width,
        int Height,
        double TransparentThreshold,
        double LineTransparencyRatio,
        List<(int Start, int Length)> OpaqueColRuns,
        List<(int Start, int Length)> TransparentColRuns,
        List<(int Start, int Length)> OpaqueRowRuns,
        List<(int Start, int Length)> TransparentRowRuns);

    public sealed record InferredGrid(
        int TileWidth,
        int TileHeight,
        int SpacingX,
        int SpacingY,
        int MarginX,
        int MarginY,
        int Columns,
        int Rows,
        double Confidence);

    public static ScanResult ScanAlphaLines(Image<Rgba32> img,
        double lineTransparencyRatio = 0.98,
        byte transparentThreshold = 5)
    {
        int w = img.Width, h = img.Height;

        // Determine whether alpha is even present (some sheets are fully opaque)
        bool hasAlpha = false;
        for (int y = 0; y < h && !hasAlpha; y += Math.Max(1, h / 16))
        {
            var row = img.DangerousGetPixelRowMemory(y).Span;
            for (int x = 0; x < w; x += Math.Max(1, w / 64))
            {
                if (row[x].A != 255) { hasAlpha = true; break; }
            }
        }

        bool[] colTransparent = new bool[w];
        bool[] rowTransparent = new bool[h];

        // Column transparency
        for (int x = 0; x < w; x++)
        {
            int transparentCount = 0;
            for (int y = 0; y < h; y++)
            {
                if (img[x, y].A <= transparentThreshold) transparentCount++;
            }
            colTransparent[x] = (transparentCount / (double)h) >= lineTransparencyRatio;
        }

        // Row transparency
        for (int y = 0; y < h; y++)
        {
            int transparentCount = 0;
            var row = img.DangerousGetPixelRowMemory(y).Span;
            for (int x = 0; x < w; x++)
            {
                if (row[x].A <= transparentThreshold) transparentCount++;
            }
            rowTransparent[y] = (transparentCount / (double)w) >= lineTransparencyRatio;
        }

        var transColRuns = GetRuns(colTransparent, true);
        var opaqueColRuns = GetRuns(colTransparent, false);
        var transRowRuns = GetRuns(rowTransparent, true);
        var opaqueRowRuns = GetRuns(rowTransparent, false);

        return new ScanResult(
            HasAlpha: hasAlpha,
            Width: w,
            Height: h,
            TransparentThreshold: transparentThreshold,
            LineTransparencyRatio: lineTransparencyRatio,
            OpaqueColRuns: opaqueColRuns,
            TransparentColRuns: transColRuns,
            OpaqueRowRuns: opaqueRowRuns,
            TransparentRowRuns: transRowRuns
        );
    }

    public static InferredGrid? TryInferGridFromScan(ScanResult scan)
    {
        // Need at least one opaque run and at least one transparent run to infer spacing/margin.
        if (scan.OpaqueColRuns.Count == 0 || scan.OpaqueRowRuns.Count == 0)
            return null;

        // Margin = initial transparent run if it starts at 0, else 0.
        int marginX = (scan.TransparentColRuns.Count > 0 && scan.TransparentColRuns[0].Start == 0)
            ? scan.TransparentColRuns[0].Length : 0;
        int marginY = (scan.TransparentRowRuns.Count > 0 && scan.TransparentRowRuns[0].Start == 0)
            ? scan.TransparentRowRuns[0].Length : 0;

        // Tile width/height: mode of opaque-run lengths
        int tileW = ModeLength(scan.OpaqueColRuns);
        int tileH = ModeLength(scan.OpaqueRowRuns);

        // Spacing: mode of transparent-run lengths excluding leading margin run
        int spaceX = ModeLength(scan.TransparentColRuns.Where(r => r.Start != 0).ToList(), fallback: 0);
        int spaceY = ModeLength(scan.TransparentRowRuns.Where(r => r.Start != 0).ToList(), fallback: 0);

        // Columns/rows: count of opaque runs that match tile size (tolerate small noise)
        int cols = scan.OpaqueColRuns.Count(r => Nearly(r.Length, tileW));
        int rows = scan.OpaqueRowRuns.Count(r => Nearly(r.Length, tileH));

        if (tileW <= 0 || tileH <= 0 || cols <= 0 || rows <= 0)
            return null;

        // Simple confidence score: how consistent the runs are
        double colConsistency = Consistency(scan.OpaqueColRuns, tileW);
        double rowConsistency = Consistency(scan.OpaqueRowRuns, tileH);
        double conf = (colConsistency + rowConsistency) / 2.0;

        return new InferredGrid(tileW, tileH, spaceX, spaceY, marginX, marginY, cols, rows, conf);
    }

    // ----- Helpers -----

    private static List<(int Start, int Length)> GetRuns(bool[] arr, bool value)
    {
        var runs = new List<(int Start, int Length)>();
        int i = 0;
        while (i < arr.Length)
        {
            if (arr[i] != value) { i++; continue; }
            int start = i;
            while (i < arr.Length && arr[i] == value) i++;
            runs.Add((start, i - start));
        }
        return runs;
    }

    private static int ModeLength(List<(int Start, int Length)> runs, int fallback = 0)
    {
        if (runs.Count == 0) return fallback;

        var groups = runs.GroupBy(r => r.Length)
                         .Select(g => new { Len = g.Key, Count = g.Count() })
                         .OrderByDescending(g => g.Count)
                         .ThenByDescending(g => g.Len)
                         .ToList();

        return groups[0].Len;
    }

    private static bool Nearly(int a, int b, int tol = 1) => Math.Abs(a - b) <= tol;

    private static double Consistency(List<(int Start, int Length)> runs, int expected)
    {
        if (runs.Count == 0) return 0;
        int good = runs.Count(r => Nearly(r.Length, expected));
        return good / (double)runs.Count;
    }
}
