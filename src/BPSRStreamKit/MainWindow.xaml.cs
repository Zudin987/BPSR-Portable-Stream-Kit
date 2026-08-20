using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using BPSRStreamKit.Models;
using BPSRStreamKit.Services;

namespace BPSRStreamKit;

public partial class MainWindow : Window
{
    private readonly DetectionService _detection = new();
    private readonly SetupService _setup = new();
    private readonly ObsService _obs = new();
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshStatusAsync();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var enabled = 1;
            _ = DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int));
        }
        catch
        {
            // Dark title-bar support is cosmetic; never block the launcher because of it.
        }
    }

    private async Task RefreshStatusAsync()
    {
        var state = await _detection.DetectAsync();
        ApplyStatus(state);
    }

    private void ApplyStatus(DetectionState state)
    {
        SetStatus(ObsStatusDot, ObsStatusText, state.ObsReady,
            "OBS: ready", "OBS: one-time setup needed");
        SetStatus(GameStatusDot, GameStatusText, state.GameRunning,
            "BPSR: running", "BPSR: not open");
        SetStatus(LogsStatusDot, LogsStatusText, state.ResonanceLogsRunning,
            "Resonance Logs: running", "Resonance Logs: not open");

        if (!state.ObsReady)
        {
            HeroTitle.Text = "Set up once, then just stream";
            HeroSubtitle.Text = "Click Start Discord. The launcher will prepare portable OBS and FloodTuber automatically, without installing them into Windows.";
            DiscordButton.Content = "Set up & start Discord";
            FooterStatus.Text = "First run downloads the pinned portable OBS build from the official release.";
            return;
        }

        DiscordButton.Content = "Start Discord";

        if (!state.GameRunning)
        {
            HeroTitle.Text = "Open BPSR, then you’re ready";
            HeroSubtitle.Text = "The stream setup is ready. Open Blue Protocol: Star Resonance first so OBS can hook the game automatically.";
            FooterStatus.Text = "OBS ready • waiting for BPSR";
        }
        else if (!state.ResonanceLogsRunning)
        {
            HeroTitle.Text = "BPSR detected";
            HeroSubtitle.Text = "You can stream now. Open Resonance Logs too if you want the DPS meter and Dungeon Mech HUD included.";
            FooterStatus.Text = "OBS + BPSR ready • DPS/HUD optional";
        }
        else
        {
            HeroTitle.Text = "Ready to stream";
            HeroSubtitle.Text = "BPSR and Resonance Logs are detected. Start Discord and the prepared scene will open with your saved layout.";
            FooterStatus.Text = "Everything detected • no full-screen capture";
        }
    }

    private void SetStatus(Ellipse dot, System.Windows.Controls.TextBlock label, bool ready, string readyText, string missingText)
    {
        dot.Fill = (Brush)FindResource(ready ? "GoodBrush" : "WarnBrush");
        label.Text = ready ? readyText : missingText;
        label.Foreground = (Brush)FindResource(ready ? "TextBrush" : "MutedBrush");
    }

    private async Task StartAsync(StreamTarget target)
    {
        if (_busy) return;

        try
        {
            SetBusy(true);
            var state = await _detection.DetectAsync();

            if (!state.ObsReady)
            {
                SetupProgressPanel.Visibility = Visibility.Visible;
                var progress = new Progress<(int Percent, string Message)>(value =>
                {
                    SetupProgress.Value = value.Percent;
                    SetupStatusText.Text = value.Message;
                });

                await _setup.EnsureReadyAsync(progress);
            }

            SetupProgressPanel.Visibility = Visibility.Collapsed;
            _obs.Launch(target);

            FooterStatus.Text = target switch
            {
                StreamTarget.Discord => "Discord scene opened • share the OBS Projector window in Discord",
                StreamTarget.Twitch => "Twitch scene opened • connect your Twitch account in OBS once",
                StreamTarget.TikTok => "TikTok vertical scene opened • add your local stream key/camera method in OBS",
                _ => "OBS opened"
            };

            await Task.Delay(350);
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            SetupProgressPanel.Visibility = Visibility.Collapsed;
            MessageBox.Show(
                this,
                $"The Stream Kit couldn't finish this step.\n\n{ex.Message}\n\nNothing was installed system-wide. You can use Settings → Repair setup and try again.",
                "BPSR Stream Kit",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            FooterStatus.Text = "Setup needs attention";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        DiscordButton.IsEnabled = !busy;
        TwitchButton.IsEnabled = !busy;
        TikTokButton.IsEnabled = !busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
    }

    private async void Discord_Click(object sender, RoutedEventArgs e) => await StartAsync(StreamTarget.Discord);
    private async void Twitch_Click(object sender, RoutedEventArgs e) => await StartAsync(StreamTarget.Twitch);
    private async void TikTok_Click(object sender, RoutedEventArgs e) => await StartAsync(StreamTarget.TikTok);

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await RefreshStatusAsync();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        AdvancedPanel.Visibility = AdvancedPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private async void Repair_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        try
        {
            SetBusy(true);
            SetupProgressPanel.Visibility = Visibility.Visible;
            var progress = new Progress<(int Percent, string Message)>(value =>
            {
                SetupProgress.Value = value.Percent;
                SetupStatusText.Text = value.Message;
            });

            await _setup.EnsureReadyAsync(progress, repair: true);
            SetupProgressPanel.Visibility = Visibility.Collapsed;
            FooterStatus.Text = "Setup checked • your saved scene positions and account settings were preserved";
            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            SetupProgressPanel.Visibility = Visibility.Collapsed;
            MessageBox.Show(this, ex.Message, "Repair setup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OpenObs_Click(object sender, RoutedEventArgs e)
    {
        var state = await _detection.DetectAsync();
        if (!state.ObsReady)
        {
            await StartAsync(StreamTarget.PlainObs);
            return;
        }

        _obs.Launch(StreamTarget.PlainObs);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
