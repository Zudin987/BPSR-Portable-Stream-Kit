using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using BPSRStreamKit.Infrastructure;
using BPSRStreamKit.Models;

namespace BPSRStreamKit.Services;

public sealed class GameCatalogService
{
    private static readonly HashSet<string> ExcludedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "firefox", "chrome", "msedge", "discord", "vesktop", "obs64",
        "resonance-logs-cn", "BPSRStreamKit", "SearchHost", "StartMenuExperienceHost",
        "ShellExperienceHost", "TextInputHost", "ApplicationFrameHost", "SystemSettings",
        "Taskmgr", "dwm", "LockApp", "Widgets", "RuntimeBroker"
    };

    private readonly string _settingsPath = Path.Combine(AppPaths.Root, "user-data", "games.json");
    private readonly string _lastSelectionPath = Path.Combine(AppPaths.Root, "user-data", "last-game.txt");

    public Task<IReadOnlyList<GameTarget>> GetGameChoicesAsync()
    {
        return Task.Run<IReadOnlyList<GameTarget>>(() =>
        {
            var windows = EnumerateVisibleWindows();
            var result = new List<GameTarget>();

            var bpsr = windows.FirstOrDefault(x => x.ProcessName.Equals("StarSEA", StringComparison.OrdinalIgnoreCase));
            result.Add(bpsr is null
                ? new GameTarget("Blue Protocol: Star Resonance", "StarSEA", "StarSEA.exe", "Blue Protocol: Star Resonance", "UnityWndClass", true, false)
                : bpsr with { DisplayName = "Blue Protocol: Star Resonance", IsBpsr = true });

            foreach (var saved in LoadSaved())
            {
                if (saved.IsBpsr) continue;
                var running = windows.FirstOrDefault(x => x.ProcessName.Equals(saved.ProcessName, StringComparison.OrdinalIgnoreCase));
                if (running is not null)
                    AddUnique(result, running with { DisplayName = saved.DisplayName });
                else
                    AddUnique(result, saved with { IsRunning = false });
            }

            foreach (var candidate in windows)
            {
                if (candidate.IsBpsr || candidate.ProcessName.Equals("StarSEA", StringComparison.OrdinalIgnoreCase)) continue;
                AddUnique(result, candidate);
            }

            return result;
        });
    }

    public string? GetLastSelectedProcess()
    {
        try { return File.Exists(_lastSelectionPath) ? File.ReadAllText(_lastSelectionPath).Trim() : null; }
        catch { return null; }
    }

    public void SaveLastSelectedProcess(string processName)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_lastSelectionPath)!);
            File.WriteAllText(_lastSelectionPath, processName);
        }
        catch { }
    }

    public void Save(GameTarget target)
    {
        if (target.IsBpsr) return;

        var saved = LoadSaved().Where(x => !x.ProcessName.Equals(target.ProcessName, StringComparison.OrdinalIgnoreCase)).ToList();
        saved.Insert(0, target with { IsRunning = false });
        if (saved.Count > 12) saved = saved.Take(12).ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
    }

    private List<GameTarget> LoadSaved()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return new List<GameTarget>();
            return JsonSerializer.Deserialize<List<GameTarget>>(File.ReadAllText(_settingsPath)) ?? new List<GameTarget>();
        }
        catch
        {
            return new List<GameTarget>();
        }
    }

    private static void AddUnique(List<GameTarget> list, GameTarget item)
    {
        if (list.Any(x => x.ProcessName.Equals(item.ProcessName, StringComparison.OrdinalIgnoreCase))) return;
        list.Add(item);
    }

    private static List<GameTarget> EnumerateVisibleWindows()
    {
        var results = new List<GameTarget>();

        EnumWindows((hwnd, _) =>
        {
            try
            {
                if (!IsWindowVisible(hwnd)) return true;
                var length = GetWindowTextLength(hwnd);
                if (length < 2) return true;

                var titleBuilder = new StringBuilder(length + 1);
                _ = GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
                var title = titleBuilder.ToString().Trim();
                if (string.IsNullOrWhiteSpace(title)) return true;

                _ = GetWindowThreadProcessId(hwnd, out var pid);
                using var process = Process.GetProcessById((int)pid);
                var processName = process.ProcessName;
                if (ExcludedProcesses.Contains(processName)) return true;

                string exeName;
                try { exeName = Path.GetFileName(process.MainModule?.FileName) ?? processName + ".exe"; }
                catch { exeName = processName + ".exe"; }

                var classBuilder = new StringBuilder(256);
                _ = GetClassName(hwnd, classBuilder, classBuilder.Capacity);
                var windowClass = classBuilder.ToString();
                if (string.IsNullOrWhiteSpace(windowClass)) return true;

                var display = MakeFriendlyName(title, processName);
                results.Add(new GameTarget(display, processName, exeName, title, windowClass,
                    processName.Equals("StarSEA", StringComparison.OrdinalIgnoreCase), true));
            }
            catch
            {
                // Protected or closing windows are ignored.
            }

            return true;
        }, IntPtr.Zero);

        return results
            .GroupBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.WindowTitle.Length).First())
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string MakeFriendlyName(string title, string processName)
    {
        var separators = new[] { " - ", " — ", " | " };
        foreach (var separator in separators)
        {
            var first = title.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(first) && first.Length >= 3 && first.Length <= 48)
                return first;
        }

        return title.Length <= 52 ? title : processName;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
