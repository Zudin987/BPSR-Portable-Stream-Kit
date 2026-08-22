using System.Diagnostics;
using System.Runtime.InteropServices;
using BPSRStreamKit.Infrastructure;

namespace BPSRStreamKit.Services;

public sealed class ObsProcessV220
{
    private const uint WmClose = 0x0010;

    public bool IsRunning() => GetPortableObsProcesses().Count > 0;

    public async Task<bool> CloseGracefullyAsync(TimeSpan? timeout = null)
    {
        var processes = GetPortableObsProcesses();
        if (processes.Count == 0) return false;

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    process.Refresh();
                    if (process.HasExited) continue;
                    var hwnd = process.MainWindowHandle;
                    if (hwnd != IntPtr.Zero)
                    {
                        _ = PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
                    }
                    else
                    {
                        try { process.CloseMainWindow(); } catch { }
                    }
                }
                catch { }
            }

            var until = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(18));
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
                        var hwnd = process.MainWindowHandle;
                        if (hwnd != IntPtr.Zero) _ = PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
                    }
                    catch { }
                }
                if (remaining == 0) return true;
                await Task.Delay(500);
            }

            // Only force-close after giving OBS and its plugins plenty of time to save state.
            foreach (var process in processes)
            {
                try
                {
                    process.Refresh();
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(4));
                    }
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

    private static List<Process> GetPortableObsProcesses()
    {
        var result = new List<Process>();
        var obsExe = AppPaths.FindObsExe();
        if (string.IsNullOrWhiteSpace(obsExe)) return result;

        var expected = Path.GetFullPath(obsExe);
        var name = Path.GetFileNameWithoutExtension(obsExe);
        foreach (var process in Process.GetProcessesByName(name))
        {
            var keep = false;
            try
            {
                var actual = process.MainModule?.FileName;
                keep = !string.IsNullOrWhiteSpace(actual)
                       && Path.GetFullPath(actual).Equals(expected, StringComparison.OrdinalIgnoreCase);
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
