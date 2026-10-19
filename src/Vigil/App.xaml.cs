using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Vigil.Core;
using Vigil.UI;

namespace Vigil;

public partial class App : Application
{
    private const string InstanceName = @"Local\Vigil.Instance";
    private const string ShowSignalName = @"Local\Vigil.Show";
    private const string QuitSignalName = @"Local\Vigil.Quit";
    private const string PairSignalName = @"Local\Vigil.Pair";

    private Mutex? _instance;
    private EventWaitHandle? _showSignal;
    private EventWaitHandle? _quitSignal;
    private EventWaitHandle? _pairSignal;
    private Tray? _tray;
    private PowerEvents? _powerEvents;
    private PopoverWindow? _popover;
    private HappenedWindow? _happened;
    private DeskPill? _pill;
    private SettingsWindow? _settings;
    private WelcomeWindow? _welcome;
    private WelcomeBackWindow? _welcomeBack;
    private DispatcherTimer? _uiThrottle;
    private bool _uiDirty;

    internal Engine Engine { get; private set; } = null!;
    internal PhoneSync Phone { get; private set; } = null!;
    private PhonePairWindow? _pair;
    internal bool Quitting { get; private set; }
    internal bool HooksInstalled { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args.Select(a => a.ToLowerInvariant()).ToHashSet();

        // Integration tests only: "--data-dir <dir> --port <n>" runs an isolated second copy that
        // never touches the real power plan, settings, or single-instance lock.
        string? Arg(string name) { var i = Array.FindIndex(e.Args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase)); return i >= 0 && i + 1 < e.Args.Length ? e.Args[i + 1] : null; }
        var testDir = Arg("--data-dir");
        var testPort = int.TryParse(Arg("--port"), out var tp) ? tp : HookServer.DefaultPort;
        var testInstance = testDir is not null;
        if (testInstance) Paths.Root = testDir!;

        // Scheduled-task watchdog: put the power plan back if Vigil isn't running but left a journal.
        if (args.Contains("--restore-if-orphaned"))
        {
            var running = Mutex.TryOpenExisting(InstanceName, out var m);
            m?.Dispose();
            if (!running)
            {
                var pc = new PowerController(new WindowsPowerPlan(), Paths.Journal, useRequests: false);
                if (pc.HasJournal && pc.Restore()) History.Add("The watchdog restored your sleep settings after Vigil stopped unexpectedly.", important: true);
                // A stale "you're at the PC" marker would keep phone pushes muted.
                try { File.Delete(Paths.PresenceFile); } catch { }
            }
            Shutdown();
            return;
        }

        // "Vigil.exe --quit": ask the running copy to exit cleanly (used by the installer).
        if (args.Contains("--quit"))
        {
            try { EventWaitHandle.OpenExisting(QuitSignalName).Set(); } catch { }
            Shutdown();
            return;
        }

        var snapshotIdx = Array.FindIndex(e.Args, a => a.Equals("--snapshot", StringComparison.OrdinalIgnoreCase));
        var playback = args.Contains("--demo-playback");
        var demo = args.Contains("--demo") || playback || snapshotIdx >= 0;
        // Screenshots render windows off-screen, where the glass can't be captured,
        // unless VIGIL_SCREEN_CAPTURE=1 asks for real on-screen captures.
        _screenCapture = Environment.GetEnvironmentVariable("VIGIL_SCREEN_CAPTURE") == "1";
        WindowStyling.MaterialsAllowed = snapshotIdx < 0 || _screenCapture;
        WindowStyling.UseLayerFills(Resources);

        if (!demo && !testInstance)
        {
            _instance = new Mutex(true, InstanceName, out var isFirst);
            if (!isFirst)
            {
                // Already running: ask it to show itself (or the iPhone pairing window).
                try { EventWaitHandle.OpenExisting(args.Contains("--pair") ? PairSignalName : ShowSignalName).Set(); } catch { }
                Shutdown();
                return;
            }
            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
            var signal = _showSignal;
            new Thread(() =>
            {
                while (signal.WaitOne())
                    Dispatcher.BeginInvoke(() => ShowHappened(null));
            }) { IsBackground = true, Name = "show-signal" }.Start();

            _pairSignal = new EventWaitHandle(false, EventResetMode.AutoReset, PairSignalName);
            var pairSignal = _pairSignal;
            new Thread(() =>
            {
                while (pairSignal.WaitOne())
                    Dispatcher.BeginInvoke(ShowPhonePairing);
            }) { IsBackground = true, Name = "pair-signal" }.Start();

            _quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, QuitSignalName);
            var quit = _quitSignal;
            new Thread(() =>
            {
                if (quit.WaitOne()) Dispatcher.BeginInvoke(Quit);
            }) { IsBackground = true, Name = "quit-signal" }.Start();

