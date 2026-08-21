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

    public bool IsRunning()
    {
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    if (LooksLikeVTubeStudio(process)) return true;
                }
            }
        }
        catch { }
        return false;
    }

    public void Launch()
    {
        // A stale/background process should not block the beginner flow. If there is no usable
        // visible VTube Studio window, ask Steam to launch/restore the app again.
        if (TryGetCaptureTarget() is not null) return;
        LaunchThroughSteam();
    }

    public async Task<VTubeCaptureTarget> LaunchAndWaitAsync(TimeSpan? timeout = null)
    {
        var existing = TryGetCaptureTarget();
        if (existing is not null) return existing;

        var until = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(45));
        var nextLaunchAttempt = DateTime.MinValue;
        while (DateTime.UtcNow < until)
        {
            if (DateTime.UtcNow >= nextLaunchAttempt)
            {
                LaunchThroughSteam();
                nextLaunchAttempt = DateTime.UtcNow + TimeSpan.FromSeconds(8);
            }

            await Task.Delay(500);
            var target = TryGetCaptureTarget();
            if (target is not null) return target;
        }

        throw new InvalidOperationException(
            "VTube Studio did not open a detectable window. Install/open VTube Studio in Steam, choose a Live2D model and webcam once, then retry Full VTuber mode.");
    }

    private static void LaunchThroughSteam()
    {
        Process.Start(new ProcessStartInfo($"steam://rungameid/{SteamAppId}") { UseShellExecute = true });
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
