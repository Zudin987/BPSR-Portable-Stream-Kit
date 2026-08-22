using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BPSRStreamKit.Infrastructure;

namespace BPSRStreamKit.Services;

public sealed record FrameThemeDefinition(string Key, string DisplayName, string Detail, StreamTheme LegacyTheme);

public static class FrameThemeService
{
    private sealed record Palette(Color A, Color B, Color Accent, Color Secondary, Color Soft);

    private static readonly string SelectionFile = Path.Combine(AppPaths.Root, "user-data", "frame-theme-v2.txt");
    private static readonly string CacheRoot = Path.Combine(AppPaths.Root, "user-data", "frame-themes");

    private static readonly (string RelativePath, bool Vertical, string? Title)[] Files =
    {
        (Path.Combine("Frames", "Discord_1080p.png"), false, null),
        (Path.Combine("Frames", "TikTok_1080x1920.png"), true, null),
        (Path.Combine("Screens", "Starting_1080p.png"), false, "STARTING SOON"),
        (Path.Combine("Screens", "BRB_1080p.png"), false, "BE RIGHT BACK"),
        (Path.Combine("Screens", "Starting_TikTok_1080x1920.png"), true, "STARTING SOON"),
        (Path.Combine("Screens", "BRB_TikTok_1080x1920.png"), true, "BE RIGHT BACK")
    };

    public static IReadOnlyList<FrameThemeDefinition> Definitions { get; } = new[]
    {
        new FrameThemeDefinition("sakura", "Sakura", "Soft pink / purple frame", StreamTheme.ProfileA),
        new FrameThemeDefinition("doctor", "Chibi Doctor", "Clean cyan medical frame", StreamTheme.ProfileB),
        new FrameThemeDefinition("neon-tech", "Neon Tech", "Cyan + violet futuristic glow", StreamTheme.ProfileA),
        new FrameThemeDefinition("black-gold", "Black Gold", "Minimal premium gold accents", StreamTheme.ProfileA),
        new FrameThemeDefinition("crimson-demon", "Crimson Demon", "Angular red / black infernal style", StreamTheme.ProfileA),
        new FrameThemeDefinition("ice-crystal", "Ice Crystal", "Bright frozen blue crystal style", StreamTheme.ProfileA),
        new FrameThemeDefinition("forest-mystic", "Forest Mystic", "Emerald fantasy / nature accents", StreamTheme.ProfileA),
        new FrameThemeDefinition("cyber-orange", "Cyber Orange", "Warm orange esports-tech frame", StreamTheme.ProfileA),
        new FrameThemeDefinition("moonlight-silver", "Moonlight Silver", "Calm silver / navy night style", StreamTheme.ProfileA)
    };

    public static FrameThemeDefinition Default => Definitions[0];

