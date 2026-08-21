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
        new AvatarChoice(AvatarMode.VTubeStudio, "Full VTuber", "Face-tracked Live2D via VTube Studio + Spout2"),
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
    private bool _streamActive;
    private bool _micMuted;

    private GameTarget? SelectedGame => GameCombo.SelectedItem as GameTarget;
    private ThemeChoice? SelectedThemeChoice => ThemeCombo.SelectedItem as ThemeChoice;
    private AvatarChoice? SelectedAvatarChoice => AvatarCombo.SelectedItem as AvatarChoice;
    private static string ThemePreferenceFile => Path.Combine(AppPaths.Root, ".streamkit-theme");
    private static string AvatarPreferenceFile => Path.Combine(AppPaths.Root, ".streamkit-avatar");
    private static string ModePreferenceFile => Path.Combine(AppPaths.Root, ".streamkit-mode");
    private static string SpoutOnboardingFile => Path.Combine(AppPaths.Root, "user-data", "vtube-spout-onboarding-v2.txt");

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
        UpdateStreamControls();
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
        var spoutReady = _selectedAvatar != AvatarMode.VTubeStudio || _setup.IsSpoutReady();
        SetStatus(GameStatusDot, GameStatusText, state.GameRunning,
            $"{gameName} detected", game is null ? "Choose or open a game" : $"Waiting for {gameName}");

        var engineReady = state.ObsReady && (_selectedMode != StreamMode.AllPlatforms || state.AitumReady) && spoutReady;
        SetStatus(ObsStatusDot, ObsStatusText, engineReady,
            _selectedMode == StreamMode.AllPlatforms ? "OBS + streaming plugins ready" : "Portable OBS ready",
            _selectedMode == StreamMode.AllPlatforms ? "OBS/plugins will be prepared" : "Portable OBS needs setup");

        switch (_selectedAvatar)
        {
            case AvatarMode.VTubeStudio:
                SetStatus(AvatarStatusDot, AvatarStatusText, state.VTubeStudioRunning && spoutReady,
                    "VTube Studio + Spout2 ready",
                    spoutReady ? "VTube Studio opens automatically" : "Spout2 installs automatically");
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
            "Private game + mic routing ready", "Private audio setup needed");

        if (_streamActive)
        {
            HeroEyebrow.Text = "STREAM READY";
            HeroEyebrow.Foreground = (Brush)FindResource("GoodBrush");
            HeroTitle.Text = _selectedMode == StreamMode.DiscordOnly ? "Discord share is prepared" : "Stream controls are live";
            HeroSubtitle.Text = _selectedMode == StreamMode.DiscordOnly
                ? "Share the OBS Windowed Projector (Program) in Discord. Its audio path contains game audio only; keep your normal Discord mic enabled."
                : "Twitch/TikTok receive selected-game audio + your RNNoise microphone. Discord/system audio is deliberately excluded.";
            MainActionButton.Content = "Reopen Discord Share";
            return;
        }

        var needsSetup = !state.ObsReady
                         || (_selectedAvatar == AvatarMode.VTubeStudio && !spoutReady)
                         || (_selectedAvatar == AvatarMode.PngAvatar && !state.AvatarReady)
                         || (_selectedMode == StreamMode.AllPlatforms && !state.AitumReady);
        if (needsSetup)
        {
            HeroEyebrow.Text = "SETUP REQUIRED";
            HeroEyebrow.Foreground = (Brush)FindResource("WarnBrush");
            HeroTitle.Text = "One click from ready";
            HeroSubtitle.Text = _selectedMode == StreamMode.AllPlatforms
                ? "StreamKit will prepare portable OBS, Spout2 VTuber capture, Aitum vertical/multistream support and private audio routing."
                : "StreamKit will prepare portable OBS, transparent Spout2 VTuber capture, a Discord share projector and private game-only share audio.";
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
            ? $"{gameName} → clean OBS projector with {SelectedAvatarChoice?.DisplayName ?? "Full VTuber"}. Game audio is included; OBS mic is excluded to avoid double voice."
            : $"{gameName} → Discord projector + Twitch 16:9 + TikTok 9:16. Only game audio + your filtered mic reach public streams.";
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

        if (_streamActive)
        {
            try
            {
                SetBusy(true);
                await _automation.OpenProgramProjectorAsync();
                FooterStatus.Text = "Discord share window reopened · choose Windowed Projector (Program) in Discord";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Open Discord share", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { SetBusy(false); }
            return;
        }

        try
        {
            SetBusy(true);
            await RefreshGameChoicesAsync(preserveSelection: true);
            var game = SelectedGame ?? throw new InvalidOperationException("Open a game, press Scan games, then choose it first.");
            var state = await _detection.DetectAsync(game);
            var needAitum = _selectedMode == StreamMode.AllPlatforms;
            var needSpout = _selectedAvatar == AvatarMode.VTubeStudio;
            var showProgress = !state.ObsReady
                               || (needSpout && !_setup.IsSpoutReady())
                               || (_selectedAvatar == AvatarMode.PngAvatar && !state.AvatarReady)
                               || (needAitum && !state.AitumReady);
            if (showProgress) SetupProgressPanel.Visibility = Visibility.Visible;

            var progress = new Progress<(int Percent, string Message)>(value =>
            {
                SetupProgress.Value = value.Percent;
                SetupStatusText.Text = value.Message;
            });
            await _setup.EnsureReadyAsync(showProgress ? progress : null, needAitum: needAitum, needSpout: needSpout);

            VTubeCaptureTarget? vTubeTarget = null;
            if (_selectedAvatar == AvatarMode.VTubeStudio)
            {
                SetupProgressPanel.Visibility = Visibility.Visible;
                SetupProgress.Value = 100;
                SetupStatusText.Text = "Opening VTube Studio through Steam…";
                vTubeTarget = await _vTubeStudio.LaunchAndWaitAsync();
                SetupProgressPanel.Visibility = Visibility.Collapsed;
                ShowSpoutOnboardingIfNeeded();
            }
            SetupProgressPanel.Visibility = Visibility.Collapsed;

            state = await _detection.DetectAsync(game);
            if (!state.GameRunning)
                throw new InvalidOperationException($"{game.DisplayName} is not running. Open the game, then click Scan games.");

            _catalog.Save(game);
            _catalog.SaveLastSelectedProcess(game.ProcessName);
            AudioPrivacyService.HardenPortableObsConfig();

            if (_selectedMode == StreamMode.DiscordOnly)
            {
                _obs.Launch(StreamMode.DiscordOnly, game, _selectedTheme, _selectedAvatar, vTubeTarget);
                if (!await _automation.WaitUntilReadyAsync(TimeSpan.FromSeconds(25)))
                    throw new InvalidOperationException("OBS opened, but StreamKit could not reach its local automation server.");

                await _automation.ConfigureDiscordShareAudioAsync(alsoStreamingPlatforms: false);
                await VerifyVTubeStudioOutputAsync();
                await _automation.SetCurrentSceneAsync("Game Clean");
                await _automation.OpenProgramProjectorAsync();

                _micMuted = true;
                _streamActive = true;
                UpdateStreamControls();
                FooterStatus.Text = "Discord ready · share Windowed Projector (Program) · game audio only · OBS mic excluded";
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
            AudioPrivacyService.HardenPortableObsConfig();
            _obs.Launch(StreamMode.AllPlatforms, game, _selectedTheme, _selectedAvatar, vTubeTarget);
            var automationReady = await _automation.WaitUntilReadyAsync(TimeSpan.FromSeconds(30));
            if (!automationReady)
                throw new InvalidOperationException("OBS opened, but StreamKit could not reach the local OBS automation server.");

            await _automation.ConfigureDiscordShareAudioAsync(alsoStreamingPlatforms: true);
            try { await _automation.StartVirtualCameraAsync(); } catch { }
            await VerifyVTubeStudioOutputAsync();

            if (!_setup.HasAitumStreamOutput())
            {
                FooterStatus.Text = "One-time account setup needed · private audio routing is already prepared";
                MessageBox.Show(this,
                    "Your layouts and private audio routing are ready. One-time account setup is still needed:\n\n" +
                    "1. OBS Settings → Stream → connect Twitch.\n" +
                    "2. Aitum Stream Suite → Settings → Outputs → Add Stream → TikTok.\n" +
                    "3. Choose the Vertical canvas and enter the TikTok server/key your account provides.\n\n" +
                    "Desktop audio is disabled, so Discord friends are not sent to Twitch/TikTok. Your OBS mic uses RNNoise by default.\n\n" +
                    "After setup, close OBS. Next time Start All Platforms starts everything together.",
                    "One-time Twitch / TikTok setup", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await _automation.SwitchScenesAsync("Starting Soon", "Vertical Starting Soon");
            await _automation.StartAllStreamsAsync();
            await _automation.OpenProgramProjectorAsync();

            _micMuted = false;
            _streamActive = true;
            UpdateStreamControls();
            FooterStatus.Text = "Streaming · Starting Soon · Discord projector + Twitch + TikTok · Discord voice excluded";
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

    private void ShowSpoutOnboardingIfNeeded()
    {
        try
        {
            if (File.Exists(SpoutOnboardingFile)) return;

            MessageBox.Show(this,
                "StreamKit already installed Spout2 into its portable OBS. Do these VTube Studio steps once:\n\n" +
                "1. In VTube Studio settings, turn ON Spout2 output.\n" +
                "2. Select the Color Picker Background.\n" +
                "3. Enable Transparent in capture.\n" +
                "4. Leave the sender name as VTubeStudioSpout.\n\n" +
                "Press OK when done. StreamKit will now TEST the actual Spout output before it lets the stream continue.",
                "One-time Full VTuber setup", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch { }
    }

    private async Task VerifyVTubeStudioOutputAsync()
    {
        if (_selectedAvatar != AvatarMode.VTubeStudio) return;

        SetupProgressPanel.Visibility = Visibility.Visible;
        SetupProgress.Value = 100;
        SetupStatusText.Text = "Checking VTube Studio transparent Spout output…";
        var ready = await _automation.WaitForVTubeStudioVideoAsync(TimeSpan.FromSeconds(20));
        SetupProgressPanel.Visibility = Visibility.Collapsed;

        if (!ready)
        {
            throw new InvalidOperationException(
                "VTube Studio is open, but StreamKit cannot see a transparent VTubeStudioSpout frame yet. " +
                "In VTube Studio turn on Spout2 output, use the Color Picker Background, enable Transparent in capture, " +
                "and keep the sender name VTubeStudioSpout. Then retry.");
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SpoutOnboardingFile)!);
            File.WriteAllText(SpoutOnboardingFile, "VTubeStudioSpout video and transparency verified by StreamKit.");
        }
        catch { }
    }

    private async Task SwitchSceneAsync(string horizontal, string vertical, string label)
    {
        if (!_streamActive || _busy) return;
        try
        {
            SetBusy(true);
            await _automation.SwitchScenesAsync(horizontal, _selectedMode == StreamMode.AllPlatforms ? vertical : null);
            FooterStatus.Text = _selectedMode == StreamMode.AllPlatforms
                ? $"Streaming · {label} · horizontal + TikTok vertical switched together"
                : $"Discord share · {label}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Change stream scene", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { SetBusy(false); }
    }

    private void UpdateStreamControls()
    {
        StreamControlsPanel.Visibility = _streamActive ? Visibility.Visible : Visibility.Collapsed;
        StartingSoonButton.IsEnabled = _streamActive && !_busy;
        LiveButton.IsEnabled = _streamActive && !_busy;
        BrbButton.IsEnabled = _streamActive && !_busy;
        MicMuteButton.IsEnabled = _streamActive && !_busy && _selectedMode == StreamMode.AllPlatforms;
        MicMuteButton.Content = _selectedMode == StreamMode.DiscordOnly
            ? "Mic stays in Discord"
            : (_micMuted ? "Unmute Mic" : "Mute Mic");
        MainActionButton.Content = _streamActive ? "Reopen Discord Share" : GetActionLabel();
    }

    private void ApplyModeSelection()
    {
        SetSegment(DiscordOnlySegment, _selectedMode == StreamMode.DiscordOnly);
        SetSegment(AllPlatformsSegment, _selectedMode == StreamMode.AllPlatforms);
        MainActionButton.Content = _streamActive ? "Reopen Discord Share" : GetActionLabel();
        ActionHint.Text = _selectedMode == StreamMode.DiscordOnly
            ? "StreamKit opens a Windowed Projector automatically · share that window in Discord with sound"
            : "Discord projector + Twitch horizontal + TikTok vertical · scene buttons switch all layouts together";
        PrivacyText.Text = _selectedMode == StreamMode.DiscordOnly
            ? "Discord projector audio contains the selected game only. OBS mic is muted so your voice is not doubled; keep your normal Discord microphone enabled."
            : "Twitch/TikTok receive selected-game audio + your RNNoise microphone. Desktop/system audio is disabled, so Discord friends and notification sounds are excluded. Discord projector receives game audio, not the OBS mic.";
    }

    private void SetSegment(Button button, bool selected)
    {
        button.Background = selected ? (Brush)FindResource("AccentGradient") : Brushes.Transparent;
        button.Foreground = selected ? Brushes.White : (Brush)FindResource("MutedBrush");
    }

    private string GetActionLabel() => _selectedMode == StreamMode.DiscordOnly ? "Open Discord Share" : "Start All Platforms";

    private void UpdateGameCard()
    {
        GameLayoutText.Text = SelectedGame?.LayoutLabel ?? "Open a game, then Scan games";
    }

    private void UpdateThemeCard() => ThemeDetailText.Text = SelectedThemeChoice?.Detail ?? "Sakura · decorated pink frame";
    private void UpdateAvatarCard() => AvatarDetailText.Text = SelectedAvatarChoice?.Detail ?? "Face-tracked Live2D via VTube Studio + Spout2";

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
        DiscordOnlySegment.IsEnabled = !busy && !_streamActive;
        AllPlatformsSegment.IsEnabled = !busy && !_streamActive;
        AvatarCombo.IsEnabled = !busy && !_streamActive;
        ThemeCombo.IsEnabled = !busy && !_streamActive;
        GameCombo.IsEnabled = !busy && !_streamActive;
        StartingSoonButton.IsEnabled = !busy && _streamActive;
        LiveButton.IsEnabled = !busy && _streamActive;
        BrbButton.IsEnabled = !busy && _streamActive;
        MicMuteButton.IsEnabled = !busy && _streamActive && _selectedMode == StreamMode.AllPlatforms;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
    }

    private async void MainAction_Click(object sender, RoutedEventArgs e) => await StartAsync();
    private async void StartingSoon_Click(object sender, RoutedEventArgs e) => await SwitchSceneAsync("Starting Soon", "Vertical Starting Soon", "Starting Soon");
    private async void Live_Click(object sender, RoutedEventArgs e) => await SwitchSceneAsync("Game Clean", "Vertical Live", "Live");
    private async void Brb_Click(object sender, RoutedEventArgs e) => await SwitchSceneAsync("BRB", "Vertical BRB", "BRB");

    private async void MicMute_Click(object sender, RoutedEventArgs e)
    {
        if (!_streamActive || _selectedMode != StreamMode.AllPlatforms || _busy) return;
        try
        {
            SetBusy(true);
            _micMuted = await _automation.ToggleMicMutedAsync();
            FooterStatus.Text = _micMuted ? "OBS mic muted for Twitch/TikTok" : "OBS mic live · RNNoise enabled";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Microphone", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SetBusy(false);
            UpdateStreamControls();
        }
    }

    private async void DiscordOnlySegment_Click(object sender, RoutedEventArgs e)
    {
        if (_streamActive) return;
        _selectedMode = StreamMode.DiscordOnly;
        SavePreferences();
        ApplyModeSelection();
        await RefreshStatusAsync();
    }

    private async void AllPlatformsSegment_Click(object sender, RoutedEventArgs e)
    {
        if (_streamActive) return;
        _selectedMode = StreamMode.AllPlatforms;
        SavePreferences();
        ApplyModeSelection();
        await RefreshStatusAsync();
    }

    private async void AvatarCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingAvatar || _streamActive || SelectedAvatarChoice is not { } choice) return;
        _selectedAvatar = choice.Mode;
        SavePreferences();
        UpdateAvatarCard();
        ApplyModeSelection();
        await RefreshStatusAsync();
    }

    private async void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingTheme || _streamActive || SelectedThemeChoice is not { } choice) return;
        _selectedTheme = choice.Theme;
        SavePreferences();
        UpdateThemeCard();
        await RefreshStatusAsync();
    }

    private async void GameCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingGames || _streamActive) return;
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
        if (_busy || _streamActive) return;
        await RefreshGameChoicesAsync(preserveSelection: true);
        await RefreshStatusAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!_streamActive) await RefreshGameChoicesAsync(preserveSelection: true);
        await RefreshStatusAsync();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        AdvancedPanel.Visibility = AdvancedPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void Repair_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _streamActive) return;
        try
        {
            SetBusy(true);
            SetupProgressPanel.Visibility = Visibility.Visible;
            var progress = new Progress<(int Percent, string Message)>(value =>
            {
                SetupProgress.Value = value.Percent;
                SetupStatusText.Text = value.Message;
            });
            await _setup.EnsureReadyAsync(progress, repair: true,
                needAitum: _selectedMode == StreamMode.AllPlatforms,
                needSpout: _selectedAvatar == AvatarMode.VTubeStudio);
            AudioPrivacyService.HardenPortableObsConfig();
            FooterStatus.Text = "Repair complete · local account settings preserved · private audio routing restored";
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
            await _automation.RestoreNormalAudioMonitoringAsync();
        }
        catch { }
        finally
        {
            _obs.Stop();
            _streamActive = false;
            _micMuted = false;
            UpdateStreamControls();
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