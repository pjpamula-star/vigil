using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Vigil.Core;

/// <summary>
/// Ties everything together. Lives on the UI thread: hook events are marshalled here,
/// so the tracker, the power controller and the UI all see one consistent world.
/// </summary>
public sealed class Engine : IDisposable
{
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _timer;
    private readonly KeepAwakePolicy _policy = new();
    private readonly SafetyMonitor _safety = new();
    private readonly ISensors _sensors;
    private HookServer? _server;
    private BackupWifiMonitor? _wifi;
    private DateTime _lastSafetyCheck = DateTime.MinValue;
    private DateTime? _lockedAt;

    /// <summary>When you left: the earliest of lock, lid close or sleep. Cleared when you're back.</summary>
    private DateTime? _awaySince;
    private bool _dirty;
    private bool _disposed;
    private bool _subscribed;

    /// <summary>Which "still waiting on you" reminders went out, per session (1 = 20-min nudge, 2 = 10-min warning).</summary>
    private readonly Dictionary<string, int> _nudged = new();
    private SafetyLevel _lastSafetyLevel = SafetyLevel.Ok;

    internal AppSettings Settings { get; }
    public SessionTracker Tracker { get; } = new();
    public PowerController Power { get; }
    public Summarizer Summarizer { get; }
    public ClaudeSetup Claude { get; }
    public CodexSetup Codex { get; }

    public HoldDecision Decision { get; private set; } = new(false, HoldKind.None, "Starting…", "");
    public SafetyStatus Safety { get; private set; } = new(SafetyLevel.Ok, "All clear", "", false, false);
    public DateTime? ManualUntil { get; private set; }
    public bool ServerRunning => _server is not null;
    public string? ServerError { get; private set; }
    public Recap? LastRecap { get; private set; }
    public bool IsLocked => _lockedAt is not null;

    /// <summary>Null until Windows reports it (desktops have no lid).</summary>
    public bool? LidOpen { get; private set; }

    /// <summary>Whether the PC is awake, asleep or off, for the phone.</summary>
    public PcSleep Sleep { get; }

    /// <summary>When the phone was last told the PC went to sleep (so "All done" and "asleep" aren't both sent).</summary>
    private DateTime _sleepToldAt = DateTime.MinValue;

    /// <summary>For telling "Vigil keeps running with the screen off" from "Windows suspended it".</summary>
    private DateTime _lastTickAt = DateTime.MinValue;
    private DateTime _runningSince = DateTime.Now;

    /// <summary>Something visible changed.</summary>
    public event Action? Changed;

    /// <summary>A toast-worthy moment: (title, message).</summary>
    public event Action<string, string>? Toast;

    /// <summary>You came back and there's news.</summary>
    public event Action<Recap>? WelcomeBack;

    /// <summary>Something worth a phone notification (needs you, finished, too hot).</summary>
    public event Action<PhoneMessage>? Note;

    /// <summary>Where Claude's hooks reach Vigil. Only changed for integration tests (--port).</summary>
    public int Port { get; init; } = HookServer.DefaultPort;

    /// <summary>Integration-test instance: real hooks and logic, but never touches the power plan or the lock screen.</summary>
    public bool TestInstance { get; init; }

    /// <summary>Demo mode: fake sessions, no hooks, and the real power plan is never touched.</summary>
    public bool Demo { get; }

    internal Engine(Dispatcher ui, AppSettings settings, ISensors? sensors = null, bool demo = false, bool testInstance = false)
    {
        TestInstance = testInstance;
        _ui = ui;
        Settings = settings;
        Demo = demo;
        _sensors = sensors ?? new WindowsSensors();
        Power = demo || testInstance
            ? new PowerController(new MemoryPowerPlan(), Path.Combine(Paths.Root, "test-journal.json"), useRequests: false)
            : new PowerController(new WindowsPowerPlan(), Paths.Journal);
        Summarizer = new Summarizer(Paths.SummarizerDir);
        Claude = new ClaudeSetup(Paths.ClaudeSettings, Paths.ClaudeSettingsBackupDir);
        Codex = new CodexSetup(Paths.CodexHome, Paths.ClaudeSettingsBackupDir);
        Tracker.IgnoreCwd = Paths.SummarizerDir;
        ApplySettings();

        Sleep = new PcSleep(!demo && Native.IsModernStandby());
        Sleep.Changed += OnSleepChanged;

        Tracker.Changed += _ => { _dirty = true; Changed?.Invoke(); };
        Tracker.TurnEnded += OnTurnEnded;
        Tracker.NeedsYou += OnNeedsYou;

        _timer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += (_, _) => Tick();
    }

