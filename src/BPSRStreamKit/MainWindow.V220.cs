using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BPSRStreamKit.Models;
using BPSRStreamKit.Services;

namespace BPSRStreamKit;

public partial class MainWindow
{
    private readonly SceneLayoutV220Service _sceneLayoutV220 = new();
    private readonly ObsControlV220 _obsControlV220 = new();
    private readonly ObsProcessV220 _obsProcessV220 = new();
    private Button? _bpsrSceneButtonV220;
    private bool _v220UiInitialized;
    private bool _sceneControlsReadyV220;
    private bool _twitchStartedV220;
    private bool _tiktokStartedV220;

    private static readonly bool V220UiHookRegistered = RegisterV220UiHook();

    private static bool RegisterV220UiHook()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(V220WindowLoaded));
        return true;
    }

    private static void V220WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window || window._v220UiInitialized) return;
        window.Dispatcher.BeginInvoke(new Action(window.InitializeV220Ui), DispatcherPriority.ContextIdle);
    }

    private void InitializeV220Ui()
    {
        if (_v220UiInitialized) return;
        _v220UiInitialized = true;

        LiveButton.Content = "Game Clean";
        LiveButton.ToolTip = "Show the game, frame and avatar without DPS/mechanic HUD panels.";
        StartingSoonButton.ToolTip = "Show Starting Soon on Discord and every connected public output.";
        BrbButton.ToolTip = "Show BRB on Discord and every connected public output.";

        if (StartingSoonButton.Parent is UniformGrid sceneGrid)
        {
            sceneGrid.Children.Remove(StartingSoonButton);
            sceneGrid.Children.Remove(LiveButton);
            sceneGrid.Children.Remove(BrbButton);
            sceneGrid.Children.Remove(MicMuteButton);
            sceneGrid.Columns = 4;
            sceneGrid.Children.Add(StartingSoonButton);
            sceneGrid.Children.Add(BrbButton);
            sceneGrid.Children.Add(LiveButton);

            _bpsrSceneButtonV220 = new Button
            {
                Content = "BPSR",
                Height = 40,
                MinWidth = 0,
                Margin = new Thickness(3),
                Padding = new Thickness(6, 0, 6, 0),
                ToolTip = "Show the game + avatar with the DPS meter and dungeon mechanic HUD."
            };
            _bpsrSceneButtonV220.Style = (Style)FindResource("SecondaryButton");
            sceneGrid.Children.Add(_bpsrSceneButtonV220);
        }

        if (StopStreamButton.Parent is Grid bottomGrid)
        {
            if (bottomGrid.ColumnDefinitions.Count < 3)
                bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (MicMuteButton.Parent is Panel oldParent) oldParent.Children.Remove(MicMuteButton);
            MicMuteButton.Height = 36;
            MicMuteButton.MinWidth = 104;
            MicMuteButton.Margin = new Thickness(0, 0, 6, 0);
            MicMuteButton.Padding = new Thickness(10, 0, 10, 0);
            MicMuteButton.FontSize = 11;
            Grid.SetColumn(MicMuteButton, 1);
            Grid.SetColumn(StopStreamButton, 2);
            bottomGrid.Children.Add(MicMuteButton);
        }

        var controlsTitle = DescendantsV220<TextBlock>(StreamControlsPanel)
            .FirstOrDefault(x => string.Equals(x.Text, "LIVE CONTROLS", StringComparison.Ordinal));
        if (controlsTitle is not null) controlsTitle.Text = "SCENE CONTROLS";

        var controlsHint = DescendantsV220<TextBlock>(StreamControlsPanel)
            .FirstOrDefault(x => x.Text?.Contains("leave the streaming engine minimized", StringComparison.OrdinalIgnoreCase) == true);
        if (controlsHint is not null)
            controlsHint.Text = "These four scenes stay in sync across Discord and every connected public output.";

        RewritePlatformSetupCopyV220();
        UpdateV220ControlsUi();
    }

    private void RewritePlatformSetupCopyV220()
    {
        var textBlocks = DescendantsV220<TextBlock>(PlatformSetupPanel).ToList();
        var eyebrow = textBlocks.FirstOrDefault(x => x.Text?.Contains("ONE-TIME TWITCH + TIKTOK", StringComparison.OrdinalIgnoreCase) == true);
        if (eyebrow is not null) eyebrow.Text = "TWITCH + OPTIONAL TIKTOK SETUP";

        var title = textBlocks.FirstOrDefault(x => x.Text == "Connect your two streaming accounts once");
        if (title is not null) title.Text = "Connect Twitch now, add TikTok whenever you want";

        var subtitle = textBlocks.FirstOrDefault(x => x.Text?.StartsWith("You do not need to understand", StringComparison.Ordinal) == true);
        if (subtitle is not null)
            subtitle.Text = "Twitch + Discord can continue even when you do not have a TikTok stream key yet.";

        var twitch = textBlocks.FirstOrDefault(x => x.Text?.StartsWith("Twitch", StringComparison.Ordinal) == true);
        if (twitch is not null)
            twitch.Text = "Twitch   Settings → Stream → Service: Twitch → Connect Account. This is only needed if you want Twitch.";

        var tiktok = textBlocks.FirstOrDefault(x => x.Text?.StartsWith("TikTok", StringComparison.Ordinal) == true);
        if (tiktok is not null)
            tiktok.Text = "TikTok   When your account gets a stream key: Outputs → Add Stream → choose Vertical → paste the server/key.";

        var after = textBlocks.FirstOrDefault(x => x.Text?.StartsWith("After this first setup", StringComparison.Ordinal) == true);
        if (after is not null)
            after.Text = "No TikTok key? That is fine. StreamKit keeps Discord/Twitch usable and you can come back here later.";

        var finishButton = DescendantsV220<Button>(PlatformSetupPanel)
            .FirstOrDefault(x => string.Equals(x.Content?.ToString(), "I’m finished", StringComparison.Ordinal)
                              || string.Equals(x.Content?.ToString(), "I'm finished", StringComparison.Ordinal));
        if (finishButton is not null) finishButton.Content = "Check TikTok";

        var footer = textBlocks.FirstOrDefault(x => x.Text?.Contains("close the setup window", StringComparison.OrdinalIgnoreCase) == true);
        if (footer is not null) footer.Text = "Missing TikTok never blocks Discord or Twitch. StreamKit only checks whether a key/output exists.";
    }

    private async Task StartV220Async()
    {
        if (_busy) return;

        if (_streamActive)
        {
            try
            {
                SetBusy(true);
                await RunEngineStepWithRetryAsync(() => _automation.OpenProgramProjectorAsync(), "Reopening your Discord share window…", 5);
                MinimizePortableObsWindow();
                FooterStatus.Text = "Discord share window reopened";
            }
            catch (Exception ex) { ShowProblem("Couldn’t reopen the share window", FriendlyError(ex)); }
            finally
            {
                SetBusy(false);
                UpdateV220ControlsUi();
            }
            return;
        }

        try
        {
            HideProblem();
            _platformSetupNeeded = false;
            SetBusy(true);
            UpdateV220ControlsUi();
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
            _sceneLayoutV220.PrepareBaseScenes(_selectedAvatar);

            VTubeCaptureTarget? vTubeTarget = null;
            if (_selectedAvatar == AvatarMode.VTubeStudio)
            {
                SetupProgressPanel.Visibility = Visibility.Visible;
                SetupProgress.Value = 100;
                SetupStatusText.Text = "Waiting for VTube Studio to finish opening…";
                vTubeTarget = await _vTubeStudio.LaunchAndWaitAsync(TimeSpan.FromSeconds(60));
                ShowAvatarSetupGuide(force: !File.Exists(AvatarVerifiedFile));
            }

            state = await _detection.DetectAsync(game);
            if (!state.GameRunning)
                throw new InvalidOperationException($"{game.DisplayName} is not running. Open it, then press Find games.");

            _catalog.Save(game);
            _catalog.SaveLastSelectedProcess(game.ProcessName);
            AudioPrivacyService.HardenPortableObsConfig();

            if (_obsProcessV220.IsRunning())
            {
                SetupStatusText.Text = "Closing the previous StreamKit preview safely…";
                await _obsProcessV220.CloseGracefullyAsync();
                await Task.Delay(500);
            }

            if (_selectedMode == StreamMode.DiscordOnly)
            {
                _sceneLayoutV220.PrepareBaseScenes(_selectedAvatar);
                _obs.Launch(StreamMode.DiscordOnly, game, _selectedTheme, _selectedAvatar, vTubeTarget);
                await WaitForStreamingEngineAsync(TimeSpan.FromSeconds(45));
                _sceneControlsReadyV220 = true;
                UpdateV220ControlsUi();

                await RunEngineStepWithRetryAsync(
                    () => _automation.ConfigureDiscordShareAudioAsync(alsoStreamingPlatforms: false),
                    "Protecting your Discord audio…");

                if (!await CheckAvatarConnectionAsync(TimeSpan.FromSeconds(55)))
                    throw new InvalidOperationException("Your avatar is still loading or not connected yet. Leave VTube Studio open, then try Check my avatar again.");

                await RunEngineStepWithRetryAsync(() => _automation.SetCurrentSceneAsync("Game Clean"), "Opening Game Clean…", 5);
                await SyncAvatarTransformsV220Async(includeVertical: false);
                await RunEngineStepWithRetryAsync(() => _automation.OpenProgramProjectorAsync(), "Opening your Discord share window…", 5);
                MinimizePortableObsWindow();

                _micMuted = true;
                _streamActive = true;
                _twitchStartedV220 = false;
                _tiktokStartedV220 = false;
                UpdateStreamControls();
                UpdateV220ControlsUi();
                SetupProgressPanel.Visibility = Visibility.Collapsed;
                PlatformSetupPanel.Visibility = Visibility.Collapsed;
                FooterStatus.Text = "Discord ready · Game Clean · use the four scene buttons anytime";
                return;
            }

            _setup.EnsureAitumProfileConfig();
            _sceneLayoutV220.PrepareBaseScenes(_selectedAvatar);
            var verticalUuid = _setup.GetVerticalCanvasUuid();
            if (string.IsNullOrWhiteSpace(verticalUuid))
            {
                SetupProgressPanel.Visibility = Visibility.Visible;
                SetupProgress.Value = 100;
                SetupStatusText.Text = "Preparing the phone-shaped TikTok layout…";
                _obs.LaunchAitumBootstrap(game, _selectedTheme, _selectedAvatar, vTubeTarget);
                await WaitForStreamingEngineAsync(TimeSpan.FromSeconds(50));
                verticalUuid = await WaitForVerticalCanvasAsync(TimeSpan.FromSeconds(30));
                if (string.IsNullOrWhiteSpace(verticalUuid))
                    throw new InvalidOperationException("TikTok’s vertical canvas could not be prepared. Use Fix setup once and retry.");
                await _obsProcessV220.CloseGracefullyAsync();
                await Task.Delay(700);
            }

            _sceneLayoutV220.PrepareBaseScenes(_selectedAvatar);
            _obs.PrepareAllPlatforms(verticalUuid, game, _selectedTheme, _selectedAvatar, vTubeTarget);
            _sceneLayoutV220.FinalizeVerticalBpsrScene(verticalUuid);
            AudioPrivacyService.HardenPortableObsConfig();
            _obs.Launch(StreamMode.AllPlatforms, game, _selectedTheme, _selectedAvatar, vTubeTarget);
            await WaitForStreamingEngineAsync(TimeSpan.FromSeconds(50));
            _sceneControlsReadyV220 = true;
            UpdateV220ControlsUi();

            await RunEngineStepWithRetryAsync(
                () => _automation.ConfigureDiscordShareAudioAsync(alsoStreamingPlatforms: true),
                "Protecting your public-stream audio…");
            await TryEngineStepAsync(() => _automation.StartVirtualCameraAsync(), "Preparing the optional Discord camera…");

            if (!await CheckAvatarConnectionAsync(TimeSpan.FromSeconds(55)))
                throw new InvalidOperationException("Your avatar is still loading or not connected yet. Leave VTube Studio open, then retry.");

            await SyncAvatarTransformsV220Async(includeVertical: true);
            await RunEngineStepWithRetryAsync(
                () => _automation.SwitchScenesAsync("Starting Soon", "Vertical Starting Soon"),
                "Preparing Starting Soon everywhere…", 5);

            var hasTikTok = _setup.HasAitumStreamOutput();
            _twitchStartedV220 = false;
            _tiktokStartedV220 = false;
            Exception? publicStartError = null;

            if (hasTikTok)
            {
                try
                {
                    await RunEngineStepWithRetryAsync(() => _automation.StartAllStreamsAsync(), "Starting Twitch + TikTok…", 4);
                    _twitchStartedV220 = true;
                    _tiktokStartedV220 = true;
                }
                catch (Exception ex)
                {
                    publicStartError = ex;
                    try
                    {
                        await _obsControlV220.StartMainStreamAsync();
                        _twitchStartedV220 = true;
                    }
                    catch (Exception twitchEx) { publicStartError = twitchEx; }
                }
            }
            else
            {
                try
                {
                    SetupStatusText.Text = "TikTok is not connected — starting Twitch only…";
                    await _obsControlV220.StartMainStreamAsync();
                    _twitchStartedV220 = true;
                }
                catch (Exception ex) { publicStartError = ex; }
            }

            await RunEngineStepWithRetryAsync(() => _automation.OpenProgramProjectorAsync(), "Opening your Discord share window…", 5);
            MinimizePortableObsWindow();

            _micMuted = false;
            _streamActive = true;
            UpdateStreamControls();
            UpdateV220ControlsUi();
            SetupProgressPanel.Visibility = Visibility.Collapsed;

            if (!hasTikTok)
            {
                PlatformSetupPanel.Visibility = Visibility.Visible;
                _platformSetupNeeded = false;
            }
            else
            {
                PlatformSetupPanel.Visibility = Visibility.Collapsed;
            }

            if (!_twitchStartedV220)
            {
                ShowProblem(
                    "Twitch is not connected yet",
                    "Discord is ready and scene controls work. Open account setup to connect Twitch. TikTok can still be added later when you receive a stream key.");
                FooterStatus.Text = "Discord ready · Twitch not connected · TikTok optional";
            }
            else if (!_tiktokStartedV220)
            {
                HideProblem();
                FooterStatus.Text = "Live · Starting Soon · Discord + Twitch · TikTok can be added later";
            }
            else
            {
                HideProblem();
                FooterStatus.Text = "Live · Starting Soon · Discord + Twitch + TikTok";
            }

            _ = publicStartError;
        }
        catch (Exception ex)
        {
            SetupProgressPanel.Visibility = Visibility.Collapsed;
            ShowProblem("StreamKit couldn’t finish automatically", FriendlyError(ex));
            FooterStatus.Text = "Needs attention · StreamKit did not launch another duplicate app";
        }
        finally
        {
            SetupProgressPanel.Visibility = Visibility.Collapsed;
            if (!_obsProcessV220.IsRunning() && !_streamActive) _sceneControlsReadyV220 = false;
            SetBusy(false);
            UpdateV220ControlsUi();
            try { await RefreshStatusAsync(); } catch { }
        }
    }

    private async Task SwitchSceneV220Async(string scene)
    {
        if (_busy || (!_streamActive && !_sceneControlsReadyV220)) return;
        try
        {
            SetBusy(true);
            UpdateV220ControlsUi();
            var includeVertical = _selectedMode == StreamMode.AllPlatforms;
            switch (scene)
            {
                case "Starting Soon":
                    await RunEngineStepWithRetryAsync(
                        () => _automation.SwitchScenesAsync("Starting Soon", includeVertical ? "Vertical Starting Soon" : null),
                        "Switching to Starting Soon…", 5);
                    break;
                case "BRB":
                    await RunEngineStepWithRetryAsync(
                        () => _automation.SwitchScenesAsync("BRB", includeVertical ? "Vertical BRB" : null),
                        "Switching to BRB…", 5);
                    break;
                case "BPSR":
                    await RunEngineStepWithRetryAsync(
                        () => _automation.SwitchScenesAsync("BPSR", includeVertical ? "Vertical BPSR" : null),
                        "Switching to BPSR HUD…", 5);
                    break;
                default:
                    await RunEngineStepWithRetryAsync(
                        () => _automation.SwitchScenesAsync("Game Clean", includeVertical ? "Vertical Live" : null),
                        "Switching to Game Clean…", 5);
                    scene = "Game Clean";
                    break;
            }

            SetupProgressPanel.Visibility = Visibility.Collapsed;
            FooterStatus.Text = _streamActive ? $"Live · {scene} · all active views switched" : $"Preview · {scene}";
        }
        catch (Exception ex) { ShowProblem("Couldn’t change the scene", FriendlyError(ex)); }
        finally
        {
            SetBusy(false);
            UpdateV220ControlsUi();
        }
    }

    private async Task StopV220Async()
    {
        if (_busy) return;
        try
        {
            SetBusy(true);
            UpdateV220ControlsUi();
            try { await _automation.StopAllStreamsAsync(); } catch { }
            try { await _obsControlV220.StopMainStreamAsync(); } catch { }
            try { await _automation.StopVirtualCameraAsync(); } catch { }
            try { await _automation.RestoreNormalAudioMonitoringAsync(); } catch { }
            FooterStatus.Text = "Stopping safely · waiting for OBS to save its state…";
            await _obsProcessV220.CloseGracefullyAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            _streamActive = false;
            _sceneControlsReadyV220 = false;
            _twitchStartedV220 = false;
            _tiktokStartedV220 = false;
            _micMuted = false;
            PlatformSetupPanel.Visibility = Visibility.Collapsed;
            UpdateStreamControls();
            FooterStatus.Text = "Stopped cleanly · avatar app left open for next time";
            SetBusy(false);
            UpdateV220ControlsUi();
            try { await RefreshStatusAsync(); } catch { }
        }
    }

    private async Task CheckTikTokV220Async()
    {
        if (_busy) return;
        try
        {
            SetBusy(true);
            FooterStatus.Text = "Checking for a TikTok Vertical output…";
            await Task.Delay(350);
            if (_setup.HasAitumStreamOutput())
            {
                _platformSetupNeeded = false;
                PlatformSetupPanel.Visibility = Visibility.Collapsed;
                HideProblem();
                FooterStatus.Text = _streamActive
                    ? "TikTok saved · it will join automatically the next time you start all platforms"
                    : "TikTok saved · ready for the next all-platform start";
            }
            else
            {
                _platformSetupNeeded = false;
                PlatformSetupPanel.Visibility = Visibility.Visible;
                HideProblem();
                FooterStatus.Text = "TikTok not connected yet — that’s okay; Discord/Twitch remain usable";
                if (_obsProcessV220.IsRunning()) RestorePortableObsWindow();
            }
        }
        catch (Exception ex)
        {
            _platformSetupNeeded = false;
            ShowProblem("Couldn’t check TikTok yet", FriendlyError(ex));
        }
        finally
        {
            SetBusy(false);
            UpdateV220ControlsUi();
            try { await RefreshStatusAsync(); } catch { }
        }
    }

    private async Task OpenObsSetupV220Async()
    {
        if (_busy) return;
        try
        {
            SetBusy(true);
            if (_obsProcessV220.IsRunning())
            {
                RestorePortableObsWindow();
                FooterStatus.Text = "Streaming engine is already open · reused the existing window";
                return;
            }

            var state = await _detection.DetectAsync(SelectedGame);
            if (!state.ObsReady)
                await _setup.EnsureReadyAsync(needAitum: true, needSpout: _selectedAvatar == AvatarMode.VTubeStudio);
            _sceneLayoutV220.PrepareBaseScenes(_selectedAvatar);
            _obs.Launch(StreamMode.PlainObs, null, _selectedTheme, _selectedAvatar);
            if (await _automation.WaitUntilReadyAsync(TimeSpan.FromSeconds(45)))
                _sceneControlsReadyV220 = true;
            RestorePortableObsWindow();
            FooterStatus.Text = "Account setup opened once · StreamKit will wait instead of launching duplicates";
        }
        catch (Exception ex) { ShowProblem("Couldn’t open account setup", FriendlyError(ex)); }
        finally
        {
            SetBusy(false);
            UpdateV220ControlsUi();
        }
    }

    private async Task CheckAvatarV220Async()
    {
        if (_busy || _selectedAvatar != AvatarMode.VTubeStudio) return;
        if (_streamActive)
        {
            ShowProblem("Avatar check is unavailable while live", "Stop the current share/stream before running the avatar test.");
            return;
        }

        try
        {
            HideProblem();
            SetBusy(true);
            await _setup.EnsureReadyAsync(needSpout: true);
            _sceneLayoutV220.PrepareBaseScenes(_selectedAvatar);
            await _vTubeStudio.LaunchAndWaitAsync(TimeSpan.FromSeconds(60));
            if (_obsProcessV220.IsRunning()) await _obsProcessV220.CloseGracefullyAsync();

            _obs.LaunchAvatarPreview(_selectedTheme);
            await WaitForStreamingEngineAsync(TimeSpan.FromSeconds(45));
            _sceneControlsReadyV220 = true;
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
            ShowProblem("Avatar not connected yet", FriendlyError(ex));
        }
        finally
        {
            await _obsProcessV220.CloseGracefullyAsync();
            _sceneControlsReadyV220 = false;
            SetupProgressPanel.Visibility = Visibility.Collapsed;
            SetBusy(false);
            UpdateV220ControlsUi();
            try { await RefreshStatusAsync(); } catch { }
        }
    }

    private async Task SyncAvatarTransformsV220Async(bool includeVertical)
    {
        if (_selectedAvatar != AvatarMode.VTubeStudio) return;
        await _obsControlV220.SyncSceneItemTransformAsync("Game Clean", "BPSR", "VTube Studio Avatar");
        if (includeVertical)
            await _obsControlV220.SyncSceneItemTransformAsync("Vertical Live", "Vertical BPSR", "Vertical - VTube Studio Avatar");
    }

    private void UpdateV220ControlsUi()
    {
        if (!_v220UiInitialized) return;
        var sceneReady = _sceneControlsReadyV220 || _streamActive;
        StreamControlsPanel.Visibility = sceneReady ? Visibility.Visible : Visibility.Collapsed;
        StartingSoonButton.IsEnabled = sceneReady && !_busy;
        BrbButton.IsEnabled = sceneReady && !_busy;
        LiveButton.IsEnabled = sceneReady && !_busy;
        if (_bpsrSceneButtonV220 is not null) _bpsrSceneButtonV220.IsEnabled = sceneReady && !_busy;
        MicMuteButton.IsEnabled = _streamActive && !_busy && _selectedMode == StreamMode.AllPlatforms;
        StopStreamButton.IsEnabled = sceneReady && !_busy;
    }

    private static IEnumerable<T> DescendantsV220<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in DescendantsV220<T>(child)) yield return nested;
        }
    }
}
