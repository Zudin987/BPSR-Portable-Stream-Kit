using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BPSRStreamKit.Infrastructure;

namespace BPSRStreamKit.Services;

public sealed record FrameThemeDefinition(string Key, string DisplayName, string Detail, StreamTheme LegacyTheme);

public static class FrameThemeService
{
    private sealed record Palette(Color A, Color B, Color Accent, Color Secondary, Color Soft, Color Deep);

    private const string RenderVersion = "v2.4.1-premium-2";
    private const string PreviewFileName = "Preview_640x360.png";
    private static readonly string SelectionFile = Path.Combine(AppPaths.Root, "user-data", "frame-theme-v2.txt");
    private static readonly string CacheRoot = Path.Combine(AppPaths.Root, "user-data", "frame-themes-v3");

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
        new FrameThemeDefinition("sakura", "Sakura", "Elegant petals · blush pink + lavender", StreamTheme.ProfileA),
        new FrameThemeDefinition("doctor", "Chibi Doctor", "Clean glass HUD · cyan + mint", StreamTheme.ProfileB),
        new FrameThemeDefinition("neon-tech", "Neon Tech", "Layered cyber HUD · cyan + violet", StreamTheme.ProfileA),
        new FrameThemeDefinition("black-gold", "Black Gold", "Luxury minimal · matte black + warm gold", StreamTheme.ProfileA),
        new FrameThemeDefinition("crimson-demon", "Crimson Demon", "Sharp infernal shards · crimson + ember", StreamTheme.ProfileA),
        new FrameThemeDefinition("ice-crystal", "Ice Crystal", "Frosted facets · ice blue + silver", StreamTheme.ProfileA),
        new FrameThemeDefinition("forest-mystic", "Forest Mystic", "Enchanted vines · emerald + teal", StreamTheme.ProfileA),
        new FrameThemeDefinition("cyber-orange", "Cyber Orange", "Tactical esports HUD · orange + charcoal", StreamTheme.ProfileA),
        new FrameThemeDefinition("moonlight-silver", "Moonlight Silver", "Celestial elegance · silver + navy", StreamTheme.ProfileA)
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
        var sourceRoot = EnsureGenerated(theme);
        CopyToActiveTheme(theme, sourceRoot);
        SaveSelection(theme.Key);
    }

    public static string GetPreviewPath(FrameThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Path.Combine(EnsureGenerated(theme), PreviewFileName);
    }

    private static void SaveSelection(string key)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SelectionFile)!);
        AtomicFile.WriteAllText(SelectionFile, key);
    }

    private static string EnsureGenerated(FrameThemeDefinition theme)
    {
        var root = Path.Combine(CacheRoot, theme.Key);
        var marker = Path.Combine(root, "renderer-version.txt");
        var complete = Files.All(x => File.Exists(Path.Combine(root, x.RelativePath)))
                       && File.Exists(Path.Combine(root, PreviewFileName))
                       && File.Exists(marker)
                       && string.Equals(File.ReadAllText(marker).Trim(), RenderVersion, StringComparison.Ordinal);
        if (complete) return root;

        Directory.CreateDirectory(root);
        foreach (var file in Files)
        {
            var destination = Path.Combine(root, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var width = file.Vertical ? 1080 : 1920;
            var height = file.Vertical ? 1920 : 1080;
            if (file.Title is null) RenderFrame(destination, width, height, theme);
            else RenderScreen(destination, width, height, theme, file.Title);
        }

        RenderPreview(Path.Combine(root, PreviewFileName), theme);
        AtomicFile.WriteAllText(marker, RenderVersion);
        return root;
    }

    private static void CopyToActiveTheme(FrameThemeDefinition theme, string sourceRoot)
    {
        foreach (var file in Files)
        {
            var source = Path.Combine(sourceRoot, file.RelativePath);
            var destination = ActivePath(theme, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, true);
        }
    }

    private static string ActivePath(FrameThemeDefinition theme, string relativePath)
    {
        if (theme.LegacyTheme == StreamTheme.ProfileB)
        {
            var doctorRoot = Path.Combine(AppPaths.AssetsDirectory, "Themes", "Profile_B_Doctor");
            return Path.Combine(doctorRoot, relativePath);
        }

        if (relativePath.StartsWith("Frames", StringComparison.Ordinal))
        {
            return Path.Combine(AppPaths.AssetsDirectory, "Frames",
                Path.GetFileName(relativePath) == "Discord_1080p.png"
                    ? "01_Minimal_Thin_1080p.png"
                    : "05_TikTok_Minimal_1080x1920.png");
        }

        return Path.Combine(AppPaths.AssetsDirectory, "Screens", Path.GetFileName(relativePath));
    }

    private static void RenderFrame(string path, int width, int height, FrameThemeDefinition theme)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
            DrawPremiumFrame(dc, width, height, theme, false);
        }
        Save(path, visual, width, height);
    }

    private static void RenderPreview(string path, FrameThemeDefinition theme)
    {
        const int width = 640;
        const int height = 360;
        var p = GetPalette(theme.Key);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Mix(p.Deep, Colors.Black, 0.22), Mix(p.B, p.Deep, 0.52), 18), null,
                new Rect(0, 0, width, height));
            DrawGlow(dc, new Point(width * 0.72, height * 0.28), width * 0.34, p.Accent, 72);
            DrawGlow(dc, new Point(width * 0.20, height * 0.78), width * 0.26, p.Secondary, 42);

            dc.DrawRoundedRectangle(ColorBrush(p.Soft, 17), null, new Rect(width * 0.07, height * 0.12, width * 0.23, height * 0.055), 5, 5);
            dc.DrawRoundedRectangle(ColorBrush(p.Soft, 14), null, new Rect(width * 0.75, height * 0.10, width * 0.17, height * 0.19), 8, 8);
            for (var i = 0; i < 4; i++)
                dc.DrawRoundedRectangle(ColorBrush(p.Soft, (byte)(12 + i * 3)), null,
                    new Rect(width * (0.35 + i * 0.055), height * 0.78, width * 0.043, height * 0.075), 5, 5);

            DrawPremiumFrame(dc, width, height, theme, true);

            var chip = new Rect(width * 0.045, height * 0.82, width * 0.34, height * 0.105);
            dc.DrawRoundedRectangle(ColorBrush(p.Deep, 205), new Pen(ColorBrush(p.Accent, 135), 1.2), chip, 9, 9);
            var name = FitText(theme.DisplayName.ToUpperInvariant(), 16, 10, chip.Width - 24, Brushes.White, "Segoe UI Semibold");
            dc.DrawText(name, new Point(chip.X + 12, chip.Y + (chip.Height - name.Height) / 2));
        }
        Save(path, visual, width, height);
    }

    private static void RenderScreen(string path, int width, int height, FrameThemeDefinition theme, string title)
    {
        var p = GetPalette(theme.Key);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(p.A, p.B, 28), null, new Rect(0, 0, width, height));
            DrawGlow(dc, new Point(width * 0.18, height * 0.18), Math.Min(width, height) * 0.62, p.Accent, 95);
            DrawGlow(dc, new Point(width * 0.82, height * 0.80), Math.Min(width, height) * 0.72, p.Secondary, 66);
            DrawAtmosphere(dc, width, height, p, theme.Key);
            DrawPremiumFrame(dc, width, height, theme, true);

            var panelWidth = width * (width > height ? 0.56 : 0.76);
            var panelHeight = Math.Min(height * 0.25, panelWidth * 0.42);
            var panel = new Rect((width - panelWidth) / 2, (height - panelHeight) / 2, panelWidth, panelHeight);
            dc.DrawRoundedRectangle(ColorBrush(p.Deep, 198), new Pen(ColorBrush(p.Accent, 95), Math.Max(1.4, Math.Min(width, height) * 0.0014)), panel, 24, 24);
            dc.DrawRoundedRectangle(null, new Pen(ColorBrush(p.Soft, 54), 1), new Rect(panel.X + 8, panel.Y + 8, panel.Width - 16, panel.Height - 16), 19, 19);

            DrawScreenEmblem(dc, width / 2, panel.Y - Math.Min(width, height) * 0.055, Math.Min(width, height) * 0.028, theme.Key, p);

            var titleMax = panel.Width * 0.86;
            var titleSize = Math.Max(42, Math.Min(width, height) * (width > height ? 0.076 : 0.067));
            var heading = FitText(title, titleSize, 34, titleMax, Brushes.White, "Segoe UI Semibold");
            var titleY = panel.Y + panel.Height * 0.28 - heading.Height * 0.5;
            DrawTextShadow(dc, heading, new Point((width - heading.Width) / 2, titleY));

            var subtitleValue = title.StartsWith("START", StringComparison.OrdinalIgnoreCase) ? "PLEASE WAIT A MOMENT" : "THANKS FOR WAITING";
            var subtitle = FitText(subtitleValue, Math.Max(17, titleSize * 0.245), 13, titleMax,
                ColorBrush(p.Secondary, 245), "Segoe UI Semibold");
            dc.DrawText(subtitle, new Point((width - subtitle.Width) / 2, titleY + heading.Height + Math.Max(14, titleSize * 0.12)));

            var themeLabel = FitText(theme.DisplayName.ToUpperInvariant(), Math.Max(13, titleSize * 0.17), 11, panel.Width * 0.48,
                ColorBrush(p.Soft, 225), "Segoe UI");
            dc.DrawText(themeLabel, new Point((width - themeLabel.Width) / 2, panel.Bottom - themeLabel.Height - 18));

            var divider = Math.Min(panel.Width * 0.24, width * 0.18);
            dc.DrawLine(new Pen(ColorBrush(p.Accent, 100), 1.5),
                new Point(width / 2 - divider / 2, panel.Bottom + 22), new Point(width / 2 + divider / 2, panel.Bottom + 22));
        }
        Save(path, visual, width, height);
    }

    private static void DrawPremiumFrame(DrawingContext dc, int width, int height, FrameThemeDefinition theme, bool strongBackdrop)
    {
        var p = GetPalette(theme.Key);
        var s = Math.Min(width, height);
        var outer = Math.Max(5.0, s * 0.0065);
        var inner = outer + Math.Max(8.0, s * 0.0085);
        var radius = Math.Max(8.0, s * 0.012);
        var rect = new Rect(outer, outer, width - outer * 2, height - outer * 2);

        dc.DrawRoundedRectangle(null, new Pen(ColorBrush(p.Accent, strongBackdrop ? (byte)48 : (byte)36), Math.Max(7, s * 0.0105)), rect, radius, radius);
        dc.DrawRoundedRectangle(null, new Pen(ColorBrush(p.Secondary, 118), Math.Max(2.5, s * 0.0031)), rect, radius, radius);
        dc.DrawRoundedRectangle(null, new Pen(ColorBrush(p.Accent, 238), Math.Max(1.4, s * 0.0017)), rect, radius, radius);
        dc.DrawRoundedRectangle(null, new Pen(ColorBrush(p.Soft, 92), Math.Max(0.9, s * 0.0009)),
            new Rect(inner, inner, width - inner * 2, height - inner * 2), radius * 0.75, radius * 0.75);

        DrawCornerPlates(dc, width, height, p, theme.Key);
        DrawEdgeSegments(dc, width, height, p, theme.Key);
        DrawThemeMotifs(dc, width, height, p, theme.Key);
        DrawBottomSignature(dc, width, height, p, theme.Key);
    }

    private static void DrawCornerPlates(DrawingContext dc, int width, int height, Palette p, string key)
    {
        var s = Math.Min(width, height);
        var inset = Math.Max(12, s * 0.017);
        var longEdge = Math.Max(62, s * 0.085);
        var shortEdge = Math.Max(30, s * 0.040);
        var fill = ColorBrush(p.Deep, 132);
        var accent = new Pen(ColorBrush(p.Accent, 220), Math.Max(1.2, s * 0.0015));
        var second = new Pen(ColorBrush(p.Secondary, 128), Math.Max(0.8, s * 0.0009));

        void Plate(double x, double y, double sx, double sy)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(x, y), true, true);
                c.LineTo(new Point(x + sx * longEdge, y), true, false);
                c.LineTo(new Point(x + sx * (longEdge - shortEdge * 0.35), y + sy * shortEdge * 0.28), true, false);
                c.LineTo(new Point(x + sx * shortEdge * 0.42, y + sy * shortEdge * 0.28), true, false);
                c.LineTo(new Point(x + sx * shortEdge * 0.28, y + sy * longEdge), true, false);
                c.LineTo(new Point(x, y + sy * longEdge), true, false);
            }
            dc.DrawGeometry(fill, accent, g);
            dc.DrawLine(second, new Point(x + sx * shortEdge * 0.4, y + sy * shortEdge * 0.5),
                new Point(x + sx * (longEdge * 0.74), y + sy * shortEdge * 0.5));
        }

        Plate(inset, inset, 1, 1);
        Plate(width - inset, inset, -1, 1);
        Plate(inset, height - inset, 1, -1);
        Plate(width - inset, height - inset, -1, -1);

        if (key is "neon-tech" or "cyber-orange" or "doctor")
        {
            var node = ColorBrush(p.Secondary, 230);
            var r = Math.Max(2.4, s * 0.003);
            dc.DrawEllipse(node, null, new Point(inset + longEdge * 0.78, inset + shortEdge * 0.5), r, r);
            dc.DrawEllipse(node, null, new Point(width - inset - longEdge * 0.78, height - inset - shortEdge * 0.5), r, r);
        }
    }

    private static void DrawEdgeSegments(DrawingContext dc, int width, int height, Palette p, string key)
    {
        var s = Math.Min(width, height);
        var y = Math.Max(11, s * 0.014);
        var x = Math.Max(11, s * 0.014);
        var accent = new Pen(ColorBrush(p.Accent, 180), Math.Max(1.2, s * 0.0014));
        var soft = new Pen(ColorBrush(p.Soft, 85), Math.Max(0.8, s * 0.0008));
        var topSpan = width * (key == "black-gold" ? 0.12 : 0.18);
        var sideSpan = height * 0.13;

        dc.DrawLine(accent, new Point(width / 2 - topSpan, y), new Point(width / 2 - topSpan * 0.22, y));
        dc.DrawLine(accent, new Point(width / 2 + topSpan * 0.22, y), new Point(width / 2 + topSpan, y));
        dc.DrawLine(soft, new Point(width / 2 - topSpan * 0.18, y + 5), new Point(width / 2 + topSpan * 0.18, y + 5));
        dc.DrawLine(accent, new Point(x, height / 2 - sideSpan), new Point(x, height / 2 - sideSpan * 0.25));
        dc.DrawLine(accent, new Point(width - x, height / 2 + sideSpan * 0.25), new Point(width - x, height / 2 + sideSpan));
    }

    private static void DrawBottomSignature(DrawingContext dc, int width, int height, Palette p, string key)
    {
        var s = Math.Min(width, height);
        var barWidth = Math.Min(width * 0.22, s * 0.38);
        var barHeight = Math.Max(12, s * 0.016);
        var y = height - Math.Max(15, s * 0.022);
        var x = (width - barWidth) / 2;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x, y), true, true);
            c.LineTo(new Point(x + barWidth * 0.40, y), true, false);
            c.LineTo(new Point(x + barWidth * 0.46, y - barHeight), true, false);
            c.LineTo(new Point(x + barWidth * 0.54, y - barHeight), true, false);
            c.LineTo(new Point(x + barWidth * 0.60, y), true, false);
            c.LineTo(new Point(x + barWidth, y), true, false);
            c.LineTo(new Point(x + barWidth * 0.82, y + barHeight * 0.45), true, false);
            c.LineTo(new Point(x + barWidth * 0.18, y + barHeight * 0.45), true, false);
        }
        dc.DrawGeometry(ColorBrush(p.Deep, 150), new Pen(ColorBrush(p.Accent, 155), Math.Max(1, s * 0.0011)), g);
        if (key == "black-gold")
            Diamond(dc, width / 2, y - barHeight * 0.55, Math.Max(5, s * 0.007), ColorBrush(p.Accent, 220), new Pen(ColorBrush(p.Secondary, 180), 1));
    }

    private static void DrawThemeMotifs(DrawingContext dc, int width, int height, Palette p, string key)
    {
        var s = Math.Min(width, height);
        var accent = ColorBrush(p.Accent, 205);
        var secondary = ColorBrush(p.Secondary, 175);
        var soft = ColorBrush(p.Soft, 125);
        var pen = new Pen(accent, Math.Max(1.3, s * 0.0015));
        var motif = Math.Max(14, s * 0.019);

        switch (key)
        {
            case "sakura":
                PetalCluster(dc, width * 0.105, height * 0.105, motif * 0.95, accent, secondary, 18);
                PetalCluster(dc, width * 0.89, height * 0.88, motif * 0.82, secondary, accent, 205);
                DrawArcAccent(dc, width * 0.13, height * 0.12, motif * 3.1, p.Accent, 92, -12);
                break;
            case "doctor":
                DrawMedicalCross(dc, width * 0.89, height * 0.105, motif * 0.82, accent, ColorBrush(p.Deep, 200));
                DrawHeartbeat(dc, width, height, new Pen(secondary, Math.Max(1.4, s * 0.0017)));
                DrawPill(dc, width * 0.11, height * 0.87, motif * 1.15, -28, soft, pen);
                break;
            case "neon-tech":
                Circuit(dc, width, height, pen, secondary, 0.18);
                Hex(dc, width * 0.87, height * 0.13, motif * 0.92, null, new Pen(secondary, Math.Max(1, s * 0.0012)));
                Hex(dc, width * 0.13, height * 0.84, motif * 0.62, ColorBrush(p.Accent, 44), pen);
                break;
            case "black-gold":
                Diamond(dc, width * 0.5, height * 0.045, motif * 0.75, ColorBrush(p.Deep, 180), new Pen(accent, Math.Max(1.2, s * 0.0014)));
                DrawLuxuryFan(dc, width * 0.10, height * 0.11, motif * 2.0, pen);
                DrawLuxuryFan(dc, width * 0.90, height * 0.89, motif * 2.0, pen, 180);
                break;
            case "crimson-demon":
                Spikes(dc, width * 0.09, height * 0.11, motif * 1.65, accent);
                Spikes(dc, width * 0.91, height * 0.89, motif * 1.45, secondary);
                Slash(dc, width * 0.84, height * 0.12, motif * 2.2, -28, new Pen(accent, Math.Max(2, s * 0.0024)));
                Slash(dc, width * 0.865, height * 0.135, motif * 1.55, -28, new Pen(soft, Math.Max(1.2, s * 0.0014)));
                break;
            case "ice-crystal":
                CrystalCluster(dc, width * 0.09, height * 0.11, motif * 1.25, accent, secondary);
                CrystalCluster(dc, width * 0.91, height * 0.88, motif * 1.08, secondary, soft);
                Star(dc, width * 0.18, height * 0.08, motif * 0.42, soft);
                Star(dc, width * 0.82, height * 0.91, motif * 0.32, secondary);
                break;
            case "forest-mystic":
                DrawVine(dc, width * 0.09, height * 0.11, motif * 3.0, 28, pen, accent);
                DrawVine(dc, width * 0.91, height * 0.88, motif * 2.6, 208, new Pen(secondary, pen.Thickness), secondary);
                RuneRing(dc, width * 0.86, height * 0.15, motif * 1.05, soft, new Pen(accent, Math.Max(1, s * 0.0011)));
                break;
            case "cyber-orange":
                Circuit(dc, width, height, pen, secondary, 0.16);
                DrawChevronStack(dc, width * 0.87, height * 0.13, motif * 1.2, accent, -1);
                DrawChevronStack(dc, width * 0.13, height * 0.87, motif * 1.0, secondary, 1);
                break;
            case "moonlight-silver":
                Crescent(dc, width * 0.10, height * 0.12, motif * 1.25, accent);
                Star(dc, width * 0.16, height * 0.08, motif * 0.50, secondary);
                Star(dc, width * 0.88, height * 0.86, motif * 0.42, soft);
                DrawArcAccent(dc, width * 0.88, height * 0.84, motif * 2.3, p.Secondary, 80, 190);
                break;
        }
    }

    private static void DrawAtmosphere(DrawingContext dc, int width, int height, Palette p, string key)
    {
        var soft = ColorBrush(p.Soft, 18);
        var accent = ColorBrush(p.Accent, 22);
        var stripeWidth = Math.Max(60, width * 0.075);
        for (var i = -2; i < 8; i++)
        {
            var x = i * stripeWidth * 1.7;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(x, 0), true, true);
                c.LineTo(new Point(x + stripeWidth, 0), true, false);
                c.LineTo(new Point(x + stripeWidth + height * 0.24, height), true, false);
                c.LineTo(new Point(x + height * 0.24, height), true, false);
            }
            dc.DrawGeometry(i % 2 == 0 ? accent : soft, null, g);
        }

        if (key is "sakura" or "moonlight-silver")
        {
            var star = ColorBrush(p.Secondary, 76);
            for (var i = 0; i < 11; i++)
            {
                var px = width * (0.08 + ((i * 0.083) % 0.84));
                var py = height * (0.08 + ((i * 0.137) % 0.82));
                Star(dc, px, py, Math.Max(3, Math.Min(width, height) * (0.003 + (i % 3) * 0.001)), star);
            }
        }
    }

    private static void DrawScreenEmblem(DrawingContext dc, double x, double y, double size, string key, Palette p)
    {
        var accent = ColorBrush(p.Accent, 235);
        var secondary = ColorBrush(p.Secondary, 205);
        var pen = new Pen(accent, Math.Max(1.2, size * 0.08));
        switch (key)
        {
            case "sakura": PetalCluster(dc, x, y, size * 0.65, accent, secondary, 0); break;
            case "doctor": DrawMedicalCross(dc, x, y, size * 0.70, accent, ColorBrush(p.Deep, 220)); break;
            case "black-gold": Diamond(dc, x, y, size * 0.72, ColorBrush(p.Deep, 220), pen); break;
            case "crimson-demon": Spikes(dc, x, y + size * 0.18, size * 0.76, accent); break;
            case "ice-crystal": CrystalCluster(dc, x, y, size * 0.62, accent, secondary); break;
            case "forest-mystic": RuneRing(dc, x, y, size * 0.72, ColorBrush(p.Soft, 80), pen); break;
            case "moonlight-silver": Crescent(dc, x, y, size * 0.70, accent); break;
            default: Hex(dc, x, y, size * 0.72, ColorBrush(p.Deep, 180), pen); break;
        }
    }

    private static FormattedText FitText(string value, double startSize, double minSize, double maxWidth, Brush brush, string face)
    {
        var size = startSize;
        while (true)
        {
            var text = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(face), size, brush, 1.0);
            if (text.Width <= maxWidth || size <= minSize) return text;
            size = Math.Max(minSize, size - Math.Max(1, startSize * 0.045));
        }
    }

    private static void DrawTextShadow(DrawingContext dc, FormattedText text, Point point)
    {
        var shadow = text.BuildGeometry(new Point(point.X + 3, point.Y + 4));
        dc.DrawGeometry(ColorBrush(Colors.Black, 150), null, shadow);
        dc.DrawText(text, point);
    }

    private static void DrawGlow(DrawingContext dc, Point center, double radius, Color color, byte alpha)
    {
        var gradient = new RadialGradientBrush();
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, color.R, color.G, color.B), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha / 3), color.R, color.G, color.B), 0.46));
        gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        dc.DrawEllipse(gradient, null, center, radius, radius);
    }

    private static void DrawArcAccent(DrawingContext dc, double x, double y, double radius, Color color, byte alpha, double rotation)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            var start = PointOnCircle(x, y, radius, rotation);
            var end = PointOnCircle(x, y, radius, rotation + 92);
            c.BeginFigure(start, false, false);
            c.ArcTo(end, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
        }
        dc.DrawGeometry(null, new Pen(ColorBrush(color, alpha), Math.Max(1.1, radius * 0.045)), g);
    }

    private static Point PointOnCircle(double x, double y, double radius, double degrees)
    {
        var r = degrees * Math.PI / 180.0;
        return new Point(x + Math.Cos(r) * radius, y + Math.Sin(r) * radius);
    }

    private static void PetalCluster(DrawingContext dc, double x, double y, double size, Brush a, Brush b, double rotation)
    {
        for (var i = 0; i < 5; i++)
        {
            var angle = rotation + i * 72;
            var rad = angle * Math.PI / 180.0;
            var cx = x + Math.Cos(rad) * size * 0.55;
            var cy = y + Math.Sin(rad) * size * 0.55;
            dc.PushTransform(new RotateTransform(angle + 28, cx, cy));
            dc.DrawEllipse(i % 2 == 0 ? a : b, null, new Point(cx, cy), size * 0.48, size * 0.22);
            dc.Pop();
        }
        dc.DrawEllipse(ColorBrush(Colors.White, 170), null, new Point(x, y), size * 0.16, size * 0.16);
    }

    private static void DrawMedicalCross(DrawingContext dc, double x, double y, double size, Brush fill, Brush cutout)
    {
        var arm = size * 0.34;
        dc.DrawRoundedRectangle(fill, null, new Rect(x - arm / 2, y - size, arm, size * 2), arm * 0.22, arm * 0.22);
        dc.DrawRoundedRectangle(fill, null, new Rect(x - size, y - arm / 2, size * 2, arm), arm * 0.22, arm * 0.22);
        dc.DrawEllipse(cutout, null, new Point(x, y), size * 0.23, size * 0.23);
    }

    private static void DrawHeartbeat(DrawingContext dc, int width, int height, Pen pen)
    {
        var y = height * 0.90;
        var x = width * 0.34;
        var span = width * 0.32;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x, y), false, false);
            c.LineTo(new Point(x + span * 0.25, y), true, false);
            c.LineTo(new Point(x + span * 0.34, y - height * 0.022), true, false);
            c.LineTo(new Point(x + span * 0.40, y + height * 0.045), true, false);
            c.LineTo(new Point(x + span * 0.48, y - height * 0.065), true, false);
            c.LineTo(new Point(x + span * 0.56, y + height * 0.018), true, false);
            c.LineTo(new Point(x + span * 0.66, y), true, false);
            c.LineTo(new Point(x + span, y), true, false);
        }
        dc.DrawGeometry(null, pen, g);
    }

    private static void DrawPill(DrawingContext dc, double x, double y, double size, double angle, Brush fill, Pen pen)
    {
        dc.PushTransform(new RotateTransform(angle, x, y));
        dc.DrawRoundedRectangle(fill, pen, new Rect(x - size, y - size * 0.38, size * 2, size * 0.76), size * 0.38, size * 0.38);
        dc.DrawLine(pen, new Point(x, y - size * 0.34), new Point(x, y + size * 0.34));
        dc.Pop();
    }

    private static void Circuit(DrawingContext dc, int width, int height, Pen pen, Brush node, double span)
    {
        var y = height * 0.055;
        var x1 = width * (0.5 - span / 2);
        var x2 = width * (0.5 + span / 2);
        foreach (var yy in new[] { y, height - y })
        {
            dc.DrawLine(pen, new Point(x1, yy), new Point(x2, yy));
            dc.DrawEllipse(node, null, new Point(x1, yy), 4, 4);
            dc.DrawEllipse(node, null, new Point(x2, yy), 4, 4);
            dc.DrawEllipse(node, null, new Point((x1 + x2) / 2, yy), 2.5, 2.5);
        }
    }

    private static void Hex(DrawingContext dc, double x, double y, double size, Brush? fill, Pen pen)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            for (var i = 0; i < 6; i++)
            {
                var pt = PointOnCircle(x, y, size, -30 + i * 60);
                if (i == 0) c.BeginFigure(pt, fill is not null, true);
                else c.LineTo(pt, true, false);
            }
        }
        dc.DrawGeometry(fill, pen, g);
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

    private static void DrawLuxuryFan(DrawingContext dc, double x, double y, double size, Pen pen, double rotation = 0)
    {
        dc.PushTransform(new RotateTransform(rotation, x, y));
        for (var i = 0; i < 3; i++)
        {
            var offset = i * size * 0.20;
            dc.DrawLine(pen, new Point(x, y), new Point(x + size - offset, y + size * 0.42 + offset * 0.35));
        }
        dc.Pop();
    }

    private static void Spikes(DrawingContext dc, double x, double y, double size, Brush fill)
    {
        for (var i = -1; i <= 1; i++)
        {
            var cx = x + i * size * 0.55;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(cx - size * 0.22, y + size * 0.55), true, true);
                c.LineTo(new Point(cx, y - size * (0.72 + Math.Abs(i) * 0.22)), true, false);
                c.LineTo(new Point(cx + size * 0.22, y + size * 0.55), true, false);
            }
            dc.DrawGeometry(fill, null, g);
        }
    }

    private static void Slash(DrawingContext dc, double x, double y, double size, double angle, Pen pen)
    {
        dc.PushTransform(new RotateTransform(angle, x, y));
        dc.DrawLine(pen, new Point(x - size, y), new Point(x + size, y));
        dc.Pop();
    }

    private static void CrystalCluster(DrawingContext dc, double x, double y, double size, Brush a, Brush b)
    {
        Crystal(dc, x, y, size, a, 0);
        Crystal(dc, x - size * 0.62, y + size * 0.30, size * 0.72, b, -20);
        Crystal(dc, x + size * 0.62, y + size * 0.32, size * 0.68, b, 22);
    }

    private static void Crystal(DrawingContext dc, double x, double y, double size, Brush fill, double angle)
    {
        dc.PushTransform(new RotateTransform(angle, x, y));
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x, y - size), true, true);
            c.LineTo(new Point(x + size * 0.48, y - size * 0.18), true, false);
            c.LineTo(new Point(x + size * 0.28, y + size), true, false);
            c.LineTo(new Point(x - size * 0.36, y + size * 0.66), true, false);
            c.LineTo(new Point(x - size * 0.48, y - size * 0.18), true, false);
        }
        dc.DrawGeometry(fill, null, g);
        dc.Pop();
    }

    private static void DrawVine(DrawingContext dc, double x, double y, double size, double angle, Pen pen, Brush leafBrush)
    {
        dc.PushTransform(new RotateTransform(angle, x, y));
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x - size * 0.48, y + size * 0.24), false, false);
            c.BezierTo(new Point(x - size * 0.20, y - size * 0.45), new Point(x + size * 0.22, y + size * 0.44), new Point(x + size * 0.48, y - size * 0.20), true, false);
        }
        dc.DrawGeometry(null, pen, g);
        Leaf(dc, x - size * 0.14, y - size * 0.12, size * 0.18, -28, leafBrush, pen);
        Leaf(dc, x + size * 0.18, y + size * 0.06, size * 0.16, 35, leafBrush, pen);
        dc.Pop();
    }

    private static void Leaf(DrawingContext dc, double x, double y, double size, double angle, Brush fill, Pen pen)
    {
        dc.PushTransform(new RotateTransform(angle, x, y));
        dc.DrawEllipse(fill, pen, new Point(x, y), size, size * 0.42);
        dc.DrawLine(pen, new Point(x - size, y), new Point(x + size, y));
        dc.Pop();
    }

    private static void RuneRing(DrawingContext dc, double x, double y, double size, Brush fill, Pen pen)
    {
        dc.DrawEllipse(fill, pen, new Point(x, y), size, size);
        dc.DrawEllipse(null, pen, new Point(x, y), size * 0.68, size * 0.68);
        for (var i = 0; i < 4; i++)
        {
            var a = i * 90 + 45;
            dc.DrawLine(pen, PointOnCircle(x, y, size * 0.74, a), PointOnCircle(x, y, size * 1.06, a));
        }
    }

    private static void DrawChevronStack(DrawingContext dc, double x, double y, double size, Brush fill, double direction)
    {
        for (var i = 0; i < 3; i++)
        {
            var offset = i * size * 0.42 * direction;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(x + offset - size * 0.45 * direction, y - size * 0.35), true, true);
                c.LineTo(new Point(x + offset + size * 0.25 * direction, y), true, false);
                c.LineTo(new Point(x + offset - size * 0.45 * direction, y + size * 0.35), true, false);
                c.LineTo(new Point(x + offset - size * 0.18 * direction, y), true, false);
            }
            dc.DrawGeometry(fill, null, g);
        }
    }

    private static void Crescent(DrawingContext dc, double x, double y, double size, Brush fill)
    {
        var outer = new EllipseGeometry(new Point(x, y), size, size);
        var inner = new EllipseGeometry(new Point(x + size * 0.38, y - size * 0.08), size * 0.88, size * 0.88);
        dc.DrawGeometry(fill, null, new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner));
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
        "sakura" => new(C("#140A17"), C("#35132A"), C("#FF8FBF"), C("#C6A5FF"), C("#FFE0EF"), C("#0A0710")),
        "doctor" => new(C("#061418"), C("#103039"), C("#67E8F1"), C("#A8FFE9"), C("#D9FFFA"), C("#071215")),
        "neon-tech" => new(C("#050817"), C("#1A0B2D"), C("#27E9FF"), C("#C356FF"), C("#9FEFFF"), C("#050711")),
        "black-gold" => new(C("#080808"), C("#211A0D"), C("#E8C36A"), C("#FFF0B0"), C("#C79B45"), C("#050505")),
        "crimson-demon" => new(C("#120407"), C("#310810"), C("#FF405D"), C("#FF8A68"), C("#FFC0C9"), C("#080204")),
        "ice-crystal" => new(C("#061621"), C("#12384D"), C("#80E8FF"), C("#D8F8FF"), C("#A7E6F6"), C("#041018")),
        "forest-mystic" => new(C("#06140E"), C("#123326"), C("#58E49A"), C("#9FE6D5"), C("#B6F4D3"), C("#04100A")),
        "cyber-orange" => new(C("#130E07"), C("#342008"), C("#FF9A38"), C("#FFD08A"), C("#FFC782"), C("#0C0804")),
        "moonlight-silver" => new(C("#070D1A"), C("#1A2540"), C("#DCE8FF"), C("#92AEE0"), C("#F5F8FF"), C("#050914")),
        _ => new(C("#080B13"), C("#1A1E29"), C("#A7B8D6"), C("#DDE7F7"), C("#FFFFFF"), C("#05070B"))
    };

    private static SolidColorBrush ColorBrush(Color color, byte alpha) => new(Color.FromArgb(alpha, color.R, color.G, color.B));

    private static Color Mix(Color a, Color b, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        byte Blend(byte av, byte bv) => (byte)Math.Round(av + (bv - av) * amount);
        return Color.FromArgb(255, Blend(a.R, b.R), Blend(a.G, b.G), Blend(a.B, b.B));
    }

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
