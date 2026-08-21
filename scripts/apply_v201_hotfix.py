from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return (ROOT / path).read_text(encoding='utf-8')


def write(path, text):
    (ROOT / path).write_text(text, encoding='utf-8')


def replace_once(text, old, new, label):
    if old not in text:
        raise SystemExit(f'missing expected pattern: {label}')
    return text.replace(old, new, 1)

# 1) Fix beginner ComboBoxes showing record/debug strings instead of friendly names.
p = 'src/BPSRStreamKit/App.xaml'
s = read(p)
s = replace_once(s,
'''                                    <ContentPresenter Margin="{TemplateBinding Padding}"
                                                      VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                                                      HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                                      Content="{TemplateBinding SelectionBoxItem}"
                                                      ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                                                      ContentStringFormat="{TemplateBinding SelectionBoxItemStringFormat}"/>''',
'''                                    <TextBlock Margin="{TemplateBinding Padding}"
                                               VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                                               Foreground="{TemplateBinding Foreground}"
                                               Text="{Binding SelectedItem.DisplayName, RelativeSource={RelativeSource TemplatedParent}}"
                                               TextTrimming="CharacterEllipsis"/>''',
'ComboBox selected item display')
s = replace_once(s,
'''                        <Border x:Name="Root" Background="{TemplateBinding Background}" CornerRadius="8" Padding="{TemplateBinding Padding}">
                            <ContentPresenter/>
                        </Border>''',
'''                        <Border x:Name="Root" Background="{TemplateBinding Background}" CornerRadius="8" Padding="{TemplateBinding Padding}">
                            <TextBlock Text="{Binding Content.DisplayName, RelativeSource={RelativeSource TemplatedParent}}"
                                       Foreground="{TemplateBinding Foreground}" TextTrimming="CharacterEllipsis"/>
                        </Border>''',
'ComboBox dropdown item display')
write(p, s)

