using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using BPSRStreamKit.Infrastructure;

namespace BPSRStreamKit;

public partial class MainWindow
{
    private DispatcherTimer? _obsCrashGuardV220;
    private IntPtr _lastObsCrashDialogV220;
    private DateTime _lastObsCrashDialogActionUtcV220 = DateTime.MinValue;
    private static readonly bool ObsCrashGuardHookRegisteredV220 = RegisterObsCrashGuardHookV220();

    private static bool RegisterObsCrashGuardHookV220()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ObsCrashGuardWindowLoadedV220));
        return true;
    }

    private static void ObsCrashGuardWindowLoadedV220(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._obsCrashGuardV220 is not null) return;

        window._obsCrashGuardV220 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        window._obsCrashGuardV220.Tick += (_, _) => window.TryContinuePortableObsNormallyV220();
        window._obsCrashGuardV220.Start();
        window.Closed += (_, _) => window._obsCrashGuardV220?.Stop();
    }

    private void TryContinuePortableObsNormallyV220()
    {
        try
        {
            var expectedExe = AppPaths.FindObsExe();
            if (string.IsNullOrWhiteSpace(expectedExe)) return;
            var expectedPath = Path.GetFullPath(expectedExe);

            EnumWindowsCrashV220((hwnd, _) =>
            {
                try
                {
                    if (!IsWindowVisibleCrashV220(hwnd)) return true;
                    var length = GetWindowTextLengthCrashV220(hwnd);
                    if (length <= 0) return true;

                    var title = new StringBuilder(length + 1);
                    _ = GetWindowTextCrashV220(hwnd, title, title.Capacity);
                    if (!title.ToString().Contains("OBS Studio Crash Detected", StringComparison.OrdinalIgnoreCase))
                        return true;

                    _ = GetWindowThreadProcessIdCrashV220(hwnd, out var pid);
                    using var process = Process.GetProcessById((int)pid);
                    string? actualPath = null;
                    try { actualPath = process.MainModule?.FileName; } catch { }
                    if (string.IsNullOrWhiteSpace(actualPath)
                        || !Path.GetFullPath(actualPath).Equals(expectedPath, StringComparison.OrdinalIgnoreCase))
                        return true;

                    // OBS 32 shows this prompt after an unclean close and its default button is
                    // "Run in Normal Mode". StreamKit requires third-party plugins + WebSockets,
                    // so Safe Mode cannot provide a working StreamKit session.
                    var recentlyHandled = hwnd == _lastObsCrashDialogV220
                                          && DateTime.UtcNow - _lastObsCrashDialogActionUtcV220 < TimeSpan.FromSeconds(3);
                    if (recentlyHandled) return false;

                    _lastObsCrashDialogV220 = hwnd;
                    _lastObsCrashDialogActionUtcV220 = DateTime.UtcNow;
                    _ = SetForegroundWindowCrashV220(hwnd);
                    _ = PostMessageCrashV220(hwnd, WmKeyDownV220, new IntPtr(VkReturnV220), IntPtr.Zero);
                    _ = PostMessageCrashV220(hwnd, WmKeyUpV220, new IntPtr(VkReturnV220), IntPtr.Zero);
                    FooterStatus.Text = "OBS recovered in Normal Mode · continuing setup automatically";
                    return false;
                }
                catch { return true; }
            }, IntPtr.Zero);
        }
        catch { }
    }

    private const uint WmKeyDownV220 = 0x0100;
    private const uint WmKeyUpV220 = 0x0101;
    private const int VkReturnV220 = 0x0D;

    private delegate bool ObsCrashEnumWindowsProcV220(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "EnumWindows")]
    private static extern bool EnumWindowsCrashV220(ObsCrashEnumWindowsProcV220 lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    private static extern bool IsWindowVisibleCrashV220(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    private static extern int GetWindowTextCrashV220(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextLengthW")]
    private static extern int GetWindowTextLengthCrashV220(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    private static extern uint GetWindowThreadProcessIdCrashV220(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    private static extern bool SetForegroundWindowCrashV220(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "PostMessageW")]
    private static extern bool PostMessageCrashV220(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