            DispatcherUnhandledException += (_, ex) =>
            {
                Log.Error("Unhandled UI exception", ex.Exception);
                ex.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            {
                Log.Error("Fatal exception", ex.ExceptionObject as Exception);
                // Last chance: never leave the PC stuck awake.
                try { Engine?.Power.Restore(); } catch { }
            };
        }

        var settings = demo ? new AppSettings { FirstRunDone = true, Ephemeral = true } : AppSettings.Load();
        Engine = new Engine(Dispatcher, settings, demo: demo, testInstance: testInstance) { Port = testPort };
        Engine.Changed += () => _uiDirty = true;
        // The eye's little life: held still for screenshots or if you turned it off, paused while nobody can see it.
        EyeMotion.Enabled = settings.EyeAlive && snapshotIdx < 0;
        Engine.Changed += () => EyeMotion.Paused = Engine.IsLocked || Engine.Sleep.DisplayOn == false;
        Engine.Toast += (t, m) => Toast(t, m);
        Engine.WelcomeBack += ShowWelcomeBack;
        RefreshHooksInstalled();
        Phone = new PhoneSync(this);
        Engine.Changed += () => Phone.Changed();

        if (playback) Sky.FixedNow = DemoData.PlaybackMorning;
        if (demo) DemoData.Load(Engine.Tracker, playback ? DemoData.PlaybackMorning : null);
        Engine.Start();
        // Tell the phone Vigil is back (after an update, a restart or a crash) instead of waiting for a change.
        Phone.Changed();

        if (snapshotIdx >= 0)
        {
            var dir = snapshotIdx + 1 < e.Args.Length ? e.Args[snapshotIdx + 1] : Path.Combine(Environment.CurrentDirectory, "screenshots");
            Dispatcher.BeginInvoke(async () => await Snapshots(dir), DispatcherPriority.ApplicationIdle);
            return;
        }

        if (testInstance) { Log.Info($"Test instance on port {testPort}"); return; }

        if (playback)
        {
            StartPlayback(e.Args);
            return;
        }

        _tray = new Tray(this);
        _powerEvents = demo ? null : new PowerEvents((open, initial) => Engine.OnLidChanged(open, initial), () => Dispatcher.BeginInvoke(Quit), () => TogglePopover(),
            on => Engine.OnDisplayChanged(on));
        SessionEnding += (_, _) =>
        {
            // Shutting down or restarting: tell the phone it's off (the update goes out before Vigil exits).
            Engine.Sleep.TurningOff(PcSleep.ShuttingDown);
            Quit();
        };