    public void Start()
    {
        // If Vigil died while holding the PC awake, put the user's settings back first.
        if (Power.HasJournal)
        {
            Log.Warn("Found a power journal from a previous run; restoring");
            Power.Restore();
            History.Add("Vigil restarted and restored your sleep settings.");
        }

        if (Demo)
        {
            _timer.Start();
            Tick();
            return;
        }

        var saved = Json.Load<List<Session>>(Paths.State);
        if (saved is not null) Tracker.Load(saved);

        try
        {
            _server = new HookServer((payload, rc, agent) => _ui.BeginInvoke(() => OnHook(payload, rc, agent)), Port, status: StatusJson,
                test: TestInstance ? what => _ui.BeginInvoke(() => Simulate(what)) : null);
            _server.Start();
        }
        catch (Exception ex)
        {
            _server = null;
            ServerError = ex.Message;
            Log.Error("Couldn't start the hook listener", ex);
        }

        RestoreAwayState();

        if (!TestInstance)
        {
            _wifi = new BackupWifiMonitor(_ui, Settings, () => Power.Holding);
            _wifi.Said += (text, toast) =>
            {
                History.Add(text, important: toast);
                if (toast) Toast?.Invoke("Wi-Fi", text);
            };
            _wifi.Note += m => Note?.Invoke(m);
            _wifi.Start();
        }

        SystemEvents.SessionSwitch += OnSessionSwitch;
        _subscribed = true;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SetPresent(!IsLocked);

        _timer.Start();
        Tick();
    }

    public void ApplySettings()
    {
        _policy.WaitForYou = TimeSpan.FromMinutes(Math.Max(0, Settings.WaitForYouMinutes));
        _policy.UsageLimitWait = TimeSpan.FromHours(Math.Max(0, Settings.UsageLimitWaitHours));
        _policy.Grace = TimeSpan.FromMinutes(Math.Max(0, Settings.ReleaseGraceMinutes));
        Tracker.QuietAfter = TimeSpan.FromMinutes(Math.Max(5, Settings.QuietAfterMinutes));
        _safety.EaseOffBatteryPercent = Settings.EaseOffBatteryPercent;
        _safety.HibernateBatteryPercent = Settings.HibernateBatteryPercent;
        Summarizer.Enabled = Settings.AiHeadlines;
    }

    // ---------- manual holds ----------

    public void KeepAwakeFor(TimeSpan duration) => KeepAwakeUntil(DateTime.Now + duration);

    public void KeepAwakeUntil(DateTime until)
    {
        ManualUntil = until;
        Log.Info($"Manual keep-awake until {until}");
        History.Add($"You asked Vigil to keep the PC awake until {until:h:mm tt}.", kind: ActivityKind.You);
        Tick();
    }

    /// <summary>The hold-to-stop button: drop manual holds and let the PC sleep now, even mid-work.</summary>
    public void StopNow()
    {
        ManualUntil = null;
        _snoozedUntil = DateTime.Now + TimeSpan.FromMinutes(30);
        Log.Info("Stopped by user");
        History.Add("You paused Vigil for 30 minutes.", kind: ActivityKind.You);
        Tick();
    }

    private DateTime? _snoozedUntil;
    public bool Snoozed => _snoozedUntil is { } s && s > DateTime.Now;
    public DateTime? SnoozedUntil => Snoozed ? _snoozedUntil : null;

    public void Resume()
    {
        _snoozedUntil = null;
        Tick();
    }

    // ---------- core loop ----------

