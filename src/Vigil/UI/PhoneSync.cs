using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;
using Vigil.Core;

namespace Vigil.UI;

/// <summary>
/// "Vigil on your phone". Keeps the phone's copy of the status current: sends within a few seconds
/// of any change, plus a heartbeat every two minutes while anything is running (so the phone can
/// always say how fresh it is). Forwards notifications, by default only while you're away.
/// The pairing key is a 256-bit secret; the relay only ever stores its hash.
/// </summary>
internal sealed class PhoneSync
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Bursts of hook events (a busy session can fire several a second) coalesce into one send.</summary>
    private static readonly TimeSpan Coalesce = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(2);

    private readonly App _app;
    private readonly DispatcherTimer _heartbeat;
    private string _lastSignature = "";
    private string _lastTitle = "";
    private DateTime _lastSent = DateTime.MinValue;
    private bool _pending;
    private bool _sending;
    private bool _scheduled;

    public PhoneSync(App app)
    {
        _app = app;
        app.Engine.Note += m => _ = NotifyAsync(m);
        app.Engine.Sleep.Changed += OnSleepChanged;
        _heartbeat = new DispatcherTimer(Heartbeat, DispatcherPriority.Background, (_, _) => Beat(), app.Dispatcher);
        _heartbeat.Start();
    }

    private AppSettings S => _app.Engine.Settings;
    public bool Enabled => S.PhoneKey.Length > 0 && S.PhonePaired;

    /// <summary>When the last update reached the relay (for the PC's own "phone is up to date" line).</summary>
    public DateTime? LastSent => _lastSent == DateTime.MinValue ? null : _lastSent;

    // ---------- pairing ----------

    /// <summary>The link the QR code holds. Creates the key on first use.</summary>
    public string PairingLink()
    {
        if (S.PhoneKey.Length == 0)
        {
            S.PhoneKey = Base64Url(RandomNumberGenerator.GetBytes(32));
            S.PhonePaired = false;
            S.Save();
        }
        return $"{S.PhoneServer.TrimEnd('/')}/?k={S.PhoneKey}";
    }

    /// <summary>Asks the relay whether the phone finished pairing (turned notifications on).</summary>
    public async Task<bool> CheckPairedAsync()
    {
        if (S.PhoneKey.Length == 0) return false;
        try
        {
            using var req = Request(HttpMethod.Get, "/api/status");
            using var res = await Http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return false;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var paired = doc.RootElement.TryGetProperty("paired", out var p) && p.GetBoolean();
            if (paired != S.PhonePaired)
            {
                S.PhonePaired = paired;
                S.Save();
                if (paired)
                {
                    History.Add("Your iPhone is connected. Vigil will notify it when Claude needs you or finishes.", kind: ActivityKind.You);
                    _lastSignature = "";
                    Changed();
                }
            }
            return paired;
        }
        catch (Exception ex)
        {
            Log.Warn($"Phone pairing check failed: {ex.Message}");
            return S.PhonePaired;
        }
    }

    /// <summary>Forget this phone everywhere and make a fresh key next time.</summary>
    public async Task DisconnectAsync()
    {
        if (S.PhoneKey.Length > 0)
        {
            try
            {
                using var req = Request(HttpMethod.Delete, "/api/pair");
                await Http.SendAsync(req);
            }
            catch (Exception ex) { Log.Warn($"Phone disconnect: {ex.Message}"); }
        }
        S.PhoneKey = "";
        S.PhonePaired = false;
        S.Save();
    }

    // ---------- status ----------

    /// <summary>Call whenever anything changes. Sends a few seconds later if what the phone shows would change.</summary>
    public void Changed()
    {
        if (!Enabled) return;
        _pending = true;
        if (_scheduled) return;
        _scheduled = true;
        var wait = Coalesce - (DateTime.Now - _lastSent);
        if (wait < TimeSpan.FromMilliseconds(300)) wait = TimeSpan.FromMilliseconds(300);
        var timer = new DispatcherTimer(DispatcherPriority.Background, _app.Dispatcher) { Interval = wait };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            _scheduled = false;
            await SendIfChangedAsync(force: false);
        };
        timer.Start();
    }

    /// <summary>
    /// While awake and idle, still check in this often, so the phone can tell "awake, nothing running" from
    /// "asleep or off" (it stops hearing from the PC).
    /// </summary>
    private static readonly TimeSpan IdleBeat = TimeSpan.FromMinutes(5);

    private void Beat()
    {
        if (!Enabled) return;
        // Asleep: the phone already has the last word ("asleep since ..."), and Windows may only be
        // waking the PC for a moment.
        if (_app.Engine.Sleep.State != PcPower.Awake) return;
        var anythingRunning = _app.Engine.Power.Holding ||
                              _app.Engine.Tracker.Snapshot().Any(s => s.State is SessionState.Working or SessionState.NeedsYou);
        if (anythingRunning || DateTime.Now - _lastSent > IdleBeat - TimeSpan.FromSeconds(30)) _ = SendIfChangedAsync(force: true);
    }

    // ---------- asleep, awake, off ----------

    /// <summary>
    /// The PC just went to sleep, woke, or is shutting down: tell the phone now, not in a few seconds,
    /// because a sleeping laptop suspends Vigil almost at once.
    /// </summary>
    private void OnSleepChanged(bool asleep)
    {
        if (!Enabled) return;
        var state = _app.Engine.Sleep.State;
        if (state == PcPower.Off || !_app.Dispatcher.CheckAccess())
        {
            // Shutting down, or Windows is suspending right now: send before returning.
            SendBlocking(TimeSpan.FromSeconds(2.5));
            return;
        }
        _ = SendIfChangedAsync(force: true);
    }

    /// <summary>Builds the status on the UI thread and sends it, waiting at most <paramref name="limit"/>.</summary>
    private void SendBlocking(TimeSpan limit)
    {
        try
        {
            var started = DateTime.Now;
            var built = _app.Dispatcher.CheckAccess()
                ? BuildStatus()
                : _app.Dispatcher.Invoke(BuildStatus, DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(1));
            var left = limit - (DateTime.Now - started);
            // The UI thread didn't get to it in time (Invoke gives up and returns nothing): send nothing rather than an empty status.
            if (left <= TimeSpan.Zero || built.payload is null) return;
            // On a pool thread: waiting on the UI thread for an await that wants the UI thread would never end.
            var sent = Task.Run(() => PostAsync(new { status = built.payload }));
            if (sent.Wait(left) && sent.Result)
            {
                _lastSignature = built.signature;
                _lastTitle = built.title;
                _lastSent = DateTime.Now;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Phone update before sleep failed: {ex.Message}");
        }
    }

    private async Task SendIfChangedAsync(bool force)
    {
        if (_sending) { _pending = true; return; }
        _pending = false;
        var (payload, signature, title) = BuildStatus();
        if (!force && signature == _lastSignature) return;

        _sending = true;
        var ok = false;
        try
        {
            // The overall state changing (e.g. Awake → Asleep) is worth a durable copy on the relay.
            var durable = title != _lastTitle;
            ok = await PostAsync(new { status = payload, durable });
            if (ok)
            {
                _lastSignature = signature;
                _lastTitle = title;
                _lastSent = DateTime.Now;
            }
        }
        finally
        {
            _sending = false;
        }
        if (ok) _retryDelay = TimeSpan.Zero;
        else RetryLater();
        if (_pending) Changed();
    }

    private TimeSpan _retryDelay = TimeSpan.Zero;
    private DispatcherTimer? _retry;

    /// <summary>
    /// The update didn't go through (no internet yet, say right after waking): try again soon, then less
    /// often, so the phone catches up as soon as the PC is back online.
    /// </summary>
    private void RetryLater()
    {
        if (_retry is not null || _app.Engine.Sleep.State != PcPower.Awake) return;
        _retryDelay = _retryDelay == TimeSpan.Zero ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(Math.Min(120, _retryDelay.TotalSeconds * 2));
        _retry = new DispatcherTimer(DispatcherPriority.Background, _app.Dispatcher) { Interval = _retryDelay };
        _retry.Tick += (_, _) =>
        {
            _retry?.Stop();
            _retry = null;
            _ = SendIfChangedAsync(force: true);
        };
        _retry.Start();
    }

    private async Task NotifyAsync(PhoneMessage m)
    {
        if (!Enabled) return;
        if (S.PhoneOnlyWhenAway && !Away && !m.Always) return;
        // Send the fresh status along with the notification, so opening it shows the latest.
        var (payload, signature, title) = BuildStatus();
        var ok = await PostAsync(new
        {
            status = payload,
            notify = new { title = m.Title, body = m.Body, tag = m.Tag, url = m.Url, urgent = m.Urgent, badge = NeedsYouCount() },
        });
        if (ok)
        {
            _lastSignature = signature;
            _lastTitle = title;
            _lastSent = DateTime.Now;
        }
    }

    /// <summary>Not at the PC: it's locked, the lid is closed, or the screen has gone off.</summary>
    private bool Away => _app.Engine.IsLocked || _app.Engine.LidOpen == false || _app.Engine.Sleep.DisplayOn == false;

    /// <summary>The red number on the phone's Vigil icon.</summary>
    private int NeedsYouCount() => _app.Engine.Tracker.Snapshot().Count(s => s.State == SessionState.NeedsYou);

    /// <summary>"Send a test notification" from the PC: always delivered, even while you're at the PC.</summary>
    public Task SendTestAsync() => NotifyAsync(new PhoneMessage(
        "Vigil", "Test notification. If you can read this on your Lock Screen, you're set.", null, true, "test") { Always = true });

    /// <summary>
    /// Everything the phone shows, already in plain words, with real timestamps so the phone can show
    /// live durations ("working for 14 min") without asking the PC again.
    /// </summary>
    private (object payload, string signature, string title) BuildStatus()
    {
        var engine = _app.Engine;
        var st = Status.Of(_app);
        var now = DateTime.Now;
        // Background runs have no tab to go back to, so the phone leaves them out too.
        var all = engine.Tracker.Snapshot().Where(s => s.Background != true).ToList();
        App.AssignColors(engine);

        // What matters now: everything working or waiting on you, problems from the last 12 hours,
        // and the five most recent finished sessions. Idle sessions you never prompted are left out.
        var active = all.Where(s => s.State is SessionState.Working or SessionState.NeedsYou or SessionState.Paused
                                    || (s.State is SessionState.Failed or SessionState.Quiet && now - s.LastEventAt < TimeSpan.FromHours(12)));
        var finished = all.Where(s => (s.State is SessionState.Done || (s.State == SessionState.Ended && s.CurrentTurn is { Outcome: SessionState.Done }))
                                      && now - s.LastEventAt < TimeSpan.FromHours(12))
                          .OrderByDescending(s => s.LastEventAt).Take(5);
        var sessions = active.Concat(finished)
            .OrderBy(SessionVM.Rank).ThenByDescending(s => s.LastEventAt)
            .Take(12)
            .Select(s =>
            {
                var turn = s.CurrentTurn;
                var facts = s.FactsSince(now - TimeSpan.FromHours(24));
                return new
                {
                    id = ShortId(s.Id),
                    name = s.Name,
                    folder = s.AvatarKey,
                    hue = Avatars.HueIndex(s.AvatarKey),
                    state = s.State == SessionState.Ended && turn is { Outcome: SessionState.Done } ? "done" : WireState(s.State),
                    stateLabel = Palette.StateLabel(s),
                    since = Iso(s.StateSince),
                    turnStartedAt = turn is not null ? Iso(turn.StartedAt) : null,
                    lastActivityAt = Iso(s.LastEventAt),
                    now = s.State == SessionState.Working ? s.NowLine : null,
                    task = turn?.Prompt is { Length: > 0 } p ? Text.Clip(p, 600) : null,
                    summary = s.State == SessionState.Working ? null : s.Summary,
                    reason = s.Reason,
                    facts = string.Join(" · ", facts.Chips().Where(c => !c.Text.StartsWith("Asked:", StringComparison.Ordinal)).Select(c => c.Text)),
                    question = facts.Question,
                    steps = s.Timeline.AsEnumerable().Reverse().Take(12).Select(e => new
                    {
                        at = Iso(e.At),
                        text = e.Kind == EntryKind.Edit && e.Count > 2 ? $"Edited {e.Count} files" : e.Kind == EntryKind.Edit ? $"Edited {e.Text}" : e.Text,
                        kind = e.Kind.ToString().ToLowerInvariant(),
                    }).ToList(),
                    url = s.RemoteUrl,
                    agent = s.Agent == Session.Codex ? "Codex" : null,
                    // What the phone card shows on one line, and the time/elapsed beside it.
                    line = SessionVM.From(s).Line,
                    meta = SessionVM.From(s).When, // the phone shows the agent on its own
                    // For the session screen on the phone.
                    startedAt = Iso(s.StartedAt),
                    prompts = s.Turns.Count,
                    said = turn?.LastMessage is { Length: > 0 } m && s.State != SessionState.Working ? Text.Clip(Text.Collapse(m), 700) : null,
                    files = facts.Files.Select(Path.GetFileName).Where(f => !string.IsNullOrEmpty(f)).Take(12).ToList(),
                    moreFiles = Math.Max(0, facts.Files.Count - 12),
                    commits = facts.Commits.TakeLast(5).ToList(),
                    helpers = s.ActiveSubagents,
                    background = s.AwaitedTasks.Take(4).ToList(),
                    worked = (int)Activity.Spans(s, now - TimeSpan.FromHours(24), now, now).Where(x => x.Kind == SpanKind.Working).Sum(x => x.Length.TotalSeconds),
                    // For the activity chart: when Claude worked or waited on you in the last 12 hours.
                    spans = Activity.Spans(s, now - TimeSpan.FromHours(12), now, now).TakeLast(12).Select(x => new
                    {
                        start = Iso(x.Start),
                        end = Iso(x.End),
                        kind = x.Kind == SpanKind.Working ? "working" : "waiting",
                    }).ToList(),
                };
            }).ToList();

        var battery = System.Windows.Forms.SystemInformation.PowerStatus;
        var hasBattery = battery.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery;
        var payload = new
        {
            version = 2,
            updatedAt = Iso(now),
            pc = Environment.MachineName,
            away = Away,
            sleep = SleepStatus(engine),
            title = st.Title,
            line = st.Line,
            tone = Tone(st),
            holding = engine.Power.Holding,
            power = new
            {
                lid = engine.LidOpen is { } open ? (open ? "open" : "closed") : null,
                source = hasBattery ? (battery.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline ? "battery" : "plugged in") : "plugged in",
                battery = hasBattery && battery.BatteryLifePercent is >= 0 and <= 1 ? (int?)Math.Round(battery.BatteryLifePercent * 100) : null,
                heat = engine.Safety.Level switch { SafetyLevel.Hot => "hot", SafetyLevel.Warm => "warm", _ => "normal" },
            },
            color = st.ColorKey switch { "Blue" => "blue", "Lavender" => "blue", "Orange" => "orange", "Green" => "green", "Red" => "red", "Text1" => "white", _ => "gray" },
            icon = st.Icon switch
            {
                TrayState.Idle => "idle",
                TrayState.NeedsYou => "needs",
                TrayState.Hot => "hot",
                TrayState.Manual => "manual",
                TrayState.Paused => "paused",
                _ => "working",
            },
            facts = Status.Facts(engine),
            recap = BuildRecap(all, now),
            counts = new
            {
                working = all.Count(s => s.State == SessionState.Working),
                needsYou = all.Count(s => s.State == SessionState.NeedsYou),
                done = sessions.Count(s => s.state == "done"),
            },
            sessions,
        };

        // What the phone shows: state, each session's state, live line and summary.
        var signature = string.Join("|", st.Title, st.Line, engine.Power.Holding, engine.LidOpen, engine.IsLocked, engine.Safety.Level,
            engine.Sleep.State, engine.Sleep.Since, engine.Sleep.DisplayOn,
            string.Join(";", sessions.Select(s => $"{s.id}:{s.state}:{s.now}:{s.summary}:{s.reason}:{s.facts}:{s.steps.Count}")));
        return (payload, signature, st.Title);
    }

    /// <summary>
    /// Awake, asleep or off, since when, and how long it has been idle. The phone shows this as the PC's
    /// current state; "asleep" is the PC's last word before Windows suspended it.
    /// </summary>
    private static object SleepStatus(Engine engine)
    {
        var sleep = engine.Sleep;
        var idle = sleep.State == PcPower.Awake ? Native.IdleTime() : null;
        return new
        {
            state = sleep.State switch { PcPower.Asleep => "asleep", PcPower.Off => "off", _ => "awake" },
            since = Iso(sleep.Since),
            why = sleep.OffReason,
            screen = sleep.DisplayOn switch { true => "on", false => "off", _ => null },
            idleMinutes = idle is { } i ? (int?)Math.Floor(i.TotalMinutes) : null,
            sleptAt = sleep.LastSleptAt is { } s ? Iso(s) : null,
            wokeAt = sleep.LastWokeAt is { } w ? Iso(w) : null,
            afterWork = sleep.State == PcPower.Asleep && sleep.SleptAfterWork,
        };
    }

    /// <summary>"What happened" for the phone: the last 24 hours, needs-you first.</summary>
    private static object BuildRecap(IReadOnlyList<Session> all, DateTime now)
    {
        var recap = Recap.Build(all, now - TimeSpan.FromHours(24), now);
        return new
        {
            title = recap.Title,
            range = "last 24 hours",
            items = recap.Items.Take(8).Select(i =>
            {
                var vm = new RecapItemVM { Item = i };
                return new
                {
                    id = ShortId(i.Session.Id),
                    name = i.Session.Name,
                    folder = i.Session.AvatarKey,
                    hue = Avatars.HueIndex(i.Session.AvatarKey),
                    state = WireState(i.State),
                    stateText = vm.StateText,
                    headline = vm.Headline,
                    chips = i.Chips.Select(c => new { text = c.Text, tone = c.Tone.ToString().ToLowerInvariant() }).ToList(),
                    url = i.Session.RemoteUrl,
                };
            }).ToList(),
        };
    }

    private static string Tone(Status st) => st.Icon switch
    {
        TrayState.Hot => st.ColorKey == "Red" ? "danger" : "warning",
        TrayState.NeedsYou => "needs",
        TrayState.Working => st.ColorKey == "Green" ? "done" : "working",
        TrayState.Manual => "manual",
        TrayState.Paused => "paused",
        _ => "idle",
    };

    private static string WireState(SessionState s) => s switch
    {
        SessionState.Working => "working",
        SessionState.NeedsYou => "needs",
        SessionState.Done => "done",
        SessionState.Failed => "failed",
        SessionState.Paused => "paused",
        SessionState.Quiet => "quiet",
        SessionState.Ended => "ended",
        _ => "ready",
    };

    private static string Iso(DateTime local) => local.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

    private static string ShortId(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..10].ToLowerInvariant();

    // ---------- plumbing ----------

    private async Task<bool> PostAsync(object body)
    {
        // Not holding the PC awake: hold it for the moment it takes to send, so a laptop that's about to
        // sleep can't suspend Vigil mid-send (the phone would keep the last thing it heard).
        var engine = _app.Engine;
        using var wake = engine.Power.Holding || engine.Demo || engine.TestInstance ? null : BriefWake.Hold("Vigil: updating your phone");
        try
        {
            using var req = Request(HttpMethod.Post, "/api/update");
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var res = await Http.SendAsync(req);
            if (!res.IsSuccessStatusCode)
            {
                Log.Warn($"Phone update failed: {(int)res.StatusCode}");
                return false;
            }
            var text = await res.Content.ReadAsStringAsync();
            if (text.Contains("push failed", StringComparison.Ordinal)) Log.Warn($"Phone push: {text}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Phone update failed: {ex.Message}");
            return false;
        }
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, S.PhoneServer.TrimEnd('/') + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", S.PhoneKey);
        return req;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
