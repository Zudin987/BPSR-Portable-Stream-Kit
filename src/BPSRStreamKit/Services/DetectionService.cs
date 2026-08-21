using System.Diagnostics;
using System.IO;
using BPSRStreamKit.Infrastructure;
using BPSRStreamKit.Models;

namespace BPSRStreamKit.Services;

public sealed class DetectionService
{
    public Task<DetectionState> DetectAsync(GameTarget? selectedGame = null)
    {
        return Task.Run(() =>
        {
            var obsPath = AppPaths.FindObsExe();
            var processName = selectedGame?.ProcessName ?? "StarSEA";
            var game = FindProcess(processName);
            var logs = FindProcess("resonance-logs-cn");

            var obsRoot = AppPaths.FindObsRoot();
            var avatarReady = obsRoot is not null
                && File.Exists(Path.Combine(obsRoot, "obs-plugins", "64bit", "flood-tuber.dll"))
                && File.Exists(Path.Combine(AppPaths.AssetsDirectory, "MyAvatar", "idle.png"))
                && File.Exists(Path.Combine(AppPaths.AssetsDirectory, "MyAvatar", "talk_a.png"));

            var aitumReady = false;
            if (obsRoot is not null)
            {
                try
                {
                    var pluginRoot = Path.Combine(obsRoot, "obs-plugins");
                    aitumReady = Directory.Exists(pluginRoot) && Directory.EnumerateFiles(pluginRoot, "*.dll", SearchOption.AllDirectories)
                        .Any(x => Path.GetFileName(x).Contains("aitum", StringComparison.OrdinalIgnoreCase));
                }
                catch { }
            }

            var vtubeRunning = false;
            try
            {
                vtubeRunning = Process.GetProcesses().Any(p =>
                {
                    try
                    {
                        using (p)
                            return p.ProcessName.Replace(" ", string.Empty, StringComparison.Ordinal)
                                .Contains("VTubeStudio", StringComparison.OrdinalIgnoreCase);
                    }
                    catch { p.Dispose(); return false; }
                });
            }
            catch { }

            var audioReady = obsPath is not null
                && File.Exists(Path.Combine(AppPaths.TemplatesDirectory, "basic", "profiles", "Discord Share", "basic.ini"));

            return new DetectionState(
                ObsReady: obsPath is not null,
                GameRunning: game is not null,
                ResonanceLogsRunning: logs is not null,
                AvatarReady: avatarReady,
                AudioIsolationReady: audioReady,
                VTubeStudioRunning: vtubeRunning,
                AitumReady: aitumReady,
                ObsPath: obsPath,
                GamePath: SafeProcessPath(game),
                ResonanceLogsPath: SafeProcessPath(logs));
        });
    }

    private static Process? FindProcess(string processName)
    {
        try { return Process.GetProcessesByName(processName).FirstOrDefault(); }
        catch { return null; }
    }

    private static string? SafeProcessPath(Process? process)
    {
        if (process is null) return null;
        try { return process.MainModule?.FileName; }
        catch { return null; }
        finally { process.Dispose(); }
    }
}
