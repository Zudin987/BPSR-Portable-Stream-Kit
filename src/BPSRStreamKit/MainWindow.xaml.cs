using System.ComponentModel;
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
    private sealed record ThemeChoice(StreamTheme Theme, string DisplayName, string Detail);
    private sealed record AvatarChoice(AvatarMode Mode, string DisplayName, string Detail);

    private enum PendingObsPurpose
    {
        None,
        DiscordShare,
        PublicStream,
        TikTokBootstrap
    }

    private readonly DetectionService _detection = new();
    private readonly SetupService _setup = new();
    private readonly ObsService _obs = new();
    private readonly ObsAutomationService _automation = new();
    private readonly ObsControlService _obsControl = new();
    private readonly ObsProcessService _obsProcess = new();
    private readonly ObsCrashRecoveryService _obsCrashRecovery = new();
    private readonly SceneLayoutService _sceneLayout = new();
    private readonly AitumStateService _aitumState = new();
    private readonly VTubeStudioService _vTubeStudio = new();
    private readonly GameCatalogService _catalog = new();
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _obsRecoveryTimer;

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
    private DetectionState? _lastDetection;
    private AitumState _lastAitum = new(false, null, false);
    private bool _busy;
    private bool _statusRefreshRunning;
    private bool _loadingGames;
    private bool _loadingTheme;
    private bool _loadingAvatar;
    private bool _streamActive;
    private bool _sceneControlsReady;
    private bool _verticalScenesReady;
    private bool _twitchStarted;
    private bool _tiktokStarted;
    private bool _micMuted;
    private bool _returningUser;
    private bool _quickMode;
    private bool _allowClose;
    private bool _recoveryPending;
    private PendingObsPurpose _pendingObsPurpose;
    private Func<Task>? _problemAction;

    private GameTarget? SelectedGame => GameCombo.SelectedItem as GameTarget;
    private ThemeChoice? SelectedThemeChoice => ThemeCombo.SelectedItem as ThemeChoice;
    private AvatarChoice? SelectedAvatarChoice => AvatarCombo.SelectedItem as AvatarChoice;
    private static string ThemePreferenceFile => Path.Combine(AppPaths.Root, ".streamkit-theme");
    private static string AvatarPreferenceFile => Path.Combine(AppPaths.Root, ".streamkit-avatar");
    private static string ModePreferenceFile => Path.Combine(AppPaths.Root, ".streamkit-mode");
    private static string AvatarVerifiedFile => Path.Combine(AppPaths.Root, "user-data", "vtube-avatar-verified-v3.txt");
    private static string QuickLaunchMarkerFile => Path.Combine(AppPaths.Root, "user-data", "returning-user-v1.txt");

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

        QuickLaunchPanel.StartRequested += async (_, _) => await StartAsync();
        QuickLaunchPanel.CustomizeRequested += (_, _) =>
        {
            _quickMode = false;
            ApplyQuickLaunchLayout();
        };

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _statusTimer.Tick += async (_, _) =>
        {
            if (!_busy) await RefreshStatusAsync();
        };

        _obsRecoveryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _obsRecoveryTimer.Tick += (_, _) =>
        {
            if (!_obsProcess.IsRunning()) return;
            if (_obsCrashRecovery.TryContinuePortableObsNormally())
                FooterStatus.Text = "OBS recovered in Normal Mode · continuing automatically";
        };

        var version = typeof(MainWindow).Assembly.GetName().Version;
        VersionText.Text = version is null ? "v2.3.1" : $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _returningUser = HasReturningUserState();
        _quickMode = _returningUser;
        ApplyModeSelection();
        await RefreshGameChoicesAsync(preserveSelection: false);
        await RefreshStatusAsync();
        UpdateUiState();
        ApplyQuickLaunchLayout();
        _statusTimer.Start();
        _obsRecoveryTimer.Start();
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

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            _statusTimer.Stop();
            _obsRecoveryTimer.Stop();
            return;
        }

        if (!_streamActive && !_sceneControlsReady && !_recoveryPending && !_obsProcess.IsRunning())
        {
            _statusTimer.Stop();
            _obsRecoveryTimer.Stop();
            return;
        }

        e.Cancel = true;
        if (_busy)
        {
            FooterStatus.Text = "Finish the current setup step before closing StreamKit";
            return;
        }

        await StopAsync();
        _allowClose = true;
        Close();
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
        if (_statusRefreshRunning) return;
        _statusRefreshRunning = true;
        try
        {
            if (_recoveryPending && !_obsProcess.IsRunning())
            {
                _recoveryPending = false;
                _pendingObsPurpose = PendingObsPurpose.None;
            }

            _lastDetection = await _detection.DetectAsync(SelectedGame);
            _lastAitum = _aitumState.Read();
            ApplyStatus(_lastDetection, _lastAitum);
            RefreshQuickLaunch();
        }
        catch
        {
            // A transient process closing between enumerations should not turn into an app-level error.
        }
        finally
        {
            _statusRefreshRunning = false;
        }
    }

    private void ApplyStatus(DetectionState state, AitumState aitum)
    {
        var game = SelectedGame;
        var gameName = game?.DisplayName ?? "your game";
        var avatarToolsReady = _selectedAvatar != AvatarMode.VTubeStudio || _setup.IsSpoutReady();
        var pngReady = _selectedAvatar != AvatarMode.PngAvatar || state.AvatarReady;
        var needsTikTokHelper = _selectedMode == StreamMode.AllPlatforms && aitum.TikTokOutputConfigured;

        SetStatus(GameStatusDot, GameStatusText, state.GameRunning,
            $"{gameName} is ready", game is null ? "Choose a game" : $"Open {gameName}");

        var engineReady = state.ObsReady && avatarToolsReady && pngReady && (!needsTikTokHelper || aitum.PluginReady);
        SetStatus(ObsStatusDot, ObsStatusText, engineReady,
            "Streaming tools are ready", "Streaming tools will prepare themselves");

        switch (_selectedAvatar)
        {
            case AvatarMode.VTubeStudio:
                SetStatus(AvatarStatusDot, AvatarStatusText, state.VTubeStudioRunning && avatarToolsReady,
                    File.Exists(AvatarVerifiedFile) ? "Avatar connected" : "Avatar app is open",
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

        TikTokStatusRow.Visibility = _selectedMode == StreamMode.AllPlatforms ? Visibility.Visible : Visibility.Collapsed;
        if (_selectedMode == StreamMode.AllPlatforms)
        {
            var tiktokReady = aitum.TikTokOutputConfigured && aitum.PluginReady;
            SetStatus(TikTokStatusDot, TikTokStatusText, tiktokReady,
                "TikTok vertical output is ready",
                aitum.TikTokOutputConfigured ? "TikTok helper needs repair" : "TikTok not connected · optional");
        }

        if (_streamActive)
        {
            HeroEyebrow.Text = "LIVE CONTROLS READY";
            HeroEyebrow.Foreground = (Brush)FindResource("GoodBrush");
            if (_selectedMode == StreamMode.DiscordOnly)
            {
                HeroTitle.Text = "Discord share is ready";
                HeroSubtitle.Text = "Share the clean projector window in Discord with sound. Your normal Discord microphone stays separate.";
            }
            else if (_tiktokStarted)
            {
                HeroTitle.Text = "Twitch + TikTok are live";
                HeroSubtitle.Text = "Discord share is ready too. The four scene buttons keep horizontal and vertical views synchronized.";
            }
            else if (_twitchStarted)
            {
                HeroTitle.Text = "Twitch is live · Discord is ready";
                HeroSubtitle.Text = "TikTok is optional and can be connected later without rebuilding this setup.";
            }
            else
            {
                HeroTitle.Text = "Discord share is ready";
                HeroSubtitle.Text = "Twitch is not connected yet. Your scenes and Discord share still work normally.";
            }
            MainActionButton.Content = "Reopen Discord share window";
            return;
        }

        if (_recoveryPending && _obsProcess.IsRunning())
        {
            HeroEyebrow.Text = "OBS IS OPEN";
            HeroEyebrow.Foreground = (Brush)FindResource("WarnBrush");
            HeroTitle.Text = "Your OBS session was kept running";
            HeroSubtitle.Text = "StreamKit controls took longer than expected, but your game capture was not stopped. Wait a moment, then retry the controls on this same OBS session.";
            MainActionButton.Content = "Retry StreamKit controls";
            FooterStatus.Text = "OBS kept open · no duplicate launch · retry controls when ready";
            return;
        }

        var needsSetup = !state.ObsReady
                         || (_selectedAvatar == AvatarMode.VTubeStudio && !avatarToolsReady)
                         || (_selectedAvatar == AvatarMode.PngAvatar && !state.AvatarReady)
                         || (needsTikTokHelper && !aitum.PluginReady);

        if (needsSetup)
        {
            HeroEyebrow.Text = "FIRST RUN";
            HeroEyebrow.Foreground = (Brush)FindResource("WarnBrush");
            HeroTitle.Text = "StreamKit will set itself up";
            HeroSubtitle.Text = "Press the main button. Only the components required by your current setup will be prepared inside this portable folder.";
            MainActionButton.Content = "Set up & " + GetActionLabel();
            FooterStatus.Text = "Automatic first-run setup · your local account settings stay in this folder";
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
        HeroTitle.Text = _selectedMode == StreamMode.DiscordOnly ? "Ready to share on Discord" : "Ready for Twitch + Discord";
        HeroSubtitle.Text = _selectedMode == StreamMode.DiscordOnly
            ? $"{gameName} + {SelectedAvatarChoice?.DisplayName ?? "your avatar"} are ready. StreamKit will open one clean share window."
            : aitum.TikTokOutputConfigured
                ? $"{gameName} is ready for Discord, Twitch and your saved TikTok vertical output."
                : $"{gameName} is ready for Twitch + Discord. TikTok can be added later if you get a stream key.";
        FooterStatus.Text = $"Ready · {SelectedAvatarChoice?.DisplayName} · {SelectedThemeChoice?.DisplayName}";
    }

    private static void SetStatus(Ellipse dot, TextBlock label, bool ready, string readyText, string missingText)
    {
        if (Application.Current?.MainWindow is not MainWindow window) return;
        dot.Fill = (Brush)window.FindResource(ready ? "GoodBrush" : "WarnBrush");
        label.Text = ready ? readyText : missingText;
        label.Foreground = (Brush)window.FindResource(ready ? "TextBrush" : "MutedBrush");
    }

    private async Task StartAsync()
    {
        if (_busy) return;

        if (_recoveryPending)
        {
            if (_obsProcess.IsRunning())
            {
                await ResumeExistingSessionAsync();
                return;
            }

            _recoveryPending = false;
            _pendingObsPurpose = PendingObsPurpose.None;
        }

        if (_streamActive)
        {
            try
            {
                SetBusy(true);
                await RunEngineStepWithRetryAsync(() => _automation.OpenProgramProjectorAsync(), "Reopening your Discord share window…", 5);
                MinimizePortableObsWindow();
                FooterStatus.Text = "Discord share window reopened";
            }
            catch (Exception ex) { ShowProblemForException("Couldn’t reopen the share window", ex, retry: StartAsync); }
            finally
            {
                HideProgress();
                SetBusy(false);
                UpdateUiState();
            }
            return;
        }

        try
        {
            HideProblem();
            SetBusy(true);
            await RefreshGameChoicesAsync(preserveSelection: true);
            var game = SelectedGame ?? throw new InvalidOperationException("Choose a running game first.");
            var state = await _detection.DetectAsync(game);
            var aitumBefore = _aitumState.Read();
            var wantTikTok = _selectedMode == StreamMode.AllPlatforms && aitumBefore.TikTokOutputConfigured;
            var needAvatarBridge = _selectedAvatar == AvatarMode.VTubeStudio;
            var showSetupProgress = !state.ObsReady
                                    || (needAvatarBridge && !_setup.IsSpoutReady())
                                    || (_selectedAvatar == AvatarMode.PngAvatar && !state.AvatarReady)
                                    || (wantTikTok && !aitumBefore.PluginReady);

            var progress = new Progress<(int Percent, string Message)>(value =>
                ShowProgress(MakeProgressFriendly(value.Message), value.Percent));
            await _setup.EnsureReadyAsync(showSetupProgress ? progress : null,
                needAitum: wantTikTok,
                needSpout: needAvatarBridge);
            _sceneLayout.PrepareBaseScenes(_selectedAvatar);

            VTubeCaptureTarget? vTubeTarget = null;
            if (_selectedAvatar == AvatarMode.VTubeStudio)
            {
                ShowProgress("Waiting for VTube Studio to finish opening…");
                vTubeTarget = await _vTubeStudio.LaunchAndWaitAsync(TimeSpan.FromSeconds(90));
                ShowAvatarSetupGuide(force: !File.Exists(AvatarVerifiedFile));
            }

            state = await _detection.DetectAsync(game);
            if (!state.GameRunning)
                throw new InvalidOperationException($"{game.DisplayName} is not running. Open it, then press Find games.");

            _catalog.Save(game);
            _catalog.SaveLastSelectedProcess(game.ProcessName);
            AudioPrivacyService.HardenPortableObsConfig();

            if (_obsProcess.IsRunning())
            {
                ShowProgress("Closing the previous StreamKit preview safely…");
                await _obsProcess.CloseGracefullyAsync();
                await Task.Delay(400);
            }

            if (_selectedMode == StreamMode.DiscordOnly)
            {
                await StartDiscordOnlyAsync(game, vTubeTarget);
                MarkReturningUser();
                return;
            }

            await StartPublicModeAsync(game, vTubeTarget, wantTikTok);
            MarkReturningUser();
        }
        catch (Exception ex)
        {
            if (_obsProcess.IsRunning())
            {
                _recoveryPending = true;
                RestorePortableObsWindow();
                var actionLabel = _pendingObsPurpose == PendingObsPurpose.TikTokBootstrap
                    ? "Retry TikTok layout"
                    : "Retry controls";
                ShowProblem(
                    "OBS stayed open",
                    $"{FriendlyError(ex)} StreamKit kept this OBS session running and will reuse the same copy when you retry.",
                    actionLabel,
                    ResumeExistingSessionAsync);
                FooterStatus.Text = "OBS kept open · your game capture was not stopped";
            }
            else
            {
                _recoveryPending = false;
                _pendingObsPurpose = PendingObsPurpose.None;
                _sceneControlsReady = false;
                _verticalScenesReady = false;
                ShowProblemForException("StreamKit couldn’t finish automatically", ex, retry: StartAsync);
                FooterStatus.Text = "Needs attention · your saved choices were kept";
            }
        }
        finally
        {
            HideProgress();
            SetBusy(false);
            UpdateUiState();
            try { await RefreshStatusAsync(); } catch { }
        }
    }

    private async Task StartDiscordOnlyAsync(GameTarget game, VTubeCaptureTarget? vTubeTarget)
    {
        _pendingObsPurpose = PendingObsPurpose.DiscordShare;
        _obs.Launch(StreamMode.DiscordOnly, game, _selectedTheme, _selectedAvatar, vTubeTarget);
        await WaitForStreamingEngineAsync(TimeSpan.FromSeconds(75));
        await CompleteDiscordOnlySessionAsync();
    }

    private async Task CompleteDiscordOnlySessionAsync()
    {
        _sceneControlsReady = true;
        _verticalScenesReady = false;
        UpdateUiState();

        var audioReady = await TryEngineStepAsync(
            () => _automation.ConfigureDiscordShareAudioAsync(alsoStreamingPlatforms: false),
            "Protecting your Discord audio…");

        var avatarReady = await CheckAvatarWithoutBlockingStartAsync(TimeSpan.FromSeconds(12));

        await RunEngineStepWithRetryAsync(() => _automation.SetCurrentSceneAsync("Game Clean"), "Opening Game Clean…", 6);
        await SyncAvatarTransformsAsync(includeVertical: false);
        await RunEngineStepWithRetryAsync(() => _automation.OpenProgramProjectorAsync(), "Opening your Discord share window…", 6);
        MinimizePortableObsWindow();

        _micMuted = true;
        _streamActive = true;
        _twitchStarted = false;
        _tiktokStarted = false;
        _recoveryPending = false;
        _pendingObsPurpose = PendingObsPurpose.None;
        HideProblem();

        FooterStatus.Text = "Discord ready · Game Clean · share the projector window with sound";
        if (!avatarReady) FooterStatus.Text += " · avatar still loading in VTube Studio";
        if (!audioReady) FooterStatus.Text += " · audio controls may need a retry";
    }

    private async Task StartPublicModeAsync(GameTarget game, VTubeCaptureTarget? vTubeTarget, bool wantTikTok)
    {
        string? verticalUuid = null;
        if (wantTikTok)
        {
            _setup.EnsureAitumProfileConfig();
            verticalUuid = _aitumState.Read().VerticalCanvasUuid ?? _setup.GetVerticalCanvasUuid();
            if (string.IsNullOrWhiteSpace(verticalUuid))
            {
                ShowProgress("Preparing the phone-shaped TikTok layout…");
                _pendingObsPurpose = PendingObsPurpose.TikTokBootstrap;
                _obs.LaunchAitumBootstrap(game, _selectedTheme, _selectedAvatar, vTubeTarget);
                await WaitForStreamingEngineAsync(TimeSpan.FromSeconds(75));
                verticalUuid = await WaitForVerticalCanvasAsync(TimeSpan.FromSeconds(30));
                if (string.IsNullOrWhiteSpace(verticalUuid))
                    throw new InvalidOperationException("TikTok’s vertical canvas could not be prepared. Open TikTok setup once and retry.");
                await _obsProcess.CloseGracefullyAsync();
                _pendingObsPurpose = PendingObsPurpose.None;
                await Task.Delay(500);
            }

            _sceneLayout.PrepareBaseScenes(_selectedAvatar);
            _obs.PrepareAllPlatforms(verticalUuid, game, _selectedTheme, _selectedAvatar, vTubeTarget);
            _sceneLayout.FinalizeVerticalBpsrScene(verticalUuid);
            _verticalScenesReady = true;
        }
        else
        {
            _sceneLayout.PrepareBaseScenes(_selectedAvatar);
            _verticalScenesReady = false;
        }

        AudioPrivacyService.HardenPortableObsConfig();
        _pendingObsPurpose = PendingObsPurpose.PublicStream;
        _obs.Launch(StreamMode.AllPlatforms, game, _selectedTheme, _selectedAvatar, vTubeTarget);
        await WaitForStreamingEngineAsync(TimeSpan.FromSeconds(75));
        await CompletePublicSessionAsync(wantTikTok && _verticalScenesReady);
    }

    private async Task CompletePublicSessionAsync(bool wantTikTok)
    {
        _sceneControlsReady = true;
        UpdateUiState();

        var audioReady = await TryEngineStepAsync(
            () => _automation.ConfigureDiscordShareAudioAsync(alsoStreamingPlatforms: true),
            "Protecting your public-stream audio…");
        await TryEngineStepAsync(() => _automation.StartVirtualCameraAsync(), "Preparing the optional Discord camera…");

        var avatarReady = await CheckAvatarWithoutBlockingStartAsync(TimeSpan.FromSeconds(12));
        await SyncAvatarTransformsAsync(includeVertical: _verticalScenesReady);
        var startingSceneReady = await TryEngineStepAsync(
            () => _automation.SwitchScenesAsync("Starting Soon", _verticalScenesReady ? "Vertical Starting Soon" : null),
            "Preparing Starting Soon…");

        _twitchStarted = false;
        _tiktokStarted = false;
        Exception? publicStartError = null;

        if (wantTikTok && _verticalScenesReady)
        {
            try
            {
                await RunEngineStepWithRetryAsync(() => _automation.StartAllStreamsAsync(), "Starting Twitch + TikTok…", 4);
                _twitchStarted = true;
                _tiktokStarted = true;
            }
            catch (Exception ex)
            {
                publicStartError = ex;
                try
                {
                    await _obsControl.StartMainStreamAsync();
                    _twitchStarted = true;
                }
                catch (Exception twitchEx) { publicStartError = twitchEx; }
            }
        }
        else
        {
            try
            {
                ShowProgress("Starting Twitch…");
                await _obsControl.StartMainStreamAsync();
                _twitchStarted = true;
            }
            catch (Exception ex) { publicStartError = ex; }
        }

        _micMuted = false;
        _streamActive = true;
        _recoveryPending = false;
        _pendingObsPurpose = PendingObsPurpose.None;

        var projectorReady = await TryEngineStepAsync(
            () => _automation.OpenProgramProjectorAsync(),
            "Opening your Discord share window…");
        if (projectorReady) MinimizePortableObsWindow();
        else RestorePortableObsWindow();

        if (!_twitchStarted)
        {
            ShowProblem(
                "Twitch is not connected yet",
                "Discord/OBS stayed open and all scene controls remain available. Connect Twitch in portable OBS under Settings → Stream, then retry when ready.",
                "Open streaming engine",
                () => OpenObsForSetupAsync(needAitum: false));
            FooterStatus.Text = "OBS ready · Twitch not connected · TikTok remains optional";
        }
        else if (!projectorReady)
        {
            ShowProblem(
                "Twitch is live · Discord share window is still connecting",
                "Your public stream was not stopped. Use Reopen Discord share window after OBS finishes loading its controls.",
                "Retry Discord window",
                async () =>
                {
                    await RunEngineStepWithRetryAsync(() => _automation.OpenProgramProjectorAsync(), "Opening your Discord share window…", 6);
                    MinimizePortableObsWindow();
                });
            FooterStatus.Text = _tiktokStarted
                ? "Live · Twitch + TikTok · Discord window needs retry"
                : "Live · Twitch · Discord window needs retry · TikTok optional";
        }
        else if (_tiktokStarted)
        {
            HideProblem();
            FooterStatus.Text = "Live · Starting Soon · Discord + Twitch + TikTok";
        }
        else
        {
            HideProblem();
            FooterStatus.Text = "Live · Starting Soon · Discord + Twitch · TikTok optional";
        }

        if (!avatarReady) FooterStatus.Text += " · avatar still loading";
        if (!audioReady) FooterStatus.Text += " · audio controls may need retry";
        if (!startingSceneReady) FooterStatus.Text += " · scene controls may need retry";
        _ = publicStartError;
    }

    private async Task ResumeExistingSessionAsync()
    {
        if (_busy) return;
        if (!_obsProcess.IsRunning())
        {
            _recoveryPending = false;
            _pendingObsPurpose = PendingObsPurpose.None;
            await StartAsync();
            return;
        }

        try
        {
            HideProblem();
            SetBusy(true);
            ShowProgress("Reconnecting StreamKit controls to the OBS window already open…");
            if (!await _automation.WaitUntilReadyAsync(TimeSpan.FromSeconds(75)))
                throw new InvalidOperationException("OBS is open, but StreamKit controls are still not responding yet.");

            if (_pendingObsPurpose == PendingObsPurpose.TikTokBootstrap)
            {
                var verticalUuid = await WaitForVerticalCanvasAsync(TimeSpan.FromSeconds(25));
                if (string.IsNullOrWhiteSpace(verticalUuid))
                    throw new InvalidOperationException("TikTok’s vertical canvas is still being prepared. Leave OBS open and retry again in a moment.");

                ShowProgress("TikTok layout is ready · restarting OBS once to load the final stream scenes…");
                await _obsProcess.CloseGracefullyAsync();
                _recoveryPending = false;
                _pendingObsPurpose = PendingObsPurpose.None;
                HideProgress();
                SetBusy(false);
                await StartAsync();
                return;
            }

            if (_pendingObsPurpose == PendingObsPurpose.DiscordShare || _selectedMode == StreamMode.DiscordOnly)
                await CompleteDiscordOnlySessionAsync();
            else
                await CompletePublicSessionAsync(_lastAitum.TikTokOutputConfigured && _verticalScenesReady);

            MarkReturningUser();
        }
        catch (Exception ex)
        {
            _recoveryPending = _obsProcess.IsRunning();
            if (_recoveryPending)
            {
                RestorePortableObsWindow();
                ShowProblem(
                    "OBS is still open",
                    $"{FriendlyError(ex)} Nothing was closed. Wait a moment and retry controls on this same OBS session.",
                    _pendingObsPurpose == PendingObsPurpose.TikTokBootstrap ? "Retry TikTok layout" : "Retry controls",
                    ResumeExistingSessionAsync);
                FooterStatus.Text = "OBS kept open · controls can be retried without restarting it";
            }
            else
            {
                _pendingObsPurpose = PendingObsPurpose.None;
                ShowProblemForException("OBS closed before StreamKit could reconnect", ex, retry: StartAsync);
            }
        }
        finally
        {
            HideProgress();
            SetBusy(false);
            UpdateUiState();
            try { await RefreshStatusAsync(); } catch { }
        }
    }

    private async Task<bool> CheckAvatarWithoutBlockingStartAsync(TimeSpan timeout)
    {
        if (_selectedAvatar != AvatarMode.VTubeStudio) return true;
        try
        {
            var ready = await CheckAvatarConnectionAsync(timeout);
            if (!ready)
            {
                AvatarSetupPanel.Visibility = Visibility.Visible;
                AvatarCheckText.Text = "VTube Studio is still loading. Your OBS/share stays open; the avatar can connect when it is ready.";
            }
            return ready;
        }
        catch
        {
            AvatarSetupPanel.Visibility = Visibility.Visible;
            AvatarCheckText.Text = "VTube Studio is still loading. Your OBS/share stays open; use Check my avatar later if needed.";
            return false;
        }
    }

    private async Task SwitchSceneAsync(string scene)
    {
        if (_busy || (!_streamActive && !_sceneControlsReady)) return;
        try
        {
            SetBusy(true);
            UpdateUiState();
            var verticalScene = _verticalScenesReady ? scene switch
            {
                "Starting Soon" => "Vertical Starting Soon",
                "BRB" => "Vertical BRB",
                "BPSR" => "Vertical BPSR",
                _ => "Vertical Live"
            } : null;
            var horizontalScene = scene == "Game Clean" ? "Game Clean" : scene;

            await RunEngineStepWithRetryAsync(
                () => _automation.SwitchScenesAsync(horizontalScene, verticalScene),
                $"Switching to {scene}…", 5);

            HideProgress();
            var suffix = _verticalScenesReady ? " · horizontal + vertical" : string.Empty;
            FooterStatus.Text = (_streamActive ? "Live" : "Preview") + $" · {scene}{suffix}";

            if (scene == "BPSR" && _lastDetection?.ResonanceLogsRunning != true)
                FooterStatus.Text += " · HUD tool not detected yet";
        }
        catch (Exception ex) { ShowProblemForException("Couldn’t change the scene", ex); }
        finally
        {
            HideProgress();
            SetBusy(false);
            UpdateUiState();
        }
    }

    private async Task StopAsync()
    {
        if (_busy) return;
        try
        {
            SetBusy(true);
            UpdateUiState();
            if (_streamActive)
            {
                try { await _automation.StopAllStreamsAsync(); } catch { }
                try { await _obsControl.StopMainStreamAsync(); } catch { }
                try { await _automation.StopVirtualCameraAsync(); } catch { }
                try { await _automation.RestoreNormalAudioMonitoringAsync(); } catch { }
            }
            ShowProgress("Stopping safely · waiting for OBS to save its state…");
            await _obsProcess.CloseGracefullyAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            _streamActive = false;
            _sceneControlsReady = false;
            _verticalScenesReady = false;
            _twitchStarted = false;
            _tiktokStarted = false;
            _micMuted = false;
            _recoveryPending = false;
            _pendingObsPurpose = PendingObsPurpose.None;
            HideProgress();
            PlatformSetupPanel.Visibility = Visibility.Collapsed;
            FooterStatus.Text = "Stopped cleanly · avatar app left open for next time";
            SetBusy(false);
            UpdateUiState();
            try { await RefreshStatusAsync(); } catch { }
        }
    }

    private async Task CheckTikTokAsync()
    {
        if (_busy) return;
        try
        {
            SetBusy(true);
            ShowProgress("Checking for your TikTok Vertical output…");
            await Task.Delay(300);
            _lastAitum = _aitumState.Read();
            if (_lastAitum.TikTokOutputConfigured)
            {
                PlatformSetupPanel.Visibility = Visibility.Collapsed;
                HideProblem();
                FooterStatus.Text = "TikTok saved · it will join automatically on your next public start";
                if (!_streamActive && !_recoveryPending && _obsProcess.IsRunning())
                {
                    ShowProgress("Saving TikTok setup and closing OBS safely…");
                    await _obsProcess.CloseGracefullyAsync();
                    _sceneControlsReady = false;
                }
            }
            else
            {
                PlatformSetupPanel.Visibility = Visibility.Visible;
                HideProblem();
                FooterStatus.Text = "TikTok not connected yet · Twitch + Discord are unaffected";
                RestorePortableObsWindow();
            }
        }
        catch (Exception ex) { ShowProblemForException("Couldn’t check TikTok yet", ex); }
        finally
        {
            HideProgress();
            SetBusy(false);
            UpdateUiState();
            await RefreshStatusAsync();
        }
    }

    private async Task OpenObsForSetupAsync(bool needAitum)
    {
        if (_busy) return;
        try
        {
            SetBusy(true);
            HideProblem();
            var state = await _detection.DetectAsync(SelectedGame);
            var needSpout = _selectedAvatar == AvatarMode.VTubeStudio;
            if (!state.ObsReady || (needAitum && !_aitumState.Read().PluginReady) || (needSpout && !_setup.IsSpoutReady()))
            {
                var progress = new Progress<(int Percent, string Message)>(value =>
                    ShowProgress(MakeProgressFriendly(value.Message), value.Percent));
                await _setup.EnsureReadyAsync(progress, needAitum: needAitum, needSpout: needSpout);
            }

            if (needAitum) _setup.EnsureAitumProfileConfig();
            _sceneLayout.PrepareBaseScenes(_selectedAvatar);

            if (_obsProcess.IsRunning())
            {
                RestorePortableObsWindow();
            }
            else
            {
                _obs.Launch(StreamMode.PlainObs, null, _selectedTheme, _selectedAvatar);
                ShowProgress("Opening the streaming engine…");
                if (!await _automation.WaitUntilReadyAsync(TimeSpan.FromSeconds(75)))
                    throw new InvalidOperationException("The streaming engine did not finish opening in time.");
                RestorePortableObsWindow();
            }

            _sceneControlsReady = true;
            FooterStatus.Text = needAitum
                ? "TikTok setup is open · add one Vertical stream output, then return to Check TikTok"
                : "Streaming engine is open · connect Twitch under Settings → Stream";
        }
        catch (Exception ex)
        {
            ShowProblemForException("Couldn’t open the streaming engine", ex,
                retry: () => OpenObsForSetupAsync(needAitum));
        }
        finally
        {
            HideProgress();
            SetBusy(false);
            UpdateUiState();
        }
    }

    private async Task CheckAvatarAsync()
    {
        if (_busy || _selectedAvatar != AvatarMode.VTubeStudio) return;
        if (_streamActive || _recoveryPending)
        {
            ShowProblem("Avatar check is unavailable right now", "Stop the current OBS/share session before running the separate avatar test.");
            return;
        }

        try
        {
            HideProblem();
            SetBusy(true);
            var progress = new Progress<(int Percent, string Message)>(value =>
                ShowProgress(MakeProgressFriendly(value.Message), value.Percent));
            await _setup.EnsureReadyAsync(progress, needSpout: true);
            _sceneLayout.PrepareBaseScenes(_selectedAvatar);
            ShowProgress("Waiting for VTube Studio to finish opening…");
            await _vTubeStudio.LaunchAndWaitAsync(TimeSpan.FromSeconds(75));

            if (_obsProcess.IsRunning()) await _obsProcess.CloseGracefullyAsync();
            _obs.LaunchAvatarPreview(_selectedTheme);
            await WaitForStreamingEngineAsync(TimeSpan.FromSeconds(60));
            MinimizePortableObsWindow();

            if (!await CheckAvatarConnectionAsync(TimeSpan.FromSeconds(40)))
            {
                try { if (File.Exists(AvatarVerifiedFile)) File.Delete(AvatarVerifiedFile); } catch { }
                throw new InvalidOperationException("Your avatar is still loading or not visible yet. Leave VTube Studio open and try again in a moment.");
            }

            AvatarCheckText.Text = "Avatar connected ✓  You can start sharing now.";
            FooterStatus.Text = "Avatar connected · ready to start";
        }
        catch (Exception ex)
        {
            try { if (File.Exists(AvatarVerifiedFile)) File.Delete(AvatarVerifiedFile); } catch { }
            ShowProblemForException("Avatar not connected yet", ex, retry: CheckAvatarAsync);
        }
        finally
        {
            try { await _obsProcess.CloseGracefullyAsync(); } catch { }
            HideProgress();
            SetBusy(false);
            UpdateUiState();
            await RefreshStatusAsync();
        }
    }

    private async Task RepairAsync()
    {
        if (_busy || _streamActive || _recoveryPending) return;
        try
        {
            HideProblem();
            SetBusy(true);
            var aitum = _aitumState.Read();
            var needAitum = _selectedMode == StreamMode.AllPlatforms && aitum.TikTokOutputConfigured;
            var progress = new Progress<(int Percent, string Message)>(value =>
                ShowProgress(MakeProgressFriendly(value.Message), value.Percent));
            await _setup.EnsureReadyAsync(progress, repair: true,
                needAitum: needAitum,
                needSpout: _selectedAvatar == AvatarMode.VTubeStudio);
            _sceneLayout.PrepareBaseScenes(_selectedAvatar);
            AudioPrivacyService.HardenPortableObsConfig();
            FooterStatus.Text = "Setup repaired · account settings and saved choices were kept";
        }
        catch (Exception ex) { ShowProblemForException("Fix setup needs attention", ex, retry: RepairAsync); }
        finally
        {
            HideProgress();
            SetBusy(false);
            UpdateUiState();
            await RefreshStatusAsync();
        }
    }

    private async Task WaitForStreamingEngineAsync(TimeSpan timeout)
    {
        ShowProgress("Starting the streaming engine in the background…");
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
                ShowProgress(friendlyStatus);
                await action();
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                if (!LooksLikeStartupDelay(ex) && attempt >= 2) break;
                if (attempt == attempts) break;
                ShowProgress($"{friendlyStatus.TrimEnd('…')} — waiting a moment…");
                await Task.Delay(Math.Min(650 + (attempt * 250), 1900));
            }
        }
        throw new InvalidOperationException("The streaming engine is still starting. StreamKit retried automatically but it did not become ready in time.", last);
    }

    private async Task<bool> TryEngineStepAsync(Func<Task> action, string friendlyStatus)
    {
        try
        {
            await RunEngineStepWithRetryAsync(action, friendlyStatus, 5);
            return true;
        }
        catch { return false; }
    }

    private static bool LooksLikeStartupDelay(Exception ex)
    {
        var text = (ex.Message + " " + ex.InnerException?.Message).ToLowerInvariant();
        return text.Contains("not ready") || text.Contains("starting") || text.Contains("timeout")
               || text.Contains("timed out") || text.Contains("connection") || text.Contains("websocket")
               || text.Contains("perform the request") || text.Contains("closed the connection")
               || text.Contains("not responding");
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
        AvatarCheckText.Text = "Keep VTube Studio open, then click Check my avatar.";
    }

    private async Task<bool> CheckAvatarConnectionAsync(TimeSpan timeout)
    {
        if (_selectedAvatar != AvatarMode.VTubeStudio) return true;

        ShowProgress("Checking that your avatar is visible and transparent…");
        var ready = await _automation.WaitForVTubeStudioVideoAsync(timeout);
        if (!ready)
        {
            AvatarSetupPanel.Visibility = Visibility.Visible;
            AvatarCheckText.Text = "Avatar not detected yet. Check the setup steps, then try again.";
            return false;
        }

        try { AtomicFile.WriteAllText(AvatarVerifiedFile, "VTube Studio transparent avatar output verified by StreamKit."); }
        catch { }

        AvatarSetupPanel.Visibility = Visibility.Collapsed;
        AvatarCheckText.Text = "Avatar connected ✓";
        return true;
    }

    private async Task SyncAvatarTransformsAsync(bool includeVertical)
    {
        if (_selectedAvatar != AvatarMode.VTubeStudio) return;
        await _obsControl.SyncSceneItemTransformAsync("Game Clean", "BPSR", "VTube Studio Avatar");
        if (includeVertical)
            await _obsControl.SyncSceneItemTransformAsync("Vertical Live", "Vertical BPSR", "Vertical - VTube Studio Avatar");
    }

    private void ShowProgress(string message, int? percent = null)
    {
        SetupProgressPanel.Visibility = Visibility.Visible;
        SetupProgress.IsIndeterminate = percent is null;
        if (percent is not null) SetupProgress.Value = Math.Clamp(percent.Value, 0, 100);
        SetupStatusText.Text = message;
    }

    private void HideProgress()
    {
        SetupProgress.IsIndeterminate = false;
        SetupProgressPanel.Visibility = Visibility.Collapsed;
    }

    private void ApplyModeSelection()
    {
        SetSegment(DiscordOnlySegment, _selectedMode == StreamMode.DiscordOnly);
        SetSegment(AllPlatformsSegment, _selectedMode == StreamMode.AllPlatforms);
        MainActionButton.Content = _recoveryPending
            ? "Retry StreamKit controls"
            : _streamActive ? "Reopen Discord share window" : GetActionLabel();

        if (_selectedMode == StreamMode.DiscordOnly)
        {
            ActionHint.Text = "StreamKit opens one clean window. Share that window in Discord with sound.";
            PrivacyText.Text = "Discord gets only the selected game sound. Your voice stays on your normal Discord microphone, so it is not doubled.";
        }
        else
        {
            ActionHint.Text = _lastAitum.TikTokOutputConfigured
                ? "StreamKit starts Twitch + Discord and your saved vertical TikTok output together."
                : "Start Twitch + Discord now. TikTok is optional and can be added later.";
            PrivacyText.Text = "Public streams get the selected game + your cleaned microphone. Desktop/system audio stays out, so Discord friends and notifications are not broadcast.";
        }
        UpdateUiState();
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
        return _lastAitum.TikTokOutputConfigured ? "Go Live Everywhere" : "Start Twitch + Discord";
    }

    private void UpdateUiState()
    {
        var sceneReady = _sceneControlsReady || _streamActive;
        var sessionOpen = _streamActive || _recoveryPending;
        SceneControlsPanel.Visibility = sceneReady || _recoveryPending ? Visibility.Visible : Visibility.Collapsed;
        StartingSoonButton.IsEnabled = sceneReady && !_busy;
        BrbButton.IsEnabled = sceneReady && !_busy;
        GameCleanButton.IsEnabled = sceneReady && !_busy;
        BpsrButton.IsEnabled = sceneReady && !_busy;
        StopStreamButton.IsEnabled = (sceneReady || _recoveryPending) && !_busy;
        MicMuteButton.IsEnabled = _streamActive && !_busy && _selectedMode == StreamMode.AllPlatforms;
        MicMuteButton.Content = _selectedMode == StreamMode.DiscordOnly
            ? "Discord mic separate"
            : (_micMuted ? "Unmute Mic" : "Mute Mic");
        MicMuteButton.Visibility = _selectedMode == StreamMode.AllPlatforms ? Visibility.Visible : Visibility.Collapsed;

        MainActionButton.IsEnabled = !_busy;
        DiscordOnlySegment.IsEnabled = !_busy && !sessionOpen;
        AllPlatformsSegment.IsEnabled = !_busy && !sessionOpen;
        AvatarCombo.IsEnabled = !_busy && !sessionOpen;
        ThemeCombo.IsEnabled = !_busy && !sessionOpen;
        GameCombo.IsEnabled = !_busy && !sessionOpen;
        CheckAvatarButton.IsEnabled = !_busy && !sessionOpen;

        TikTokSetupShortcut.Visibility = _selectedMode == StreamMode.AllPlatforms
                                         && !_lastAitum.TikTokOutputConfigured
                                         && PlatformSetupPanel.Visibility != Visibility.Visible
                                         && !_recoveryPending
            ? Visibility.Visible
            : Visibility.Collapsed;
        SceneControlHint.Text = _recoveryPending
            ? "OBS is still open. Retry StreamKit controls when it finishes loading; Stop closes it only if you choose to."
            : _verticalScenesReady
                ? "These four scenes stay synchronized across horizontal and TikTok vertical views."
                : "These four scenes control the active horizontal share/stream view.";

        if (_recoveryPending) MainActionButton.Content = "Retry StreamKit controls";
        else if (!_streamActive) MainActionButton.Content = GetActionLabel();
        Cursor = _busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
        ApplyQuickLaunchLayout();
        RefreshQuickLaunch();
    }

    private void UpdateGameCard() =>
        GameLayoutText.Text = SelectedGame?.LayoutLabel ?? "Open a game, then press Find games";

    private void UpdateAvatarCard()
    {
        AvatarDetailText.Text = SelectedAvatarChoice?.Detail ?? "Face-tracked Live2D avatar · recommended";
        if (_selectedAvatar != AvatarMode.VTubeStudio) AvatarSetupPanel.Visibility = Visibility.Collapsed;
    }

    private bool HasReturningUserState()
    {
        if (File.Exists(QuickLaunchMarkerFile)) return true;
        try
        {
            var rememberedGame = _catalog.GetLastSelectedProcess();
            return !string.IsNullOrWhiteSpace(rememberedGame) && !string.IsNullOrWhiteSpace(AppPaths.FindObsExe());
        }
        catch { return false; }
    }

    private void MarkReturningUser()
    {
        if (_returningUser) return;
        try { AtomicFile.WriteAllText(QuickLaunchMarkerFile, "Returning-user Quick Launch enabled after a successful StreamKit start."); }
        catch { }
        _returningUser = true;
        _quickMode = true;
    }

    private void ApplyQuickLaunchLayout()
    {
        var showQuick = _returningUser && _quickMode && !_streamActive && !_recoveryPending;
        var showSetup = !_streamActive && !_recoveryPending && (!_returningUser || !_quickMode);
        QuickLaunchPanel.Visibility = showQuick ? Visibility.Visible : Visibility.Collapsed;
        SetupSectionLabel.Visibility = showSetup ? Visibility.Visible : Visibility.Collapsed;
        SetupGrid.Visibility = showSetup ? Visibility.Visible : Visibility.Collapsed;
        QuickLaunchReturnButton.Visibility = _returningUser && !_quickMode && !_streamActive && !_recoveryPending
            ? Visibility.Visible
            : Visibility.Collapsed;

        HeaderSubtitle.Text = _recoveryPending
            ? "OBS is open. StreamKit will reconnect to the same session instead of restarting it."
            : showQuick
                ? "Your saved setup is ready in one click."
                : _returningUser
                    ? "Adjust anything, then return to Quick Launch when ready."
                    : "Three steps. No streaming software knowledge needed.";
    }

    private void RefreshQuickLaunch()
    {
        if (!_returningUser) return;
        var destination = _selectedMode == StreamMode.DiscordOnly
            ? "Discord"
            : _lastAitum.TikTokOutputConfigured ? "Discord + Twitch + TikTok" : "Discord + Twitch";
        var status = _recoveryPending
            ? "OBS is already open. Retry StreamKit controls."
            : _lastDetection?.GameRunning == true
                ? "Saved setup is ready."
                : SelectedGame is null ? "Choose a game in Customize setup." : $"Open {SelectedGame.DisplayName} to start.";
        QuickLaunchPanel.SetState(
            SelectedGame?.DisplayName ?? "Choose a game",
            status,
            SelectedAvatarChoice?.DisplayName ?? "Full VTuber",
            SelectedThemeChoice?.DisplayName ?? "Sakura",
            destination,
            _recoveryPending ? "Retry StreamKit controls" : GetActionLabel(),
            _selectedMode == StreamMode.AllPlatforms && !_lastAitum.TikTokOutputConfigured
                ? "TikTok is optional. Your saved Twitch + Discord setup can start now."
                : ActionHint.Text,
            !_busy && !_streamActive && !_recoveryPending);
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
            AtomicFile.WriteAllText(ThemePreferenceFile, _selectedTheme == StreamTheme.ProfileB ? "B" : "A");
            AtomicFile.WriteAllText(AvatarPreferenceFile, _selectedAvatar switch { AvatarMode.PngAvatar => "png", AvatarMode.None => "none", _ => "vtube" });
            AtomicFile.WriteAllText(ModePreferenceFile, _selectedMode == StreamMode.AllPlatforms ? "all" : "discord");
        }
        catch { }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UpdateUiState();
    }

    private void ShowProblem(string title, string message, string? actionLabel = null, Func<Task>? action = null)
    {
        ProblemTitle.Text = title;
        ProblemText.Text = message;
        _problemAction = action;
        ProblemActionButton.Visibility = action is null ? Visibility.Collapsed : Visibility.Visible;
        if (actionLabel is not null) ProblemActionButton.Content = actionLabel;
        ProblemPanel.Visibility = Visibility.Visible;
    }

    private void ShowProblemForException(string title, Exception ex, Func<Task>? retry = null)
    {
        var text = (ex.Message + " " + ex.InnerException?.Message).ToLowerInvariant();
        if (text.Contains("avatar") || text.Contains("vtube") || text.Contains("spout"))
        {
            ShowProblem(title, FriendlyError(ex), "Avatar help", () =>
            {
                _vTubeStudio.Launch();
                ShowAvatarSetupGuide(force: true);
                return Task.CompletedTask;
            });
            return;
        }
        if (text.Contains("tiktok") || text.Contains("vertical") || text.Contains("aitum"))
        {
            ShowProblem(title, FriendlyError(ex), "Open TikTok setup", () => OpenObsForSetupAsync(needAitum: true));
            return;
        }
        if (retry is not null && LooksLikeStartupDelay(ex))
        {
            ShowProblem(title, FriendlyError(ex), "Retry controls", retry);
            return;
        }
        if (text.Contains("game") || text.Contains("running"))
        {
            ShowProblem(title, FriendlyError(ex), "Find games", async () =>
            {
                await RefreshGameChoicesAsync(preserveSelection: true);
                await RefreshStatusAsync();
            });
            return;
        }
        if (retry is not null)
        {
            ShowProblem(title, FriendlyError(ex), "Retry", retry);
            return;
        }
        ShowProblem(title, FriendlyError(ex), "Fix setup", RepairAsync);
    }

    private void HideProblem()
    {
        _problemAction = null;
        ProblemPanel.Visibility = Visibility.Collapsed;
        ProblemActionButton.Visibility = Visibility.Collapsed;
    }

    private static string FriendlyError(Exception ex)
    {
        var text = (ex.Message + " " + ex.InnerException?.Message).ToLowerInvariant();
        if (text.Contains("avatar") || text.Contains("vtube") || text.Contains("spout"))
            return "The avatar is not reaching StreamKit yet. Keep VTube Studio open; this no longer needs to stop an otherwise working OBS session.";
        if (text.Contains("not ready") || text.Contains("starting") || text.Contains("websocket") || text.Contains("connection") || text.Contains("timeout") || text.Contains("not responding"))
            return "StreamKit controls took longer than expected to connect. If OBS is already open, leave it open and retry the controls; StreamKit will reuse the same process.";
        if (text.Contains("vertical") || text.Contains("tiktok") || text.Contains("aitum"))
            return "The optional TikTok vertical output is not ready. Twitch + Discord remain usable; open TikTok setup when you want to fix it.";
        if (text.Contains("game") || text.Contains("running"))
            return "Open the game first, press Find games, select it, then try again.";
        if (text.Contains("twitch") || text.Contains("stream service") || text.Contains("service"))
            return "Twitch is not connected in portable OBS yet. Open the streaming engine and connect Twitch under Settings → Stream.";
        return "StreamKit kept your local settings safe. Retry, or use Fix setup if the same problem continues.";
    }

    private static string MakeProgressFriendly(string message)
    {
        var lower = message.ToLowerInvariant();
        if (lower.Contains("spout")) return "Preparing transparent avatar capture…";
        if (lower.Contains("aitum")) return "Preparing optional TikTok support…";
        if (lower.Contains("obs")) return "Preparing the streaming engine…";
        if (lower.Contains("flood")) return "Preparing the talking avatar fallback…";
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
    private async void StartingSoon_Click(object sender, RoutedEventArgs e) => await SwitchSceneAsync("Starting Soon");
    private async void Brb_Click(object sender, RoutedEventArgs e) => await SwitchSceneAsync("BRB");
    private async void GameClean_Click(object sender, RoutedEventArgs e) => await SwitchSceneAsync("Game Clean");
    private async void Bpsr_Click(object sender, RoutedEventArgs e) => await SwitchSceneAsync("BPSR");
    private async void StopStream_Click(object sender, RoutedEventArgs e) => await StopAsync();
    private async void CheckTikTok_Click(object sender, RoutedEventArgs e) => await CheckTikTokAsync();
    private async void CheckAvatar_Click(object sender, RoutedEventArgs e) => await CheckAvatarAsync();
    private async void OpenStreamingSetup_Click(object sender, RoutedEventArgs e) => await OpenObsForSetupAsync(needAitum: true);
    private async void OpenObs_Click(object sender, RoutedEventArgs e) => await OpenObsForSetupAsync(needAitum: false);
    private async void Repair_Click(object sender, RoutedEventArgs e) => await RepairAsync();

    private async void MicMute_Click(object sender, RoutedEventArgs e)
    {
        if (!_streamActive || _selectedMode != StreamMode.AllPlatforms || _busy) return;
        try
        {
            SetBusy(true);
            _micMuted = await _automation.ToggleMicMutedAsync();
            FooterStatus.Text = _micMuted ? "Public-stream microphone muted" : "Public-stream microphone live · noise cleanup on";
        }
        catch (Exception ex) { ShowProblemForException("Couldn’t change the microphone", ex); }
        finally
        {
            SetBusy(false);
            UpdateUiState();
        }
    }

    private async void DiscordOnlySegment_Click(object sender, RoutedEventArgs e)
    {
        if (_streamActive || _recoveryPending || _busy) return;
        _selectedMode = StreamMode.DiscordOnly;
        SavePreferences();
        ApplyModeSelection();
        await RefreshStatusAsync();
    }

    private async void AllPlatformsSegment_Click(object sender, RoutedEventArgs e)
    {
        if (_streamActive || _recoveryPending || _busy) return;
        _selectedMode = StreamMode.AllPlatforms;
        SavePreferences();
        _lastAitum = _aitumState.Read();
        ApplyModeSelection();
        await RefreshStatusAsync();
    }

    private async void AvatarCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingAvatar || _streamActive || _recoveryPending || _busy || SelectedAvatarChoice is not { } choice) return;
        _selectedAvatar = choice.Mode;
        SavePreferences();
        UpdateAvatarCard();
        ApplyModeSelection();
        await RefreshStatusAsync();
    }

    private async void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingTheme || _streamActive || _recoveryPending || _busy || SelectedThemeChoice is not { } choice) return;
        _selectedTheme = choice.Theme;
        SavePreferences();
        await RefreshStatusAsync();
    }

    private async void GameCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingGames || _streamActive || _recoveryPending || _busy) return;
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
        if (_busy || _streamActive || _recoveryPending) return;
        await RefreshGameChoicesAsync(preserveSelection: true);
        await RefreshStatusAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!_streamActive && !_recoveryPending) await RefreshGameChoicesAsync(preserveSelection: true);
        await RefreshStatusAsync();
    }

    private void AvatarHelp_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAvatar != AvatarMode.VTubeStudio)
        {
            ShowProblem("Avatar help", "Choose Full VTuber if you want the VTube Studio setup guide.");
            return;
        }
        _vTubeStudio.Launch();
        ShowAvatarSetupGuide(force: true);
    }

    private void OpenVTubeStudio_Click(object sender, RoutedEventArgs e) => _vTubeStudio.Launch();

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        AdvancedPanel.Visibility = AdvancedPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    private void QuickLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (_recoveryPending) return;
        _quickMode = true;
        ApplyQuickLaunchLayout();
    }

    private void ShowTikTokSetup_Click(object sender, RoutedEventArgs e)
    {
        if (_recoveryPending) return;
        PlatformSetupPanel.Visibility = Visibility.Visible;
        TikTokSetupShortcut.Visibility = Visibility.Collapsed;
        FooterStatus.Text = "TikTok is optional · open setup only when you have a server + stream key";
    }

    private async void ProblemAction_Click(object sender, RoutedEventArgs e)
    {
        var action = _problemAction;
        if (action is null || _busy) return;
        HideProblem();
        try { await action(); }
        catch (Exception ex) { ShowProblemForException("That action still needs attention", ex); }
    }

    private void DismissProblem_Click(object sender, RoutedEventArgs e) => HideProblem();

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try { using var process = Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.Root) { UseShellExecute = true }); }
        catch { }
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
