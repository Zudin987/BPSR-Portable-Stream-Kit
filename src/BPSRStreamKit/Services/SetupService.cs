using System.IO.Compression;
using System.Management;
using System.Net.Http;
using System.Security.Cryptography;
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

        progress?.Report((3, "Checking portable OBS…"));
        var obsExe = AppPaths.FindObsExe();

        if (obsExe is null)
        {
            progress?.Report((8, $"Downloading OBS {ObsVersion}…"));
            await InstallObsAsync(progress);
        }

        var obsRoot = AppPaths.FindObsRoot() ?? throw new InvalidOperationException("OBS was downloaded but obs64.exe could not be found.");
        File.WriteAllText(Path.Combine(obsRoot, "portable_mode.txt"), string.Empty);

        progress?.Report((55, "Checking FloodTuber avatar support…"));
        await EnsureFloodTuberAsync(obsRoot, progress);

        progress?.Report((76, "Preparing your stream layout…"));
        InstallConfigTemplates(repair);

        progress?.Report((95, "Final check…"));
        ValidateCoreFiles();

        progress?.Report((100, "Ready."));
    }

    private async Task InstallObsAsync(IProgress<(int Percent, string Message)>? progress)
    {
        var zipPath = Path.Combine(AppPaths.CacheDirectory, $"OBS-Studio-{ObsVersion}-Windows-x64.zip");

        if (!File.Exists(zipPath) || !await VerifySha256Async(zipPath, ObsSha256))
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            await DownloadFileAsync(ObsUrl, zipPath, 8, 47, progress, "Downloading portable OBS");
        }

        if (!await VerifySha256Async(zipPath, ObsSha256))
            throw new InvalidDataException("OBS download failed its SHA-256 verification. Nothing was installed.");

        progress?.Report((49, "Extracting portable OBS…"));

        if (Directory.Exists(AppPaths.ObsDirectory))
        {
            try { Directory.Delete(AppPaths.ObsDirectory, true); }
            catch { /* A partial folder may contain locked files; extraction below will report a useful error. */ }
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
            await DownloadFileAsync(FloodTuberUrl, zipPath, 56, 66, progress, "Downloading FloodTuber");

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
            throw new DirectoryNotFoundException("The templates folder is missing from this Stream Kit release.");

        var configRoot = AppPaths.ObsConfigRoot() ?? throw new InvalidOperationException("Portable OBS config path could not be resolved.");
        Directory.CreateDirectory(configRoot);

        var encoder = DetectSimpleEncoder();
        var portableRoot = AppPaths.Root.Replace('\\', '/');

        foreach (var template in Directory.EnumerateFiles(AppPaths.TemplatesDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(AppPaths.TemplatesDirectory, template);
            var destination = Path.Combine(configRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            // Protect the user's working layout/profile. Repair fills missing files; it does not reset custom positions,
            // linked accounts, stream keys, or other user settings.
            if (File.Exists(destination))
                continue;

            var text = File.ReadAllText(template)
                .Replace("__PACKROOT__", portableRoot, StringComparison.Ordinal)
                .Replace("__ENCODER__", encoder, StringComparison.Ordinal);

            File.WriteAllText(destination, text);
        }

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
        catch
        {
            // Fall back to x264 if Windows GPU enumeration is unavailable.
        }

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
    }

    private async Task DownloadFileAsync(
        string url,
        string destination,
        int startPercent,
        int endPercent,
        IProgress<(int Percent, string Message)>? progress,
        string label)
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