    private void OnHook(JsonElement payload, string? remoteId, string agent = Session.Claude)
    {
        Tracker.Handle(payload, agent);
        if (agent == Session.Codex && !Settings.CodexSeen)
        {
            // The first event proves Codex trusted the hooks; Settings stops asking you to approve them.
            Settings.CodexSeen = true;
            Settings.Save();
        }
        var id = payload.Str("session_id");
        if (id is null) return;
        var session = Tracker.Get(id);
        if (session is null) return;

        var url = TranscriptScanner.UrlFromBridgeId(remoteId);
        var evt = payload.Str("hook_event_name");
        // Codex transcripts aren't Claude's format (and Codex has no claude.ai link).
        if (session.Agent == Session.Claude && (url is not null || evt is "SessionStart" or "Stop" or "UserPromptSubmit"))
        {
            var (title, scannedUrl) = evt is "Stop" or "SessionStart" ? TranscriptScanner.Scan(session.TranscriptPath) : (null, null);
            Tracker.SetTitleAndUrl(session, title, url ?? scannedUrl);
        }
        Tick();
    }

    private DateTime _liveReadAt = DateTime.MinValue;
    private IReadOnlyDictionary<string, LiveClaude>? _live;
    private readonly ClaudeProcesses _claudeProcesses = new();

    /// <summary>
    /// Claude Code's own list of running sessions, read at most every few seconds and only while a Claude
    /// session looks busy (that's all the tracker uses it for).
    /// </summary>
    private IReadOnlyDictionary<string, LiveClaude>? LiveClaudeSessions(DateTime now)
    {
        if (Demo) return null;
        var busy = Tracker.Snapshot().Any(s => s.Agent == Session.Claude && s.State is SessionState.Working or SessionState.NeedsYou or SessionState.Quiet);
        if (!busy) return null;
        if (_live is null || now - _liveReadAt >= TimeSpan.FromSeconds(4))
        {
            _live = _claudeProcesses.Read(ClaudeRegistry.Dir);
            _liveReadAt = now;
        }
        return _live;
    }

    private void Tick()
    {
        if (_disposed) return;
        var now = DateTime.Now;
        Tracker.Tick(LiveClaudeSessions(now));
        if (ManualUntil is { } m && m <= now) ManualUntil = null;

        var sessions = Tracker.Snapshot();
        var decision = _policy.Decide(sessions, ManualUntil, now);
        if (Snoozed)
            decision = new HoldDecision(false, HoldKind.None, "Paused by you.", $"Vigil won't keep your PC awake until {_snoozedUntil:h:mm tt}.");

        var wasHolding = Power.Holding;
        try
        {
            if (decision.Hold && !Power.Holding) Power.Hold();
            else if (!decision.Hold && Power.Holding)
            {
                Power.Release();
                _safety.Reset();
            }
        }
        catch (Exception ex)
        {
            decision = decision with { Detail = $"Couldn't change power settings: {ex.Message}" };
        }

        if (!wasHolding && Power.Holding)
        {
            var who = sessions.Where(s => s.State == SessionState.Working).Select(s => s.Name).ToList();
            var why = decision.Kind switch
            {
                HoldKind.Working when who.Count > 0 => $"{string.Join(", ", who.Take(3))} {(who.Count == 1 ? "is" : "are")} working",
                HoldKind.WaitingForYou => "Claude is waiting on you",
                HoldKind.UsageLimit => "Claude is waiting for the usage limit to reset",
                HoldKind.Manual => $"you asked for it until {ManualUntil:h:mm tt}",
                _ => "Claude is working",
            };
            History.Add($"Keeping your PC awake: {why}.", kind: ActivityKind.Awake);
        }
        if (wasHolding && !Power.Holding)
        {
            History.Add(Snoozed ? "Paused by you. Normal sleep settings are back." : "Nothing left to do. Normal sleep settings are back.", kind: ActivityKind.Sleep);
            if (!Snoozed)
            {
                OnAllDone(sessions);
                SleepNowIfLidClosed();
            }
        }
        // After the "All done" note, so the phone hears it before the PC drops into sleep.
        Sleep.Hold(Power.Holding);
        NoticeRunningWithScreenOff(now);
        NudgeWaitingSessions(sessions, now);

        // Safety checks: every 10 s while holding, every minute otherwise (for the status line).
        var interval = Power.Holding ? TimeSpan.FromSeconds(10) : TimeSpan.FromMinutes(1);
        if (now - _lastSafetyCheck >= interval)
        {
            _lastSafetyCheck = now;
            CheckSafety(now);
        }

        Decision = decision;
        if (_dirty) SaveState();
        Changed?.Invoke();
    }

