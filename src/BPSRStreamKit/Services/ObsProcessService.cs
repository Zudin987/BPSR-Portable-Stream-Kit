using System.Diagnostics;
using System.Runtime.InteropServices;
using BPSRStreamKit.Infrastructure;

namespace BPSRStreamKit.Services;

public sealed class ObsProcessService
{
    private const uint WmClose = 0x0010;

    public bool IsRunning()
    {
        var processes = GetPortableObsProcesses();
        try { return processes.Count > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    public async Task<bool> CloseGracefullyAsync(TimeSpan? timeout = null)
    {
        var processes = GetPortableObsProcesses();
        if (processes.Count == 0) return false;

        try
        {
            foreach (var process in processes) RequestClose(process);

            var until = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
            while (DateTime.UtcNow < until)
            {
                var remaining = 0;
                foreach (var process in processes)
                {
                    try
                    {
                        process.Refresh();
                        if (process.HasExited) continue;
                        remaining++;
                    }
                    catch { }
                }

                if (remaining == 0) return true;
                await Task.Delay(400);
            }

            // Last resort only. A normal WM_CLOSE gets the full timeout so OBS/plugins can save state.
            foreach (var process in processes)
            {
                try
                {
                    process.Refresh();
                    if (process.HasExited) continue;
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(4));
                }
                catch { }
            }
            return true;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static void RequestClose(Process process)
    {
        try
        {
            process.Refresh();
            if (process.HasExited) return;
            var hwnd = process.MainWindowHandle;
            if (hwnd != IntPtr.Zero) _ = PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
            else _ = process.CloseMainWindow();
        }
        catch { }
    }

    private static List<Process> GetPortableObsProcesses()
    {
        var result = new List<Process>();
        var obsExe = AppPaths.FindObsExe();
        if (string.IsNullOrWhiteSpace(obsExe)) return result;

        var expectedPath = Path.GetFullPath(obsExe);
        var processName = Path.GetFileNameWithoutExtension(obsExe);
        foreach (var process in Process.GetProcessesByName(processName))
        {
            var keep = false;
            try
            {
                var actualPath = process.MainModule?.FileName;
                keep = !string.IsNullOrWhiteSpace(actualPath)
                       && Path.GetFullPath(actualPath).Equals(expectedPath, StringComparison.OrdinalIgnoreCase);
            }
            catch { }

            if (keep) result.Add(process);
            else process.Dispose();
        }
        return result;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
