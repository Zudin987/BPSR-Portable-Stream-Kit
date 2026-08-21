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

    public bool IsRunning() => TryGetProcess() is not null;

    public void Launch()
    {
        if (IsRunning()) return;
        Process.Start(new ProcessStartInfo($"steam://rungameid/{SteamAppId}") { UseShellExecute = true });
    }

    public async Task<VTubeCaptureTarget> LaunchAndWaitAsync(TimeSpan? timeout = null)
    {
        Launch();
        var until = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(25));
        while (DateTime.UtcNow < until)
        {
            var target = TryGetCaptureTarget();
            if (target is not null) return target;
            await Task.Delay(500);
        }

        return new VTubeCaptureTarget("VTube Studio", "UnityWndClass", "VTube Studio.exe");
    }

    public VTubeCaptureTarget? TryGetCaptureTarget()
    {
        VTubeCaptureTarget? found = null;
        EnumWindows((hwnd, _) =>
        {
            try
            {
                if (!IsWindowVisible(hwnd)) return true;
                _ = GetWindowThreadProcessId(hwnd, out var pid);
                using var process = Process.GetProcessById((int)pid);
                if (!LooksLikeVTubeStudio(process)) return true;

                var titleLength = GetWindowTextLength(hwnd);
                if (titleLength < 1) return true;
                var title = new StringBuilder(titleLength + 1);
                _ = GetWindowText(hwnd, title, title.Capacity);

                var windowClass = new StringBuilder(256);
                _ = GetClassName(hwnd, windowClass, windowClass.Capacity);

                var exe = "VTube Studio.exe";
                try { exe = Path.GetFileName(process.MainModule?.FileName) ?? exe; } catch { }
                found = new VTubeCaptureTarget(title.ToString(), windowClass.ToString(), exe);
                return false;
            }
            catch { return true; }
        }, IntPtr.Zero);
        return found;
    }

    private static Process? TryGetProcess()
    {
        try
        {
            return Process.GetProcesses().FirstOrDefault(LooksLikeVTubeStudio);
        }
        catch { return null; }
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