    public static FrameThemeDefinition? Find(string? key) => Definitions.FirstOrDefault(x =>
        string.Equals(x.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static FrameThemeDefinition? FindByDisplayName(string? name) => Definitions.FirstOrDefault(x =>
        string.Equals(x.DisplayName, name, StringComparison.Ordinal));

    public static string ReadSelectionKey()
    {
        try
        {
            if (File.Exists(SelectionFile))
            {
                var key = File.ReadAllText(SelectionFile).Trim();
                if (Find(key) is not null) return key;
            }

            var legacy = Path.Combine(AppPaths.Root, ".streamkit-theme");
            if (File.Exists(legacy) && File.ReadAllText(legacy).Trim().Equals("B", StringComparison.OrdinalIgnoreCase))
                return "doctor";
        }
        catch { }
        return "sakura";
    }

    public static void Activate(FrameThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        if (theme.Key == "doctor")
        {
            SaveSelection(theme.Key);
            return;
        }

        EnsureSakuraBackup();
        var sourceRoot = theme.Key == "sakura"
            ? Path.Combine(CacheRoot, "sakura-original")
            : EnsureGenerated(theme);
        CopyToActiveProfileA(sourceRoot);
        SaveSelection(theme.Key);
    }

    private static void SaveSelection(string key)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SelectionFile)!);
        AtomicFile.WriteAllText(SelectionFile, key);
    }

    private static void EnsureSakuraBackup()
    {
        var root = Path.Combine(CacheRoot, "sakura-original");
        if (Files.All(x => File.Exists(Path.Combine(root, x.RelativePath)))) return;

        foreach (var file in Files)
        {
            var source = ActiveProfileAPath(file.RelativePath);
            if (!File.Exists(source))
                throw new FileNotFoundException("The bundled Sakura frame assets are missing. Extract the complete StreamKit ZIP or use Fix setup.", source);
            var destination = Path.Combine(root, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, true);
        }
    }

    private static string EnsureGenerated(FrameThemeDefinition theme)
    {
        var root = Path.Combine(CacheRoot, theme.Key);
        if (Files.All(x => File.Exists(Path.Combine(root, x.RelativePath)))) return root;

        foreach (var file in Files)
        {
            var destination = Path.Combine(root, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var width = file.Vertical ? 1080 : 1920;
            var height = file.Vertical ? 1920 : 1080;
            if (file.Title is null) RenderFrame(destination, width, height, theme.Key);
            else RenderScreen(destination, width, height, theme, file.Title);
        }
        return root;
    }

    private static void CopyToActiveProfileA(string sourceRoot)
    {
        foreach (var file in Files)
        {
            var source = Path.Combine(sourceRoot, file.RelativePath);
            var destination = ActiveProfileAPath(file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, true);
        }
    }

    private static string ActiveProfileAPath(string relativePath)
    {
        if (relativePath.StartsWith("Frames", StringComparison.Ordinal))
        {
            return Path.Combine(AppPaths.AssetsDirectory, "Frames",
                Path.GetFileName(relativePath) == "Discord_1080p.png"
                    ? "01_Minimal_Thin_1080p.png"
                    : "05_TikTok_Minimal_1080x1920.png");
        }
        return Path.Combine(AppPaths.AssetsDirectory, "Screens", Path.GetFileName(relativePath));
    }

    private static void RenderFrame(string path, int width, int height, string key)
    {
        var p = GetPalette(key);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
            var accent = Brush(p.Accent, 235);
            var secondary = Brush(p.Secondary, 185);
            var mainPen = new Pen(accent, Math.Max(2, Math.Min(width, height) * 0.0022));
            var thinPen = new Pen(Brush(p.Soft, 120), Math.Max(1, Math.Min(width, height) * 0.0009));
            var margin = Math.Max(6, Math.Min(width, height) * 0.007);
            dc.DrawRoundedRectangle(null, mainPen, new Rect(margin, margin, width - margin * 2, height - margin * 2), 10, 10);
            dc.DrawRoundedRectangle(null, thinPen, new Rect(margin + 8, margin + 8, width - (margin + 8) * 2, height - (margin + 8) * 2), 8, 8);
            DrawCorners(dc, width, height, mainPen, secondary, key);
            DrawMotifs(dc, width, height, p, key);
        }
        Save(path, visual, width, height);
    }

    private static void RenderScreen(string path, int width, int height, FrameThemeDefinition theme, string title)
    {
        var p = GetPalette(theme.Key);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(p.A, p.B, 35), null, new Rect(0, 0, width, height));
            var margin = Math.Max(18, Math.Min(width, height) * 0.025);
            dc.DrawRoundedRectangle(null, new Pen(Brush(p.Accent, 235), Math.Max(2, Math.Min(width, height) * 0.002)),
                new Rect(margin, margin, width - margin * 2, height - margin * 2), 18, 18);
            DrawMotifs(dc, width, height, p, theme.Key);

            var titleSize = Math.Max(48, Math.Min(width, height) * 0.075);
            var heading = Text(title, titleSize, Brushes.White, "Segoe UI Semibold");
            var top = (height - heading.Height) / 2 - titleSize * 0.25;
            dc.DrawText(heading, new Point((width - heading.Width) / 2, top));

            var label = Text(theme.DisplayName.ToUpperInvariant(), Math.Max(18, titleSize * 0.28), Brush(p.Secondary, 255));
            dc.DrawText(label, new Point((width - label.Width) / 2, top + heading.Height + 18));
            var line = Math.Min(width * 0.24, 360);
            dc.DrawLine(new Pen(Brush(p.Soft, 150), 2), new Point(width / 2 - line / 2, top - 28), new Point(width / 2 + line / 2, top - 28));
        }
        Save(path, visual, width, height);
    }

    private static FormattedText Text(string value, double size, Brush brush, string face = "Segoe UI") =>
        new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(face), size, brush, 1.0);

    private static void DrawCorners(DrawingContext dc, int width, int height, Pen pen, Brush secondary, string key)
    {
        var length = Math.Max(55, Math.Min(width, height) * 0.065);
        var inset = Math.Max(14, Math.Min(width, height) * 0.018);
        void Corner(double x, double y, double sx, double sy)
        {
            dc.DrawLine(pen, new Point(x, y), new Point(x + sx * length, y));
            dc.DrawLine(pen, new Point(x, y), new Point(x, y + sy * length));
            if (key is "neon-tech" or "cyber-orange") dc.DrawEllipse(secondary, null, new Point(x + sx * length, y), 4, 4);
        }
        Corner(inset, inset, 1, 1);
        Corner(width - inset, inset, -1, 1);
        Corner(inset, height - inset, 1, -1);
        Corner(width - inset, height - inset, -1, -1);
    }

    private static void DrawMotifs(DrawingContext dc, int width, int height, Palette p, string key)
    {
        var accent = Brush(p.Accent, 185);
        var secondary = Brush(p.Secondary, 150);
        var pen = new Pen(accent, Math.Max(1.5, Math.Min(width, height) * 0.0015));
        var s = Math.Max(16, Math.Min(width, height) * 0.018);
        switch (key)
        {
            case "black-gold":
                Diamond(dc, width * 0.5, height * 0.04, s, accent, pen);
                Diamond(dc, width * 0.5, height * 0.96, s, accent, pen);
                break;
            case "crimson-demon":
                Spikes(dc, width * 0.08, height * 0.09, s * 1.5, accent);
                Spikes(dc, width * 0.92, height * 0.91, s * 1.5, accent);
                break;
            case "ice-crystal":
                Diamond(dc, width * 0.09, height * 0.10, s * 1.7, accent, new Pen(secondary, 1.5));
                Diamond(dc, width * 0.91, height * 0.90, s * 1.7, accent, new Pen(secondary, 1.5));
                break;
            case "forest-mystic":
                Leaf(dc, width * 0.08, height * 0.12, s * 1.8, 35, accent, pen);
                Leaf(dc, width * 0.92, height * 0.88, s * 1.8, 215, accent, pen);
                break;
            case "moonlight-silver":
                dc.DrawEllipse(null, pen, new Point(width * 0.09, height * 0.11), s * 1.2, s * 1.2);
                Star(dc, width * 0.16, height * 0.08, s * 0.7, secondary);
                Star(dc, width * 0.88, height * 0.86, s * 0.55, secondary);
                break;
            case "cyber-orange":
                Circuit(dc, width, height, pen, accent, 0.18);
                break;
            default:
                Circuit(dc, width, height, pen, accent, 0.24);
                Star(dc, width * 0.82, height * 0.11, s * 0.55, secondary);
                break;
        }
    }

    private static void Circuit(DrawingContext dc, int width, int height, Pen pen, Brush node, double span)
    {
        var y = height * 0.045;
        var x1 = width * (0.5 - span / 2);
        var x2 = width * (0.5 + span / 2);
        foreach (var yy in new[] { y, height - y })
        {
            dc.DrawLine(pen, new Point(x1, yy), new Point(x2, yy));
            dc.DrawEllipse(node, null, new Point(x1, yy), 4, 4);
            dc.DrawEllipse(node, null, new Point(x2, yy), 4, 4);
        }
    }

    private static void Diamond(DrawingContext dc, double x, double y, double size, Brush fill, Pen pen)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x, y - size), true, true);
            c.LineTo(new Point(x + size, y), true, false);
            c.LineTo(new Point(x, y + size), true, false);
            c.LineTo(new Point(x - size, y), true, false);
        }
        dc.DrawGeometry(fill, pen, g);
    }

    private static void Spikes(DrawingContext dc, double x, double y, double size, Brush fill)
    {
        for (var i = -1; i <= 1; i++)
        {
            var cx = x + i * size * 0.55;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(cx - size * 0.2, y + size * 0.5), true, true);
                c.LineTo(new Point(cx, y - size * (0.7 + Math.Abs(i) * 0.25)), true, false);
                c.LineTo(new Point(cx + size * 0.2, y + size * 0.5), true, false);
            }
            dc.DrawGeometry(fill, null, g);
        }
    }

    private static void Leaf(DrawingContext dc, double x, double y, double size, double angle, Brush fill, Pen pen)
    {
        dc.PushTransform(new RotateTransform(angle, x, y));
        dc.DrawEllipse(fill, pen, new Point(x, y), size, size * 0.42);
        dc.DrawLine(pen, new Point(x - size, y), new Point(x + size, y));
        dc.Pop();
    }

    private static void Star(DrawingContext dc, double x, double y, double size, Brush fill)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x, y - size), true, true);
            c.LineTo(new Point(x + size * 0.24, y - size * 0.24), true, false);
            c.LineTo(new Point(x + size, y), true, false);
            c.LineTo(new Point(x + size * 0.24, y + size * 0.24), true, false);
            c.LineTo(new Point(x, y + size), true, false);
            c.LineTo(new Point(x - size * 0.24, y + size * 0.24), true, false);
            c.LineTo(new Point(x - size, y), true, false);
            c.LineTo(new Point(x - size * 0.24, y - size * 0.24), true, false);
        }
        dc.DrawGeometry(fill, null, g);
    }

    private static Palette GetPalette(string key) => key switch
    {
        "black-gold" => new(C("#090909"), C("#1B160C"), C("#E3BC68"), C("#FFF0B0"), C("#B58B37")),
        "crimson-demon" => new(C("#120407"), C("#2A0710"), C("#FF405D"), C("#B31236"), C("#FF8A9B")),
        "ice-crystal" => new(C("#061621"), C("#123247"), C("#80E8FF"), C("#D8F8FF"), C("#67BFD8")),
        "forest-mystic" => new(C("#06140E"), C("#123023"), C("#58E49A"), C("#B4F1D0"), C("#48966B")),
        "cyber-orange" => new(C("#130E07"), C("#2D1A08"), C("#FF9A38"), C("#FFD08A"), C("#B86A1C")),
        "moonlight-silver" => new(C("#070D1A"), C("#172137"), C("#D9E5FF"), C("#91A9D9"), C("#7184AA")),
        _ => new(C("#060A18"), C("#170B25"), C("#36E5FF"), C("#B75BFF"), C("#6FAFC7"))
    };

    private static SolidColorBrush Brush(Color color, byte alpha) =>
        new(Color.FromArgb(alpha, color.R, color.G, color.B));

    private static Color C(string value) => (Color)ColorConverter.ConvertFromString(value);

    private static void Save(string path, DrawingVisual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
