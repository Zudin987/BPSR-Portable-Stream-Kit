using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BPSRStreamKit.Infrastructure;

namespace BPSRStreamKit.Services;

public sealed record FrameThemeDefinition(
    string Key,
    string DisplayName,
    string Detail,
    StreamTheme LegacyTheme);

public static class FrameThemeService
{
    private sealed record ThemePalette(Color BackgroundA, Color BackgroundB, Color Accent, Color Secondary, Color Soft);

    private static readonly string SelectionFile = Path.Combine(AppPaths.Root, "user-data", "frame-theme-v2.txt");
    private static readonly string CacheRoot = Path.Combine(AppPaths.Root, "user-data", "frame-themes");

    private static readonly (string RelativePath, bool Vertical, string? ScreenTitle)[] ThemeFiles =
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

    public static FrameThemeDefinition? FindByDisplayName(string? displayName) => Definitions.FirstOrDefault(x =>
        string.Equals(x.DisplayName, displayName, StringComparison.Ordinal));

    public static string ReadSelectionKey()
    {
        try
        {
            if (File.Exists(SelectionFile))
            {
                var saved = File.ReadAllText(SelectionFile).Trim();
                if (Find(saved) is not null) return saved;
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
        var backupRoot = Path.Combine(CacheRoot, "sakura-original");
        if (ThemeFiles.All(x => File.Exists(Path.Combine(backupRoot, x.RelativePath)))) return;

        Directory.CreateDirectory(backupRoot);
        foreach (var file in ThemeFiles)
        {
            var source = ActiveProfileAPath(file.RelativePath);
            if (!File.Exists(source))
                throw new FileNotFoundException("The bundled Sakura frame assets are missing. Extract the complete StreamKit ZIP or use Fix setup.", source);

            var destination = Path.Combine(backupRoot, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
    }

    private static string EnsureGenerated(FrameThemeDefinition theme)
    {
        var root = Path.Combine(CacheRoot, theme.Key);
        if (ThemeFiles.All(x => File.Exists(Path.Combine(root, x.RelativePath)))) return root;

        foreach (var file in ThemeFiles)
        {
            var destination = Path.Combine(root, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var width = file.Vertical ? 1080 : 1920;
            var height = file.Vertical ? 1920 : 1080;
            if (file.ScreenTitle is null)
                RenderFrame(destination, width, height, theme.Key);
            else
                RenderScreen(destination, width, height, theme, file.ScreenTitle);
        }
        return root;
    }

    private static void CopyToActiveProfileA(string sourceRoot)
    {
        foreach (var file in ThemeFiles)
        {
            var source = Path.Combine(sourceRoot, file.RelativePath);
            var destination = ActiveProfileAPath(file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
    }

    private static string ActiveProfileAPath(string relativePath)
    {
        if (relativePath.StartsWith("Frames", StringComparison.Ordinal))
        {
            var file = Path.GetFileName(relativePath);
            return Path.Combine(AppPaths.AssetsDirectory, "Frames", file == "Discord_1080p.png"
                ? "01_Minimal_Thin_1080p.png"
                : "05_TikTok_Minimal_1080x1920.png");
        }

        var name = Path.GetFileName(relativePath);
        return Path.Combine(AppPaths.AssetsDirectory, "Screens", name);
    }

    private static void RenderFrame(string path, int width, int height, string key)
    {
        var palette = Palette(key);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
            var accent = new SolidColorBrush(palette.Accent);
            var secondary = new SolidColorBrush(palette.Secondary);
            var soft = new SolidColorBrush(Color.FromArgb(120, palette.Soft.R, palette.Soft.G, palette.Soft.B));
            var mainPen = new Pen(accent, Math.Max(2, Math.Min(width, height) * 0.0022));
            var thinPen = new Pen(soft, Math.Max(1, Math.Min(width, height) * 0.0009));
            var margin = Math.Max(6, Math.Min(width, height) * 0.007);

            dc.DrawRoundedRectangle(null, mainPen, new Rect(margin, margin, width - margin * 2, height - margin * 2), 10, 10);
            dc.DrawRoundedRectangle(null, thinPen, new Rect(margin + 8, margin + 8, width - (margin + 8) * 2, height - (margin + 8) * 2), 8, 8);
            DrawCornerBrackets(dc, width, height, mainPen, secondary, key);
            DrawThemeMotifs(dc, width, height, palette, key);
        }
        SaveVisual(path, visual, width, height);
    }

    private static void RenderScreen(string path, int width, int height, FrameThemeDefinition theme, string title)
    {
        var palette = Palette(theme.Key);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var background = new LinearGradientBrush(palette.BackgroundA, palette.BackgroundB, 35);
            dc.DrawRectangle(background, null, new Rect(0, 0, width, height));

            var accentBrush = new SolidColorBrush(palette.Accent);
            var secondaryBrush = new SolidColorBrush(palette.Secondary);
            var softBrush = new SolidColorBrush(Color.FromArgb(150, palette.Soft.R, palette.Soft.G, palette.Soft.B));
            var borderPen = new Pen(accentBrush, Math.Max(2, Math.Min(width, height) * 0.002));
            var margin = Math.Max(18, Math.Min(width, height) * 0.025);
            dc.DrawRoundedRectangle(null, borderPen, new Rect(margin, margin, width - margin * 2, height - margin * 2), 18, 18);
            DrawThemeMotifs(dc, width, height, palette, theme.Key);

            var titleSize = Math.Max(48, Math.Min(width, height) * 0.075);
            var titleText = new FormattedText(title, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI Semibold"), titleSize, Brushes.White, 1.0);
            var titlePoint = new Point((width - titleText.Width) / 2, (height - titleText.Height) / 2 - titleSize * 0.25);
            dc.DrawText(titleText, titlePoint);

            var label = theme.DisplayName.ToUpperInvariant();
            var labelText = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), Math.Max(18, titleSize * 0.28), secondaryBrush, 1.0);
            dc.DrawText(labelText, new Point((width - labelText.Width) / 2, titlePoint.Y + titleText.Height + 18));

            var lineWidth = Math.Min(width * 0.24, 360);
            dc.DrawLine(new Pen(softBrush, 2), new Point(width / 2 - lineWidth / 2, titlePoint.Y - 28),
                new Point(width / 2 + lineWidth / 2, titlePoint.Y - 28));
        }
        SaveVisual(path, visual, width, height);
    }

    private static void DrawCornerBrackets(DrawingContext dc, int width, int height, Pen pen, Brush secondary, string key)
    {
        var length = Math.Max(55, Math.Min(width, height) * 0.065);
        var inset = Math.Max(14, Math.Min(width, height) * 0.018);

        void Corner(double x, double y, double sx, double sy)
        {
            dc.DrawLine(pen, new Point(x, y), new Point(x + sx * length, y));
            dc.DrawLine(pen, new Point(x, y), new Point(x, y + sy * length));
            if (key is "neon-tech" or "cyber-orange")
                dc.DrawEllipse(secondary, null, new Point(x + sx * length, y), 4, 4);
        }

        Corner(inset, inset, 1, 1);
        Corner(width - inset, inset, -1, 1);
        Corner(inset, height - inset, 1, -1);
        Corner(width - inset, height - inset, -1, -1);
    }

    private static void DrawThemeMotifs(DrawingContext dc, int width, int height, ThemePalette palette, string key)
    {
        var accent = new SolidColorBrush(Color.FromArgb(185, palette.Accent.R, palette.Accent.G, palette.Accent.B));
        var secondary = new SolidColorBrush(Color.FromArgb(150, palette.Secondary.R, palette.Secondary.G, palette.Secondary.B));
        var pen = new Pen(accent, Math.Max(1.5, Math.Min(width, height) * 0.0015));
        var s = Math.Max(16, Math.Min(width, height) * 0.018);

        switch (key)
        {
            case "black-gold":
                DrawDiamond(dc, new Point(width * 0.5, height * 0.035), s, accent, pen);
                DrawDiamond(dc, new Point(width * 0.5, height * 0.965), s, accent, pen);
                break;
            case "crimson-demon":
                DrawSpikeCluster(dc, new Point(width * 0.08, height * 0.08), s * 1.5, accent);
                DrawSpikeCluster(dc, new Point(width * 0.92, height * 0.92), s * 1.5, accent);
                break;
            case "ice-crystal":
                DrawCrystal(dc, new Point(width * 0.09, height * 0.09), s * 1.7, accent, secondary);
                DrawCrystal(dc, new Point(width * 0.91, height * 0.91), s * 1.7, accent, secondary);
                break;
            case "forest-mystic":
                DrawLeaf(dc, new Point(width * 0.08, height * 0.12), s * 1.8, 35, accent, pen);
                DrawLeaf(dc, new Point(width * 0.92, height * 0.88), s * 1.8, 215, accent, pen);
                break;
            case "moonlight-silver":
                dc.DrawEllipse(null, pen, new Point(width * 0.09, height * 0.11), s * 1.2, s * 1.2);
                DrawStar(dc, new Point(width * 0.16, height * 0.08), s * 0.7, secondary);
                DrawStar(dc, new Point(width * 0.88, height * 0.86), s * 0.55, secondary);
                break;
            case "cyber-orange":
                DrawCircuit(dc, width, height, pen, accent, 0.18);
                break;
            default:
                DrawCircuit(dc, width, height, pen, accent, 0.24);
                DrawStar(dc, new Point(width * 0.82, height * 0.11), s * 0.55, secondary);
                break;
        }
    }

    private static void DrawCircuit(DrawingContext dc, int width, int height, Pen pen, Brush nodeBrush, double span)
    {
        var y = height * 0.045;
        var x1 = width * (0.5 - span / 2);
        var x2 = width * (0.5 + span / 2);
        dc.DrawLine(pen, new Point(x1, y), new Point(x2, y));
        dc.DrawEllipse(nodeBrush, null, new Point(x1, y), 4, 4);
        dc.DrawEllipse(nodeBrush, null, new Point(x2, y), 4, 4);
        dc.DrawLine(pen, new Point(x1, height - y), new Point(x2, height - y));
    }

    private static void DrawDiamond(DrawingContext dc, Point center, double size, Brush fill, Pen pen)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(center.X, center.Y - size), true, true);
            ctx.LineTo(new Point(center.X + size, center.Y), true, false);
            ctx.LineTo(new Point(center.X, center.Y + size), true, false);
            ctx.LineTo(new Point(center.X - size, center.Y), true, false);
        }
        dc.DrawGeometry(fill, pen, geometry);
    }

    private static void DrawSpikeCluster(DrawingContext dc, Point center, double size, Brush fill)
    {
        for (var i = -1; i <= 1; i++)
        {
            var x = center.X + i * size * 0.55;
            var geometry = new StreamGeometry();
            using var ctx = geometry.Open();
            ctx.BeginFigure(new Point(x - size * 0.2, center.Y + size * 0.5), true, true);
            ctx.LineTo(new Point(x, center.Y - size * (0.7 + Math.Abs(i) * 0.25)), true, false);
            ctx.LineTo(new Point(x + size * 0.2, center.Y + size * 0.5), true, false);
            dc.DrawGeometry(fill, null, geometry);
        }
    }

    private static void DrawCrystal(DrawingContext dc, Point center, double size, Brush fill, Brush secondary)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(center.X, center.Y - size), true, true);
            ctx.LineTo(new Point(center.X + size * 0.55, center.Y), true, false);
            ctx.LineTo(new Point(center.X, center.Y + size), true, false);
            ctx.LineTo(new Point(center.X - size * 0.55, center.Y), true, false);
        }
        dc.DrawGeometry(fill, null, geometry);
        dc.DrawLine(new Pen(secondary, 1.5), new Point(center.X, center.Y - size), new Point(center.X, center.Y + size));
    }

    private static void DrawLeaf(DrawingContext dc, Point center, double size, double angle, Brush fill, Pen pen)
    {
        dc.PushTransform(new RotateTransform(angle, center.X, center.Y));
        dc.DrawEllipse(fill, pen, center, size, size * 0.42);
        dc.DrawLine(pen, new Point(center.X - size, center.Y), new Point(center.X + size, center.Y));
        dc.Pop();
    }

    private static void DrawStar(DrawingContext dc, Point center, double size, Brush fill)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(center.X, center.Y - size), true, true);
            ctx.LineTo(new Point(center.X + size * 0.24, center.Y - size * 0.24), true, false);
            ctx.LineTo(new Point(center.X + size, center.Y), true, false);
            ctx.LineTo(new Point(center.X + size * 0.24, center.Y + size * 0.24), true, false);
            ctx.LineTo(new Point(center.X, center.Y + size), true, false);
            ctx.LineTo(new Point(center.X - size * 0.24, center.Y + size * 0.24), true, false);
            ctx.LineTo(new Point(center.X - size, center.Y), true, false);
            ctx.LineTo(new Point(center.X - size * 0.24, center.Y - size * 0.24), true, false);
        }
        dc.DrawGeometry(fill, null, geometry);
    }

    private static ThemePalette Palette(string key) => key switch
    {
        "black-gold" => new ThemePalette(C("#090909"), C("#1B160C"), C("#E3BC68"), C("#FFF0B0"), C("#B58B37")),
        "crimson-demon" => new ThemePalette(C("#120407"), C("#2A0710"), C("#FF405D"), C("#B31236"), C("#FF8A9B")),
        "ice-crystal" => new ThemePalette(C("#061621"), C("#123247"), C("#80E8FF"), C("#D8F8FF"), C("#67BFD8")),
        "forest-mystic" => new ThemePalette(C("#06140E"), C("#123023"), C("#58E49A"), C("#B4F1D0"), C("#48966B")),
        "cyber-orange" => new ThemePalette(C("#130E07"), C("#2D1A08"), C("#FF9A38"), C("#FFD08A"), C("#B86A1C")),
        "moonlight-silver" => new ThemePalette(C("#070D1A"), C("#172137"), C("#D9E5FF"), C("#91A9D9"), C("#7184AA")),
        _ => new ThemePalette(C("#060A18"), C("#170B25"), C("#36E5FF"), C("#B75BFF"), C("#6FAFC7"))
    };

    private static Color C(string value) => (Color)ColorConverter.ConvertFromString(value);

    private static void SaveVisual(string path, DrawingVisual visual, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