# 2) Repair/migrate old portable OBS scene paths when the ZIP is extracted to a new folder.
p = 'src/BPSRStreamKit/Services/SetupService.cs'
s = read(p)
s = replace_once(s,
'''        EnsureCleanGameScenes(configRoot, useSpout);
        var userIni = Path.Combine(configRoot, "user.ini");''',
'''        EnsureCleanGameScenes(configRoot, useSpout);
        RebasePortableSceneAssets(configRoot);
        var userIni = Path.Combine(configRoot, "user.ini");''',
'rebase call')
insert_before = '''    private static void EnsureObsUpdatePolicy()\n'''
helper = r'''    private static void RebasePortableSceneAssets(string configRoot)
    {
        var sceneRoot = Path.Combine(configRoot, "basic", "scenes");
        RebaseSceneAssets(Path.Combine(sceneRoot, "BPSR_Horizontal.json"), vertical: false);
        RebaseSceneAssets(Path.Combine(sceneRoot, "BPSR_TikTok_Vertical.json"), vertical: true);
    }

    private static void RebaseSceneAssets(string file, bool vertical)
    {
        if (!File.Exists(file)) return;
        JsonObject? root;
        try { root = JsonNode.Parse(File.ReadAllText(file))?.AsObject(); }
        catch { return; }
        var sources = root?["sources"]?.AsArray();
        if (root is null || sources is null) return;

        static string ObsPath(string path) => path.Replace('\\', '/');
        var frameName = vertical ? "TikTok Minimal Frame" : "Minimal Stream Frame";
        var existingFrame = FindSource(sources, frameName)?["settings"]?["file"]?.GetValue<string>() ?? string.Empty;
        var useDoctorTheme = existingFrame.Contains("Profile_B_Doctor", StringComparison.OrdinalIgnoreCase);

        string avatarDirectory;
        string frameFile;
        string startingFile;
        string brbFile;
        string verticalFrameFile;
        string verticalStartingFile;
        string verticalBrbFile;

        if (useDoctorTheme)
        {
            var themeRoot = Path.Combine(AppPaths.AssetsDirectory, "Themes", "Profile_B_Doctor");
            avatarDirectory = Path.Combine(themeRoot, "Avatar");
            frameFile = Path.Combine(themeRoot, "Frames", vertical ? "TikTok_1080x1920.png" : "Discord_1080p.png");
            startingFile = Path.Combine(themeRoot, "Screens", vertical ? "Starting_TikTok_1080x1920.jpg" : "Starting_1080p.jpg");
            brbFile = Path.Combine(themeRoot, "Screens", vertical ? "BRB_TikTok_1080x1920.jpg" : "BRB_1080p.jpg");
            verticalFrameFile = Path.Combine(themeRoot, "Frames", "TikTok_1080x1920.png");
            verticalStartingFile = Path.Combine(themeRoot, "Screens", "Starting_TikTok_1080x1920.jpg");
            verticalBrbFile = Path.Combine(themeRoot, "Screens", "BRB_TikTok_1080x1920.jpg");
        }
        else
        {
            avatarDirectory = Path.Combine(AppPaths.AssetsDirectory, "MyAvatar");
            frameFile = Path.Combine(AppPaths.AssetsDirectory, "Frames", vertical ? "05_TikTok_Minimal_1080x1920.png" : "01_Minimal_Thin_1080p.png");
            startingFile = Path.Combine(AppPaths.AssetsDirectory, "Screens", vertical ? "Starting_TikTok_1080x1920.png" : "Starting_1080p.png");
            brbFile = Path.Combine(AppPaths.AssetsDirectory, "Screens", vertical ? "BRB_TikTok_1080x1920.png" : "BRB_1080p.png");
            verticalFrameFile = Path.Combine(AppPaths.AssetsDirectory, "Frames", "05_TikTok_Minimal_1080x1920.png");
            verticalStartingFile = Path.Combine(AppPaths.AssetsDirectory, "Screens", "Starting_TikTok_1080x1920.png");
            verticalBrbFile = Path.Combine(AppPaths.AssetsDirectory, "Screens", "BRB_TikTok_1080x1920.png");
        }

        void SetImage(string name, string path)
        {
            var source = FindSource(sources, name);
            if (source is null) return;
            var settings = source["settings"]?.AsObject() ?? new JsonObject();
            source["settings"] = settings;
            settings["file"] = ObsPath(path);
        }

        void SetAvatar(string name)
        {
            var avatar = FindSource(sources, name);
            if (avatar is null) return;
            var settings = avatar["settings"]?.AsObject() ?? new JsonObject();
            avatar["settings"] = settings;
            settings["path_idle"] = ObsPath(Path.Combine(avatarDirectory, "idle.png"));
            settings["path_blink"] = ObsPath(Path.Combine(avatarDirectory, "blink.png"));
            settings["path_action"] = ObsPath(Path.Combine(avatarDirectory, "action.png"));
            settings["path_talk_1"] = ObsPath(Path.Combine(avatarDirectory, "talk_a.png"));
            settings["path_talk_2"] = ObsPath(Path.Combine(avatarDirectory, "talk_a.png"));
            settings["path_talk_3"] = ObsPath(Path.Combine(avatarDirectory, "talk_a.png"));
            settings["custom_avatars_path"] = ObsPath(avatarDirectory);
        }

        SetImage(frameName, frameFile);
        SetImage("Starting Screen", startingFile);
        SetImage("BRB Screen", brbFile);
        SetAvatar("FloodTuber Avatar");

        // Existing all-platform collections can contain imported vertical sources too.
        if (!vertical)
        {
            SetImage("Vertical - Stream Frame", verticalFrameFile);
            SetImage("Vertical - Starting Screen", verticalStartingFile);
            SetImage("Vertical - BRB Screen", verticalBrbFile);
            SetAvatar("Vertical - PNG Avatar");
        }

        File.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    }

'''
if insert_before not in s:
    raise SystemExit('missing EnsureObsUpdatePolicy insertion point')
s = s.replace(insert_before, helper + insert_before, 1)
write(p, s)

