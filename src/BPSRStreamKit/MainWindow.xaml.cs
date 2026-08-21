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
        new ThemeChoice(StreamTheme.ProfileA, "Sakura", "Soft pink / purple frame"),
        new ThemeChoice(StreamTheme.ProfileB, "Chibi Doctor", "Clean cyan medical frame")
    };

    private readonly IReadOnlyList<AvatarChoice> _avatars = new[]
    {
        new AvatarChoice(AvatarMode.VTubeStudio, "Full VTuber", "Face-tracked Live2D avatar · recommended"),
        new AvatarChoice(AvatarMode.PngAvatar, "Simple Talking Avatar", "Lightweight animated avatar · no webcam needed"),
        new AvatarChoice(AvatarMode.None, "No Avatar", "Show only the game + frame")
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
    private bool _platformSetupNeeded;

    private GameTarget? SelectedGame => GameCombo.SelectedItem as GameTarget;
    private ThemeChoice? SelectedThemeChoice => ThemeCombo.SelectedItem as ThemeChoice;
    private AvatarChoice? SelectedAvatarChoice => AvatarCombo.SelectedItem as AvatarChoice;
    private static string ThemePreferenceFile => Path.Combine(AppPaths.Root, ".streamkit-theme");
    private static string AvatarPreferenceFile => Path.Combine(AppPaths.Root, ".streamkit-avatar");
    private static string ModePreferenceFile => Path.Combine(AppPaths.Root, ".streamkit-mode");
    private static string AvatarVerifiedFile => Path.Combine(AppPaths.Root, "user-data", "vtube-avatar-verified-v3.txt");

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
        var gameName = game?.DisplayName ?? "your game";
        var avatarToolsReady = _selectedAvatar != AvatarMode.VTubeStudio || _setup.IsSpoutReady();

        SetStatus(GameStatusDot, GameStatusText, state.GameRunning,
            $"{gameName} is ready", game is null ? "Choose a game" : $"Open {gameName}");

        var engineReady = state.ObsReady && (_selectedMode != StreamMode.AllPlatforms || state.AitumReady) && avatarToolsReady;
        SetStatus(ObsStatusDot, ObsStatusText, engineReady,
            "Streaming tools are ready", "Streaming tools will set themselves up");

        switch (_selectedAvatar)
        {
            case AvatarMode.VTubeStudio:
                SetStatus(AvatarStatusDot, AvatarStatusText, state.VTubeStudioRunning && avatarToolsReady,
                    File.Exists(AvatarVerifiedFile) ? "Avatar connected" : "Avatar app is ready",
                    avatarToolsReady ? "Avatar app opens automatically" : "Avatar tools will install automatically");
                break;
            case AvatarMode.PngAvatar:
                SetStatus(AvatarStatusDot, AvatarStatusText, state.AvatarReady,
                    "Talking avatar is ready", "Talking avatar will set itself up");
                break;
            default:
                SetStatus(AvatarStatusDot, AvatarStatusText, true, "No avatar selected", "No avatar selected");
                break;
        }

        SetStatus(AudioStatusDot, AudioStatusText, state.AudioIsolationReady,
            "Sound protection is ready", "Sound protection will be applied automatically");

        if (_platformSetupNeeded)
        {
            HeroEyebrow.Text = "ONE-TIME SETUP";
            HeroEyebrow.Foreground = (Brush)FindResource("WarnBrush");
            HeroTitle.Text = "Connect Twitch and TikTok once";
            HeroSubtitle.Text = "Follow the simple account steps shown above. After this, StreamKit can start everything for you without touching the streaming engine.";
            MainActionButton.Content = "Continue account setup";
            FooterStatus.Text = "Waiting for Twitch / TikTok account setup";
            return;
        }

        if (_streamActive)
        {
            HeroEyebrow.Text = "LIVE CONTROLS READY";
            HeroEyebrow.Foreground = (Brush)FindResource("GoodBrush");
            HeroTitle.Text = _selectedMode == StreamMode.DiscordOnly ? "Discord share is ready" : "You’re live";
            HeroSubtitle.Text = _selectedMode == StreamMode.DiscordOnly
                ? "In Discord, share the clean StreamKit/OBS projector window with sound. Your normal Discord microphone stays separate, so your voice is not doubled."
                : "Use the simple controls below. Your game + cleaned microphone go to Twitch/TikTok, while Discord friends and desktop sounds stay out.";
            MainActionButton.Content = "Reopen Discord share window";
            return;
        }

        var needsSetup = !state.ObsReady
                         || (_selectedAvatar == AvatarMode.VTubeStudio && !avatarToolsReady)
                         || (_selectedAvatar == AvatarMode.PngAvatar && !state.AvatarReady)
                         || (_selectedMode == StreamMode.AllPlatforms && !state.AitumReady);

        if (needsSetup)
        {
            HeroEyebrow.Text = "FIRST RUN";
            HeroEyebrow.Foreground = (Brush)FindResource("WarnBrush");
            HeroTitle.Text = "StreamKit will set itself up";
            HeroSubtitle.Text = "Press the big button. Required streaming components install inside this folder and the safe audio defaults are applied automatically.";
            MainActionButton.Content = "Set up & " + GetActionLabel();
            FooterStatus.Text = "Automatic first-run setup · nothing installs system-wide except the external avatar app you already use";
            return;
        }

        MainActionButton.Content = GetActionLabel();
        if (!state.GameRunning)
        {
            HeroEyebrow.Text = "STEP 1";
            HeroEyebrow.Foreground = (Brush)FindResource("WarnBrush");
            HeroTitle.Text = game is null ? "Choose a game" : $"Open {gameName}";
            HeroSubtitle.Text = "Open the game first, then press Find games if it is not listed.";
            FooterStatus.Text = "Waiting for your game";
            return;
        }

        HeroEyebrow.Text = "READY";
        HeroEyebrow.Foreground = (Brush)FindResource("GoodBrush");
        HeroTitle.Text = _selectedMode == StreamMode.DiscordOnly ? "Ready to share on Discord" : "Ready to go live";
        HeroSubtitle.Text = _selectedMode == StreamMode.DiscordOnly
            ? $"{gameName} + {SelectedAvatarChoice?.DisplayName ?? "your avatar"} are ready. StreamKit will open one clean window for Discord to share with sound."
            : $"{gameName} + {SelectedAvatarChoice?.DisplayName ?? "your avatar"} are ready for Discord, Twitch and TikTok.";
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

        if (_platformSetupNeeded)
        {
            PlatformSetupPanel.Visibility = Visibility.Visible;
            RestorePortableObsWindow();
            return;
        }

        if (_streamActive)
        {
            try
            {
                SetBusy(true);
                await RunEngineStepWithRetryAsync(() => _automation.OpenProgramProjectorAsync(), "Reopening your Discord share window…");
                MinimizePortableObsWindow();
                FooterStatus.Text = "Discord share window reopened";
            }
            catch (Exception ex) { ShowProblem("Couldn’t reopen the share window", FriendlyError(ex)); }
            finally { SetBusy(false); }
            return;
        }

        try
        {
            HideProblem();
            SetBusy(true);
            await RefreshGameChoicesAsync(preserveSelection: true);
            var game = SelectedGame ?? throw new InvalidOperationException("Choose a running game first.");
            var state = await _detection.DetectAsync(game);
            var needAitum = _selectedMode == StreamMode.AllPlatforms;
            var needAvatarBridge = _selectedAvatar == AvatarMode.VTubeStudio;
            var showProgress = !state.ObsReady
                               || (needAvatarBridge && !_setup.IsSpoutReady())
                               || (_selectedAvatar == AvatarMode.PngAvatar && !state.AvatarReady)
                               || (needAitum && !state.AitumReady);

            if (showProgress) SetupProgressPanel.Visibility = Visibility.Visible;
            var progress = new Progress<(int Percent, string Message)>(value =>
            {
                SetupProgress.Value = value.Percent;
                SetupStatusText.Text = MakeProgressFriendly(value.Message);
            });
            await _setup.EnsureReadyAsync(showProgress ? progress : null, needAitum: needAitum, needSpout: needAvatarBridge);

            VTubeCaptureTarget? vTubeTarget = null;
            if (_selectedAvatar == AvatarMode.VTubeStudio)
            {
                SetupProgressPanel.Visibility = Visibility.Visible;
                SetupProgress.Value = 100;
                SetupStatusText.Text = "Opening your avatar app…";
                vTubeTarget = await _vTubeStudio.LaunchAndWaitAsync();
                ShowAvatarSetupGuide(force: !File.Exists(AvatarVerifiedFile));
            }

            state = await _detection.DetectAsync(game);
            if (!state.GameRunning)
                throw new InvalidOperationException($"{game.DisplayName} is not running. Open it, then press Find games.");

            _catalog.Save(game);
            _catalog.SaveLastSelectedProcess(game.ProcessName);
            AudioPrivacyService.HardenPortableObsConfig();

            if (_selectedMode == StreamMode.DiscordOnly)
            {
                _obs.Launch(StreamMode.DiscordOnly, game, _selectedTheme, _selectedAvatar, vTubeTarget);
                await WaitForStreamingEngineAsync(TimeSpan.FromSeconds(35));
                await Task.Delay(1200);

                await RunEngineStepWithRetryAsync(
                    () => _automation.ConfigureDiscordShareAudioAsync(alsoStreamingPlatforms: false),
                    "Protecting your Discord audio…");

                if (!await CheckAvatarConnectionAsync(TimeSpan.FromSeconds(45)))
                {
                    _obs.Stop();
                    throw new InvalidOperationException("Your avatar is not connected yet. Follow the three avatar steps shown in StreamKit, then click Check my avatar.");
                }

                await RunEngineStepWithRetryAsync(() => _automation.SetCurrentSceneAsync("Game Clean"), "Preparing your clean game view…");
                await RunEngineStepWithRetryAsync(() => _automation.OpenProgramProjectorAsync(), "Opening the window you’ll share in Discord…");
                MinimizePortableObsWindow();

                _micMuted = true;
                _streamActive = true;
                UpdateStreamControls();
                SetupProgressPanel.Visibility = Visibility.Collapsed;
                FooterStatus.Text = "Discord ready · share the clean projector window with sound · keep your normal Discord mic on";
                return;
            }

            _setup.EnsureAitumProfileConfig();
            var verticalUuid = _setup.GetVerticalCanvasUuid();
            if (string.IsNullOrWhiteSpace(verticalUuid))
            {
                SetupProgressPanel.Visibility = Visibility.Visible;
                SetupProgress.Value = 100;
                SetupStatusText.Text = "Preparing the phone-shaped TikTok layout…";
                _obs.LaunchAitumBootstrap(game, _selectedTheme, _selectedAvatar, vTubeTarget);
                await WaitForStreamingEngineAsync(TimeSpan.FromSeconds(40));
                verticalUuid = await WaitForVerticalCanvasAsync(TimeSpan.FromSeconds(20));
                if (string.IsNullOrWhiteSpace(verticalUuid))
                    throw new InvalidOperationException("TikTok’s vertical layout could not be prepared automatically. Use Fix setup and try again.");
                _obs.Stop();
                await Task.Delay(1800);
            }

            _obs.PrepareAllPlatforms(verticalUuid, game, _selectedTheme, _selectedAvatar, vTubeTarget);
            AudioPrivacyService.HardenPortableObsConfig();
            _obs.Launch(StreamMode.AllPlatforms, game, _selectedTheme, _selectedAvatar, vTubeTarget);
            await WaitForStreamingEngineAsync(TimeSpan.FromSeconds(40));
            await Task.Delay(1200);

            await RunEngineStepWithRetryAsync(
                () => _automation.ConfigureDiscordShareAudioAsync(alsoStreamingPlatforms: true),
                "Protecting your public-stream audio…");
            await TryEngineStepAsync(() => _automation.StartVirtualCameraAsync(), "Preparing the optional Discord camera…");

            if (!await CheckAvatarConnectionAsync(TimeSpan.FromSeconds(45)))
            {
                _obs.Stop();
                throw new InvalidOperationException("Your avatar is not connected yet. Follow the three avatar steps shown in StreamKit, then click Check my avatar.");
            }

            if (!_setup.HasAitumStreamOutput())
            {
                _platformSetupNeeded = true;
                PlatformSetupPanel.Visibility = Visibility.Visible;
                SetupProgressPanel.Visibility = Visibility.Collapsed;
                RestorePortableObsWindow();
                FooterStatus.Text = "One-time Twitch / TikTok account setup needed";
                return;
            }

            await RunEngineStepWithRetryAsync(() => _automation.SwitchScenesAsync("Starting Soon", "Vertical Starting Soon"), "Preparing your Starting Soon screens…");
            await RunEngineStepWithRetryAsync(() => _automation.StartAllStreamsAsync(), "Starting Twitch and TikTok…");
            await RunEngineStepWithRetryAsync(() => _automation.OpenProgramProjectorAsync(), "Opening your Discord share window…");
            MinimizePortableObsWindow();

            _micMuted = false;
            _streamActive = true;
            UpdateStreamControls();
            SetupProgressPanel.Visibility = Visibility.Collapsed;
            FooterStatus.Text = "Live · Starting Soon · Discord + Twitch + TikTok";
        }
        catch (Exception ex)
        {
            SetupProgressPanel.Visibility = Visibility.Collapsed;
            ShowProblem("StreamKit couldn’t finish automatically", FriendlyError(ex));
            FooterStatus.Text = "Needs one quick fix · your account settings were kept";
        }
        finally
        {
            SetupProgressPanel.Visibility = Visibility.Collapsed;
            SetBusy(false);
            await RefreshStatusAsync();
        }
    }

    private async Task WaitForStreamingEngineAsync(TimeSpan timeout)
    {
        SetupProgressPanel.Visibility = Visibility.Visible;
        SetupProgress.Value = 100;
        SetupStatusText.Text = "Starting the streaming engine in the background…";
        if (!await _automation.WaitUntilReadyAsync(timeout))
            throw new InvalidOperationException("The streaming engine did not finish starting in time.");
    }

    private async Task RunEngineStepWithRetryAsync(Func<Task> action, string friendlyStatus, int attempts = 10)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                SetupProgressPanel.Visibility = Visibility.Visible;
                SetupProgress.Value = 100;
                SetupStatusText.Text = friendlyStatus;
                await action();
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                if (!LooksLikeStartupDelay(ex) && attempt >= 2) break;
                if (attempt == attempts) break;
                SetupStatusText.Text = $"{friendlyStatus.TrimEnd('…')} — waiting a moment…";
                await Task.Delay(Math.Min(650 + (attempt * 250), 1900));
            }
        }

        throw new InvalidOperationException("The streaming engine is still starting. StreamKit retried automatically but it did not become ready in time.", last);
    }

    private async Task TryEngineStepAsync(Func<Task> action, string friendlyStatus)
    {
        try { await RunEngineStepWithRetryAsync(action, friendlyStatus, 5); }
        catch { }
    }

    private static bool LooksLikeStartupDelay(Exception ex)
    {
        var text = (ex.Message + " " + ex.InnerException?.Message).ToLowerInvariant();
        return text.Contains("not ready") || text.Contains("starting") || text.Contains("timeout") ||
               text.Contains("timed out") || text.Contains("connection") || text.Contains("websocket") ||
               text.Contains("perform the request") || text.Contains("closed the connection");
    }

    private async Task<string?> WaitForVerticalCanvasAsync(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            try
            {
                var uuid = await _automation.GetVerticalCanvasUuidAsync();
                if (!string.IsNullOrWhiteSpace(uuid)) return uuid;
            }
            catch { }
            await Task.Delay(900);
        }
        return null;
    }

    private void ShowAvatarSetupGuide(bool force)
    {
        if (_selectedAvatar != AvatarMode.VTubeStudio) return;
        if (!force && File.Exists(AvatarVerifiedFile)) return;
        AvatarSetupPanel.Visibility = Visibility.Visible;
        AvatarCheckText.Text = "StreamKit will check the avatar automatically when you start.";
    }

    private async Task<bool> CheckAvatarConnectionAsync(TimeSpan timeout)
    {
        if (_selectedAvatar != AvatarMode.VTubeStudio) return true;

        SetupProgressPanel.Visibility = Visibility.Visible;
        SetupProgress.Value = 100;
        SetupStatusText.Text = "Checking that your avatar is visible and transparent…";
        var ready = await _automation.WaitForVTubeStudioVideoAsync(timeout);

        if (!ready)
        {
            AvatarSetupPanel.Visibility = Visibility.Visible;
            AvatarCheckText.Text = "Avatar not detected yet. Check the 3 steps, then try again.";
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AvatarVerifiedFile)!);
            File.WriteAllText(AvatarVerifiedFile, "VTube Studio transparent avatar output verified by StreamKit.");
        }
        catch { }

        AvatarSetupPanel.Visibility = Visibility.Collapsed;
        AvatarCheckText.Text = "Avatar connected ✓";
        return true;
    }

    private async Task SwitchSceneAsync(string horizontal, string vertical, string label)
    {
        if (!_streamActive || _busy) return;
        try
        {
            SetBusy(true);
            await RunEngineStepWithRetryAsync(
                () => _automation.SwitchScenesAsync(horizontal, _selectedMode == StreamMode.AllPlatforms ? vertical : null),
                $"Switching to {label}…", 5);
            SetupProgressPanel.Visibility = Visibility.Collapsed;
            FooterStatus.Text = _selectedMode == StreamMode.AllPlatforms ? $"Live · {label} · all layouts switched" : $"Discord share · {label}";
        }
        catch (Exception ex) { ShowProblem("Couldn’t change the screen", FriendlyError(ex)); }
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
            ? "Discord mic stays separate"
            : (_micMuted ? "Unmute Mic" : "Mute Mic");
        MainActionButton.Content = _streamActive ? "Reopen Discord share window" : GetActionLabel();
    }

    private void ApplyModeSelection()
    {
        SetSegment(DiscordOnlySegment, _selectedMode == StreamMode.DiscordOnly);
        SetSegment(AllPlatformsSegment, _selectedMode == StreamMode.AllPlatforms);
        MainActionButton.Content = _streamActive ? "Reopen Discord share window" : GetActionLabel();
        ActionHint.Text = _selectedMode == StreamMode.DiscordOnly
            ? "StreamKit opens one clean window. Share that window in Discord with sound."
            : "StreamKit handles the horizontal + phone layouts together. Account connection is one-time only.";
        PrivacyText.Text = _selectedMode == StreamMode.DiscordOnly
            ? "Discord gets the selected game sound only. Your voice stays on your normal Discord microphone, so it is not doubled."
            : "Twitch/TikTok get the selected game + your cleaned microphone. Desktop/system sound is not captured, so Discord friends and notification sounds stay out.";
    }

    private void SetSegment(Button button, bool selected)
    {
        button.Background = selected ? (Brush)FindResource("AccentGradient") : new SolidColorBrush(Color.FromRgb(23, 28, 38));
        button.Foreground = selected ? Brushes.White : (Brush)FindResource("MutedBrush");
        button.BorderBrush = selected ? Brushes.Transparent : (Brush)FindResource("StrokeBrush");
    }

    private string GetActionLabel()
    {
        if (_selectedMode == StreamMode.DiscordOnly) return "Start Discord Share";
        return _setup.HasAitumStreamOutput() ? "Go Live Everywhere" : "Set up Twitch & TikTok";
    }

    private void UpdateGameCard()
    {
        GameLayoutText.Text = SelectedGame?.LayoutLabel ?? "Open a game, then press Find games";
    }

    private void UpdateAvatarCard()
    {
        AvatarDetailText.Text = SelectedAvatarChoice?.Detail ?? "Face-tracked Live2D avatar · recommended";
        if (_selectedAvatar != AvatarMode.VTubeStudio) AvatarSetupPanel.Visibility = Visibility.Collapsed;
    }

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
        DiscordOnlySegment.IsEnabled = !busy && !_streamActive && !_platformSetupNeeded;
        AllPlatformsSegment.IsEnabled = !busy && !_streamActive && !_platformSetupNeeded;
        AvatarCombo.IsEnabled = !busy && !_streamActive && !_platformSetupNeeded;
        ThemeCombo.IsEnabled = !busy && !_streamActive && !_platformSetupNeeded;
        GameCombo.IsEnabled = !busy && !_streamActive && !_platformSetupNeeded;
        StartingSoonButton.IsEnabled = !busy && _streamActive;
        LiveButton.IsEnabled = !busy && _streamActive;
        BrbButton.IsEnabled = !busy && _streamActive;
        MicMuteButton.IsEnabled = !busy && _streamActive && _selectedMode == StreamMode.AllPlatforms;
        CheckAvatarButton.IsEnabled = !busy && !_streamActive;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
    }

    private void ShowProblem(string title, string message)
    {
        ProblemTitle.Text = title;
        ProblemText.Text = message;
        ProblemPanel.Visibility = Visibility.Visible;
    }

    private void HideProblem() => ProblemPanel.Visibility = Visibility.Collapsed;

    private static string FriendlyError(Exception ex)
    {
        var text = (ex.Message + " " + ex.InnerException?.Message).ToLowerInvariant();
        if (text.Contains("avatar") || text.Contains("vtube") || text.Contains("spout"))
            return "Your avatar app is open, but the avatar is not reaching StreamKit yet. Follow the 3-step Avatar Help card, click Check my avatar, then start again.";
        if (text.Contains("not ready") || text.Contains("starting") || text.Contains("websocket") || text.Contains("connection"))
            return "The streaming engine took longer than expected to start. StreamKit already retried automatically. Click Fix setup, wait for it to finish, then try again.";
        if (text.Contains("vertical") || text.Contains("tiktok") || text.Contains("aitum"))
            return "The TikTok layout is not ready yet. Click Fix setup, then use the one-time Twitch + TikTok setup card.";
        if (text.Contains("game") || text.Contains("running"))
            return "Open the game first, press Find games, select it, then try again.";
        return "StreamKit kept your local settings safe. Click Fix setup and try again. If it still fails, Advanced settings can open the streaming engine for troubleshooting.";
    }

    private static string MakeProgressFriendly(string message)
    {
        var lower = message.ToLowerInvariant();
        if (lower.Contains("spout")) return "Preparing transparent avatar capture…";
        if (lower.Contains("aitum")) return "Preparing Twitch + TikTok support…";
        if (lower.Contains("obs")) return "Preparing the streaming engine…";
        if (lower.Contains("flood")) return "Preparing the simple avatar fallback…";
        if (lower.Contains("websocket")) return "Connecting StreamKit controls…";
        return message;
    }

    private void MinimizePortableObsWindow() => SetPortableObsWindowState(SW_MINIMIZE, bringToFront: false);
    private void RestorePortableObsWindow() => SetPortableObsWindowState(SW_RESTORE, bringToFront: true);

    private void SetPortableObsWindowState(int command, bool bringToFront)
    {
        var obsExe = AppPaths.FindObsExe();
        if (string.IsNullOrWhiteSpace(obsExe)) return;
        var expectedPath = Path.GetFullPath(obsExe);
        var name = Path.GetFileNameWithoutExtension(obsExe);

        foreach (var process in Process.GetProcessesByName(name))
        {
            try
            {
                var actual = process.MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(actual) || !Path.GetFullPath(actual).Equals(expectedPath, StringComparison.OrdinalIgnoreCase)) continue;
                process.Refresh();
                var hwnd = process.MainWindowHandle;
                if (hwnd == IntPtr.Zero) continue;
                _ = ShowWindow(hwnd, command);
                if (bringToFront) _ = SetForegroundWindow(hwnd);
            }
            catch { }
            finally { process.Dispose(); }
        }
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
            FooterStatus.Text = _micMuted ? "Public-stream microphone muted" : "Public-stream microphone live · noise cleanup on";
        }
        catch (Exception ex) { ShowProblem("Couldn’t change the microphone", FriendlyError(ex)); }
        finally
        {
            SetBusy(false);
            UpdateStreamControls();
        }
    }

    private async void DiscordOnlySegment_Click(object sender, RoutedEventArgs e)
    {
        if (_streamActive || _platformSetupNeeded) return;
        _selectedMode = StreamMode.DiscordOnly;
        SavePreferences();
        ApplyModeSelection();
        await RefreshStatusAsync();
    }

    private async void AllPlatformsSegment_Click(object sender, RoutedEventArgs e)
    {
        if (_streamActive || _platformSetupNeeded) return;
        _selectedMode = StreamMode.AllPlatforms;
        SavePreferences();
        ApplyModeSelection();
        await RefreshStatusAsync();
    }

    private async void AvatarCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingAvatar || _streamActive || _platformSetupNeeded || SelectedAvatarChoice is not { } choice) return;
        _selectedAvatar = choice.Mode;
        SavePreferences();
        UpdateAvatarCard();
        ApplyModeSelection();
        await RefreshStatusAsync();
    }

    private async void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingTheme || _streamActive || _platformSetupNeeded || SelectedThemeChoice is not { } choice) return;
        _selectedTheme = choice.Theme;
        SavePreferences();
        await RefreshStatusAsync();
    }

    private async void GameCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingGames || _streamActive || _platformSetupNeeded) return;
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
        if (_busy || _streamActive || _platformSetupNeeded) return;
        await RefreshGameChoicesAsync(preserveSelection: true);
        await RefreshStatusAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!_streamActive && !_platformSetupNeeded) await RefreshGameChoicesAsync(preserveSelection: true);
        await RefreshStatusAsync();
    }

    private void AvatarHelp_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAvatar != AvatarMode.VTubeStudio)
        {
            ShowProblem("Avatar help", "Choose Full VTuber if you want the face-tracked VTube Studio setup guide.");
            return;
        }
        _vTubeStudio.Launch();
        ShowAvatarSetupGuide(force: true);
    }

    private async void CheckAvatar_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _selectedAvatar != AvatarMode.VTubeStudio) return;
        if (_streamActive)
        {
            ShowProblem("Avatar check is unavailable while live", "Stop the current share/stream before running the avatar test. Your live OBS session will not be restarted.");
            return;
        }
        try
        {
            HideProblem();
            SetBusy(true);
            var game = SelectedGame ?? throw new InvalidOperationException("Choose a running game first so StreamKit can open its preview engine.");
            await _setup.EnsureReadyAsync(needSpout: true);
            await _vTubeStudio.LaunchAndWaitAsync();
            _obs.Launch(StreamMode.DiscordOnly, game, _selectedTheme, AvatarMode.VTubeStudio, null);
            await WaitForStreamingEngineAsync(TimeSpan.FromSeconds(35));
            await Task.Delay(1000);
            MinimizePortableObsWindow();

            if (!await CheckAvatarConnectionAsync(TimeSpan.FromSeconds(25)))
                throw new InvalidOperationException("Your avatar is not visible yet. Re-check the three avatar steps and try again.");

            AvatarCheckText.Text = "Avatar connected ✓  You can start sharing now.";
            FooterStatus.Text = "Avatar connected · ready to start";
        }
        catch (Exception ex) { ShowProblem("Avatar not connected yet", FriendlyError(ex)); }
        finally
        {
            _obs.Stop();
            SetupProgressPanel.Visibility = Visibility.Collapsed;
            SetBusy(false);
            await RefreshStatusAsync();
        }
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
            HideProblem();
            SetBusy(true);
            SetupProgressPanel.Visibility = Visibility.Visible;
            var progress = new Progress<(int Percent, string Message)>(value =>
            {
                SetupProgress.Value = value.Percent;
                SetupStatusText.Text = MakeProgressFriendly(value.Message);
            });
            await _setup.EnsureReadyAsync(progress, repair: true,
                needAitum: _selectedMode == StreamMode.AllPlatforms,
                needSpout: _selectedAvatar == AvatarMode.VTubeStudio);
            AudioPrivacyService.HardenPortableObsConfig();
            FooterStatus.Text = "Setup fixed · your account settings were kept";
        }
        catch (Exception ex) { ShowProblem("Fix setup needs attention", FriendlyError(ex)); }
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
        if (_busy) return;
        try
        {
            SetBusy(true);
            var state = await _detection.DetectAsync(SelectedGame);
            if (!state.ObsReady)
                await _setup.EnsureReadyAsync(needAitum: _selectedMode == StreamMode.AllPlatforms, needSpout: _selectedAvatar == AvatarMode.VTubeStudio);
            _obs.Launch(StreamMode.PlainObs, null, _selectedTheme, _selectedAvatar);
            await Task.Delay(1000);
            RestorePortableObsWindow();
        }
        catch (Exception ex) { ShowProblem("Couldn’t open the streaming engine", FriendlyError(ex)); }
        finally { SetBusy(false); }
    }

    private async void OpenStreamingSetup_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            SetBusy(true);
            if (!RestoreExistingObs())
                _obs.Launch(StreamMode.PlainObs, null, _selectedTheme, _selectedAvatar);
            await Task.Delay(900);
            RestorePortableObsWindow();
            FooterStatus.Text = "Account setup window opened · follow the two exact steps in StreamKit";
        }
        catch (Exception ex) { ShowProblem("Couldn’t open account setup", FriendlyError(ex)); }
        finally { SetBusy(false); }
    }

    private bool RestoreExistingObs()
    {
        var obsExe = AppPaths.FindObsExe();
        if (string.IsNullOrWhiteSpace(obsExe)) return false;
        var expectedPath = Path.GetFullPath(obsExe);
        var name = Path.GetFileNameWithoutExtension(obsExe);
        var found = false;
        foreach (var process in Process.GetProcessesByName(name))
        {
            try
            {
                var actual = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(actual) && Path.GetFullPath(actual).Equals(expectedPath, StringComparison.OrdinalIgnoreCase)) found = true;
            }
            catch { }
            finally { process.Dispose(); }
        }
        if (found) RestorePortableObsWindow();
        return found;
    }

    private async void PlatformSetupDone_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            SetBusy(true);
            _obs.Stop();
            await Task.Delay(1500);
            if (!_setup.HasAitumStreamOutput())
            {
                _platformSetupNeeded = true;
                PlatformSetupPanel.Visibility = Visibility.Visible;
                ShowProblem("TikTok setup is not finished yet", "StreamKit still can’t find the TikTok Vertical output. Open account setup again, finish the TikTok step, then click I’m finished.");
                return;
            }

            _platformSetupNeeded = false;
            PlatformSetupPanel.Visibility = Visibility.Collapsed;
            HideProblem();
            FooterStatus.Text = "Accounts saved · press Go Live Everywhere when ready";
        }
        finally
        {
            SetBusy(false);
            await RefreshStatusAsync();
        }
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
            FooterStatus.Text = "Stopped · avatar app left open for next time";
            SetBusy(false);
            await RefreshStatusAsync();
        }
    }

    private void DismissProblem_Click(object sender, RoutedEventArgs e) => HideProblem();

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.Root) { UseShellExecute = true });
    }

    private const int SW_MINIMIZE = 6;
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