    private void CheckSafety(DateTime now)
    {
        var status = _safety.Evaluate(_sensors.Thermal(), _sensors.Battery(), now);
        Safety = status;
        if (!Settings.HeatProtection || !Power.Holding)
        {
            if (Power.CpuCapped) Power.UncapCpu();
            _lastSafetyLevel = status.Level;
            return;
        }

        try
        {
            if (status.ShouldEaseOff && !Power.CpuCapped) Power.CapCpu((uint)Math.Clamp(Settings.CpuCapPercent, 30, 99));
            else if (!status.ShouldEaseOff && Power.CpuCapped) Power.UncapCpu();
        }
        catch (Exception ex)
        {
            Log.Error("CPU cap change failed", ex);
        }

        if (status.Level != _lastSafetyLevel && status.Level == SafetyLevel.Warm)
        {
            History.Add($"{status.Headline}. {status.Detail}", important: true);
            Toast?.Invoke(status.Headline, status.Detail);
            _ = PhoneNotes.SendAsync(Settings.NtfyTopic, $"Vigil: {status.Headline}", status.Detail, "high");
            Note?.Invoke(new PhoneMessage(status.Headline, status.Detail, null, true, "safety"));
        }
        _lastSafetyLevel = status.Level;

        if (status.ShouldHibernate)
        {
            var text = $"Hibernated at {now:h:mm tt} to protect your PC. {status.Detail}";
            History.Add(text, important: true);
            _ = PhoneNotes.SendAsync(Settings.NtfyTopic, $"Vigil: {status.Headline}", text + " Your sessions resume when you open it.", "urgent");
            Note?.Invoke(new PhoneMessage($"{status.Headline}: hibernating", "Your PC is switching off safely. Claude picks up where it left off when you open it.", null, true, "safety"));
            SaveState();
            // Give the phone note a moment to leave the machine.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _safety.Reset();
                if (!Demo && !TestInstance) Power.HibernateNow();
            };
            timer.Start();
        }
    }

    // ---------- notifications ----------

    private async void OnTurnEnded(Session session, Turn turn)
    {
        // Background runs (scheduled scripts, claude --bg): no summary, no log line, no notification.
        if (session.Background == true) return;
        try
        {
            var headline = await Summarizer.SummarizeAsync(session, turn);
            Tracker.SetHeadline(session, turn, headline);
            History.Add(turn.Outcome switch
            {
                SessionState.Done => $"{session.Name} finished: {headline}",
                SessionState.Paused => $"{session.Name} paused at the usage limit.",
                SessionState.Failed => $"{session.Name} stopped: {session.Reason}",
                _ => $"{session.Name}: {headline}",
            }, important: turn.Outcome == SessionState.Failed, kind: turn.Outcome == SessionState.Done ? ActivityKind.Done : ActivityKind.Session);
            var question = turn.Facts.Question;
            Note?.Invoke(turn.Outcome switch
            {
                SessionState.Done when question is not null => new PhoneMessage($"{session.Name} has a question", $"{headline} Asked: {question}", session.RemoteUrl, true, $"s-{session.Id}"),
                SessionState.Done => new PhoneMessage($"{session.Name} finished", headline, session.RemoteUrl, false, $"s-{session.Id}"),
                SessionState.Failed => new PhoneMessage($"{session.Name} stopped", session.Reason ?? headline, session.RemoteUrl, true, $"s-{session.Id}"),
                SessionState.Paused => new PhoneMessage($"{session.Name} paused", "Hit the usage limit. Claude continues when it resets, and Vigil keeps your PC awake until then.", session.RemoteUrl, false, $"s-{session.Id}"),
                _ => new PhoneMessage(session.Name, headline, session.RemoteUrl, false, $"s-{session.Id}"),
            });
        }
        catch (Exception ex)
        {
            Log.Error("Summary failed", ex);
            Tracker.SetHeadline(session, turn, Summarizer.Fallback(turn));
        }
    }

    private void OnNeedsYou(Session session)
    {
        if (session.Background == true) return;
        var text = session.Reason ?? "Waiting for your answer";
        History.Add($"{session.Name} needs you: {text}", kind: ActivityKind.NeedsYou);
        if (!IsLocked) Toast?.Invoke($"{session.Name} needs you", text);
        _ = PhoneNotes.SendAsync(Settings.NtfyTopic, $"{session.Name} needs you", text, "high");
        Note?.Invoke(new PhoneMessage($"{session.Name} needs you", text, session.RemoteUrl, true, $"s-{session.Id}"));
    }

    /// <summary>
    /// Windows suspends Vigil within seconds once a Modern Standby laptop sleeps. If its 5-second tick keeps
    /// coming for minutes with the screen off, something else is keeping the PC awake.
    /// </summary>
    private void NoticeRunningWithScreenOff(DateTime now)
    {
        if (now - _lastTickAt > TimeSpan.FromSeconds(20)) _runningSince = now;
        _lastTickAt = now;
        if (Sleep.State == PcPower.Asleep && now - _runningSince >= TimeSpan.FromMinutes(3) && now - Sleep.Since >= TimeSpan.FromMinutes(3))
        {
            Log.Info("Still running with the screen off: something else is keeping the PC awake");
            Sleep.StillRunning();
        }
    }

    public void OnDisplayChanged(bool on)
    {
        if (Sleep.DisplayOn == on) return;
        Log.Info(on ? "Screen on" : "Screen off");
        Sleep.Display(on);
        Changed?.Invoke();
    }

    /// <summary>Will the PC be asleep the moment Vigil lets go? (Screen already off, or lid closed and Vigil sleeps it.)</summary>
    private bool SleepsWhenReleased =>
        (Sleep.ModernStandby && Sleep.DisplayOn == false) || (LidOpen == false && Settings.SleepWhenDoneLidClosed && !Demo && !TestInstance);

    private void OnSleepChanged(bool asleep)
    {
        if (asleep) Log.Info(Sleep.State == PcPower.Off ? $"Going off ({Sleep.OffReason})" : "Asleep");
        else Log.Info(Sleep.LastSleptAt is { } slept && Sleep.LastWokeAt is { } woke && woke >= slept ? $"Awake again after {Text.Duration(woke - slept)}" : "Awake");
        // Claude finished a little while ago and now the PC has gone to sleep: say so, unless "All done" just did.
        if (asleep && Sleep.State == PcPower.Asleep && Sleep.SleptAfterWork && Settings.PhoneNotifySleep
            && DateTime.Now - _sleepToldAt > TimeSpan.FromMinutes(10) && _ui.CheckAccess())
        {
            _sleepToldAt = DateTime.Now;
            Note?.Invoke(new PhoneMessage("Your PC is asleep", $"Claude was done, so it went to sleep at {Sleep.Since:h:mm tt}.", null, false, "pc-sleep") { Always = true });
        }
        if (_ui.CheckAccess()) Changed?.Invoke();
    }

    private void OnAllDone(IReadOnlyList<Session> sessions)
    {
        // One digest instead of a vague "all done": what finished, newest first.
        var finished = sessions.Where(s => s.State == SessionState.Done && DateTime.Now - s.StateSince < TimeSpan.FromHours(12))
            .OrderByDescending(s => s.StateSince).ToList();
        var lines = finished.Take(3).Select(s => $"{s.Name}: {Text.Clip(s.Headline ?? "finished", 90)}");
        var digest = finished.Count > 0
            ? string.Join("\n", lines) + (finished.Count > 3 ? $"\n+{finished.Count - 3} more" : "")
            : "Nothing left to do.";
        var sleepsNow = SleepsWhenReleased;
        if (sleepsNow) _sleepToldAt = DateTime.Now;
        Note?.Invoke(new PhoneMessage(
            finished.Count > 0 ? $"All done: {finished.Count} finished" : "All done",
            digest + (sleepsNow
                ? $"\nYour PC went to sleep at {DateTime.Now:h:mm tt}."
                : "\nYour PC will go to sleep on its own when it's idle."), null, false, "all-done"));
        if (!string.IsNullOrWhiteSpace(Settings.NtfyTopic) && finished.Count > 0)
            _ = PhoneNotes.SendAsync(Settings.NtfyTopic, "All done. Your PC can sleep now.", digest);
    }

    // ---------- presence ----------

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        _ui.BeginInvoke(() =>
        {
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                case SessionSwitchReason.ConsoleDisconnect:
                case SessionSwitchReason.RemoteDisconnect:
                    if (_lockedAt is null) History.Add("You left (PC locked).", kind: ActivityKind.You);
                    _lockedAt ??= DateTime.Now;
                    _awaySince ??= _lockedAt;
                    SaveAwayState();
                    SetPresent(false);
                    break;
                case SessionSwitchReason.SessionUnlock:
                case SessionSwitchReason.ConsoleConnect:
                case SessionSwitchReason.SessionLogon:
                    SetPresent(true);
                    _lockedAt = null;
                    MarkBack();
                    break;
            }
            Changed?.Invoke();
        });
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        // Right here, not on the UI thread: Windows allows only a moment between this event and suspending.
        if (e.Mode == PowerModes.Suspend) Sleep.Suspend();
        _ui.BeginInvoke(() =>
        {
            if (e.Mode == PowerModes.Suspend)
            {
                _awaySince ??= DateTime.Now;
                SaveAwayState();
            }
            else if (e.Mode == PowerModes.Resume)
            {
                Log.Info("Resumed from sleep");
                _safety.Reset();
                Sleep.Resume();
                BackSoonIfUnlocked();
            }
            Tick();
        });
    }

    /// <summary>The lid closed. If we're keeping the PC awake, lock it so nobody can use it in your bag.</summary>
    public void OnLidChanged(bool open, bool initial = false)
    {
        if (Demo) return;
        LidOpen = open;
        Changed?.Invoke();
        if (initial) return;
        Log.Info(open ? "Lid opened" : "Lid closed");
        History.Add(open ? "Lid opened." : Power.Holding
            ? $"Lid closed. Still awake{(Settings.LockOnLidClose ? ", and locked" : "")}."
            : "Lid closed. Your PC goes to sleep as usual.", kind: ActivityKind.Lid);
        if (!open)
        {
            _awaySince ??= DateTime.Now;
            SaveAwayState();
        }
        if (!open && Power.Holding && Settings.LockOnLidClose && !IsLocked)
        {
            _lockedAt = DateTime.Now;
            SetPresent(false);
            Native.LockWorkStation();
        }
        if (open) BackSoonIfUnlocked();
    }

    /// <summary>
    /// Opened the lid or woke the PC. If Windows asks you to sign in, the unlock will say you're back;
    /// if it doesn't (no password on wake), give it a few seconds and treat this as being back.
    /// </summary>
    private void BackSoonIfUnlocked()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!IsLocked) MarkBack();
        };
        timer.Start();
    }

    private void MarkBack()
    {
        var from = _awaySince;
        _awaySince = null;
        SaveAwayState();
        if (from is not { } f) return;
        History.Add("You're back.", kind: ActivityKind.You);
        MaybeWelcomeBack(f);
    }

    /// <summary>
    /// A session waiting on you: nudge after 20 minutes, then warn 10 minutes before Vigil stops waiting
    /// and lets the PC sleep. Answering (from the phone) resets it.
    /// </summary>
    private void NudgeWaitingSessions(IReadOnlyList<Session> sessions, DateTime now)
    {
        foreach (var id in _nudged.Keys.ToList())
            if (sessions.FirstOrDefault(s => s.Id == id) is not { State: SessionState.NeedsYou }) _nudged.Remove(id);

        var limit = TimeSpan.FromMinutes(Settings.WaitForYouMinutes);
        foreach (var s in sessions.Where(s => s.State == SessionState.NeedsYou))
        {
            var waited = now - s.StateSince;
            var stage = _nudged.GetValueOrDefault(s.Id);
            var sleepsAt = s.StateSince + limit;
            var next = NextNudge(waited, limit, stage);
            if (next == stage) continue;
            if (next == 2)
            {
                _nudged[s.Id] = 2;
                Note?.Invoke(new PhoneMessage($"{s.Name}: 10 minutes left",
                    $"Still waiting for you ({Text.Clip(s.Reason, 80)}). Answer by {sleepsAt:h:mm tt} or your PC goes to sleep.", s.RemoteUrl, true, $"s-{s.Id}"));
            }
            else
            {
                _nudged[s.Id] = 1;
                Note?.Invoke(new PhoneMessage($"{s.Name} is still waiting",
                    $"{Text.Clip(s.Reason, 90)}. Your PC stays awake until {sleepsAt:h:mm tt}.", s.RemoteUrl, true, $"s-{s.Id}"));
            }
        }
    }

    /// <summary>
    /// Which reminder is due: 1 = "still waiting" (after 20 min), 2 = "10 minutes left" (before Vigil stops waiting).
    /// Returns the current stage when nothing new is due. Each fires at most once.
    /// </summary>
    internal static int NextNudge(TimeSpan waited, TimeSpan limit, int stage)
    {
        var warnAt = limit - TimeSpan.FromMinutes(10);
        if (stage < 2 && limit >= TimeSpan.FromMinutes(20) && waited >= warnAt && waited < limit) return 2;
        if (stage < 1 && waited >= TimeSpan.FromMinutes(20) && waited < warnAt) return 1;
        return stage;
    }

    /// <summary>
    /// Done, and the lid is closed (probably in a bag): don't sit there awake until the idle timer.
    /// Sleep now, or hibernate on battery so it can't drain or heat up.
    /// </summary>
    private void SleepNowIfLidClosed()
    {
        if (Demo || TestInstance || !Settings.SleepWhenDoneLidClosed || LidOpen != false) return;
        var onBattery = _sensors.Battery().OnBattery;
        var hibernate = onBattery && Native.IsPwrHibernateAllowed();
        History.Add(hibernate
            ? "All done with the lid closed on battery, so Vigil hibernated the PC."
            : "All done with the lid closed, so Vigil put the PC to sleep.", kind: ActivityKind.Sleep);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) }; // let the phone note go out first
        var started = DateTime.Now;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (Power.Holding || LidOpen != false) return; // something started again, or you opened the lid
            // Long overdue: the PC slept meanwhile (a Modern Standby laptop sleeps by itself as soon as Vigil
            // lets go) and this is it waking, perhaps because you're opening the lid. Don't put it back to sleep.
            if (DateTime.Now - started > TimeSpan.FromSeconds(30)) return;
            Log.Info(hibernate ? "Hibernating: done, lid closed, on battery" : "Sleeping: done, lid closed");
            Native.SetSuspendState(hibernate, false, false);
        };
        timer.Start();
    }

    private void MaybeWelcomeBack(DateTime from)
    {
        var now = DateTime.Now;
        var away = now - from;
        if (!Settings.ShowWelcomeBack)
        {
            Log.Info("Welcome back skipped: turned off in settings");
            return;
        }
        if (away < TimeSpan.FromMinutes(Settings.WelcomeBackMinAwayMinutes))
        {
            Log.Info($"Welcome back skipped: away only {away.TotalSeconds:0}s");
            return;
        }
        var recap = Recap.Build(Tracker.Snapshot(), from, now);
        if (!recap.HasNews)
        {
            Log.Info($"Welcome back skipped: nothing from Claude in the {Text.Duration(away)} you were away");
            return;
        }
        Log.Info($"Welcome back: away {Text.Duration(away)}, {recap.Items.Count} session(s)");
        LastRecap = recap;
        WelcomeBack?.Invoke(recap);
    }

    // ---------- away state survives restarts ----------

    private sealed class AwayState
    {
        public DateTime? AwaySince { get; set; }
        public DateTime? LockedAt { get; set; }
    }

    private void SaveAwayState()
    {
        if (Demo) return;
        try { Json.SaveAtomic(Paths.Away, new AwayState { AwaySince = _awaySince, LockedAt = _lockedAt }); }
        catch (Exception ex) { Log.Warn($"Saving away state failed: {ex.Message}"); }
    }

    /// <summary>
    /// Vigil may restart while you're away (an update, a crash). Pick up where it left off:
    /// remember when you left, and notice if the PC is locked right now.
    /// </summary>
    private void RestoreAwayState()
    {
        var saved = Json.Load<AwayState>(Paths.Away);
        var lockedNow = Native.IsSessionLocked();
        if (saved?.AwaySince is { } since && DateTime.Now - since < TimeSpan.FromDays(2)) _awaySince = since;
        if (lockedNow)
        {
            _lockedAt = saved?.LockedAt ?? DateTime.Now;
            _awaySince ??= _lockedAt;
            Log.Info($"Started while locked; away since {_awaySince:t}");
        }
        else if (_awaySince is not null)
        {
            // You came back while Vigil wasn't running: tell you what happened now.
            Log.Info("Started after you came back; showing what happened");
            _ui.BeginInvoke(MarkBack, DispatcherPriority.ApplicationIdle);
        }
        SaveAwayState();
    }

    public Recap BuildRecap(TimeSpan window)
    {
        var now = DateTime.Now;
        var recap = Recap.Build(Tracker.Snapshot(), now - window, now);
        LastRecap = recap;
        return recap;
    }

    private static void SetPresent(bool present)
    {
        try
        {
            if (present) File.WriteAllText(Paths.PresenceFile, DateTime.Now.ToString("O"));
            else if (File.Exists(Paths.PresenceFile)) File.Delete(Paths.PresenceFile);
        }
        catch (Exception ex)
        {
            Log.Warn($"Presence file: {ex.Message}");
        }
    }

    // ---------- persistence ----------

    private void SaveState()
    {
        if (Demo) return;
        try
        {
            Json.SaveAtomic(Paths.State, Tracker.Snapshot().Where(s => s.State != SessionState.Ended || DateTime.Now - s.LastEventAt < TimeSpan.FromHours(24)).ToList());
            _dirty = false;
        }
        catch (Exception ex)
        {
            Log.Warn($"Saving sessions failed: {ex.Message}");
        }
    }

    /// <summary>Test copies: act as if Windows reported this.</summary>
    private void Simulate(string what)
    {
        Log.Info($"Test: {what}");
        switch (what)
        {
            case "screen-off": OnDisplayChanged(false); break;
            case "screen-on": OnDisplayChanged(true); break;
            case "lid-closed": OnLidChanged(false); break;
            case "lid-open": OnLidChanged(true); break;
            // Windows raises this on its own thread, and the phone must hear before it returns.
            case "suspend": Task.Run(() => OnPowerModeChanged(null, new PowerModeChangedEventArgs(PowerModes.Suspend))); break;
            case "shutdown": Sleep.TurningOff(PcSleep.ShuttingDown); break;
            case "resume": OnPowerModeChanged(null, new PowerModeChangedEventArgs(PowerModes.Resume)); break;
        }
        Tick();
    }

    private string StatusJson() => JsonSerializer.Serialize(new
    {
        holding = Power.Holding,
        pc = Sleep.State.ToString(),
        pcSince = Sleep.Since,
        reason = Decision.Kind.ToString(),
        headline = Decision.Headline,
        sessions = Tracker.Snapshot().Select(s => new { s.Id, s.Name, state = s.State.ToString(), s.NowLine, s.Headline, s.Reason, files = s.AllFacts.Files.Count, background = s.Background, avatar = s.AvatarKey, waitingOnTask = s.WaitingOnBackgroundTask, agent = s.Agent }),
        detail = Decision.Detail,
        cpuCapped = Power.CpuCapped,
        safety = Safety.Level.ToString(),
    });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        if (_subscribed)
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
        _server?.Dispose();
        _wifi?.Dispose();
        SaveState();
        Power.Dispose();
        if (!Demo) SetPresent(false);
        (_sensors as IDisposable)?.Dispose();
    }
}

/// <summary>One phone notification.</summary>
public sealed record PhoneMessage(string Title, string Body, string? Url, bool Urgent, string Tag)
{
    /// <summary>Send even while you're at the PC (tests).</summary>
    public bool Always { get; init; }
}