# 3) VTube Studio: if a stale background process exists but no visible window, invoke Steam again.
p = 'src/BPSRStreamKit/Services/VTubeStudioService.cs'
s = read(p)
s = replace_once(s,
'''    public void Launch()
    {
        if (IsRunning()) return;
        Process.Start(new ProcessStartInfo($"steam://rungameid/{SteamAppId}") { UseShellExecute = true });
    }

    public async Task<VTubeCaptureTarget> LaunchAndWaitAsync(TimeSpan? timeout = null)
    {
        Launch();
        var until = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(35));
        while (DateTime.UtcNow < until)
        {
            var target = TryGetCaptureTarget();
            if (target is not null) return target;
            await Task.Delay(500);
        }
''',
'''    public void Launch()
    {
        if (TryGetCaptureTarget() is not null) return;
        LaunchThroughSteam();
    }

    public async Task<VTubeCaptureTarget> LaunchAndWaitAsync(TimeSpan? timeout = null)
    {
        var existing = TryGetCaptureTarget();
        if (existing is not null) return existing;

        var until = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(45));
        var nextLaunchAttempt = DateTime.MinValue;
        while (DateTime.UtcNow < until)
        {
            if (DateTime.UtcNow >= nextLaunchAttempt)
            {
                LaunchThroughSteam();
                nextLaunchAttempt = DateTime.UtcNow + TimeSpan.FromSeconds(8);
            }

            await Task.Delay(500);
            var target = TryGetCaptureTarget();
            if (target is not null) return target;
        }
''',
'robust VTube launch')
marker = '''    public VTubeCaptureTarget? TryGetCaptureTarget()\n'''
launch_helper = '''    private static void LaunchThroughSteam()\n    {\n        Process.Start(new ProcessStartInfo($"steam://rungameid/{SteamAppId}") { UseShellExecute = true });\n    }\n\n'''
if marker not in s:
    raise SystemExit('missing VTube helper insertion point')
s = s.replace(marker, launch_helper + marker, 1)
write(p, s)

# 4) Beginner runtime recovery: launch avatar immediately, close stale portable OBS before controlled start,
#    and keep error state visible instead of simultaneously claiming READY.
p = 'src/BPSRStreamKit/MainWindow.xaml.cs'
s = read(p)
s = replace_once(s,
'''    private bool _micMuted;
    private bool _platformSetupNeeded;
''',
'''    private bool _micMuted;
    private bool _platformSetupNeeded;
    private bool _hasProblem;
''',
'problem state field')
s = replace_once(s,
'''        SetStatus(AudioStatusDot, AudioStatusText, state.AudioIsolationReady,
            "Sound protection is ready", "Sound protection will be applied automatically");

        if (_platformSetupNeeded)
''',
'''        SetStatus(AudioStatusDot, AudioStatusText, state.AudioIsolationReady,
            "Sound protection is ready", "Sound protection will be applied automatically");

        if (_hasProblem && !_streamActive)
        {
            HeroEyebrow.Text = "NEEDS ATTENTION";
            HeroEyebrow.Foreground = (Brush)FindResource("BadBrush");
            HeroTitle.Text = ProblemTitle.Text;
            HeroSubtitle.Text = ProblemText.Text;
            MainActionButton.Content = "Try again";
            FooterStatus.Text = "One fix needed · your local settings are safe";
            return;
        }

        if (_platformSetupNeeded)
''',
'problem overrides ready')
s = replace_once(s,
'''            var needAitum = _selectedMode == StreamMode.AllPlatforms;
            var needAvatarBridge = _selectedAvatar == AvatarMode.VTubeStudio;
            var showProgress = !state.ObsReady
''',
'''            var needAitum = _selectedMode == StreamMode.AllPlatforms;
            var needAvatarBridge = _selectedAvatar == AvatarMode.VTubeStudio;
            if (needAvatarBridge)
            {
                // Open the avatar app immediately, before any repair/download work can fail.
                _vTubeStudio.Launch();
                ShowAvatarSetupGuide(force: !File.Exists(AvatarVerifiedFile));
            }
            var showProgress = !state.ObsReady
''',
'early avatar launch')
s = replace_once(s,
'''            _catalog.Save(game);
            _catalog.SaveLastSelectedProcess(game.ProcessName);
            AudioPrivacyService.HardenPortableObsConfig();

            if (_selectedMode == StreamMode.DiscordOnly)
''',
'''            _catalog.Save(game);
            _catalog.SaveLastSelectedProcess(game.ProcessName);
            AudioPrivacyService.HardenPortableObsConfig();
            await EnsureControlledObsRestartAsync();

            if (_selectedMode == StreamMode.DiscordOnly)
''',
'close stale OBS before controlled launch')
s = replace_once(s,
'''    private async Task WaitForStreamingEngineAsync(TimeSpan timeout)
    {
''',
'''    private async Task EnsureControlledObsRestartAsync()
    {
        if (!_obs.Stop()) return;
        SetupProgressPanel.Visibility = Visibility.Visible;
        SetupProgress.Value = 100;
        SetupStatusText.Text = "Closing an old streaming window so StreamKit can restart it cleanly…";
        await Task.Delay(1400);
    }

    private async Task WaitForStreamingEngineAsync(TimeSpan timeout)
    {
''',
'controlled restart helper')
s = replace_once(s,
'''            var game = SelectedGame ?? throw new InvalidOperationException("Choose a running game first so StreamKit can open its preview engine.");
            await _setup.EnsureReadyAsync(needSpout: true);
            await _vTubeStudio.LaunchAndWaitAsync();
            _obs.Launch(StreamMode.DiscordOnly, game, _selectedTheme, AvatarMode.VTubeStudio, null);
''',
'''            var game = SelectedGame ?? throw new InvalidOperationException("Choose a running game first so StreamKit can open its preview engine.");
            _vTubeStudio.Launch();
            await _setup.EnsureReadyAsync(needSpout: true);
            await _vTubeStudio.LaunchAndWaitAsync();
            await EnsureControlledObsRestartAsync();
            _obs.Launch(StreamMode.DiscordOnly, game, _selectedTheme, AvatarMode.VTubeStudio, null);
''',
'avatar check stale OBS recovery')
s = replace_once(s,
'''    private void ShowProblem(string title, string message)
    {
        ProblemTitle.Text = title;
        ProblemText.Text = message;
        ProblemPanel.Visibility = Visibility.Visible;
    }

    private void HideProblem() => ProblemPanel.Visibility = Visibility.Collapsed;
''',
'''    private void ShowProblem(string title, string message)
    {
        _hasProblem = true;
        ProblemTitle.Text = title;
        ProblemText.Text = message;
        ProblemPanel.Visibility = Visibility.Visible;
    }

    private void HideProblem()
    {
        _hasProblem = false;
        ProblemPanel.Visibility = Visibility.Collapsed;
    }
''',
'problem state methods')
s = replace_once(s,
'''        if (text.Contains("avatar") || text.Contains("vtube") || text.Contains("spout"))
            return "Your avatar app is open, but the avatar is not reaching StreamKit yet. Follow the 3-step Avatar Help card, click Check my avatar, then start again.";
''',
'''        if (text.Contains("avatar") || text.Contains("vtube") || text.Contains("spout"))
            return "StreamKit could not connect to your avatar yet. Click Avatar help; StreamKit will open VTube Studio for you, then use Check my avatar.";
''',
'friendlier avatar error')
write(p, s)

