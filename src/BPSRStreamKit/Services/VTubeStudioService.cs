using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BPSRStreamKit.Services;

public sealed record VTubeCaptureTarget(string WindowTitle, string WindowClass, string ExecutableName)
{
    public string ObsWindowString => $"{Escape(WindowTitle)}:{WindowClass}:{ExecutableName}";
    private static string Escape(string value) => value.Replace(":", "#3A", StringComparison.Ordinal);
}

public sealed class VTubeStudioService
{
    public const int SteamAppId = 1325860;
    private static readonly SemaphoreSlim LaunchGate = new(1, 1);
    private static readonly object LaunchSync = new();
    private static DateTime _lastLaunchRequestUtc = DateTime.MinValue;

    public bool IsRunning() => IsAnyVTubeProcessRunning();

    public void Launch()
    {
        if (TryGetCaptureTarget() is not null || IsAnyVTubeProcessRunning()) return;
        RequestSteamLaunchIfAllowed(TimeSpan.FromSeconds(15));
    }

    public async Task<VTubeCaptureTarget> LaunchAndWaitAsync(TimeSpan? timeout = null)
    {
        await LaunchGate.WaitAsync();
        try
        {
            var existing = TryGetCaptureTarget();
            if (existing is not null) return existing;

            var until = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));
            var recoveryAttemptUtc = DateTime.UtcNow + TimeSpan.FromSeconds(25);
            var recoveryUsed = false;

            if (!IsAnyVTubeProcessRunning()) RequestSteamLaunchIfAllowed(TimeSpan.FromSeconds(15));

            while (DateTime.UtcNow < until)
            {
                var target = TryGetCaptureTarget();
                if (target is not null) return target;

                if (!IsAnyVTubeProcessRunning())
                {
                    RequestSteamLaunchIfAllowed(TimeSpan.FromSeconds(15));
                }
                else if (!recoveryUsed && DateTime.UtcNow >= recoveryAttemptUtc)
                {
                    recoveryUsed = true;
                    RequestSteamLaunchIfAllowed(TimeSpan.FromSeconds(25), allowWhenRunning: true);
                }

                await Task.Delay(750);
            }

            throw new InvalidOperationException(
                "VTube Studio is taking too long to show its window. Leave it open, wait until your model appears, then try again.");
        }
        finally
        {
            LaunchGate.Release();
        }
    }

    private static void RequestSteamLaunchIfAllowed(TimeSpan minimumGap, bool allowWhenRunning = false)
    {
        if (!allowWhenRunning && IsAnyVTubeProcessRunning()) return;

        lock (LaunchSync)
        {
            if (DateTime.UtcNow - _lastLaunchRequestUtc < minimumGap) return;
            _lastLaunchRequestUtc = DateTime.UtcNow;
        }
        LaunchThroughSteam();
    }

    private static bool IsAnyVTubeProcessRunning()
    {
        Process[] processes;
        try { processes = Process.GetProcesses(); }
        catch { return false; }

        try
        {
            foreach (var process in processes)
            {
                try { if (LooksLikeVTubeStudio(process)) return true; }
                catch { }
            }
            return false;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static void LaunchThroughSteam()
    {
        var uri = $"steam://rungameid/{SteamAppId}";
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = uri,
                UseShellExecute = true
            });
        }
        catch
        {
            try { using var process = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
            catch { }
        }
    }

    public VTubeCaptureTarget? TryGetCaptureTarget()
    {
        VTubeCaptureTarget? found = null;
        EnumWindows((hwnd, _) =>
        {
            try
            {
                if (!IsWindowVisible(hwnd)) return true;
                GetWindowThreadProcessId(hwnd, out var pid);
                using var process = Process.GetProcessById((int)pid);
                if (!LooksLikeVTubeStudio(process)) return true;

                var titleLength = GetWindowTextLength(hwnd);
                if (titleLength < 1) return true;
                var title = new StringBuilder(titleLength + 1);
                GetWindowText(hwnd, title, title.Capacity);

                var windowClass = new StringBuilder(256);
                GetClassName(hwnd, windowClass, windowClass.Capacity);

                var exe = "VTube Studio.exe";
                try { exe = Path.GetFileName(process.MainModule?.FileName) ?? exe; } catch { }
                found = new VTubeCaptureTarget(title.ToString(), windowClass.ToString(), exe);
                return false;
            }
            catch { return true; }
        }, IntPtr.Zero);
        return found;
    }

    private static bool LooksLikeVTubeStudio(Process process)
    {
        try
        {
            var name = process.ProcessName.Replace(" ", string.Empty, StringComparison.Ordinal);
            return name.Contains("VTubeStudio", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")]
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
