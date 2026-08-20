using System.IO;
using System.IO.Compression;
using System.Management;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using BPSRStreamKit.Infrastructure;

namespace BPSRStreamKit.Services;

public sealed class SetupService
{
    private const string ObsVersion = "32.2.1";
    private const string FloodTuberVersion = "1.1.0";

    private const string ObsUrl = "https://github.com/obsproject/obs-studio/releases/download/32.2.1/OBS-Studio-32.2.1-Windows-x64.zip";
    private const string FloodTuberUrl = "https://github.com/justflood/flood-tuber/releases/download/v1.1.0/FloodTuber-Portable-v1.1.0.zip";
    private const string ObsSha256 = "db64a2934f8261f85b1410b84be011207a0afda5400d008289f1f1e211bcc7de";

    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromMinutes(15)
    };

    public async Task EnsureReadyAsync(IProgress<(int Percent, string Message)>? progress = null, bool repair = false)
    {
        Directory.CreateDirectory(AppPaths.CacheDirectory);

        progress?.Report((3, "Checking stream engine…"));
        var obsExe = AppPaths.FindObsExe();
        if (obsExe is null)
        {
            progress?.Report((8, $"Downloading OBS {ObsVersion}…"));
            await InstallObsAsync(progress);
        }

        var obsRoot = AppPaths.FindObsRoot() ?? throw new InvalidOperationException("OBS was downloaded but obs64.exe could not be found.");
        File.WriteAllText(Path.Combine(obsRoot, "portable_mode.txt"), string.Empty);

        progress?.Report((55, "Syncing avatar layer…"));
        await EnsureFloodTuberAsync(obsRoot, progress);

        progress?.Report((76, "Preparing stream layouts…"));
        InstallConfigTemplates(repair);

        progress?.Report((95, "Running final safety check…"));
        ValidateCoreFiles();

        progress?.Report((100, "Ready."));
    }

    private async Task InstallObsAsync(IProgress<(int Percent, string Message)>? progress)
    {
        var zipPath = Path.Combine(AppPaths.CacheDirectory, $"OBS-Studio-{ObsVersion}-Windows-x64.zip");

        if (!File.Exists(zipPath) || !await VerifySha256Async(zipPath, ObsSha256))
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            await DownloadFileAsync(ObsUrl, zipPath, 8, 47, progress, "Downloading portable stream engine");
        }

        if (!await VerifySha256Async(zipPath, ObsSha256))
            throw new InvalidDataException("OBS download failed its SHA-256 verification. Nothing was installed.");

        progress?.Report((49, "Unpacking stream engine…"));
        if (Directory.Exists(AppPaths.ObsDirectory))
        {
            try { Directory.Delete(AppPaths.ObsDirectory, true); }
            catch { }
        }

        Directory.CreateDirectory(AppPaths.ObsDirectory);
        ZipFile.ExtractToDirectory(zipPath, AppPaths.ObsDirectory, overwriteFiles: true);
    }

    private async Task EnsureFloodTuberAsync(string obsRoot, IProgress<(int Percent, string Message)>? progress)
    {
        var pluginDll = Path.Combine(obsRoot, "obs-plugins", "64bit", "flood-tuber.dll");
        if (File.Exists(pluginDll)) return;

        var zipPath = Path.Combine(AppPaths.CacheDirectory, $"FloodTuber-Portable-v{FloodTuberVersion}.zip");
        if (!File.Exists(zipPath))
            await DownloadFileAsync(FloodTuberUrl, zipPath, 56, 66, progress, "Downloading avatar support");

        var tempDir = Path.Combine(AppPaths.CacheDirectory, "floodtuber-temp");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);
        ZipFile.ExtractToDirectory(zipPath, tempDir, overwriteFiles: true);

        var sourceDll = Directory.EnumerateFiles(tempDir, "flood-tuber.dll", SearchOption.AllDirectories).FirstOrDefault();
        if (sourceDll is null)
            throw new InvalidDataException("FloodTuber archive did not contain flood-tuber.dll.");

        Directory.CreateDirectory(Path.GetDirectoryName(pluginDll)!);
        File.Copy(sourceDll, pluginDll, overwrite: true);

        var dataDirectory = Directory.EnumerateDirectories(tempDir, "flood-tuber", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Replace('\\', '/').Contains("/data/obs-plugins/flood-tuber", StringComparison.OrdinalIgnoreCase));

        if (dataDirectory is not null)
        {
            var destination = Path.Combine(obsRoot, "data", "obs-plugins", "flood-tuber");
            CopyDirectory(dataDirectory, destination, overwrite: true);
        }

        try { Directory.Delete(tempDir, true); } catch { }
    }

    private void InstallConfigTemplates(bool repair)
    {
        if (!Directory.Exists(AppPaths.TemplatesDirectory))
            throw new DirectoryNotFoundException("The templates folder is missing from this StreamKit release.");

        var configRoot = AppPaths.ObsConfigRoot() ?? throw new InvalidOperationException("Portable OBS config path could not be resolved.");
        Directory.CreateDirectory(configRoot);

        var encoder = DetectSimpleEncoder();
        var portableRoot = AppPaths.Root.Replace('\\', '/');

        foreach (var template in Directory.EnumerateFiles(AppPaths.TemplatesDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(AppPaths.TemplatesDirectory, template);
            var destination = Path.Combine(configRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            if (File.Exists(destination))
                continue;

            var text = File.ReadAllText(template)
                .Replace("__PACKROOT__", portableRoot, StringComparison.Ordinal)
                .Replace("__ENCODER__", encoder, StringComparison.Ordinal);

            File.WriteAllText(destination, text);
        }

        EnsureCleanGameScenes(configRoot);

        var userIni = Path.Combine(configRoot, "user.ini");
        if (!File.Exists(userIni))
        {
            File.WriteAllText(userIni,
                "[Basic]\n" +
                "Profile=Discord Share\n" +
                "ProfileDir=Discord Share\n" +
                "SceneCollection=BPSR Horizontal\n" +
                "SceneCollectionFile=BPSR_Horizontal\n");
        }
    }

    private static void EnsureCleanGameScenes(string configRoot)
    {
        var sceneRoot = Path.Combine(configRoot, "basic", "scenes");
        EnsureCleanGameScene(Path.Combine(sceneRoot, "BPSR_Horizontal.json"), "Discord Share", "Game Clean", "Minimal Stream Frame");
        EnsureCleanGameScene(Path.Combine(sceneRoot, "BPSR_TikTok_Vertical.json"), "TikTok Live", "Game Clean Vertical", "TikTok Minimal Frame");
    }

    private static void EnsureCleanGameScene(string file, string baseSceneName, string cleanSceneName, string frameSourceName)
    {
        if (!File.Exists(file)) return;

        var root = JsonNode.Parse(File.ReadAllText(file))?.AsObject();
        var sources = root?["sources"]?.AsArray();
        if (root is null || sources is null) return;

        var selectedSource = FindSource(sources, "Selected Game + Audio");
        if (selectedSource is null)
        {
            var bpsrSource = FindSource(sources, "BPSR Game + Audio");
            if (bpsrSource is null) return;

            selectedSource = bpsrSource.DeepClone().AsObject();
            selectedSource["name"] = "Selected Game + Audio";
            selectedSource["uuid"] = Guid.NewGuid().ToString();
            var settings = selectedSource["settings"]?.AsObject() ?? new JsonObject();
            selectedSource["settings"] = settings;
            settings["window"] = "Waiting for selected game:WindowClass:game.exe";
            settings["capture_mode"] = "window";
            settings["capture_audio"] = true;
            sources.Insert(Math.Max(0, sources.IndexOf(bpsrSource) + 1), selectedSource);
        }

        if (FindSource(sources, cleanSceneName) is not null)
        {
            File.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
            return;
        }

        var baseScene = FindSource(sources, baseSceneName);
        if (baseScene is null) return;

        var cleanScene = baseScene.DeepClone().AsObject();
        cleanScene["name"] = cleanSceneName;
        cleanScene["uuid"] = Guid.NewGuid().ToString();
        cleanScene["hotkeys"] = new JsonObject { ["OBSBasic.SelectScene"] = new JsonArray() };

        var cleanSettings = cleanScene["settings"]?.AsObject() ?? new JsonObject();
        cleanScene["settings"] = cleanSettings;
        cleanSettings["id_counter"] = 3;

        var baseItems = baseScene["settings"]?["items"]?.AsArray();
        if (baseItems is null) return;

        JsonObject? CloneItem(string sourceName)
        {
            return baseItems.OfType<JsonObject>()
                .FirstOrDefault(x => string.Equals(x["name"]?.GetValue<string>(), sourceName, StringComparison.Ordinal))
                ?.DeepClone().AsObject();
        }

        var gameItem = CloneItem("BPSR Game + Audio");
        var frameItem = CloneItem(frameSourceName);
        var avatarItem = CloneItem("FloodTuber Avatar");
        if (gameItem is null || frameItem is null || avatarItem is null) return;

        gameItem["name"] = "Selected Game + Audio";
        gameItem["source_uuid"] = selectedSource["uuid"]?.GetValue<string>();
        gameItem["id"] = 1;
        frameItem["id"] = 2;
        avatarItem["id"] = 3;

        cleanSettings["items"] = new JsonArray(gameItem, frameItem, avatarItem);
        sources.Add(cleanScene);

        var sceneOrder = root["scene_order"]?.AsArray();
        if (sceneOrder is not null && !sceneOrder.OfType<JsonObject>().Any(x => string.Equals(x["name"]?.GetValue<string>(), cleanSceneName, StringComparison.Ordinal)))
            sceneOrder.Add(new JsonObject { ["name"] = cleanSceneName });

        File.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    }

    private static JsonObject? FindSource(JsonArray sources, string name)
    {
        return sources.OfType<JsonObject>()
            .FirstOrDefault(x => string.Equals(x["name"]?.GetValue<string>(), name, StringComparison.Ordinal));
    }

    private static string DetectSimpleEncoder()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            var names = searcher.Get().Cast<ManagementObject>()
                .Select(obj => Convert.ToString(obj["Name"]) ?? string.Empty)
                .ToArray();

            if (names.Any(n => n.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))) return "nvenc";
            if (names.Any(n => n.Contains("AMD", StringComparison.OrdinalIgnoreCase) || n.Contains("Radeon", StringComparison.OrdinalIgnoreCase))) return "amd";
            if (names.Any(n => n.Contains("Intel", StringComparison.OrdinalIgnoreCase))) return "qsv";
        }
        catch { }

        return "x264";
    }

    private static void ValidateCoreFiles()
    {
        if (AppPaths.FindObsExe() is null)
            throw new FileNotFoundException("Portable OBS is missing after setup.");

        var obsRoot = AppPaths.FindObsRoot()!;
        var floodTuber = Path.Combine(obsRoot, "obs-plugins", "64bit", "flood-tuber.dll");
        if (!File.Exists(floodTuber))
            throw new FileNotFoundException("FloodTuber plugin is missing after setup.");

        var requiredAssets = new[]
        {
            Path.Combine(AppPaths.AssetsDirectory, "Frames", "01_Minimal_Thin_1080p.png"),
            Path.Combine(AppPaths.AssetsDirectory, "Frames", "05_TikTok_Minimal_1080x1920.png"),
            Path.Combine(AppPaths.AssetsDirectory, "MyAvatar", "idle.png"),
            Path.Combine(AppPaths.AssetsDirectory, "MyAvatar", "talk_a.png"),
            Path.Combine(AppPaths.AssetsDirectory, "Screens", "Starting_1080p.png"),
            Path.Combine(AppPaths.AssetsDirectory, "Screens", "BRB_1080p.png")
        };

        var missing = requiredAssets.FirstOrDefault(path => !File.Exists(path));
        if (missing is not null)
            throw new FileNotFoundException("This StreamKit build is missing a required visual asset. Download the complete release ZIP again.", missing);
    }

    private async Task DownloadFileAsync(string url, string destination, int startPercent, int endPercent,
        IProgress<(int Percent, string Message)>? progress, string label)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read));
            readTotal += read;

            if (total is > 0)
            {
                var fraction = Math.Clamp((double)readTotal / total.Value, 0, 1);
                var percent = startPercent + (int)Math.Round((endPercent - startPercent) * fraction);
                progress?.Report((percent, $"{label}… {fraction:P0}"));
            }
        }
    }

    private static async Task<bool> VerifySha256Async(string file, string expected)
    {
        if (!File.Exists(file)) return false;
        await using var stream = File.OpenRead(file);
        var hash = await SHA256.HashDataAsync(stream);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(string source, string destination, bool overwrite)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite);

        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)), overwrite);
    }
}