# 5) Version + release metadata.
p = 'src/BPSRStreamKit/BPSRStreamKit.csproj'
s = read(p).replace('<Version>2.0.0</Version>', '<Version>2.0.1</Version>') \
           .replace('<FileVersion>2.0.0.0</FileVersion>', '<FileVersion>2.0.1.0</FileVersion>') \
           .replace('<AssemblyVersion>2.0.0.0</AssemblyVersion>', '<AssemblyVersion>2.0.1.0</AssemblyVersion>')
write(p, s)

p = 'src/BPSRStreamKit/MainWindow.xaml'
s = read(p).replace('Text="v2.0.0"', 'Text="v2.0.1"')
write(p, s)

p = '.github/workflows/build-windows.yml'
s = read(p).replace('StreamKit v0.4.2', 'StreamKit v2.0.1')
write(p, s)

p = '.github/workflows/release-beginner-ux.yml'
s = read(p)
s = s.replace("branches: ['release/v2.0.0-auto']", "branches: ['release/v2.*']")
s = s.replace('StreamKit v2.0.0 - Beginner-Friendly UI', 'StreamKit v2.0.1 - Beginner-Friendly UI')
old_release = '''          gh release view v2.0.0-auto *> $null
          if ($LASTEXITCODE -eq 0) {
            gh release delete v2.0.0-auto --cleanup-tag --yes
          }
          gh release create v2.0.0-auto StreamKit-BeginnerUX.zip --title "v2.0.0 - Beginner-Friendly UI Overhaul" --notes "Fully automated UX modernization focused on newbie accessibility, built and released by AI agent."
'''
new_release = '''          $version = "${{ github.ref_name }}".Replace("release/", "")
          gh release view $version *> $null
          if ($LASTEXITCODE -eq 0) {
            gh release delete $version --cleanup-tag --yes
          }
          $target = ((git ls-remote origin refs/heads/main) -split "`t")[0]
          gh release create $version StreamKit-BeginnerUX.zip --target $target --title "$version - StreamKit Beginner UX" --notes "Fixes avatar auto-launch, transparent VTuber recovery, stale OBS startup conflicts, old portable asset paths, and beginner dropdown labels."
'''
if old_release not in s:
    raise SystemExit('missing old release block')
s = s.replace(old_release, new_release, 1)
write(p, s)

print('v2.0.1 hotfix applied')
