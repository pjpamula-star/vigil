using Vigil.Core;

namespace Vigil.Tests;

public class SessionTrackerTests : TempRoot
{
    private readonly Clock _clock = new();
    private readonly SessionTracker _t;

    public SessionTrackerTests() => _t = new SessionTracker(() => _clock.Now);

    private Session S => _t.Get("s1")!;

    [Fact]
    public void Prompt_makes_session_working_with_a_turn()
    {
        _t.Handle(Hook.Event("SessionStart"));
        Assert.Equal(SessionState.Ready, S.State);
        _t.Handle(Hook.Event("UserPromptSubmit", extra: new { prompt = "fix the crash" }));
        Assert.Equal(SessionState.Working, S.State);
        Assert.Single(S.Turns);
        Assert.Equal("fix the crash", S.Turns[0].Prompt);
        Assert.Equal("replay-tool", S.Name);
    }

    [Fact]
    public void Tool_use_sets_a_plain_english_now_line()
    {
        _t.Handle(Hook.Event("UserPromptSubmit", extra: new { prompt = "x" }));
        _t.Handle(Hook.Event("PreToolUse", extra: new { tool_name = "Edit", tool_input = new { file_path = @"C:\a\src\parser.py" } }));
        Assert.Equal("Editing parser.py", S.NowLine);
        _t.Handle(Hook.Event("PreToolUse", extra: new { tool_name = "Bash", tool_input = new { command = "pytest -q", description = "Run the tests" } }));
        Assert.Equal("Run the tests", S.NowLine);
    }

    [Fact]
    public void Permission_request_needs_you_then_tool_activity_resumes()
    {
        _t.Handle(Hook.Event("UserPromptSubmit", extra: new { prompt = "x" }));
        var fired = 0;
        _t.NeedsYou += _ => fired++;
        _t.Handle(Hook.Event("PermissionRequest", extra: new { tool_name = "Bash", tool_input = new { command = "npm install vite@6" } }));
        Assert.Equal(SessionState.NeedsYou, S.State);
        Assert.Equal("Wants to run npm install vite@6", S.Reason);
        Assert.Equal(1, fired);

        // The 6-second notification for the same prompt must not fire NeedsYou twice.
        _t.Handle(Hook.Event("Notification", extra: new { notification_type = "permission_prompt", message = "Claude needs your permission" }));
        Assert.Equal(1, fired);

        _t.Handle(Hook.Event("PostToolUse", extra: new { tool_name = "Bash", tool_input = new { command = "npm install vite@6" }, tool_response = new { stdout = "added 1 package" } }));
        Assert.Equal(SessionState.Working, S.State);
        Assert.Contains("vite@6", S.CurrentTurn!.Facts.Installs);
    }

    [Fact]
    public void Stop_marks_done_and_raises_turn_ended()
    {
        Turn? ended = null;
        _t.TurnEnded += (_, turn) => ended = turn;
        _t.Handle(Hook.Event("UserPromptSubmit", extra: new { prompt = "x" }));
        _t.Handle(Hook.Event("Stop", extra: new { last_assistant_message = "Fixed it. All tests pass.", background_tasks = Array.Empty<object>() }));
        Assert.Equal(SessionState.Done, S.State);
        Assert.NotNull(ended);
        Assert.Equal(SessionState.Done, ended!.Outcome);
        Assert.Null(S.NowLine);
    }

    [Fact]
    public void Stop_with_a_background_task_running_is_done_and_notes_the_task()
    {
        _t.Handle(Hook.Event("UserPromptSubmit", extra: new { prompt = "x" }));
        _t.Handle(Hook.Event("Stop", extra: new
        {
            last_assistant_message = "Started the build in the background.",
            background_tasks = new[] { new { id = "t1", type = "shell", status = "running", description = "npm run build" } },
        }));
        // Claude finished its turn and is idle; the task is noted, not shown as "working".
        Assert.Equal(SessionState.Done, S.State);
        Assert.True(S.WaitingOnBackgroundTask);
        Assert.Equal(new[] { "npm run build" }, S.BackgroundTasks);
    }

