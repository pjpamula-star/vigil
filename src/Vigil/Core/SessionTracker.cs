using System.Text.RegularExpressions;
using System.Text.Json;

namespace Vigil.Core;

/// <summary>
/// Turns Claude Code hook events into a picture of every session.
/// The app calls it only from the UI thread, so the UI never sees a half-updated session.
/// The lock is a second line of defense for tests and background readers.
/// </summary>
public sealed class SessionTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Session> _sessions = new();
    private readonly Func<DateTime> _now;

    /// <summary>Hook events from this folder are ignored (it's where Vigil runs its own summarizer).</summary>
    public string? IgnoreCwd { get; set; }

    public TimeSpan QuietAfter { get; set; } = TimeSpan.FromMinutes(45);

    /// <summary>Raised after any session changes.</summary>
    public event Action<Session>? Changed;

    /// <summary>Raised when a turn ends (Stop or StopFailure). The summarizer listens here.</summary>
    public event Action<Session, Turn>? TurnEnded;

    /// <summary>Raised when a session newly needs you.</summary>
    public event Action<Session>? NeedsYou;

    public SessionTracker(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.Now);

    public IReadOnlyList<Session> Snapshot()
    {
        lock (_gate) return _sessions.Values.ToList();
    }

    public Session? Get(string id)
    {
        lock (_gate) return _sessions.GetValueOrDefault(id);
    }

    public void Load(IEnumerable<Session> sessions)
    {
        lock (_gate)
        {
            foreach (var s in sessions)
            {
                if (s.Background is null || s.StartCwd is null) LearnFromTranscript(s);
                // Saved by an older Vigil, which called a turn with a background task left "working".
                if (s.State == SessionState.Working && s.NowLine?.StartsWith("Waiting for a background task", StringComparison.Ordinal) == true)
                {
                    s.State = SessionState.Done;
                    s.NowLine = null;
                }
                // After a restart, anything that was mid-flight is unknown; don't keep the PC awake for it.
                // After a restart Vigil missed whatever happened meanwhile. A transcript that changed in
                // the last few minutes means Claude is still at it: keep holding the PC awake.
                if (s.State is SessionState.Working && !TranscriptWrittenWithin(s, TimeSpan.FromMinutes(5)))
                {
                    s.State = SessionState.Quiet;
                    s.NowLine = null;
                    s.Reason = "No activity since Vigil restarted";
                }
                else if (s.State is SessionState.Working)
                {
                    s.LastEventAt = _now();
                }
                _sessions[s.Id] = s;
            }
        }
    }

    /// <summary>Insert a session as-is (demo mode and tests).</summary>
    public void Put(Session s)
    {
        lock (_gate) _sessions[s.Id] = s;
        Changed?.Invoke(s);
    }

    public void Remove(string id)
    {
        lock (_gate) _sessions.Remove(id);
    }

    /// <summary>Feed one hook payload (the JSON Claude Code, or Codex, posts).</summary>
    public void Handle(JsonElement p, string agent = Session.Claude)
    {
        var id = p.Str("session_id");
        var evt = p.Str("hook_event_name");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(evt)) return;

        var cwd = p.Str("cwd") ?? "";
        if (IgnoreCwd is not null && cwd.TrimEnd('\\', '/').Equals(IgnoreCwd.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            return;

        var now = _now();
        Session session;
        Turn? endedTurn = null;
        var becameNeedsYou = false;

        lock (_gate)
        {
            if (!_sessions.TryGetValue(id, out session!))
            {
                session = new Session { Id = id, Agent = agent, Cwd = cwd, StartCwd = cwd.Length > 0 ? cwd : null, StartedAt = now, StateSince = now };
                // Codex's transcript isn't Claude's format; a Codex session is always one you started.
                if (agent == Session.Codex) session.Background = false;
                _sessions[id] = session;
            }
            if (cwd.Length > 0) session.Cwd = cwd;
            session.TranscriptPath = p.Str("transcript_path") ?? session.TranscriptPath;
            if (session.Background is null || session.StartCwd is null) LearnFromTranscript(session);
            session.LastEventAt = now;

            switch (evt)
            {
                case "SessionStart":
                    if (session.State is SessionState.Ended or SessionState.Quiet) SetState(session, SessionState.Ready, now);
                    break;

                case "UserPromptSubmit":
                {
                    var prompt = p.Str("prompt") ?? "";
                    // A prompt that arrives mid-turn (queued message) continues the same turn.
                    if (session.CurrentTurn is not { EndedAt: null })
                        session.Turns.Add(new Turn { StartedAt = now, Prompt = prompt });
                    if (session.Turns.Count > 50) session.Turns.RemoveRange(0, session.Turns.Count - 50);
                    AddEntry(session, new TimelineEntry { At = now, Kind = EntryKind.Prompt, Text = $"You asked: “{Text.Clip(prompt, 90)}”" });
                    session.Reason = null;
                    session.NowLine = "Reading your request";
                    SetState(session, SessionState.Working, now);
                    break;
                }

                case "PreToolUse":
                {
                    var tool = p.Str("tool_name") ?? "";
                    var input = p.Obj("tool_input") ?? default;
                    session.NowLine = ToolInterpreter.NowLine(tool, input);
                    if (!RecentlyStopped(session, now)) ResumeWorking(session, now);
                    break;
                }

                case "PostToolUse":
                case "PostToolUseFailure":
                {
                    var tool = p.Str("tool_name") ?? "";
                    var input = p.Obj("tool_input") ?? default;
                    var (facts, entries) = evt == "PostToolUse"
                        ? ToolInterpreter.FromSuccess(tool, input, p.Obj("tool_response"), now)
                        : ToolInterpreter.FromFailure(tool, input, p.Str("error"), p.Obj("is_interrupt")?.ValueKind == JsonValueKind.True, now);
                    EnsureTurn(session, now).Facts.Merge(facts);
                    foreach (var e in entries) AddEntry(session, e);
                    if (!RecentlyStopped(session, now)) ResumeWorking(session, now);
                    break;
                }

                case "PermissionRequest":
                {
                    var tool = p.Str("tool_name") ?? "";
                    var input = p.Obj("tool_input") ?? default;
                    session.Reason = PermissionReason(tool, input);
                    becameNeedsYou = SetState(session, SessionState.NeedsYou, now);
                    if (becameNeedsYou) AddEntry(session, new TimelineEntry { At = now, Kind = EntryKind.NeedsYou, Text = session.Reason });
                    break;
                }

                case "Notification":
                {
                    var type = p.Str("notification_type") ?? "";
                    if (type is "permission_prompt" or "elicitation_dialog" or "elicitation_url_dialog" or "agent_needs_input")
                    {
                        if (session.State != SessionState.NeedsYou)
                        {
                            session.Reason = Text.Clip(p.Str("message") ?? "Waiting for your answer", 120);
                            becameNeedsYou = SetState(session, SessionState.NeedsYou, now);
                            if (becameNeedsYou) AddEntry(session, new TimelineEntry { At = now, Kind = EntryKind.NeedsYou, Text = session.Reason });
                        }
                    }
                    else if (type == "quota_auto_resume_fired")
                    {
                        session.Reason = null;
                        session.NowLine = "Back at it after the usage limit reset";
                        SetState(session, SessionState.Working, now);
                    }
                    else if (type == "quota_auto_resume_disabled" || type == "quota_auto_resume_stale")
                    {
                        session.Reason = "Usage limit: waiting for you to continue";
                        SetState(session, SessionState.Failed, now);
                    }
                    break;
                }

                case "SubagentStart":
                    session.ActiveSubagents++;
                    ResumeWorking(session, now);
                    break;

                case "SubagentStop":
                    session.ActiveSubagents = Math.Max(0, session.ActiveSubagents - 1);
                    break;

                case "Stop":
                {
                    var message = p.Str("last_assistant_message");
                    var turn = EnsureTurn(session, now);
                    turn.LastMessage = message;
                    turn.EndedAt = now;
                    var question = Text.TrailingQuestion(message);
                    if (question is not null) turn.Facts.Question = question;

                    session.BackgroundTasks = ReadBackgroundTasks(p);
                    session.TasksScreened = true;
                    session.ActiveSubagents = 0;
                    // Done, even with a background task still running: Claude finished the turn and
                    // is idle. (It picks up again by itself when the task ends; the keep-awake policy
                    // waits a while for that.)
                    turn.Outcome = SessionState.Done;
                    session.NowLine = null;
                    session.Reason = question;
                    SetState(session, SessionState.Done, now);
                    AddEntry(session, new TimelineEntry
                    {
                        At = now,
                        Kind = question is null ? EntryKind.Done : EntryKind.Question,
                        Text = question is null ? $"Finished: {Text.FirstSentence(message, 110)}" : $"Asked you: {question}",
                    });
                    endedTurn = turn;
                    break;
                }

                case "StopFailure":
                {
                    var error = p.Str("error") ?? "unknown";
                    var turn = EnsureTurn(session, now);
                    turn.EndedAt = now;
                    turn.LastMessage = p.Str("last_assistant_message");
                    session.NowLine = null;
                    if (error == "rate_limit")
                    {
                        turn.Outcome = SessionState.Paused;
                        session.Reason = "Hit the usage limit. Claude continues when it resets.";
                        SetState(session, SessionState.Paused, now);
                        AddEntry(session, new TimelineEntry { At = now, Kind = EntryKind.Paused, Text = "Paused: usage limit reached" });
                    }
                    else
                    {
                        turn.Outcome = SessionState.Failed;
                        session.Reason = FailureReason(error, p.Str("error_details"));
                        turn.Facts.Errors++;
                        SetState(session, SessionState.Failed, now);
                        AddEntry(session, new TimelineEntry { At = now, Kind = EntryKind.Failed, Text = $"Stopped: {session.Reason}" });
                    }
                    endedTurn = turn;
                    break;
                }

                case "Interrupt":
                {
                    // Codex: you pressed Esc mid-turn. The turn is over and it's your move.
                    var turn = EnsureTurn(session, now);
                    turn.EndedAt = now;
                    turn.Outcome = SessionState.Done;
                    turn.EndNote = "You stopped it";
                    session.NowLine = null;
                    session.Reason = null;
                    session.ActiveSubagents = 0;
                    SetState(session, SessionState.Done, now);
                    AddEntry(session, new TimelineEntry { At = now, Kind = EntryKind.Done, Text = "You stopped it" });
                    break;
                }

                case "SessionEnd":
                    session.NowLine = null;
                    session.ActiveSubagents = 0;
                    SetState(session, SessionState.Ended, now);
                    break;
            }
        }

        Changed?.Invoke(session);
        if (becameNeedsYou) NeedsYou?.Invoke(session);
        if (endedTurn is not null) TurnEnded?.Invoke(session, endedTurn);
    }

    /// <summary>How long Claude must have been idle, with no hook since, before Vigil ends a turn it never heard end.</summary>
    public static readonly TimeSpan IdleSettle = TimeSpan.FromSeconds(15);

    /// <summary>Without Claude's session list, how long a session must be silent before Vigil reads its transcript for an Esc.</summary>
    public static readonly TimeSpan InterruptSettle = TimeSpan.FromSeconds(30);

    /// <summary>Claude's process must be seen gone this long (two checks at least) before its session counts as closed.</summary>
    public static readonly TimeSpan GoneSettle = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, DateTime> _goneSince = new();

    /// <summary>
    /// Call periodically. Marks long-silent "working" sessions as quiet so they can't hold the PC forever.
    /// With <paramref name="live"/> (Claude Code's own list of running sessions), also ends turns Claude never
    /// reported ending: Esc sends no Stop hook, and a closed window sends nothing.
    /// </summary>
    public void Tick(IReadOnlyDictionary<string, LiveClaude>? live = null)
    {
        var now = _now();
        List<Session> changed = new();
        lock (_gate)
        {
            foreach (var s in _sessions.Values)
            {
                if (live is not null && Reconcile(s, live.GetValueOrDefault(s.Id), now))
                {
                    changed.Add(s);
                    continue;
                }
                // Claude saying it's busy (one long command, say) outweighs the silence, for a few hours at most.
                var busy = ClaudeSaysBusy(s, live?.GetValueOrDefault(s.Id), now);
                if (s.State == SessionState.Working && now - s.LastEventAt > QuietAfter && !TranscriptRecentlyWritten(s, now) && !busy)
                {
                    s.NowLine = null;
                    s.Reason = $"No activity for {(int)QuietAfter.TotalMinutes} minutes";
                    SetState(s, SessionState.Quiet, now);
                    changed.Add(s);
                }
                // Forget sessions that ended more than a day ago.
            }
            foreach (var id in _sessions.Values.Where(s => s.State is SessionState.Ended && now - s.LastEventAt > TimeSpan.FromHours(24)).Select(s => s.Id).ToList())
                _sessions.Remove(id);
        }
        foreach (var s in changed) Changed?.Invoke(s);
    }

    /// <summary>How long Claude's own "busy" can stand in for hooks. Past this, a silent session goes quiet anyway.</summary>
    public static readonly TimeSpan BusyTrust = TimeSpan.FromHours(3);

    private static bool ClaudeSaysBusy(Session s, LiveClaude? reg, DateTime now) =>
        reg is { Alive: true, Status: "busy" } && now - s.LastEventAt < BusyTrust;

    /// <summary>True if the session changed.</summary>
    private bool Reconcile(Session s, LiveClaude? reg, DateTime now)
    {
        // Vigil called it quiet (no news for a while, or it restarted mid-run), but Claude says it's busy:
        // one long command, say. It's working.
        if (s.State == SessionState.Quiet && s.Agent == Session.Claude && ClaudeSaysBusy(s, reg, now))
        {
            s.Reason = null;
            SetState(s, SessionState.Working, now);
            return true;
        }

        if (s.Agent != Session.Claude || s.State is not (SessionState.Working or SessionState.NeedsYou or SessionState.Quiet))
        {
            _goneSince.Remove(s.Id);
            return false;
        }

        if (reg is null)
        {
            // Not in Claude's list (an older Claude Code, or the list is elsewhere): an Esc still leaves its mark
            // in the transcript.
            if (s.State != SessionState.Quiet && now - s.LastEventAt >= InterruptSettle && TranscriptScanner.EndsWithInterrupt(s.TranscriptPath))
            {
                EndTurnUnannounced(s, now, interrupted: true);
                return true;
            }
            return false;
        }

        if (!reg.Alive)
        {
            // Seen gone once is a maybe (a file from an old window); still gone ten seconds on, with no hook
            // since, means the window was closed.
            var since = _goneSince.TryGetValue(s.Id, out var first) && first >= s.LastEventAt ? first : now;
            _goneSince[s.Id] = since;
            if (now - since < GoneSettle) return false;
            _goneSince.Remove(s.Id);
            if (s.CurrentTurn is { EndedAt: null } open)
            {
                open.EndedAt = now;
                open.EndNote = "Closed before it finished";
            }
            s.NowLine = null;
            s.Reason = null;
            s.ActiveSubagents = 0;
            SetState(s, SessionState.Ended, now);
            AddEntry(s, new TimelineEntry { At = now, Kind = EntryKind.Done, Text = "Claude was closed" });
            Log.Info($"Session {s.Id[..Math.Min(8, s.Id.Length)]}: Claude's window was closed");
            return true;
        }
        _goneSince.Remove(s.Id);

        // Idle since after the last hook, for long enough that a Stop on its way would have arrived.
        if (reg.Status is "idle" or "shell" && reg.StatusAt >= s.LastEventAt && now - reg.StatusAt >= IdleSettle)
        {
            EndTurnUnannounced(s, now, interrupted: TranscriptScanner.EndsWithInterrupt(s.TranscriptPath));
            return true;
        }
        return false;
    }

    /// <summary>The turn ended without a Stop hook: you pressed Esc, or said no at a prompt.</summary>
    private static void EndTurnUnannounced(Session s, DateTime now, bool interrupted)
    {
        var turn = EnsureTurn(s, now);
        turn.EndedAt ??= now;
        turn.Outcome = SessionState.Done;
        turn.EndNote = interrupted ? "You stopped it" : "Stopped";
        s.NowLine = null;
        s.Reason = null;
        s.ActiveSubagents = 0;
        s.BackgroundTasks.Clear();
        s.TasksScreened = true;
        SetState(s, SessionState.Done, now);
        AddEntry(s, new TimelineEntry { At = now, Kind = EntryKind.Done, Text = interrupted ? "You stopped it" : "Stopped" });
        Log.Info($"Session {s.Id[..Math.Min(8, s.Id.Length)]}: turn ended without a Stop hook ({(interrupted ? "Esc" : "idle")})");
    }

    public void SetHeadline(Session session, Turn turn, string headline)
    {
        lock (_gate) turn.Headline = headline;
        Changed?.Invoke(session);
    }

    public void SetTitleAndUrl(Session session, string? title, string? url)
    {
        var dirty = false;
        lock (_gate)
        {
            if (title is { Length: > 0 } && title != session.AiTitle) { session.AiTitle = title; dirty = true; }
            if (url is { Length: > 0 } && url != session.RemoteUrl) { session.RemoteUrl = url; dirty = true; }
        }
        if (dirty) Changed?.Invoke(session);
    }

    /// <summary>Where the session started and whether it's a background run, from its transcript.</summary>
    private static void LearnFromTranscript(Session s)
    {
        var (background, startCwd) = TranscriptScanner.Head(s.TranscriptPath);
        s.Background ??= background;
        s.StartCwd ??= startCwd;
    }

    private bool TranscriptWrittenWithin(Session s, TimeSpan window)
    {
        try
        {
            return s.TranscriptPath is { } t && File.Exists(t) && _now() - File.GetLastWriteTime(t) < window;
        }
        catch
        {
            return false;
        }
    }

    private bool TranscriptRecentlyWritten(Session s, DateTime now)
    {
        try
        {
            return s.TranscriptPath is { } t && File.Exists(t) && now - File.GetLastWriteTime(t) < QuietAfter;
        }
        catch
        {
            return false;
        }
    }

    private static bool SetState(Session s, SessionState state, DateTime now)
    {
        if (s.State == state) return false;
        s.State = state;
        s.StateSince = now;
        return true;
    }

    /// <summary>Tool activity means Claude is working again (e.g. after you approved a prompt).</summary>
    private static void ResumeWorking(Session s, DateTime now)
    {
        // Ended too: an event proves Claude is running, even if Vigil had concluded its window was closed.
        if (s.State is SessionState.NeedsYou or SessionState.Ready or SessionState.Quiet or SessionState.Paused or SessionState.Done or SessionState.Failed or SessionState.Ended)
        {
            s.Reason = null;
            SetState(s, SessionState.Working, now);
        }
    }

    /// <summary>
    /// Tool hooks run in the background, so one can land just after Stop.
    /// Don't let a late straggler flip a finished session back to "working".
    /// </summary>
    private static bool RecentlyStopped(Session s, DateTime now) =>
        s.State is SessionState.Done or SessionState.Failed or SessionState.Paused or SessionState.Ended && now - s.StateSince < TimeSpan.FromSeconds(5);

    private static Turn EnsureTurn(Session s, DateTime now)
    {
        if (s.CurrentTurn is { EndedAt: null } t) return t;
        // Activity without a prompt (e.g. a background task woke the session) starts an implicit turn.
        var turn = new Turn { StartedAt = now, Prompt = "" };
        s.Turns.Add(turn);
        return turn;
    }

    private static void AddEntry(Session s, TimelineEntry e)
    {
        // Collapse runs of edits into one line: "Edited 4 files".
        if (e.Kind == EntryKind.Edit && s.Timeline.Count > 0 && s.Timeline[^1] is { Kind: EntryKind.Edit } last)
        {
            if (!last.Text.Contains(e.Text, StringComparison.OrdinalIgnoreCase))
            {
                last.Count++;
                last.Text = last.Count == 2 ? $"{last.Text}, {e.Text}" : last.Text;
            }
            last.At = e.At;
            return;
        }
        s.Timeline.Add(e);
        if (s.Timeline.Count > 300) s.Timeline.RemoveRange(0, s.Timeline.Count - 300);
    }

    private static List<string> ReadBackgroundTasks(JsonElement p)
    {
        var list = new List<string>();
        if (p.Obj("background_tasks") is { ValueKind: JsonValueKind.Array } arr)
        {
            foreach (var t in arr.EnumerateArray())
            {
                var status = t.Str("status") ?? "";
                if (status is "completed" or "failed" or "killed" or "stopped") continue;
                if (NeverEnds(t.Str("type"), t.Str("description"), t.Str("command"))) continue;
                var label = t.Str("description") ?? t.Str("command") ?? t.Str("type") ?? "a background task";
                list.Add(Text.Clip(label, 60));
            }
        }
        return list;
    }

    /// <summary>Commands that run until you stop them: dev servers, file watchers, log followers, tunnels.</summary>
    private static readonly Regex EndlessCommand = new(
        @"\b(npm|pnpm|yarn|bun)\s+(run\s+)?(dev|start|serve|preview|watch)\b" +
        @"|\b(vite|nodemon|live-server|http-server|browser-sync|webpack-dev-server)\b(?!\s+(build|optimize)\b)" +
        @"|\b(webpack|ng|jekyll|mkdocs|hugo|docusaurus|zola)\s+serve(r)?\b" +
        @"|\b(next|nuxt|nuxi|astro|remix|gatsby|vercel|wrangler|netlify|expo|storybook)\s+(dev|start|preview|develop)\b" +
        @"|\b(npx|bunx|pnpm\s+dlx)\s+serve\b" +
        @"|\bhttp\.server\b|\bmanage\.py\s+runserver\b|\b(uvicorn|gunicorn|hypercorn|daphne)\b|\bflask\s+run\b" +
        @"|\brails\s+s(erver)?\b|\bphp\s+-S\b|\bjupyter\s+(lab|notebook)\b|\bstreamlit\s+run\b" +
        @"|\b(dotnet|cargo)\s+watch\b|--watch\b|\btsc\s+-w\b" +
        @"|\btail\s+(-[a-zA-Z]*f|--follow)\b|\bGet-Content\b[^|;&]*\s-Wait\b" +
        @"|\bdocker(-|\s+)compose\s+up\b(?![^|;&]*\s(-d|--detach)\b)" +
        @"|\b(ngrok|stripe\s+listen|cloudflared\s+tunnel|kubectl\s+port-forward|firebase\s+emulators:start)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The same, from how Claude described the task.</summary>
    private static readonly Regex EndlessDescription = new(
        @"\b(dev|development|preview|local|web|http|static|api|frontend|backend|vite|next|django|flask|file)\s+server\b(?!\s+(tests?|specs?|build)\b)" +
        @"|\b(start|starts|run|runs|launch|serve|keep|restart|boot)\b(\s+\w+){0,3}\s+server\b(?!\s+(tests?|specs?|build|side)\b)" +
        @"|\bwatch(es|ing)?\s+(mode|for\s+(file\s+)?changes)\b|\bfile\s+watcher\b" +
        @"|\blive\s+updates\s+for\b|\b(tail|tailing|follow|following)\s+(the\s+)?logs?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// A task Claude isn't waiting for: it runs until you stop it, so Claude never picks up again because
    /// of it. Holding the PC awake for one would say "working" long after Claude finished.
    /// Types are Claude Code's labels (see "Stop input" in its hooks reference). Monitors are event streams,
    /// such as a watch on an artifact. Dreams, auto-mode scans and memory imports are Claude Code's own
    /// housekeeping. Shell tasks (including the Monitor tool) are judged by their command. Subagents,
    /// workflows, teammates, MCP tasks and cloud sessions all finish and report back.
    /// </summary>
    internal static bool NeverEnds(string? type, string? description, string? command) => type switch
    {
        "monitor" or "dream" or "auto-mode scan" or "memory import" => true,
        "shell" => command is { Length: > 0 } ? EndlessCommand.IsMatch(command) : EndlessDescription.IsMatch(description ?? ""),
        null or "" => LooksEndless(command) || LooksEndless(description),
        _ => false,
    };

    /// <summary>Judges a task by one line of text (a command or a description), when that's all there is.</summary>
    internal static bool LooksEndless(string? text) =>
        text is { Length: > 0 } && (EndlessCommand.IsMatch(text) || EndlessDescription.IsMatch(text));

    private static string PermissionReason(string tool, JsonElement input) => tool switch
    {
        "Bash" or "PowerShell" => $"Wants to run {Text.Clip(input.Str("command"), 70)}",
        "Edit" or "MultiEdit" or "Write" => $"Wants to edit {ToolInterpreter.ShortPath(input.Str("file_path"))}",
        "WebFetch" => $"Wants to open {Text.Clip(input.Str("url"), 60)}",
        "ExitPlanMode" => "Has a plan ready for your approval",
        "AskUserQuestion" => "Has a question for you",
        _ => $"Wants to use {ToolInterpreter.NowLine(tool, input).Replace("Using ", "")}",
    };

    private static string FailureReason(string error, string? details) => error switch
    {
        "overloaded" => "Claude's servers were overloaded",
        "authentication_failed" or "oauth_org_not_allowed" => "Signed out of Claude. Log in again",
        "billing_error" or "account_on_hold" => "There's a billing problem on the account",
        "server_error" => "Claude hit a server error",
        "max_output_tokens" => "The response got too long",
        "model_not_found" => "The selected model isn't available",
        _ => Text.Clip(details ?? "Something went wrong", 80),
    };
}
