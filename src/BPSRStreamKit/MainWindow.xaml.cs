using System.Diagnostics;
using System.IO;
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
    private sealed record ThemeChoice(StreamTheme Theme, string DisplayName, string Detail);
    private sealed record AvatarChoice(AvatarMode Mode, string DisplayName, string Detail);

    private readonly DetectionService _detection = new();
    private readonly SetupService _setup = new();
    private readonly ObsService _obs = new();
    private readonly ObsAutomationService _automation = new();
    private readonly VTubeStudioService _vTubeStudio = new();
    private readonly GameCatalogService _catalog = new();
    private readonly DispatcherTimer _statusTimer;

    private readonly IReadOnlyList<ThemeChoice> _themes = new[]
    {
        new ThemeChoice(StreamTheme.ProfileA, "Profile A", "Sakura · decorated pink frame"),
        new ThemeChoice(StreamTheme.ProfileB, "Profile B", "Doctor · decorated medical frame")
    };

    private readonly IReadOnlyList<AvatarChoice> _avatars = new[]
    {
        new AvatarChoice(AvatarMode.VTubeStudio, "Full VTuber", "Face-tracked Live2D via VTube Studio"),
        new AvatarChoice(AvatarMode.PngAvatar, "PNG Avatar", "Lightweight FloodTuber fallback"),
        new AvatarChoice(AvatarMode.None, "None", "Game + frame only")
    };

    private StreamMode _selectedMode = StreamMode.DiscordOnly;
    private AvatarMode _selectedAvatar = AvatarMode.VTubeStudio;
    private StreamTheme _selectedTheme = StreamTheme.ProfileA;
    private bool _busy;
    private bool _loadingGames;
    private bool _loadingTheme;
    private bool _loadingAvatar;

    private GameTarget? SelectedGame => GameCombo.SelectedItem as GameTarget;
    private ThemeChoice? SelectedThemeChoice => ThemeCombo.SelectedItem as ThemeChoice;
    private AvatarChoice? SelectedAvatarChoice => AvatarCombo.SelectedItem as AvatarChoice;
    private static string ThemePreferenceFile => Path.Combine(AppPaths.Root, ".streamkit-theme");
    private static string AvatarPreferenceFile => Path.Combine(AppPaths.Root, ".streamkit-avatar");
    private static string ModePreferenceFile => Path.Combine(AppPaths.Root, ".streamkit-mode");

    public MainWindow()
    {
        InitializeComponent();

        _loadingTheme = true;
        ThemeCombo.ItemsSource = _themes;
        _selectedTheme = LoadSavedTheme();
        ThemeCombo.SelectedItem = _themes.FirstOrDefault(x => x.Theme == _selectedTheme) ?? _themes[0];
        _loadingTheme = false;

        _loadingAvatar = true;
        AvatarCombo.ItemsSource = _avatars;
        _selectedAvatar = LoadSavedAvatar();
        AvatarCombo.SelectedItem = _avatars.FirstOrDefault(x => x.Mode == _selectedAvatar) ?? _avatars[0];
        _loadingAvatar = false;

        _selectedMode = LoadSavedMode();
        UpdateThemeCard();
        UpdateAvatarCard();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _statusTimer.Tick += async (_, _) =>
        {
            if (!_busy) await RefreshStatusAsync();
        };
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyModeSelection();
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
        finally { _loadingGames = false; }
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
            $"{gameName} detected", game is null ? "Choose or open a game" : $"Waiting for {gameName}");

        var engineReady = state.ObsReady && (_selectedMode != StreamMode.AllPlatforms || state.AitumReady);
        SetStatus(ObsStatusDot, ObsStatusText, engineReady,
            _selectedMode == StreamMode.AllPlatforms ? "OBS + Aitum ready" : "Portable OBS ready",
            _selectedMode == StreamMode.AllPlatforms ? "OBS/Aitum will be prepared" : "Portable OBS needs setup");

        switch (_selectedAvatar)
        {
            case AvatarMode.VTubeStudio:
                SetStatus(AvatarStatusDot, AvatarStatusText, state.VTubeStudioRunning,
                    "VTube Studio tracking app open", "VTube Studio opens automatically");
                break;
            case AvatarMode.PngAvatar:
                SetStatus(AvatarStatusDot, AvatarStatusText, state.AvatarReady,
                    "PNG avatar ready", "PNG avatar fallback needs setup");
                break;
            default:
                SetStatus(AvatarStatusDot, AvatarStatusText, true, "Avatar disabled", "Avatar disabled");
                break;
        }

        SetStatus(AudioStatusDot, AudioStatusText, state.AudioIsolationReady,
            "Game + Mic isolated", "Private audio setup needed");

        var needsSetup = !state.ObsReady
                         || (_selectedAvatar == AvatarMode.PngAvatar && !state.AvatarReady)
                         || (_selectedMode == StreamMode.AllPlatforms && !state.AitumReady);
        if (needsSetup)
        {
            HeroEyebrow.Text = "SETUP REQUIRED";
            HeroEyebrow.Foreground = (Brush)FindResource("WarnBrush");
            HeroTitle.Text = "One click from ready";
            HeroSubtitle.Text = _selectedMode == StreamMode.AllPlatforms
                ? "StreamKit will prepare portable OBS, Aitum vertical/multistream support and your selected avatar mode."
                : "StreamKit will prepare portable OBS, the selected frame and your avatar mode automatically.";
            MainActionButton.Content = "Set up & " + GetActionLabel();
            FooterStatus.Text = "First-run setup stays inside this folder";
            return;
        }

        MainActionButton.Content = GetActionLabel();
        if (!state.GameRunning)
        {
            HeroEyebrow.Text = "WAITING FOR GAME";
            HeroEyebrow.Foreground = (Brush)FindResource("WarnBrush");
            HeroTitle.Text = game is null ? "Open a game" : $"Open {gameName}";
            HeroSubtitle.Text = game is null ? "Open the game you want to stream, then press Scan games." : $"Open {gameName}, then press Scan games if its window changed.";
            FooterStatus.Text = "Stream engine ready · waiting for game";
            return;
        }

        HeroEyebrow.Text = "READY";
        HeroEyebrow.Foreground = (Brush)FindResource("GoodBrush");
        HeroTitle.Text = _selectedMode == StreamMode.DiscordOnly ? "Ready for Discord" : "Ready for all platforms";
        HeroSubtitle.Text = _selectedMode == StreamMode.DiscordOnly
            ? $"{gameName} → OBS Virtual Camera with {SelectedAvatarChoice?.DisplayName ?? "Full VTuber"} and {SelectedThemeChoice?.DisplayName ?? "Profile A"}."
            : $"{gameName} → Discord camera + Twitch 16:9 + TikTok 9:16 from one OBS instance.";
        FooterStatus.Text = $"Ready · {SelectedAvatarChoice?.DisplayName} · {SelectedThemeChoice?.DisplayName}";
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
            var game = SelectedGame ?? throw new InvalidOperationException("Open a game, press Scan games, then choose it first.");
            var state = await _detection.DetectAsync(game);
            var needAitum = _selectedMode == StreamMode.AllPlatforms;
            var showProgress = !state.ObsReady || (_selectedAvatar == AvatarMode.PngAvatar && !state.AvatarReady) || (needAitum && !state.AitumReady);
            if (showProgress) SetupProgressPanel.Visibility = Visibility.Visible;

            var progress = new Progress<(int Percent, string Message)>(value =>
            {
                SetupProgress.Value = value.Percent;
                SetupStatusText.Text = value.Message;
            });
            await _setup.EnsureReadyAsync(showProgress ? progress : null, needAitum: needAitum);

            VTubeCaptureTarget? vTubeTarget = null;
            if (_selectedAvatar == AvatarMode.VTubeStudio)
            {
                SetupProgressPanel.Visibility = Visibility.Visible;
                SetupProgress.Value = 100;
                SetupStatusText.Text = "Opening VTube Studio through Steam…";
                vTubeTarget = await _vTubeStudio.LaunchAndWaitAsync();
            }
            SetupProgressPanel.Visibility = Visibility.Collapsed;

            state = await _detection.DetectAsync(game);
            if (!state.GameRunning)
                throw new InvalidOperationException($"{game.DisplayName} is not running. Open the game, then click Scan games.");

            _catalog.Save(game);
            _catalog.SaveLastSelectedProcess(game.ProcessName);

            if (_selectedMode == StreamMode.DiscordOnly)
            {
                _obs.Launch(StreamMode.DiscordOnly, game, _selectedTheme, _selectedAvatar, vTubeTarget);
                if (await _automation.WaitUntilReadyAsync(TimeSpan.FromSeconds(25)))
                {
                    try { await _automation.StartVirtualCameraAsync(); } catch { }
                }
                FooterStatus.Text = "Discord ready · choose OBS Virtual Camera in Discord · use your normal Discord mic";
                return;
            }

            _setup.EnsureAitumProfileConfig();
            var verticalUuid = _setup.GetVerticalCanvasUuid();
            if (string.IsNullOrWhiteSpace(verticalUuid))
            {
                SetupProgressPanel.Visibility = Visibility.Visible;
                SetupProgress.Value = 100;
                SetupStatusText.Text = "Creating the TikTok vertical canvas once…";
                _obs.LaunchAitumBootstrap(game, _selectedTheme, _selectedAvatar, vTubeTarget);
                if (!await _automation.WaitUntilReadyAsync(TimeSpan.FromSeconds(35)))
                    throw new InvalidOperationException("OBS opened, but its local automation connection did not become ready.");
                verticalUuid = await _automation.GetVerticalCanvasUuidAsync();
                if (string.IsNullOrWhiteSpace(verticalUuid))
                    throw new InvalidOperationException("Aitum loaded, but the Vertical canvas was not created. Open Advanced → Repair and retry.");
                _obs.Stop();
                await Task.Delay(1800);
            }

            _obs.PrepareAllPlatforms(verticalUuid, game, _selectedTheme, _selectedAvatar, vTubeTarget);
            _obs.Launch(StreamMode.AllPlatforms, game, _selectedTheme, _selectedAvatar, vTubeTarget);
            var automationReady = await _automation.WaitUntilReadyAsync(TimeSpan.FromSeconds(30));
            if (automationReady)
            {
                try { await _automation.SwitchVerticalSceneAsync(); } catch { }
                try { await _automation.StartVirtualCameraAsync(); } catch { }
            }

            if (!_setup.HasAitumStreamOutput())
            {
                FooterStatus.Text = "One-time account setup needed · OBS is ready and Discord Virtual Camera is on";
                MessageBox.Show(this,
                    "Your layouts are ready. One-time account setup is still needed:\n\n" +
                    "1. OBS Settings → Stream → connect Twitch.\n" +
                    "2. Aitum Stream Suite → Settings → Outputs → Add Stream → TikTok.\n" +
                    "3. Choose the Vertical canvas and enter the TikTok server/key your account provides.\n\n" +
                    "After that, close OBS. Next time 'Start All Platforms' starts Twitch + TikTok automatically, while Discord uses OBS Virtual Camera.",
                    "One-time Twitch / TikTok setup", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!automationReady)
                throw new InvalidOperationException("OBS opened, but StreamKit could not reach the local OBS automation server.");

            await _automation.StartAllStreamsAsync();
            FooterStatus.Text = "Live controls sent · Discord camera + Twitch horizontal + TikTok vertical";
        }
        catch (Exception ex)
        {
            SetupProgressPanel.Visibility = Visibility.Collapsed;
            MessageBox.Show(this,
                $"StreamKit couldn't finish this step.\n\n{ex.Message}\n\nOpen Advanced → Repair if the problem continues.",
                "StreamKit", MessageBoxButton.OK, MessageBoxImage.Warning);
            FooterStatus.Text = "Needs attention · existing local account settings were not erased";
        }
        finally
        {
            SetupProgressPanel.Visibility = Visibility.Collapsed;
            SetBusy(false);
            await RefreshStatusAsync();
        }
    }

    private void ApplyModeSelection()
    {
        SetSegment(DiscordOnlySegment, _selectedMode == StreamMode.DiscordOnly);
        SetSegment(AllPlatformsSegment, _selectedMode == StreamMode.AllPlatforms);
        MainActionButton.Content = GetActionLabel();
        ActionHint.Text = _selectedMode == StreamMode.DiscordOnly
            ? "OBS Virtual Camera starts automatically · in Discord choose OBS Virtual Camera as Camera"
            : "Discord camera + Twitch horizontal + TikTok vertical · Twitch/TikTok login is one-time only";
        PrivacyText.Text = _selectedMode == StreamMode.DiscordOnly
            ? "Discord receives video through OBS Virtual Camera. Keep your normal microphone selected in Discord; Virtual Camera does not carry game audio."
            : "Discord gets OBS Virtual Camera. Twitch uses the 16:9 main canvas. TikTok uses Aitum's separate 9:16 canvas. Stream keys remain only in local portable OBS/Aitum files.";
    }

    private void SetSegment(Button button, bool selected)
    {
        button.Background = selected ? (Brush)FindResource("AccentGradient") : Brushes.Transparent;
        button.Foreground = selected ? Brushes.White : (Brush)FindResource("MutedBrush");
    }

    private string GetActionLabel() => _selectedMode == StreamMode.DiscordOnly ? "Open Discord VTuber" : "Start All Platforms";

    private void UpdateGameCard()
    {
        GameLayoutText.Text = SelectedGame?.LayoutLabel ?? "Open a game, then Scan games";
    }

    private void UpdateThemeCard() => ThemeDetailText.Text = SelectedThemeChoice?.Detail ?? "Sakura · decorated pink frame";
    private void UpdateAvatarCard() => AvatarDetailText.Text = SelectedAvatarChoice?.Detail ?? "Face-tracked Live2D via VTube Studio";

    private StreamTheme LoadSavedTheme()
    {
        try { return File.Exists(ThemePreferenceFile) && File.ReadAllText(ThemePreferenceFile).Trim().Equals("B", StringComparison.OrdinalIgnoreCase) ? StreamTheme.ProfileB : StreamTheme.ProfileA; }
        catch { return StreamTheme.ProfileA; }
    }

    private AvatarMode LoadSavedAvatar()
    {
        try
        {
            if (!File.Exists(AvatarPreferenceFile)) return AvatarMode.VTubeStudio;
            return File.ReadAllText(AvatarPreferenceFile).Trim().ToLowerInvariant() switch
            {
                "png" => AvatarMode.PngAvatar,
                "none" => AvatarMode.None,
                _ => AvatarMode.VTubeStudio
            };
        }
        catch { return AvatarMode.VTubeStudio; }
    }

    private StreamMode LoadSavedMode()
    {
        try { return File.Exists(ModePreferenceFile) && File.ReadAllText(ModePreferenceFile).Trim().Equals("all", StringComparison.OrdinalIgnoreCase) ? StreamMode.AllPlatforms : StreamMode.DiscordOnly; }
        catch { return StreamMode.DiscordOnly; }
    }

    private void SavePreferences()
    {
        try
        {
            File.WriteAllText(ThemePreferenceFile, _selectedTheme == StreamTheme.ProfileB ? "B" : "A");
            File.WriteAllText(AvatarPreferenceFile, _selectedAvatar switch { AvatarMode.PngAvatar => "png", AvatarMode.None => "none", _ => "vtube" });
            File.WriteAllText(ModePreferenceFile, _selectedMode == StreamMode.AllPlatforms ? "all" : "discord");
        }
        catch { }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        MainActionButton.IsEnabled = !busy;
        DiscordOnlySegment.IsEnabled = !busy;
        AllPlatformsSegment.IsEnabled = !busy;
        AvatarCombo.IsEnabled = !busy;
        ThemeCombo.IsEnabled = !busy;
        GameCombo.IsEnabled = !busy;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
    }

    private async void MainAction_Click(object sender, RoutedEventArgs e) => await StartAsync();

    private async void DiscordOnlySegment_Click(object sender, RoutedEventArgs e)
    {
        _selectedMode = StreamMode.DiscordOnly;
        SavePreferences();
        ApplyModeSelection();
        await RefreshStatusAsync();
    }

    private async void AllPlatformsSegment_Click(object sender, RoutedEventArgs e)
    {
        _selectedMode = StreamMode.AllPlatforms;
        SavePreferences();
        ApplyModeSelection();
        await RefreshStatusAsync();
    }

    private async void AvatarCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingAvatar || SelectedAvatarChoice is not { } choice) return;
        _selectedAvatar = choice.Mode;
        SavePreferences();
        UpdateAvatarCard();
        ApplyModeSelection();
        await RefreshStatusAsync();
    }

    private async void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingTheme || SelectedThemeChoice is not { } choice) return;
        _selectedTheme = choice.Theme;
        SavePreferences();
        UpdateThemeCard();
        await RefreshStatusAsync();
    }

    private async void GameCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingGames) return;
        UpdateGameCard();
        if (SelectedGame is { } game)
        {
            _catalog.SaveLastSelectedProcess(game.ProcessName);
            _catalog.Save(game);
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
            await _setup.EnsureReadyAsync(progress, repair: true, needAitum: _selectedMode == StreamMode.AllPlatforms);
            FooterStatus.Text = "Repair complete · local OBS/Aitum account settings preserved";
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

    private void OpenVTubeStudio_Click(object sender, RoutedEventArgs e) => _vTubeStudio.Launch();

    private async void OpenObs_Click(object sender, RoutedEventArgs e)
    {
        var state = await _detection.DetectAsync(SelectedGame);
        if (!state.ObsReady)
        {
            await StartAsync();
            return;
        }
        _obs.Launch(StreamMode.PlainObs, null, _selectedTheme, _selectedAvatar);
    }

    private async void StopStream_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            SetBusy(true);
            await _automation.StopAllStreamsAsync();
            await _automation.StopVirtualCameraAsync();
        }
        catch { }
        finally
        {
            _obs.Stop();
            FooterStatus.Text = "Stream stopped · VTube Studio left open";
            SetBusy(false);
            await RefreshStatusAsync();
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.Root) { UseShellExecute = true });
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
