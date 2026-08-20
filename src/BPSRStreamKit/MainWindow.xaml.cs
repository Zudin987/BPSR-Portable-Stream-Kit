using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using BPSRStreamKit.Infrastructure;
using BPSRStreamKit.Models;
using BPSRStreamKit.Services;

namespace BPSRStreamKit;

public partial class MainWindow : Window
{
    private readonly DetectionService _detection = new();
    private readonly SetupService _setup = new();
    private readonly ObsService _obs = new();
    private readonly GameCatalogService _catalog = new();
    private readonly DispatcherTimer _statusTimer;

    private StreamTarget _selectedTarget = StreamTarget.Discord;
    private bool _busy;
    private bool _loadingGames;

    private GameTarget? SelectedGame => GameCombo.SelectedItem as GameTarget;

    public MainWindow()
    {
        InitializeComponent();
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _statusTimer.Tick += async (_, _) =>
        {
            if (!_busy) await RefreshStatusAsync();
        };
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyPlatformSelection();
        await RefreshGameChoicesAsync(preserveSelection: false);
        await RefreshStatusAsync();
        _statusTimer.Start();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var enabled = 1;
            _ = DwmSetWindowAttribute(hwnd, 20, ref enabled, sizeof(int));
        }
        catch { }
    }

    private async Task RefreshGameChoicesAsync(bool preserveSelection)
    {
        var previous = preserveSelection ? SelectedGame?.ProcessName : _catalog.GetLastSelectedProcess();
        _loadingGames = true;
        try
        {
            var choices = await _catalog.GetGameChoicesAsync();
            GameCombo.ItemsSource = choices;

            var selected = !string.IsNullOrWhiteSpace(previous)
                ? choices.FirstOrDefault(x => x.ProcessName.Equals(previous, StringComparison.OrdinalIgnoreCase))
                : null;

            GameCombo.SelectedItem = selected ?? choices.FirstOrDefault();
        }
        finally
        {
            _loadingGames = false;
        }

        UpdateGameCard();
    }

    private async Task RefreshStatusAsync()
    {
        var state = await _detection.DetectAsync(SelectedGame);
        ApplyStatus(state);
    }

    private void ApplyStatus(DetectionState state)
    {
        var game = SelectedGame;
        var gameName = game?.DisplayName ?? "Selected game";

        SetStatus(GameStatusDot, GameStatusText, state.GameRunning,
            $"{gameName} detected", $"Waiting for {gameName}");
        SetStatus(ObsStatusDot, ObsStatusText, state.ObsReady,
            "Portable engine ready", "Stream engine needs setup");
        SetStatus(AvatarStatusDot, AvatarStatusText, state.AvatarReady,
            "Avatar layer synced", "Avatar layer needs setup");
        SetStatus(AudioStatusDot, AudioStatusText, state.AudioIsolationReady,
            "Game + Mic isolated", "Audio sandbox needs setup");

        if (!state.ObsReady || !state.AvatarReady)
        {
            HeroEyebrow.Text = "SETUP REQUIRED";
            HeroEyebrow.Foreground = (Brush)FindResource("WarnBrush");
            HeroTitle.Text = "One click from ready";
            HeroSubtitle.Text = "StreamKit will prepare its portable stream engine, avatar layer and private capture layout automatically.";
            MainActionButton.Content = "Set up & " + GetActionLabel();
            FooterStatus.Text = "First run setup stays inside this folder";
            return;
        }

        MainActionButton.Content = GetActionLabel();

        if (!state.GameRunning)
        {
            HeroEyebrow.Text = "WAITING FOR GAME";
            HeroEyebrow.Foreground = (Brush)FindResource("WarnBrush");
            HeroTitle.Text = "Open your game";
            HeroSubtitle.Text = game?.IsBpsr == true
                ? "The stream engine is ready. Open Blue Protocol: Star Resonance and StreamKit will hook it automatically."
                : $"Open {gameName}, then press Scan games if its window changed.";
            FooterStatus.Text = "Private capture ready · waiting for game";
            return;
        }

        HeroEyebrow.Text = "READY";
        HeroEyebrow.Foreground = (Brush)FindResource("GoodBrush");
        HeroTitle.Text = _selectedTarget switch
        {
            StreamTarget.Discord => "Ready for Discord",
            StreamTarget.Twitch => "Ready for Twitch",
            StreamTarget.TikTok => "Ready for TikTok",
            _ => "Ready to stream"
        };

        if (game?.IsBpsr == true)
        {
            HeroSubtitle.Text = state.ResonanceLogsRunning
                ? "BPSR + Resonance Logs detected. Your full DPS/HUD layout is ready."
                : "BPSR is detected. DPS and Dungeon HUD will appear when Resonance Logs is open.";
        }
        else
        {
            HeroSubtitle.Text = $"{gameName} will use the clean layout: game + frame + avatar. BPSR-only DPS/HUD sources stay hidden.";
        }

        FooterStatus.Text = "Ready · no desktop capture · no Discord echo";
    }

    private void SetStatus(Ellipse dot, TextBlock label, bool ready, string readyText, string missingText)
    {
        dot.Fill = (Brush)FindResource(ready ? "GoodBrush" : "WarnBrush");
        label.Text = ready ? readyText : missingText;
        label.Foreground = (Brush)FindResource(ready ? "TextBrush" : "MutedBrush");
    }

    private async Task StartAsync()
    {
        if (_busy) return;

        try
        {
            SetBusy(true);
            await RefreshGameChoicesAsync(preserveSelection: true);
            var game = SelectedGame ?? throw new InvalidOperationException("Choose a game first.");
            var state = await _detection.DetectAsync(game);

            var showProgress = !state.ObsReady || !state.AvatarReady;
            if (showProgress) SetupProgressPanel.Visibility = Visibility.Visible;

            var progress = new Progress<(int Percent, string Message)>(value =>
            {
                SetupProgress.Value = value.Percent;
                SetupStatusText.Text = value.Message;
            });

            // This fast readiness pass also upgrades older installs with the clean-game scenes.
            await _setup.EnsureReadyAsync(showProgress ? progress : null);
            SetupProgressPanel.Visibility = Visibility.Collapsed;

            state = await _detection.DetectAsync(game);
            if (!state.GameRunning && !game.IsBpsr)
                throw new InvalidOperationException($"{game.DisplayName} is not running. Open the game, then click Scan games.");

            _catalog.Save(game);
            _catalog.SaveLastSelectedProcess(game.ProcessName);
            _obs.Launch(_selectedTarget, game);

            FooterStatus.Text = _selectedTarget switch
            {
                StreamTarget.Discord => "OBS ready · share the OBS Projector window in Discord",
                StreamTarget.Twitch => "Twitch layout opened · use OBS Start Streaming when your account is connected",
                StreamTarget.TikTok => "TikTok vertical layout opened · use your local TikTok stream method in OBS",
                _ => "OBS opened"
            };
        }
        catch (Exception ex)
        {
            SetupProgressPanel.Visibility = Visibility.Collapsed;
            MessageBox.Show(this,
                $"StreamKit couldn't finish this step.\n\n{ex.Message}\n\nOpen Advanced → Repair if the problem continues.",
                "StreamKit", MessageBoxButton.OK, MessageBoxImage.Warning);
            FooterStatus.Text = "Needs attention · your existing settings were not erased";
        }
        finally
        {
            SetBusy(false);
            await RefreshStatusAsync();
        }
    }

    private void ApplyPlatformSelection()
    {
        SetSegment(DiscordSegment, _selectedTarget == StreamTarget.Discord);
        SetSegment(TwitchSegment, _selectedTarget == StreamTarget.Twitch);
        SetSegment(TikTokSegment, _selectedTarget == StreamTarget.TikTok);
        MainActionButton.Content = GetActionLabel();

        ActionHint.Text = _selectedTarget switch
        {
            StreamTarget.Discord => "Game + Mic only · share OBS Projector in Discord",
            StreamTarget.Twitch => "1080p60 · Game + Mic only",
            StreamTarget.TikTok => "Vertical layout · Game + Mic only",
            _ => "Game + Mic only"
        };
    }

    private void SetSegment(Button button, bool selected)
    {
        button.Background = selected ? (Brush)FindResource("AccentGradient") : Brushes.Transparent;
        button.Foreground = selected ? Brushes.White : (Brush)FindResource("MutedBrush");
    }

    private string GetActionLabel() => _selectedTarget switch
    {
        StreamTarget.Discord => "Start Discord Stream",
        StreamTarget.Twitch => "Go Live on Twitch",
        StreamTarget.TikTok => "Start TikTok Live",
        _ => "Open Stream"
    };

    private void UpdateGameCard()
    {
        var game = SelectedGame;
        GameLayoutText.Text = game?.LayoutLabel ?? "Choose a game";
        PrivacyText.Text = game?.IsBpsr == true
            ? "Only BPSR + your mic are captured. Desktop, browsers, Discord voices and notifications stay out."
            : "Only the selected game + your mic are captured. BPSR DPS/HUD, desktop, browsers and Discord voices stay out.";
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        MainActionButton.IsEnabled = !busy;
        DiscordSegment.IsEnabled = !busy;
        TwitchSegment.IsEnabled = !busy;
        TikTokSegment.IsEnabled = !busy;
        GameCombo.IsEnabled = !busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
    }

    private async void MainAction_Click(object sender, RoutedEventArgs e) => await StartAsync();

    private async void DiscordSegment_Click(object sender, RoutedEventArgs e)
    {
        _selectedTarget = StreamTarget.Discord;
        ApplyPlatformSelection();
        await RefreshStatusAsync();
    }

    private async void TwitchSegment_Click(object sender, RoutedEventArgs e)
    {
        _selectedTarget = StreamTarget.Twitch;
        ApplyPlatformSelection();
        await RefreshStatusAsync();
    }

    private async void TikTokSegment_Click(object sender, RoutedEventArgs e)
    {
        _selectedTarget = StreamTarget.TikTok;
        ApplyPlatformSelection();
        await RefreshStatusAsync();
    }

    private async void GameCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingGames) return;
        UpdateGameCard();
        if (SelectedGame is { } game)
        {
            _catalog.SaveLastSelectedProcess(game.ProcessName);
            if (!game.IsBpsr) _catalog.Save(game);
        }
        await RefreshStatusAsync();
    }

    private async void ScanGames_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await RefreshGameChoicesAsync(preserveSelection: true);
        await RefreshStatusAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await RefreshGameChoicesAsync(preserveSelection: true);
        await RefreshStatusAsync();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        AdvancedPanel.Visibility = AdvancedPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
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
            FooterStatus.Text = "Repair complete · scene positions and account settings preserved";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Repair StreamKit", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SetupProgressPanel.Visibility = Visibility.Collapsed;
            SetBusy(false);
            await RefreshStatusAsync();
        }
    }

    private async void OpenObs_Click(object sender, RoutedEventArgs e)
    {
        var state = await _detection.DetectAsync(SelectedGame);
        if (!state.ObsReady)
        {
            await StartAsync();
            return;
        }
        _obs.Launch(StreamTarget.PlainObs);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.Root) { UseShellExecute = true });
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