    [Theory]
    // Watches on an artifact or an MCP stream never end, so Claude never picks up again: the session is simply done.
    [InlineData("monitor", "live updates for artifact https://claude.ai/artifact/abc", null)]
    [InlineData("monitor", "CI events", null)]
    // Claude Code's own housekeeping isn't your work.
    [InlineData("dream", "Tidying memory", null)]
    [InlineData("auto-mode scan", "environment scan", null)]
    [InlineData("memory import", "reading notes", null)]
    // Servers and watchers run until you stop them.
    [InlineData("shell", "Start the local preview server", "npm run dev")]
    [InlineData("shell", "Start dev server", "cd site && pnpm dev")]
    [InlineData("shell", "Serve the site", "python -m http.server 8000")]
    [InlineData("shell", "Run the API", "uvicorn app.main:app --reload")]
    [InlineData("shell", "Preview", "npx vite --port 5173")]
    [InlineData("shell", "Next", "npx next dev")]
    [InlineData("shell", "Django", "python manage.py runserver")]
    [InlineData("shell", "Follow the log", "tail -f server.log")]
    [InlineData("shell", "Follow the log", "Get-Content app.log -Wait")]
    [InlineData("shell", "Rebuild on change", "npx tsc --watch")]
    [InlineData("shell", "Rebuild on change", "dotnet watch run")]
    [InlineData("shell", "Containers", "docker compose up")]
    [InlineData("shell", "Tunnel", "ngrok http 3000")]
    // Older Claude Code: no type, only what it said it was doing.
    [InlineData(null, "Start the local preview server", null)]
    public void Tasks_that_never_end_do_not_count_as_waiting(string? type, string description, string? command)
    {
        _t.Handle(Hook.Event("UserPromptSubmit", extra: new { prompt = "x" }));
        _t.Handle(Hook.Event("Stop", extra: new
        {
            last_assistant_message = "Done.",
            background_tasks = new[] { new { id = "t1", type, status = "running", description, command } },
        }));
        Assert.Equal(SessionState.Done, S.State);
        Assert.False(S.WaitingOnBackgroundTask);
        Assert.Empty(S.AwaitedTasks);
    }

    [Theory]
    [InlineData("subagent", "Review the diff", null)]
    [InlineData("workflow", "Release", null)]
    [InlineData("teammate", "@tester", null)]
    [InlineData("MCP task", "Generate the report", null)]
    [InlineData("cloud session", "Fix the flaky test", null)]
    [InlineData("shell", "Run the tests", "dotnet test")]
    [InlineData("shell", "Run the server tests", "npm test -- server")]
    [InlineData("shell", "Build", "npx vite build")]
    [InlineData("shell", "Build", "npm run build")]
    [InlineData("shell", "Watch the build", "until [ -f out/done ]; do sleep 5; done")]
    [InlineData("shell", "Containers", "docker compose up -d && npm run e2e")]
    // A subagent that sets up a server still finishes.
    [InlineData("subagent", "Set up the dev server", null)]
    public void Tasks_Claude_waits_for_still_count(string type, string description, string? command)
    {
        _t.Handle(Hook.Event("UserPromptSubmit", extra: new { prompt = "x" }));
        _t.Handle(Hook.Event("Stop", extra: new
        {
            last_assistant_message = "Waiting on it.",
            background_tasks = new[] { new { id = "t1", type, status = "running", description, command } },
        }));
        Assert.True(S.WaitingOnBackgroundTask);
        Assert.Single(S.AwaitedTasks);
    }

    [Fact]
    public void Sessions_saved_before_the_fix_do_not_hold_for_a_watcher()
    {
        var s = new Session { Id = "old", State = SessionState.Done, BackgroundTasks = { "live updates for artifact https://claude.ai/artifact/x", "Start the local preview server" } };
        Assert.False(s.WaitingOnBackgroundTask);
        s.BackgroundTasks.Add("npm run build");
        Assert.True(s.WaitingOnBackgroundTask);
        Assert.Equal(new[] { "npm run build" }, s.AwaitedTasks);
    }

    [Fact]
    public void A_screened_list_is_trusted_as_is()
    {
        // Screened when it arrived, with the type in hand: a subagent described as setting up a server still counts.
        _t.Handle(Hook.Event("UserPromptSubmit", extra: new { prompt = "x" }));
        _t.Handle(Hook.Event("Stop", extra: new
        {
            last_assistant_message = "Waiting on it.",
            background_tasks = new[] { new { id = "t1", type = "subagent", status = "running", description = "Set up the dev server" } },
        }));
        var copy = System.Text.Json.JsonSerializer.Deserialize<Session>(System.Text.Json.JsonSerializer.Serialize(S, Json.Options), Json.Options)!;
        Assert.True(copy.WaitingOnBackgroundTask);
    }

    [Fact]
    public void Trailing_question_is_captured_and_never_lost()
    {
        _t.Handle(Hook.Event("UserPromptSubmit", extra: new { prompt = "x" }));
        _t.Handle(Hook.Event("Stop", extra: new { last_assistant_message = "Added scrubbing.\n\nShould scrubbing snap to inputs?" }));
        Assert.Equal("Should scrubbing snap to inputs?", S.CurrentTurn!.Facts.Question);
        Assert.Contains(S.AllFacts.Chips(), c => c.Text.StartsWith("Asked:") && c.Tone == ChipTone.Attention);
    }

