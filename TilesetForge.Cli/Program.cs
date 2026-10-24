using SixLabors.ImageSharp;
using TilesetForge.Core;

static string? GetArg(string[] args, string name)
{
    var idx = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (idx >= 0 && idx + 1 < args.Length) return args[idx + 1];
    return null;
}

static bool HasFlag(string[] args, string name) =>
    args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

static int GetInt(string[] args, string name, int def)
{
    var v = GetArg(args, name);
    return int.TryParse(v, out var n) ? n : def;
}

static string CreateDatedUniqueSubfolder(string parent, string baseName)
{
    Directory.CreateDirectory(parent);

    string stamp = DateTime.Now.ToString("yyyyMMdd-HHmm"); // short datetime
    string root = Path.Combine(parent, $"{baseName}_{stamp}");

    if (!Directory.Exists(root))
    {
        Directory.CreateDirectory(root);
        return root;
    }

    for (int i = 1; ; i++)
    {
        string candidate = Path.Combine(parent, $"{baseName}_{stamp}_{i:D2}");
        if (!Directory.Exists(candidate))
        {
            Directory.CreateDirectory(candidate);
            return candidate;
        }
    }
}

static void PrintUsage()
{
    Console.WriteLine("TilesetForge");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  pack  [--project <path>] [--in <folder>] [--out <folder>] [--tw <int>] [--th <int>]");
    Console.WriteLine("        [--cols <int> | --maxwidth <int>] [--pad <int>] [--space <int>]");
    Console.WriteLine();
    Console.WriteLine("  split [--project <path>] [--manifest <path>] [--atlas <path>] [--out <folder>]");
    Console.WriteLine("        [--namemode manifest|id|idandname]");
    Console.WriteLine();
    Console.WriteLine("  split-sheet [--sheet <path>] [--out <folder>] [--tw <int>] [--th <int>] [--space <int>] [--margin <int>]");
    Console.WriteLine("             [--cols <int>] [--rows <int>]");
    Console.WriteLine("             [--force] (splits even if validation confidence is low)");
    Console.WriteLine();
    Console.WriteLine("  validate-sheet --sheet <path> [--ratio <0-1>] [--alpha <0-255>]");
    Console.WriteLine("               (analyzes transparent separator lines and infers tile grid)");
    Console.WriteLine();
    Console.WriteLine("  ingest [--project <path>] [--in <folder>] [--out <folder>]");
    Console.WriteLine("         [--tw <int>] [--th <int>] [--tilesout <folder>]");
    Console.WriteLine("         [--cols <int> | --maxwidth <int>] [--pad <int>] [--space <int>]");
    Console.WriteLine("         (splits all PNGs into tiles, then packs into one atlas)");
    Console.WriteLine();
    Console.WriteLine("Project mode:");
    Console.WriteLine("  --project points to a JSON file saved with your project (recommended).");
    Console.WriteLine("  If omitted, TilesetForge uses %AppData%\\TilesetForge\\settings.json.");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine(@"  TilesetForge.Cli.exe pack --project .\tilesetforge.project.json --in .\tiles --out .\export --tw 16 --th 16 --maxwidth 4096");
    Console.WriteLine(@"  TilesetForge.Cli.exe split --project .\tilesetforge.project.json --out .\split");
}

