using System.Diagnostics;
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
    public const string AitumVersion = "1.2.1";
    public const string SpoutVersion = "1.12.0";

    private const string ObsUrl = "https://github.com/obsproject/obs-studio/releases/download/32.2.1/OBS-Studio-32.2.1-Windows-x64.zip";
    private const string FloodTuberUrl = "https://github.com/justflood/flood-tuber/releases/download/v1.1.0/FloodTuber-Portable-v1.1.0.zip";
    private const string AitumUrl = "https://github.com/Aitum/obs-aitum-stream-suite/releases/download/1.2.1/aitum-stream-suite-windows.zip";
    private const string SpoutUrl = "https://github.com/Off-World-Live/obs-spout2-plugin/releases/download/1.12.0/win-spout-1.12.0-windows-x64-portable.zip";
    private const string ObsSha256 = "db64a2934f8261f85b1410b84be011207a0afda5400d008289f1f1e211bcc7de";
    private const string SpoutSha256 = "6c5a31d6f30a44277b1955d4f85a1da1c0baa97a13075594d2bbca475104ee8a";

    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromMinutes(15)
    };

    public async Task EnsureReadyAsync(IProgress<(int Percent, string Message)>? progress = null, bool repair = false,
        bool needAitum = false, bool needSpout = false)
    {
        Directory.CreateDirectory(AppPaths.CacheDirectory);

        if (repair)
        {
            progress?.Report((1, "Closing the old streaming engine…"));
            StopPortableObsForRepair();
            await Task.Delay(900);
        }

        progress?.Report((3, "Checking stream engine…"));
        var obsExe = AppPaths.FindObsExe();
        if (obsExe is null)
        {
            progress?.Report((8, $"Downloading OBS {ObsVersion}…"));
            await InstallObsAsync(progress);
        }

        var obsRoot = AppPaths.FindObsRoot() ?? throw new InvalidOperationException("OBS was downloaded but obs64.exe could not be found.");
        if (repair) ResetRuntimeComponentsForRepair(obsRoot, needAitum, needSpout);
        File.WriteAllText(Path.Combine(obsRoot, "portable_mode.txt"), string.Empty);
        File.WriteAllText(Path.Combine(obsRoot, "disable_updater.txt"), string.Empty);

        progress?.Report((52, "Syncing PNG avatar fallback…"));
        await EnsureFloodTuberAsync(obsRoot, progress);

        if (needSpout)
        {
            progress?.Report((61, $"Checking Spout2 {SpoutVersion} for transparent VTuber capture…"));
            await EnsureSpoutAsync(obsRoot, progress);
        }

        if (needAitum)
        {
            progress?.Report((70, $"Checking Aitum Stream Suite {AitumVersion}…"));
            await EnsureAitumAsync(obsRoot, progress);
        }

        progress?.Report((82, "Preparing stream layouts…"));
        InstallConfigTemplates(repair, needSpout);
        EnsureObsUpdatePolicy();

        var password = ObsAutomationService.GetOrCreatePassword();
        ObsAutomationService.EnsureServerConfig(password);
        if (needAitum) EnsureAitumProfileConfig();

        progress?.Report((95, "Running final safety check…"));
        ValidateCoreFiles(needAitum, needSpout);
        progress?.Report((100, "Ready."));
    }

    public bool IsAitumReady()
    {
        var root = AppPaths.FindObsRoot();
        return root is not null && FindAitumPlugin(root) is not null;
    }

    public bool IsSpoutReady()
    {
        var root = AppPaths.FindObsRoot();
        return root is not null && FindSpoutPlugin(root) is not null;
    }

    public string? GetVerticalCanvasUuid()
    {
        try
        {
            var root = LoadAitumConfig();
            var canvas = root?["canvas"]?.AsArray()?.OfType<JsonObject>()
                .FirstOrDefault(x => string.Equals(x["name"]?.GetValue<string>(), "Vertical", StringComparison.OrdinalIgnoreCase));
            var uuid = canvas?["uuid"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(uuid) ? null : uuid;
        }
        catch { return null; }
    }

    public bool HasAitumStreamOutput()
    {
        try
        {
            var outputs = LoadAitumConfig()?["outputs"]?.AsArray();
            if (outputs is null) return false;
            return outputs.OfType<JsonObject>().Any(x =>
            {
                var type = x["type"]?.GetValue<string>() ?? "stream";
                var server = x["stream_server"]?.GetValue<string>() ?? x["server"]?.GetValue<string>() ?? string.Empty;
                return (string.IsNullOrWhiteSpace(type) || type.Equals("stream", StringComparison.OrdinalIgnoreCase))
                       && !string.IsNullOrWhiteSpace(server);
            });
        }
        catch { return false; }
    }

    public void EnsureAitumProfileConfig()
    {
        var path = AitumConfigPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        JsonObject root;
        try { root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? new JsonObject() : new JsonObject(); }
        catch { root = new JsonObject(); }

        root["main_stream_output_show"] = true;
        root["main_virtual_cam_output_show"] = true;

        var canvases = root["canvas"]?.AsArray();
        if (canvases is null)
        {
            canvases = new JsonArray();
            root["canvas"] = canvases;
        }

        var vertical = canvases.OfType<JsonObject>()
            .FirstOrDefault(x => string.Equals(x["name"]?.GetValue<string>(), "Vertical", StringComparison.OrdinalIgnoreCase));
        if (vertical is null)
        {
            vertical = new JsonObject
            {
                ["name"] = "Vertical",
                ["type"] = "extra",
                ["width"] = 1080,
                ["height"] = 1920,
                ["color"] = 2038295,
                ["expanded"] = true
            };
            canvases.Add(vertical);
        }
        else
        {
            vertical["name"] = "Vertical";
            vertical["type"] = "extra";
            vertical["width"] = 1080;
            vertical["height"] = 1920;
        }

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
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
            try { Directory.Delete(AppPaths.ObsDirectory, true); } catch { }
        }
        Directory.CreateDirectory(AppPaths.ObsDirectory);
        ZipFile.ExtractToDirectory(zipPath, AppPaths.ObsDirectory, overwriteFiles: true);
    }

    private async Task EnsureFloodTuberAsync(string obsRoot, IProgress<(int Percent, string Message)>? progress)
    {
        var pluginDll = Path.Combine(obsRoot, "obs-plugins", "64bit", "flood-tuber.dll");
        if (IsUsablePluginFile(pluginDll)) return;
        try { if (File.Exists(pluginDll)) File.Delete(pluginDll); } catch { }

        var zipPath = Path.Combine(AppPaths.CacheDirectory, $"FloodTuber-Portable-v{FloodTuberVersion}.zip");
        var expectedSha = await GetOfficialGitHubAssetSha256Async("justflood/flood-tuber", "v1.1.0", $"FloodTuber-Portable-v{FloodTuberVersion}.zip");
        if (!File.Exists(zipPath) || !await VerifySha256Async(zipPath, expectedSha))
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
            await DownloadFileAsync(FloodTuberUrl, zipPath, 53, 60, progress, "Downloading PNG avatar fallback");
        }
        if (!await VerifySha256Async(zipPath, expectedSha))
            throw new InvalidDataException("FloodTuber download failed its official GitHub SHA-256 verification. Nothing was installed.");

        var tempDir = Path.Combine(AppPaths.CacheDirectory, "floodtuber-temp");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, tempDir, overwriteFiles: true);
            var sourceDll = Directory.EnumerateFiles(tempDir, "flood-tuber.dll", SearchOption.AllDirectories).FirstOrDefault();
            if (sourceDll is null || !IsUsablePluginFile(sourceDll))
                throw new InvalidDataException("FloodTuber archive did not contain a usable flood-tuber.dll.");
            Directory.CreateDirectory(Path.GetDirectoryName(pluginDll)!);
            File.Copy(sourceDll, pluginDll, overwrite: true);

            var dataDirectory = Directory.EnumerateDirectories(tempDir, "flood-tuber", SearchOption.AllDirectories)
                .FirstOrDefault(path => path.Replace('\\', '/').Contains("/data/obs-plugins/flood-tuber", StringComparison.OrdinalIgnoreCase));
            if (dataDirectory is not null)
                CopyDirectory(dataDirectory, Path.Combine(obsRoot, "data", "obs-plugins", "flood-tuber"), overwrite: true);
        }
        catch
        {
            try { File.Delete(zipPath); } catch { }
            throw;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private async Task EnsureSpoutAsync(string obsRoot, IProgress<(int Percent, string Message)>? progress)
    {
        if (FindSpoutPlugin(obsRoot) is not null) return;

        var zipPath = Path.Combine(AppPaths.CacheDirectory, $"win-spout-{SpoutVersion}-windows-x64-portable.zip");
        if (!File.Exists(zipPath) || !await VerifySha256Async(zipPath, SpoutSha256))
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            await DownloadFileAsync(SpoutUrl, zipPath, 61, 68, progress, "Downloading transparent VTuber capture");
        }
        if (!await VerifySha256Async(zipPath, SpoutSha256))
            throw new InvalidDataException("Spout2 download failed its SHA-256 verification. Nothing was installed.");

        var tempDir = Path.Combine(AppPaths.CacheDirectory, "spout-temp");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, tempDir, overwriteFiles: true);
            CopyPortableObsPlugin(tempDir, obsRoot);
        }
        catch
        {
            try { File.Delete(zipPath); } catch { }
            throw new InvalidDataException("The Spout2 download was not a valid portable OBS plugin ZIP. Run Repair to retry the official pinned download.");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }

        if (FindSpoutPlugin(obsRoot) is null)
            throw new InvalidDataException("Spout2 archive did not contain win-spout.dll in the expected portable OBS layout.");
    }

    private async Task EnsureAitumAsync(string obsRoot, IProgress<(int Percent, string Message)>? progress)
    {
        if (FindAitumPlugin(obsRoot) is not null) return;

        var zipPath = Path.Combine(AppPaths.CacheDirectory, $"aitum-stream-suite-{AitumVersion}-windows.zip");
        var expectedSha = await GetOfficialGitHubAssetSha256Async("Aitum/obs-aitum-stream-suite", AitumVersion, "aitum-stream-suite-windows.zip");
        if (!File.Exists(zipPath) || !await VerifySha256Async(zipPath, expectedSha))
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
            await DownloadFileAsync(AitumUrl, zipPath, 70, 78, progress, "Downloading Aitum multistream support");
        }
        if (!await VerifySha256Async(zipPath, expectedSha))
            throw new InvalidDataException("Aitum download failed its official GitHub SHA-256 verification. Nothing was installed.");

        var tempDir = Path.Combine(AppPaths.CacheDirectory, "aitum-temp");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);
        try
        {
            ZipFile.ExtractToDirectory(zipPath, tempDir, overwriteFiles: true);
            var copied = CopyPortableObsPlugin(tempDir, obsRoot);
            if (copied == 0 || FindAitumPlugin(obsRoot) is null)
                throw new InvalidDataException("Aitum Stream Suite archive did not contain the expected portable OBS plugin files.");
        }
        catch
        {
            try { File.Delete(zipPath); } catch { }
            throw;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static int CopyPortableObsPlugin(string tempDir, string obsRoot)
    {
        var copied = 0;
        foreach (var file in Directory.EnumerateFiles(tempDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(tempDir, file);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var index = Array.FindIndex(parts, p => p.Equals("obs-plugins", StringComparison.OrdinalIgnoreCase) || p.Equals("data", StringComparison.OrdinalIgnoreCase));
            if (index < 0) continue;
            var destination = Path.Combine(new[] { obsRoot }.Concat(parts.Skip(index)).ToArray());
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
            copied++;
        }
        return copied;
    }

    private void InstallConfigTemplates(bool repair, bool useSpout)
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
            var normalized = relative.Replace('\\', '/');
            var generatedScene = normalized.Equals("basic/scenes/BPSR_Horizontal.json", StringComparison.OrdinalIgnoreCase)
                                 || normalized.Equals("basic/scenes/BPSR_TikTok_Vertical.json", StringComparison.OrdinalIgnoreCase);
            if (File.Exists(destination) && !(repair && generatedScene)) continue;
            if (repair && generatedScene && File.Exists(destination)) BackupGeneratedScene(destination);
            var text = File.ReadAllText(template)
                .Replace("__PACKROOT__", portableRoot, StringComparison.Ordinal)
                .Replace("__ENCODER__", encoder, StringComparison.Ordinal);
            File.WriteAllText(destination, text);
        }

        EnsureCleanGameScenes(configRoot, useSpout);
        var userIni = Path.Combine(configRoot, "user.ini");
        if (!File.Exists(userIni))
        {
            File.WriteAllText(userIni,
                "[Basic]\nProfile=Discord Share\nProfileDir=Discord Share\nSceneCollection=BPSR Horizontal\nSceneCollectionFile=BPSR_Horizontal\n");
        }
    }

    private static void BackupGeneratedScene(string path)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.BackupDirectory);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var name = Path.GetFileNameWithoutExtension(path);
            File.Copy(path, Path.Combine(AppPaths.BackupDirectory, $"{name}-{stamp}.json"), overwrite: true);
        }
        catch { }
    }

    private static bool IsUsablePluginFile(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).Length > 1024; }
        catch { return false; }
    }

    private static void ResetRuntimeComponentsForRepair(string obsRoot, bool needAitum, bool needSpout)
    {
        static void DeleteFile(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
        static void DeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }

        DeleteFile(Path.Combine(obsRoot, "obs-plugins", "64bit", "flood-tuber.dll"));
        DeleteDirectory(Path.Combine(obsRoot, "data", "obs-plugins", "flood-tuber"));
        DeleteFile(Path.Combine(AppPaths.CacheDirectory, $"FloodTuber-Portable-v{FloodTuberVersion}.zip"));

        if (needSpout)
        {
            DeleteFile(Path.Combine(obsRoot, "obs-plugins", "64bit", "win-spout.dll"));
            DeleteDirectory(Path.Combine(obsRoot, "data", "obs-plugins", "win-spout"));
            DeleteDirectory(Path.Combine(obsRoot, "data", "obs-plugins", "spout2"));
            DeleteFile(Path.Combine(AppPaths.CacheDirectory, $"win-spout-{SpoutVersion}-windows-x64-portable.zip"));
        }

        if (needAitum)
        {
            try
            {
                var pluginRoot = Path.Combine(obsRoot, "obs-plugins");
                if (Directory.Exists(pluginRoot))
                    foreach (var file in Directory.EnumerateFiles(pluginRoot, "*aitum*.dll", SearchOption.AllDirectories).ToArray()) DeleteFile(file);
                var dataRoot = Path.Combine(obsRoot, "data", "obs-plugins");
                if (Directory.Exists(dataRoot))
                    foreach (var dir in Directory.EnumerateDirectories(dataRoot).Where(x => Path.GetFileName(x).Contains("aitum", StringComparison.OrdinalIgnoreCase)).ToArray()) DeleteDirectory(dir);
            }
            catch { }
            DeleteFile(Path.Combine(AppPaths.CacheDirectory, $"aitum-stream-suite-{AitumVersion}-windows.zip"));
        }
    }

    private async Task<string> GetOfficialGitHubAssetSha256Async(string repository, string tag, string assetName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/tags/{tag}");
        request.Headers.TryAddWithoutValidation("User-Agent", "StreamKit/2.0.3");
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!document.RootElement.TryGetProperty("assets", out var assets))
            throw new InvalidDataException($"GitHub did not return release assets for {repository} {tag}.");

        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var name) || !string.Equals(name.GetString(), assetName, StringComparison.Ordinal)) continue;
            if (!asset.TryGetProperty("digest", out var digestNode)) break;
            var digest = digestNode.GetString() ?? string.Empty;
            if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) && digest.Length > 7)
                return digest[7..];
            break;
        }
        throw new InvalidDataException($"GitHub did not publish a SHA-256 digest for {assetName}. StreamKit will not install an unverified archive.");
    }

    private static void StopPortableObsForRepair()
    {
        var obsExe = AppPaths.FindObsExe();
        if (string.IsNullOrWhiteSpace(obsExe)) return;
        var expectedPath = Path.GetFullPath(obsExe);
        var processName = Path.GetFileNameWithoutExtension(obsExe);

        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                var actualPath = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(actualPath) ||
                    !Path.GetFullPath(actualPath).Equals(expectedPath, StringComparison.OrdinalIgnoreCase)) continue;
                if (!process.CloseMainWindow() || !process.WaitForExit(3000))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2500);
                }
            }
            catch { }
            finally { process.Dispose(); }
        }
    }

    private static void EnsureObsUpdatePolicy()
    {
        var configRoot = AppPaths.ObsConfigRoot();
        if (configRoot is null) return;
        Directory.CreateDirectory(configRoot);
        var path = Path.Combine(configRoot, "global.ini");
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();

        var general = lines.FindIndex(line => line.Trim().Equals("[General]", StringComparison.OrdinalIgnoreCase));
        if (general < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1])) lines.Add(string.Empty);
            general = lines.Count;
            lines.Add("[General]");
        }

        var nextSection = lines.FindIndex(general + 1, line => line.TrimStart().StartsWith("[", StringComparison.Ordinal));
        if (nextSection < 0) nextSection = lines.Count;
        var updateIndex = -1;
        for (var i = general + 1; i < nextSection; i++)
        {
            if (lines[i].TrimStart().StartsWith("EnableAutoUpdates=", StringComparison.OrdinalIgnoreCase))
            {
                updateIndex = i;
                break;
            }
        }

        if (updateIndex >= 0) lines[updateIndex] = "EnableAutoUpdates=false";
        else lines.Insert(nextSection, "EnableAutoUpdates=false");
        File.WriteAllLines(path, lines);
    }

    private static void EnsureCleanGameScenes(string configRoot, bool useSpout)
    {
        var sceneRoot = Path.Combine(configRoot, "basic", "scenes");
        EnsureCleanGameScene(Path.Combine(sceneRoot, "BPSR_Horizontal.json"), "Discord Share", "Game Clean", "Minimal Stream Frame", useSpout);
        EnsureCleanGameScene(Path.Combine(sceneRoot, "BPSR_TikTok_Vertical.json"), "TikTok Live", "Game Clean Vertical", "TikTok Minimal Frame", useSpout);
    }

    private static void EnsureCleanGameScene(string file, string baseSceneName, string cleanSceneName, string frameSourceName, bool useSpout)
    {
        if (!File.Exists(file)) return;
        var root = JsonNode.Parse(File.ReadAllText(file))?.AsObject();
        var sources = root?["sources"]?.AsArray();
        if (root is null || sources is null) return;

        var selectedSource = FindSource(sources, "Selected Game + Audio");
        if (selectedSource is null)
        {
            var original = FindSource(sources, "BPSR Game + Audio");
            if (original is null) return;
            selectedSource = original.DeepClone().AsObject();
            selectedSource["name"] = "Selected Game + Audio";
            selectedSource["uuid"] = Guid.NewGuid().ToString();
            var settings = selectedSource["settings"]?.AsObject() ?? new JsonObject();
            selectedSource["settings"] = settings;
            settings["window"] = "Waiting for selected game:WindowClass:game.exe";
            settings["capture_mode"] = "window";
            settings["capture_audio"] = true;
            settings["capture_cursor"] = false;
            sources.Insert(Math.Max(0, sources.IndexOf(original) + 1), selectedSource);
        }

        var vTube = FindSource(sources, "VTube Studio Avatar");
        if (vTube is null)
        {
            vTube = selectedSource.DeepClone().AsObject();
            vTube["name"] = "VTube Studio Avatar";
            vTube["uuid"] = Guid.NewGuid().ToString();
            vTube["mixers"] = 0;
            var settings = vTube["settings"]?.AsObject() ?? new JsonObject();
            vTube["settings"] = settings;
            settings["window"] = "VTube Studio:UnityWndClass:VTube Studio.exe";
            settings["capture_mode"] = "window";
            settings["capture_audio"] = false;
            settings["capture_cursor"] = false;
            settings["allow_transparency"] = true;
            settings["anti_cheat_hook"] = false;
            sources.Insert(Math.Max(0, sources.IndexOf(selectedSource) + 1), vTube);
        }
        if (useSpout)
        {
            vTube["id"] = "spout_capture";
            vTube["versioned_id"] = "spout_capture";
            vTube["mixers"] = 0;
            vTube["settings"] = new JsonObject
            {
                ["spoutsenders"] = "VTubeStudioSpout",
                ["tickspeedlimit"] = 100,
                ["compositemode"] = 4
            };
        }

        var baseScene = FindSource(sources, baseSceneName);
        if (baseScene is null) return;
        EnsureVTubeSceneItem(baseScene, vTube);
        if (file.EndsWith("BPSR_Horizontal.json", StringComparison.OrdinalIgnoreCase))
        {
            var twitchLive = FindSource(sources, "Twitch Live");
            if (twitchLive is not null) EnsureVTubeSceneItem(twitchLive, vTube);
        }
        var cleanScene = FindSource(sources, cleanSceneName);
        if (cleanScene is null)
        {
            cleanScene = baseScene.DeepClone().AsObject();
            cleanScene["name"] = cleanSceneName;
            cleanScene["uuid"] = Guid.NewGuid().ToString();
            cleanScene["hotkeys"] = new JsonObject { ["OBSBasic.SelectScene"] = new JsonArray() };
            sources.Add(cleanScene);
            var sceneOrder = root["scene_order"]?.AsArray();
            if (sceneOrder is not null) sceneOrder.Add(new JsonObject { ["name"] = cleanSceneName });
        }

        var settingsObj = cleanScene["settings"]?.AsObject() ?? new JsonObject();
        cleanScene["settings"] = settingsObj;
        var items = settingsObj["items"]?.AsArray() ?? new JsonArray();
        settingsObj["items"] = items;

        JsonObject? FindItem(string name) => items.OfType<JsonObject>().FirstOrDefault(x => string.Equals(x["name"]?.GetValue<string>(), name, StringComparison.Ordinal));
        JsonObject? CloneBaseItem(string name) => baseScene["settings"]?["items"]?.AsArray()?.OfType<JsonObject>()
            .FirstOrDefault(x => string.Equals(x["name"]?.GetValue<string>(), name, StringComparison.Ordinal))?.DeepClone().AsObject();

        var gameItem = FindItem("Selected Game + Audio") ?? FindItem("BPSR Game + Audio");
        if (gameItem is null) gameItem = CloneBaseItem("BPSR Game + Audio");
        if (gameItem is not null)
        {
            gameItem["name"] = "Selected Game + Audio";
            gameItem["source_uuid"] = selectedSource["uuid"]?.GetValue<string>();
            if (!items.Contains(gameItem)) items.Insert(0, gameItem);
        }

        var frameItem = FindItem(frameSourceName) ?? CloneBaseItem(frameSourceName);
        if (frameItem is not null && !items.Contains(frameItem)) items.Add(frameItem);

        var pngItem = FindItem("FloodTuber Avatar") ?? CloneBaseItem("FloodTuber Avatar");
        if (pngItem is not null && !items.Contains(pngItem)) items.Add(pngItem);

        var vTubeItem = FindItem("VTube Studio Avatar");
        if (vTubeItem is null)
        {
            vTubeItem = (gameItem ?? CloneBaseItem("BPSR Game + Audio"))?.DeepClone().AsObject();
            if (vTubeItem is not null)
            {
                vTubeItem["name"] = "VTube Studio Avatar";
                vTubeItem["source_uuid"] = vTube["uuid"]?.GetValue<string>();
                vTubeItem["visible"] = false;
                vTubeItem["locked"] = true;
                vTubeItem["id"] = items.OfType<JsonObject>().Select(x => x["id"]?.GetValue<int>() ?? 0).DefaultIfEmpty().Max() + 1;
                items.Add(vTubeItem);
            }
        }
        else
        {
            vTubeItem["source_uuid"] = vTube["uuid"]?.GetValue<string>();
        }

        settingsObj["id_counter"] = items.OfType<JsonObject>().Select(x => x["id"]?.GetValue<int>() ?? 0).DefaultIfEmpty().Max();
        File.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    }

    private static void EnsureVTubeSceneItem(JsonObject scene, JsonObject vTube)
    {
        var settings = scene["settings"]?.AsObject() ?? new JsonObject();
        scene["settings"] = settings;
        var items = settings["items"]?.AsArray() ?? new JsonArray();
        settings["items"] = items;

        var existing = items.OfType<JsonObject>()
            .FirstOrDefault(x => string.Equals(x["name"]?.GetValue<string>(), "VTube Studio Avatar", StringComparison.Ordinal));
        if (existing is not null)
        {
            existing["source_uuid"] = vTube["uuid"]?.GetValue<string>();
            return;
        }

        var template = items.OfType<JsonObject>()
            .FirstOrDefault(x => string.Equals(x["name"]?.GetValue<string>(), "FloodTuber Avatar", StringComparison.Ordinal))
            ?? items.OfType<JsonObject>().FirstOrDefault(x => string.Equals(x["name"]?.GetValue<string>(), "Selected Game + Audio", StringComparison.Ordinal))
            ?? items.OfType<JsonObject>().FirstOrDefault(x => string.Equals(x["name"]?.GetValue<string>(), "BPSR Game + Audio", StringComparison.Ordinal));

        var item = template?.DeepClone().AsObject() ?? new JsonObject();
        item["name"] = "VTube Studio Avatar";
        item["source_uuid"] = vTube["uuid"]?.GetValue<string>();
        item["visible"] = false;
        item["locked"] = true;
        item["rot"] = 0.0;
        item["align"] = 5;
        item["bounds_type"] = 2;
        item["bounds_align"] = 5;
        item["bounds_crop"] = false;
        item["crop_left"] = 0;
        item["crop_top"] = 0;
        item["crop_right"] = 0;
        item["crop_bottom"] = 0;
        item["pos"] = new JsonObject { ["x"] = 20.0, ["y"] = 490.0 };
        item["scale"] = new JsonObject { ["x"] = 1.0, ["y"] = 1.0 };
        item["bounds"] = new JsonObject { ["x"] = 520.0, ["y"] = 570.0 };
        item["scale_filter"] = "lanczos";
        item["id"] = items.OfType<JsonObject>().Select(x => x["id"]?.GetValue<int>() ?? 0).DefaultIfEmpty().Max() + 1;
        items.Add(item);
        settings["id_counter"] = items.OfType<JsonObject>().Select(x => x["id"]?.GetValue<int>() ?? 0).DefaultIfEmpty().Max();
    }

    private static JsonObject? FindSource(JsonArray sources, string name) => sources.OfType<JsonObject>()
        .FirstOrDefault(x => string.Equals(x["name"]?.GetValue<string>(), name, StringComparison.Ordinal));

    private static string DetectSimpleEncoder()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            var names = searcher.Get().Cast<ManagementObject>().Select(obj => Convert.ToString(obj["Name"]) ?? string.Empty).ToArray();
            if (names.Any(n => n.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))) return "nvenc";
            if (names.Any(n => n.Contains("AMD", StringComparison.OrdinalIgnoreCase) || n.Contains("Radeon", StringComparison.OrdinalIgnoreCase))) return "amd";
            if (names.Any(n => n.Contains("Intel", StringComparison.OrdinalIgnoreCase))) return "qsv";
        }
        catch { }
        return "x264";
    }

    private static void ValidateCoreFiles(bool needAitum, bool needSpout)
    {
        if (AppPaths.FindObsExe() is null) throw new FileNotFoundException("Portable OBS is missing after setup.");
        var obsRoot = AppPaths.FindObsRoot()!;
        if (!IsUsablePluginFile(Path.Combine(obsRoot, "obs-plugins", "64bit", "flood-tuber.dll")))
            throw new FileNotFoundException("FloodTuber plugin is missing or damaged after setup.");
        if (needSpout && FindSpoutPlugin(obsRoot) is null)
            throw new FileNotFoundException("Spout2 plugin is missing after setup.");
        if (needAitum && FindAitumPlugin(obsRoot) is null)
            throw new FileNotFoundException("Aitum Stream Suite is missing after setup.");

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
        if (missing is not null) throw new FileNotFoundException("This StreamKit build is missing a required visual asset. Download the complete release ZIP again.", missing);
    }

    private static string? FindSpoutPlugin(string obsRoot)
    {
        var expected = Path.Combine(obsRoot, "obs-plugins", "64bit", "win-spout.dll");
        if (IsUsablePluginFile(expected)) return expected;
        try
        {
            return Directory.EnumerateFiles(Path.Combine(obsRoot, "obs-plugins"), "win-spout.dll", SearchOption.AllDirectories)
                .FirstOrDefault(IsUsablePluginFile);
        }
        catch { return null; }
    }

    private static string? FindAitumPlugin(string obsRoot)
    {
        try
        {
            return Directory.EnumerateFiles(Path.Combine(obsRoot, "obs-plugins"), "*.dll", SearchOption.AllDirectories)
                .FirstOrDefault(x => Path.GetFileName(x).Contains("aitum", StringComparison.OrdinalIgnoreCase) && IsUsablePluginFile(x));
        }
        catch { return null; }
    }

    private static string AitumConfigPath()
    {
        var configRoot = AppPaths.ObsConfigRoot() ?? throw new InvalidOperationException("Portable OBS config path could not be resolved.");
        return Path.Combine(configRoot, "basic", "profiles", "Twitch 1080p", "aitum.json");
    }

    private static JsonObject? LoadAitumConfig()
    {
        var path = AitumConfigPath();
        if (!File.Exists(path)) return null;
        return JsonNode.Parse(File.ReadAllText(path))?.AsObject();
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
        return string.Equals(Convert.ToHexString(hash).ToLowerInvariant(), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyDirectory(string source, string destination, bool overwrite)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite);
        foreach (var directory in Directory.EnumerateDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)), overwrite);
    }
}