    [Fact]
    public void Late_tool_hook_after_stop_does_not_flip_back_to_working()
    {
        _t.Handle(Hook.Event("UserPromptSubmit", extra: new { prompt = "x" }));
        _t.Handle(Hook.Event("Stop", extra: new { last_assistant_message = "Done." }));
        _clock.Advance(TimeSpan.FromSeconds(1));
        _t.Handle(Hook.Event("PostToolUse", extra: new { tool_name = "Edit", tool_input = new { file_path = @"C:\a\b.py" } }));
        Assert.Equal(SessionState.Done, S.State);
    }

    [Fact]
    public void Rate_limit_pauses_and_auto_resume_works_again()
    {
        _t.Handle(Hook.Event("UserPromptSubmit", extra: new { prompt = "x" }));
        _t.Handle(Hook.Event("StopFailure", extra: new { error = "rate_limit", last_assistant_message = "API Error: Rate limit reached" }));
        Assert.Equal(SessionState.Paused, S.State);
        _t.Handle(Hook.Event("Notification", extra: new { notification_type = "quota_auto_resume_fired", message = "resumed" }));
        Assert.Equal(SessionState.Working, S.State);
    }

    [Fact]
    public void Other_failures_are_explained_in_plain_words()
    {
        _t.Handle(Hook.Event("UserPromptSubmit", extra: new { prompt = "x" }));
        _t.Handle(Hook.Event("StopFailure", extra: new { error = "authentication_failed" }));
        Assert.Equal(SessionState.Failed, S.State);
        Assert.Equal("Signed out of Claude. Log in again", S.Reason);
    }

    [Fact]
    public void Silent_working_session_goes_quiet_so_it_cannot_hold_forever()
    {
        _t.QuietAfter = TimeSpan.FromMinutes(45);
        _t.Handle(Hook.Event("UserPromptSubmit", extra: new { prompt = "x" }));
        _clock.Advance(TimeSpan.FromMinutes(44));
        _t.Tick();
        Assert.Equal(SessionState.Working, S.State);
        _clock.Advance(TimeSpan.FromMinutes(2));
        _t.Tick();
        Assert.Equal(SessionState.Quiet, S.State);
    }

    [Fact]
    public void Summarizer_folder_is_ignored()
    {
        _t.IgnoreCwd = @"C:\Users\sam\replay-tool";
        _t.Handle(Hook.Event("UserPromptSubmit", extra: new { prompt = "x" }));
        Assert.Null(_t.Get("s1"));
    }

    [Fact]
    public void Session_end_closes_it()
    {
        _t.Handle(Hook.Event("SessionStart"));
        _t.Handle(Hook.Event("SessionEnd", extra: new { reason = "prompt_input_exit" }));
        Assert.Equal(SessionState.Ended, S.State);
    }

    [Fact]
    public void Consecutive_edits_collapse_into_one_timeline_line()
    {
        _t.Handle(Hook.Event("UserPromptSubmit", extra: new { prompt = "x" }));
        foreach (var f in new[] { "a.py", "b.py", "c.py" })
            _t.Handle(Hook.Event("PostToolUse", extra: new { tool_name = "Edit", tool_input = new { file_path = $@"C:\p\{f}" } }));
        var edits = S.Timeline.Where(e => e.Kind == EntryKind.Edit).ToList();
        Assert.Single(edits);
        Assert.Equal(3, edits[0].Count);
        Assert.Equal(3, S.AllFacts.Files.Count);
    }

    [Fact]
    public void After_a_restart_a_session_with_a_fresh_transcript_keeps_working()
    {
        var fresh = Path.Combine(Dir, "fresh.jsonl");
        var stale = Path.Combine(Dir, "stale.jsonl");
        File.WriteAllText(fresh, "{}");
        File.WriteAllText(stale, "{}");
        File.SetLastWriteTime(stale, DateTime.Now - TimeSpan.FromHours(2));
        var t = new SessionTracker();
        t.Load(new[]
        {
            new Session { Id = "a", State = SessionState.Working, TranscriptPath = fresh },
            new Session { Id = "b", State = SessionState.Working, TranscriptPath = stale },
        });
        Assert.Equal(SessionState.Working, t.Get("a")!.State);
        Assert.Equal(SessionState.Quiet, t.Get("b")!.State);
    }
}