        // Coalesce bursts of hook events into at most ~4 UI refreshes a second.
        _uiThrottle = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) =>
        {
            if (!_uiDirty) return;
            _uiDirty = false;
            RefreshUi();
        }, Dispatcher);
        _uiThrottle.Start();

        if (!demo && !testInstance) SetDeskPill(Engine.Settings.DeskPill, save: false);

        // A phone that was mid-pairing when Vigil last closed: see if it finished.
        if (!demo && Engine.Settings.PhoneKey.Length > 0) _ = Phone.CheckPairedAsync();

        if (args.Contains("--connect") && !demo)
        {
            try { Connect(); }
            catch (Exception ex) { Log.Error("Connect from command line failed", ex); }
        }

        if (!args.Contains("--background"))
        {
            if (!settings.FirstRunDone || !HooksInstalled) ShowWelcome();
            else TogglePopover();
        }
    }

    private void RefreshUi()
    {
        AssignColors(Engine);
        _tray?.Update();
        if (_popover?.IsVisible == true) _popover.Refresh();
        _happened?.Refresh();
        _pill?.Refresh();
        if (_settings?.IsVisible == true) _settings.View.Refresh();
    }

    internal void RefreshHooksInstalled()
    {
        if (Engine.Demo) { HooksInstalled = true; return; }
        try { HooksInstalled = Engine.Claude.IsInstalled(); }
        catch (Exception ex)
        {
            Log.Warn($"Couldn't read Claude settings: {ex.Message}");
            HooksInstalled = false;
        }
    }

    // ---------- navigation ----------

    internal void TogglePopover()
    {
        _popover ??= new PopoverWindow(this);
        if (_popover.IsVisible) _popover.HidePopover();
        else if (!_popover.JustHidden) _popover.ShowNearTray();
    }

    /// <summary>"What happened": the one window.</summary>
    internal void ShowHappened(string? sessionId)
    {
        _happened ??= new HappenedWindow(this);
        _happened.ShowRecent(sessionId);
    }

    /// <summary>Show or hide the desktop status pill.</summary>
    internal void SetDeskPill(bool on, bool save = true)
    {
        if (save)
        {
            Engine.Settings.DeskPill = on;
            Engine.Settings.Save();
            _settings?.View.Refresh();
        }
        if (on && _pill is null)
        {
            _pill = new DeskPill(this);
            _pill.Refresh();
            _pill.Show();
            _pill.Start();
        }
        else if (!on && _pill is not null)
        {
            _pill.Close();
            _pill = null;
        }
    }

    /// <summary>Distinct colors for the projects active in the last day.</summary>
    internal static void AssignColors(Engine engine)
    {
        var since = DateTime.Now - TimeSpan.FromHours(24);
        Avatars.Assign(engine.Tracker.Snapshot().Where(s => (engine.Demo || s.LastEventAt >= since) && s.Background != true).Select(s => s.AvatarKey));
    }

    internal void ShowPhonePairing()
    {
        _pair?.Close();
        _pair = new PhonePairWindow(this);
        _pair.Closed += (_, _) => { _pair = null; _settings?.View.Refresh(); };
        _pair.Show();
        _pair.Activate();
    }

    internal void ShowSettings()
    {
        _settings ??= new SettingsWindow(this);
        _settings.View.Refresh();
        if (!_settings.IsVisible) _settings.Show();
        _settings.Activate();
    }

    /// <summary>First launch, and "How Vigil works".</summary>
    internal void ShowWelcome()
    {
        _welcome?.Close();
        _welcome = new WelcomeWindow(this);
        _welcome.Closed += (_, _) => _welcome = null;
        _welcome.Show();
        _welcome.Activate();
    }

    /// <summary>Preview of the Welcome back screen with made-up data.</summary>
    internal void ShowWelcomeBackExample() => PresentWelcomeBack(DemoData.OvernightRecap(), example: true);

    internal void ShowWelcomeBackExampleDetails()
    {
        _happened ??= new HappenedWindow(this);
        _happened.ShowExample(DemoData.OvernightRecap());
    }

    internal void ShowHappenedSince(DateTime from)
    {
        _happened ??= new HappenedWindow(this);
        _happened.ShowSince(from);
    }

    /// <summary>You came back (unlock, lid open or wake) and something happened while you were gone.</summary>
    private void ShowWelcomeBack(Recap recap) => PresentWelcomeBack(recap, example: false);

    /// <summary>
    /// vigil.exe --demo-playback [--playback-delay 1.5] [--playback-rect file.json]
    /// The demo recap with its entrance on a fixed timeline, for recordings that come out the same every time.
    /// The window opens after the delay (so a recorder can start first), writes its screen rectangle in
    /// device pixels if asked, and Vigil quits when it closes. Nothing on this PC is read or changed.
    /// </summary>
    private void StartPlayback(string[] args)
    {
        string? Arg(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var delay = double.TryParse(Arg("--playback-delay"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) && d >= 0 ? d : 1.0;
        var rectFile = Arg("--playback-rect");
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        AssignColors(Engine);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(0.001, delay)) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var w = new WelcomeBackWindow(this, DemoData.OvernightRecap(DemoData.PlaybackMorning), playback: true);
            w.Closed += (_, _) => Quit();
            if (rectFile is not null)
                w.ContentRendered += (_, _) =>
                {
                    var dpi = VisualTreeHelper.GetDpi(w);
                    var rect = new { x = (int)Math.Round(w.Left * dpi.DpiScaleX), y = (int)Math.Round(w.Top * dpi.DpiScaleY), width = (int)Math.Round(w.ActualWidth * dpi.DpiScaleX), height = (int)Math.Round(w.ActualHeight * dpi.DpiScaleY), scale = dpi.DpiScaleX };
                    try { File.WriteAllText(rectFile, System.Text.Json.JsonSerializer.Serialize(rect)); } catch (Exception ex) { Log.Warn($"Writing the playback rect failed: {ex.Message}"); }
                };
            w.Show();
            Log.Info("Demo playback started");
        };
        timer.Start();
    }

    private void PresentWelcomeBack(Recap recap, bool example)
    {
        // Right after unlock Windows is still switching from the lock screen to the desktop;
        // a window shown at that instant can end up behind it. Wait a beat, then come to the front.
        var delay = example ? TimeSpan.Zero : TimeSpan.FromSeconds(1.5);
        var timer = new DispatcherTimer { Interval = delay == TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _popover?.HidePopover();
            _welcomeBack?.Close();
            _welcomeBack = new WelcomeBackWindow(this, recap, example);
            _welcomeBack.Closed += (_, _) => _welcomeBack = null;
            // Topmost, but without stealing the keyboard from whatever you're typing in.
            _welcomeBack.Show();
            if (example) BringToFront(_welcomeBack);
            Log.Info(example ? "Showed the Welcome back preview" : "Showed Welcome back");
        };
        timer.Start();
    }

    /// <summary>Windows only lets the foreground app steal focus; Topmost + Activate gets past that politely.</summary>
    internal static void BringToFront(Window w)
    {
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        var wasTopmost = w.Topmost;
        w.Topmost = true;
        w.Activate();
        w.Focus();
        var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
        if (hwnd != IntPtr.Zero) Native.SetForegroundWindow(hwnd);
        w.Topmost = wasTopmost;
    }

    internal static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn($"Open URL: {ex.Message}"); }
    }

    internal void Toast(string title, string message) => _tray?.Toast(title, message);

    // ---------- setup ----------

    internal void Connect()
    {
        var claude = Engine.Claude;
        claude.Install();
        claude.SetPresenceFile(Paths.PresenceFile);
        claude.SetRemoteControlAtStartup(true);
        if (Environment.ProcessPath is { } exe && !exe.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            Core.Startup.Set(true, exe);
            Watchdog.Install(exe);
        }
        Engine.Settings.FirstRunDone = true;
        Engine.Settings.Save();
        RefreshHooksInstalled();
        RefreshUi();
        Toast("Vigil is on. Look for the eye by the clock", "It lights up while Claude works. Click it any time.");
    }

    internal void Disconnect()
    {
        Engine.Claude.Uninstall();
        Engine.Claude.SetPresenceFile(null);
        RefreshHooksInstalled();
        RefreshUi();
    }

    internal void Quit()
    {
        Quitting = true;
        // Unless Windows is shutting down (said already), the phone should know Vigil isn't watching any more.
        if (Engine is { Demo: false }) Engine.Sleep.TurningOff(PcSleep.VigilClosed);
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Quitting = true;
        _uiThrottle?.Stop();
        _tray?.Dispose();
        _powerEvents?.Dispose();
        try { Engine?.Dispose(); } catch (Exception ex) { Log.Error("Shutdown", ex); }
        _showSignal?.Dispose();
        _quitSignal?.Dispose();
        _pairSignal?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    // ---------- screenshots (vigil.exe --snapshot <dir>) ----------

    private bool _screenCapture;

    /// <summary>
    /// The eye's moves side by side (rest, half shut, shut, glancing four ways), big and as real tray
    /// icons at 16, 24 and 32 px on a dark and a light taskbar, for checking how they read.
    /// </summary>
    private static void EyeSheet(string path)
    {
        var poses = new[]
        {
            EyePose.Rest, new EyePose(0.5, 0, 0), new EyePose(0.06, 0, 0),
            new EyePose(1, -EyePose.MaxDx, 0), new EyePose(1, EyePose.MaxDx, 0),
            new EyePose(1, 7, -EyePose.MaxDy), new EyePose(1, -7, EyePose.MaxDy),
        };
        const int cell = 120;
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1C, 0x1C, 0x1E)), null, new Rect(0, 0, cell * poses.Length, 260));
            dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF3, 0xF3, 0xF3)), null, new Rect(0, 200, cell * poses.Length, 60));
            for (var i = 0; i < poses.Length; i++)
            {
                dc.PushTransform(new System.Windows.Media.TranslateTransform(i * cell + 10, 10));
                EyeView.Draw(dc, poses[i], System.Windows.Media.Brushes.White, true, 7.5, null);
                dc.Pop();
                var x = i * cell + 14.0;
                foreach (var px in new[] { 16, 24, 32 })
                {
                    dc.DrawImage(IconFactory.StateImage(TrayState.Working, px, false, poses[i]), new Rect(x, 150, px, px));
                    dc.DrawImage(IconFactory.StateImage(TrayState.Idle, px, true, poses[i]), new Rect(x, 214, px, px));
                    x += px + 8;
                }
            }
        }
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(cell * poses.Length, 260, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(dv);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var f = File.Create(path);
        enc.Save(f);
    }

    private async Task Snapshots(string dir)
    {
        Directory.CreateDirectory(dir);
        // The demo projects get distinct colors, as they would in the running app.
        AssignColors(Engine);
        try
        {
            async Task Shot(Window w, string name)
            {
                Place(w);
                if (!w.IsVisible) w.Show();
                await Settle();
                Capture(w, Path.Combine(dir, name));
            }

            EyeSheet(Path.Combine(dir, "eye-poses.png"));

            var pop = new PopoverWindow(this);
            pop.Refresh();
            await Shot(pop, "tray-card.png");
            pop.Close();

            var happened = new HappenedWindow(this) { Height = 1100 };
            _happened = happened;
            happened.ShowExample(DemoData.OvernightRecap());
            await Shot(happened, "what-happened.png");
            happened.ShowRecent("demo-api-server");
            await Shot(happened, "what-happened-today.png");
            // "See everything" from Welcome back: the "Since you left" view (this once overflowed the stack).
            happened.ShowSince(DateTime.Now - TimeSpan.FromHours(8));
            await Shot(happened, "what-happened-since.png");

            var welcome = new WelcomeWindow(this);
            await Shot(welcome, "welcome.png");
            welcome.Close();

            var settingsWin = new SettingsWindow(this) { Height = 900 };
            await Shot(settingsWin, "settings.png");

            // The recap on the demo's fixed morning (7:14 AM), the same one the launch video records.
            var clock = Sky.FixedNow;
            Sky.FixedNow = DemoData.PlaybackMorning;
            var wb = new WelcomeBackWindow(this, DemoData.OvernightRecap(DemoData.PlaybackMorning), example: false);
            Place(wb);
            wb.Show();
            await Task.Delay(3600);
            await Settle();
            Capture(wb, Path.Combine(dir, "welcome-back.png"));
            Sky.FixedNow = clock;
            wb.Close();

            var pair = new PhonePairWindow(this);
            await Shot(pair, "connect-iphone.png");
            pair.Close();

            // The desktop pill: at a glance, hovered, and the moment something finishes.
            var pill = new DeskPill(this);
            pill.LayoutLikeDocked();
            pill.Refresh();
            await Shot(pill, "pill.png");
            pill.SnapOpen();
            await Shot(pill, "pill-open.png");
            pill.Collapse();
            if (Engine.Tracker.Snapshot().FirstOrDefault(Glance.IsDone) is { } finished) pill.ShowNews(finished);
            await Shot(pill, "pill-done.png");
            pill.Close();
        }
        catch (Exception ex)
        {
            Log.Error("Snapshot failed", ex);
            File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString());
        }
        Quit();
    }

    private void Place(Window w)
    {
        w.ShowActivated = false;
        w.WindowStartupLocation = WindowStartupLocation.Manual;
        w.Left = _screenCapture ? 80 : -4000;
        w.Top = _screenCapture ? 40 : 0;
        if (_screenCapture) w.Topmost = true;
    }

    private static async Task Settle()
    {
        await Task.Delay(600);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private void Capture(Window w, string path)
    {
        if (_screenCapture) { CaptureScreen(w, path); return; }
        // Render the window's root visual (its template border), which sits at 0,0 and includes margins.
        var root = (FrameworkElement)VisualTreeHelper.GetChild(w, 0);
        var dpi = VisualTreeHelper.GetDpi(w);
        // VIGIL_SNAPSHOT_SCALE=2 renders sharper stills (for videos and retina READMEs).
        var k = double.TryParse(Environment.GetEnvironmentVariable("VIGIL_SNAPSHOT_SCALE"), System.Globalization.CultureInfo.InvariantCulture, out var s) && s is > 0 and <= 4 ? s : 1;
        var sx = dpi.DpiScaleX * k;
        var sy = dpi.DpiScaleY * k;
        var width = (int)Math.Ceiling(root.ActualWidth * sx);
        var height = (int)Math.Ceiling(root.ActualHeight * sy);
        var rtb = new RenderTargetBitmap(width, height, 96 * sx, 96 * sy, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen())
            dc.DrawRectangle(w.Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        rtb.Render(bg);
        rtb.Render(root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    /// <summary>A real screen capture, so the Windows 11 glass shows as it does on the desktop.</summary>
    private static void CaptureScreen(Window w, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(w);
        var x = (int)Math.Round(w.Left * dpi.DpiScaleX);
        var y = (int)Math.Round(w.Top * dpi.DpiScaleY);
        var width = (int)Math.Round(w.ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Round(w.ActualHeight * dpi.DpiScaleY);
        using var bmp = new System.Drawing.Bitmap(width, height);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
            g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