static bool HasArg(string[] args, string name) =>
    Array.Exists(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var cmd = args[0].ToLowerInvariant();
var rest = args.Skip(1).ToArray();

var baseDir = AppContext.BaseDirectory;

string? projectPath = GetArg(rest, "--project");
string settingsPath = string.IsNullOrWhiteSpace(projectPath)
    ? Path.Combine(baseDir, "tilesetforge.project.json")
    : Path.GetFullPath(projectPath);

var settings = AppSettings.Load(settingsPath);

try
{
    if (cmd == "pack")
    {
        var input = GetArg(rest, "--in") ?? settings.LastInputFolder ?? Path.Combine(baseDir, "tiles");
        var output = GetArg(rest, "--out") ?? settings.LastOutputFolder ?? Path.Combine(baseDir, "export");

        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);

        bool userSetMaxWidth = HasArg(rest, "--maxwidth");
        bool userSetCols = HasArg(rest, "--cols");

        var tw = GetInt(rest, "--tw", settings.DefaultTileWidth);
        var th = GetInt(rest, "--th", settings.DefaultTileHeight);

        var pad = GetInt(rest, "--pad", settings.DefaultBorder);
        var space = GetInt(rest, "--space", settings.DefaultSpacing);

        var cols = GetInt(rest, "--cols", settings.DefaultColumns);       // usually 0
        var maxWidth = GetInt(rest, "--maxwidth", settings.DefaultMaxWidth);

        if (cols <= 0)
        {
            if (maxWidth > 0)
            {
                int usableWidth = maxWidth - (pad * 2);
                int cellWidth = tw + space;

                int computed = usableWidth > 0
                    ? (usableWidth + space) / cellWidth
                    : 1;

                cols = Math.Max(1, computed);
            }
            else
            {
                // Default intelligent behavior
                const int defaultMaxWidth = 4096;
                int usableWidth = defaultMaxWidth - (pad * 2);
                int cellWidth = tw + space;
                int computed = (usableWidth + space) / cellWidth;

                cols = Math.Max(1, computed);
            }
        }

        int tileCount = Directory.GetFiles(input, "*.png").Length;
        if (tileCount == 0)
            throw new InvalidOperationException($"No PNG tiles found in: {Path.GetFullPath(input)}");

        cols = Math.Min(cols, tileCount);

        Console.WriteLine($"Input : {Path.GetFullPath(input)}");
        Console.WriteLine($"Output: {Path.GetFullPath(output)}");
        Console.WriteLine($"Tile  : {tw}x{th}, cols={cols}, pad={pad}, space={space}");

        var manifest = AtlasBuilder.BuildGridAtlas(input, output, tw, th, cols, space, pad);
        Console.WriteLine($"Wrote atlas + manifest with {manifest.Tiles.Count} tiles.");

        settings.LastManifestPath = Path.Combine(output, "tileset.json");
        settings.LastAtlasPath = Path.Combine(output, "atlas.png");
        settings.LastInputFolder = input;
        settings.LastOutputFolder = output;
        settings.DefaultTileWidth = tw;
        settings.DefaultTileHeight = th;
        settings.DefaultBorder = pad;
        settings.DefaultSpacing = space;
        if (userSetMaxWidth) settings.DefaultMaxWidth = maxWidth;
        if (userSetCols) settings.DefaultColumns = cols;
        else settings.DefaultColumns = 0; // keep auto preference

        settings.Save(settingsPath);
        Console.WriteLine($"Saved settings to: {settingsPath}");

        return 0;
    }
    else if (cmd == "split")
    {
        var output = GetArg(rest, "--out") ?? settings.LastSplitOutputFolder ?? Path.Combine(baseDir, "split");
        Directory.CreateDirectory(output);

        var manifestPath = GetArg(rest, "--manifest") ?? settings.LastManifestPath
                           ?? throw new ArgumentException("Missing --manifest (and no saved LastManifestPath).");

        var atlasPath = GetArg(rest, "--atlas") ?? settings.LastAtlasPath
                        ?? throw new ArgumentException("Missing --atlas (and no saved LastAtlasPath).");

        var nm = (GetArg(rest, "--namemode") ?? "idandname").ToLowerInvariant();

        var mode = nm switch
        {
            "manifest" => AtlasSplitter.NameMode.ManifestName,
            "id" => AtlasSplitter.NameMode.IdOnly,
            "idandname" => AtlasSplitter.NameMode.IdAndName,
            _ => AtlasSplitter.NameMode.IdAndName
        };

        Console.WriteLine($"Manifest: {Path.GetFullPath(manifestPath)}");
        Console.WriteLine($"Atlas   : {Path.GetFullPath(atlasPath)}");
        Console.WriteLine($"Output  : {Path.GetFullPath(output)}");
        Console.WriteLine($"NameMode: {mode}");

        AtlasSplitter.SplitFromManifest(manifestPath, atlasPath, output, mode);
        Console.WriteLine("Split complete.");

        settings.LastManifestPath = manifestPath;
        settings.LastAtlasPath = atlasPath;
        settings.LastSplitOutputFolder = output;

        settings.Save();
        Console.WriteLine($"Saved settings to: {AppSettings.DefaultSettingsPath}");

        return 0;
    }
    else if (cmd == "split-sheet")
    {
        var sheet = GetArg(rest, "--sheet") ?? Path.Combine(baseDir, "sheet.png");

        var baseOut = GetArg(rest, "--out") ?? Path.Combine(baseDir, "split");

        var sheetName = Path.GetFileNameWithoutExtension(sheet);

        var outFolder = CreateDatedUniqueSubfolder(baseOut, sheetName);

        // Flags
        bool force = HasFlag(rest, "--force");

        // If user provides tw/th/space/margin, we respect them; otherwise detect.
        int tw = GetInt(rest, "--tw", 0);
        int th = GetInt(rest, "--th", 0);
        int space = GetInt(rest, "--space", -1);
        int margin = GetInt(rest, "--margin", -1);

        bool userProvidedBasics = (tw > 0 && th > 0 && space >= 0 && margin >= 0);

        using var img = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(sheet);
        var infoW = img.Width;
        var infoH = img.Height;

        SheetSplit.SheetFormat fmt;

        if (userProvidedBasics)
        {
            // Manual override always wins.
            int cols = 1 + ((infoW - margin) - tw) / (tw + space);
            int rows = 1 + ((infoH - margin) - th) / (th + space);

            fmt = new SheetSplit.SheetFormat(tw, th, space, margin, cols, rows);
            Console.WriteLine("Detected by manual override (--tw/--th/--space/--margin)");
        }
        else
        {
            // Try alpha scan first
            var scan = TransparencyScan.ScanAlphaLines(img);
            var inferred = TransparencyScan.TryInferGridFromScan(scan);

            if (inferred is not null && scan.HasAlpha && inferred.Confidence >= 0.90)
            {
                fmt = new SheetSplit.SheetFormat(
                    TileWidth: inferred.TileWidth,
                    TileHeight: inferred.TileHeight,
                    Spacing: inferred.SpacingX,
                    Margin: inferred.MarginX,
                    Columns: inferred.Columns,
                    Rows: inferred.Rows);

                Console.WriteLine($"Detected by transparency scan (confidence {inferred.Confidence:P0})");
            }
            else
            {
                // Fallback to dimension math — but only proceed if --force
                if (!force)
                {
                    Console.WriteLine("Validation confidence was low; refusing to split.");
                    Console.WriteLine("Run validate-sheet first, or provide --tw --th --space --margin, or override with --force.");
                    return 1;
                }

                fmt = SheetSplit.DetectFormatByDimensions(infoW, infoH);
                Console.WriteLine("Proceeding due to --force (detected by dimension math)");
            }
        }

        Console.WriteLine($"Sheet : {Path.GetFullPath(sheet)} ({infoW}x{infoH})");
        Console.WriteLine($"Out   : {Path.GetFullPath(outFolder)}");
        Console.WriteLine($"Fmt   : {fmt.TileWidth}x{fmt.TileHeight}, space={fmt.Spacing}, margin={fmt.Margin}, cols={fmt.Columns}, rows={fmt.Rows}");

        var manifest = SheetSplit.SplitSheetToFolder(sheet, outFolder, fmt, writeTiles: true);

        // Remember last outputs (optional, but nice)
        settings.LastAtlasPath = Path.Combine(outFolder, "atlas.png");
        settings.LastManifestPath = Path.Combine(outFolder, "tileset.json");
        settings.Save(settingsPath);

        Console.WriteLine($"Wrote manifest with {manifest.Tiles.Count} tiles.");
        Console.WriteLine($"Saved settings to: {settingsPath}");
        return 0;
    }
    else if (cmd == "validate-sheet")
    {
        var sheet = GetArg(rest, "--sheet") ?? Path.Combine(baseDir, "sheet.png");
        var ratio = double.TryParse(GetArg(rest, "--ratio"), out var r) ? r : 0.98;   // line transparency ratio
        var alpha = byte.TryParse(GetArg(rest, "--alpha"), out var a) ? a : (byte)5;  // alpha threshold

        Console.WriteLine($"Sheet : {Path.GetFullPath(sheet)}");
        if (!File.Exists(sheet)) 
            throw new FileNotFoundException("Sheet not found", sheet);

        using var img = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(sheet);

        Console.WriteLine($"Size  : {img.Width}x{img.Height}");

        var scan = TransparencyScan.ScanAlphaLines(img, lineTransparencyRatio: ratio, transparentThreshold: alpha);
        Console.WriteLine($"Alpha : hasAlpha={scan.HasAlpha}, threshold<= {alpha}, lineRatio>= {ratio}");

        Console.WriteLine($"Runs  : col opaque={scan.OpaqueColRuns.Count}, col transparent={scan.TransparentColRuns.Count}, " +
                          $"row opaque={scan.OpaqueRowRuns.Count}, row transparent={scan.TransparentRowRuns.Count}");

        var inferred = TransparencyScan.TryInferGridFromScan(scan);
        if (inferred is null)
        {
            Console.WriteLine("Infer : FAILED (no consistent grid inferred from transparency lines)");
        }
        else
        {
            Console.WriteLine($"Infer : tile={inferred.TileWidth}x{inferred.TileHeight}, " +
                              $"space={inferred.SpacingX}x{inferred.SpacingY}, margin={inferred.MarginX}x{inferred.MarginY}, " +
                              $"cols={inferred.Columns}, rows={inferred.Rows}, confidence={inferred.Confidence:P1}");
        }

        // Also show what the dimension-math detector would do (useful comparison)
        try
        {
            var dim = SheetSplit.DetectFormatByDimensions(img.Width, img.Height);
            Console.WriteLine($"Dims  : tile={dim.TileWidth}x{dim.TileHeight}, space={dim.Spacing}, margin={dim.Margin}, cols={dim.Columns}, rows={dim.Rows}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Dims  : FAILED ({ex.Message})");
        }

        return 0;
    }
    else if (cmd == "ingest")
    {
        var input = GetArg(rest, "--in") ?? settings.LastInputFolder ?? Path.Combine(baseDir, "ingest_in");
        var output = GetArg(rest, "--out") ?? settings.LastOutputFolder ?? Path.Combine(baseDir, "export");

        // Where normalized tiles will be written
        var tilesOut = GetArg(rest, "--tilesout") ?? Path.Combine(baseDir, "tiles_normalized");

        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(tilesOut);

        bool userSetMaxWidth = HasArg(rest, "--maxwidth");
        bool userSetCols = HasArg(rest, "--cols");

        var tw = GetInt(rest, "--tw", settings.DefaultTileWidth == 0 ? 32 : settings.DefaultTileWidth);
        var th = GetInt(rest, "--th", settings.DefaultTileHeight == 0 ? 32 : settings.DefaultTileHeight);

        var pad = GetInt(rest, "--pad", settings.DefaultBorder);
        var space = GetInt(rest, "--space", settings.DefaultSpacing);

        var cols = GetInt(rest, "--cols", settings.DefaultColumns);       // usually 0 for auto
        var maxWidth = GetInt(rest, "--maxwidth", settings.DefaultMaxWidth);

        // Clean normalized folder so it doesn't accumulate junk
        foreach (var f in Directory.EnumerateFiles(tilesOut, "*.png"))
            File.Delete(f);

        Console.WriteLine($"Ingest In : {Path.GetFullPath(input)}");
        Console.WriteLine($"Tiles Out : {Path.GetFullPath(tilesOut)}");
        Console.WriteLine($"Export Out: {Path.GetFullPath(output)}");
        Console.WriteLine($"Tile      : {tw}x{th}");

        var ing = FolderIngest.SplitFolderToTiles(input, tilesOut, tw, th, includeSingles: true);
        if (ing.ProducedTiles == 0)
            throw new InvalidOperationException($"No PNG tiles produced from: {Path.GetFullPath(input)}");

        // Auto columns (same logic as pack)
        if (cols <= 0)
        {
            int effectiveMax = maxWidth > 0 ? maxWidth : 4096;
            int usableWidth = effectiveMax - (pad * 2);
            int cellWidth = tw + space;
            int computed = usableWidth > 0 ? (usableWidth + space) / cellWidth : 1;
            cols = Math.Max(1, computed);
        }
        cols = Math.Min(cols, ing.ProducedTiles);

        Console.WriteLine($"Tiles made: {ing.ProducedTiles} from {ing.SourceFiles} PNG files");
        Console.WriteLine($"Packing   : cols={cols}, pad={pad}, space={space}");

        var manifest = AtlasBuilder.BuildGridAtlas(tilesOut, output, tw, th, cols, space, pad);
        Console.WriteLine($"Wrote atlas + manifest with {manifest.Tiles.Count} tiles.");

        // Save “last used” values (same style as pack)
        settings.LastManifestPath = Path.Combine(output, "tileset.json");
        settings.LastAtlasPath = Path.Combine(output, "atlas.png");
        settings.LastInputFolder = input;
        settings.LastOutputFolder = output;

        settings.DefaultTileWidth = tw;
        settings.DefaultTileHeight = th;
        settings.DefaultBorder = pad;
        settings.DefaultSpacing = space;
        if (userSetMaxWidth) settings.DefaultMaxWidth = maxWidth;
        if (userSetCols) settings.DefaultColumns = cols;
        else settings.DefaultColumns = 0;

        settings.Save(settingsPath);
        Console.WriteLine($"Saved settings to: {settingsPath}");

        return 0;
    }
    else
    {
        Console.WriteLine($"Unknown command: {cmd}");
        return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
