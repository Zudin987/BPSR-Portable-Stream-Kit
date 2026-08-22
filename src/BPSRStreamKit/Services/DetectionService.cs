using System.Diagnostics;
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
            var game = GetProcessInfo(processName);
            var logs = GetProcessInfo("resonance-logs-cn");

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
                    aitumReady = Directory.Exists(pluginRoot)
                                 && Directory.EnumerateFiles(pluginRoot, "*.dll", SearchOption.AllDirectories)
                                     .Any(x => Path.GetFileName(x).Contains("aitum", StringComparison.OrdinalIgnoreCase));
                }
                catch { }
            }

            var audioReady = obsPath is not null
                && File.Exists(Path.Combine(AppPaths.TemplatesDirectory, "basic", "profiles", "Discord Share", "basic.ini"));

            return new DetectionState(
                ObsReady: obsPath is not null,
                GameRunning: game.Running,
                ResonanceLogsRunning: logs.Running,
                AvatarReady: avatarReady,
                AudioIsolationReady: audioReady,
                VTubeStudioRunning: IsVTubeStudioRunning(),
                AitumReady: aitumReady,
                ObsPath: obsPath,
                GamePath: game.Path,
                ResonanceLogsPath: logs.Path);
        });
    }

    private static (bool Running, string? Path) GetProcessInfo(string processName)
    {
        Process[] processes;
        try { processes = Process.GetProcessesByName(processName); }
        catch { return (false, null); }

        try
        {
            if (processes.Length == 0) return (false, null);
            string? path = null;
            foreach (var process in processes)
            {
                try
                {
                    if (!process.HasExited && path is null) path = process.MainModule?.FileName;
                }
                catch { }
            }
            return (true, path);
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static bool IsVTubeStudioRunning()
    {
        Process[] processes;
        try { processes = Process.GetProcesses(); }
        catch { return false; }

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    var name = process.ProcessName.Replace(" ", string.Empty, StringComparison.Ordinal);
                    if (name.Contains("VTubeStudio", StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch { }
            }
            return false;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }
}
