using System.Text.Json;
using Vigil.Core;

namespace Vigil.Tests;

/// <summary>
/// Claude Code doesn't send Stop when you press Esc, and sends nothing at all when its window is closed.
/// Vigil checks Claude's own list of running sessions so neither leaves a session "working" for 45 minutes.
/// </summary>
public class ClaudeLivenessTests : TempRoot
{
    private readonly Clock _clock = new();
    private readonly SessionTracker _t;

    public ClaudeLivenessTests() => _t = new SessionTracker(() => _clock.Now);

    private Session S => _t.Get("s1")!;

    private Dictionary<string, LiveClaude> Live(string status, DateTime at, bool alive = true) =>
        new() { ["s1"] = new LiveClaude("s1", status, at, alive) };

    private void StartWorking(string? transcript = null)
    {
        _t.Handle(Hook.Event("UserPromptSubmit", extra: new { prompt = "fix it", transcript_path = transcript ?? @"C:\nope\t.jsonl" }));
        _t.Handle(Hook.Event("PreToolUse", extra: new { tool_name = "Bash", tool_input = new { command = "npm test" }, transcript_path = transcript ?? @"C:\nope\t.jsonl" }));
    }

    [Fact]
    public void Esc_in_Claude_ends_the_turn_once_Claude_has_been_idle_a_moment()
    {
        StartWorking();
        _clock.Advance(TimeSpan.FromSeconds(5));
        var idleAt = _clock.Now;

        _clock.Advance(TimeSpan.FromSeconds(5));
        _t.Tick(Live("idle", idleAt));
        Assert.Equal(SessionState.Working, S.State); // a Stop hook may still be on its way

        _clock.Advance(TimeSpan.FromSeconds(15));
        _t.Tick(Live("idle", idleAt));
        Assert.Equal(SessionState.Done, S.State);
        Assert.Null(S.NowLine);
        Assert.NotNull(S.CurrentTurn!.EndedAt);
    }

    [Fact]
    public void A_stopped_turn_never_borrows_the_last_turns_summary()
    {
        _t.Handle(Hook.Event("UserPromptSubmit", extra: new { prompt = "fix the build" }));
        _t.Handle(Hook.Event("Stop", extra: new { last_assistant_message = "Fixed the build; tests pass." }));
        _t.SetHeadline(S, S.CurrentTurn!, "Fixed the build and the tests pass.");

        _clock.Advance(TimeSpan.FromMinutes(1));
        StartWorking();
        _clock.Advance(TimeSpan.FromSeconds(2));
        var idleAt = _clock.Now;
        _clock.Advance(TimeSpan.FromSeconds(20));
        _t.Tick(Live("idle", idleAt));

        Assert.Equal(SessionState.Done, S.State);
        Assert.Equal("Stopped", Vigil.UI.SessionVM.From(S).Line);
        Assert.Equal("Stopped", Vigil.UI.SessionVM.From(S).Headline);
        Assert.Equal("Stopped", S.Summary);
        var item = Recap.Build(new[] { S }, _clock.Now - TimeSpan.FromHours(1), _clock.Now).Items.Single();
        Assert.Equal("Fixed the build and the tests pass. Later: stopped.", item.Headline);
    }

    [Fact]
    public void A_finished_turn_shows_its_own_words_until_its_summary_arrives()
    {
        _t.Handle(Hook.Event("UserPromptSubmit", extra: new { prompt = "one" }));
        _t.Handle(Hook.Event("Stop", extra: new { last_assistant_message = "Old result." }));
        _t.SetHeadline(S, S.CurrentTurn!, "Old summary.");
        _t.Handle(Hook.Event("UserPromptSubmit", extra: new { prompt = "two" }));
        _clock.Advance(TimeSpan.FromSeconds(10));
        _t.Handle(Hook.Event("Stop", extra: new { last_assistant_message = "Renamed the folder. Nothing else changed." }));
        Assert.Equal("Renamed the folder.", S.Summary);

        // A turn with no words of its own falls back to the last summary rather than nothing.
        _t.Handle(Hook.Event("UserPromptSubmit", extra: new { prompt = "three" }));
        _clock.Advance(TimeSpan.FromSeconds(10));
        _t.Handle(Hook.Event("Stop", extra: new { last_assistant_message = "" }));
        Assert.Equal("Old summary.", S.Summary);
    }

    [Fact]
    public void Busy_or_waiting_never_ends_the_turn()
    {
        StartWorking();
        _clock.Advance(TimeSpan.FromMinutes(10));
        _t.Tick(Live("busy", _clock.Now - TimeSpan.FromMinutes(9)));
        Assert.Equal(SessionState.Working, S.State);
        _t.Tick(Live("waiting", _clock.Now - TimeSpan.FromMinutes(9)));
        Assert.Equal(SessionState.Working, S.State);
    }

    [Fact]
    public void An_idle_status_older_than_the_last_event_is_out_of_date()
    {
        var before = _clock.Now - TimeSpan.FromMinutes(1);
        StartWorking();
        _clock.Advance(TimeSpan.FromMinutes(5));
        _t.Tick(Live("idle", before));
        Assert.Equal(SessionState.Working, S.State);
    }

    [Fact]
    public void Saying_no_at_a_permission_prompt_ends_the_wait()
    {
        StartWorking();
        _t.Handle(Hook.Event("Notification", extra: new { notification_type = "permission_prompt", message = "Claude needs your permission" }));
        Assert.Equal(SessionState.NeedsYou, S.State);
        _clock.Advance(TimeSpan.FromSeconds(3));
        var idleAt = _clock.Now;
        _clock.Advance(TimeSpan.FromSeconds(20));
        _t.Tick(Live("idle", idleAt));
        Assert.Equal(SessionState.Done, S.State);
        Assert.Null(S.Reason);
    }

    [Fact]
    public void Closing_the_terminal_ends_the_session()
    {
        StartWorking();
        _clock.Advance(TimeSpan.FromSeconds(30));
        _t.Tick(Live("busy", _clock.Now - TimeSpan.FromSeconds(20), alive: false));
        Assert.Equal(SessionState.Working, S.State); // seen gone once could be a leftover file
        _clock.Advance(TimeSpan.FromSeconds(10));
        _t.Tick(Live("busy", _clock.Now - TimeSpan.FromSeconds(30), alive: false));
        Assert.Equal(SessionState.Ended, S.State);
        Assert.Null(S.NowLine);
        Assert.Equal("Closed before it finished", Vigil.UI.SessionVM.From(S).Line);
    }

    [Fact]
    public void Any_hook_brings_a_session_back_even_if_Vigil_thought_it_was_closed()
    {
        StartWorking();
        _clock.Advance(TimeSpan.FromSeconds(30));
        _t.Tick(Live("busy", _clock.Now, alive: false));
        _clock.Advance(TimeSpan.FromSeconds(10));
        _t.Tick(Live("busy", _clock.Now, alive: false));
        Assert.Equal(SessionState.Ended, S.State);

        _clock.Advance(TimeSpan.FromSeconds(10));
        _t.Handle(Hook.Event("PreToolUse", extra: new { tool_name = "Bash", tool_input = new { command = "npm test" } }));
        Assert.Equal(SessionState.Working, S.State);
    }

    [Fact]
    public void A_hook_between_two_checks_resets_the_gone_clock()
    {
        StartWorking();
        _clock.Advance(TimeSpan.FromSeconds(30));
        _t.Tick(Live("busy", _clock.Now, alive: false));
        _clock.Advance(TimeSpan.FromSeconds(5));
        _t.Handle(Hook.Event("PreToolUse", extra: new { tool_name = "Bash", tool_input = new { command = "npm test" } }));
        _clock.Advance(TimeSpan.FromSeconds(6));
        _t.Tick(Live("busy", _clock.Now, alive: false));
        Assert.Equal(SessionState.Working, S.State);
    }

    [Fact]
    public void Without_Claudes_list_an_interrupt_in_the_transcript_still_ends_the_turn()
    {
        var transcript = Path.Combine(Dir, "t.jsonl");
        File.WriteAllLines(transcript, new[]
        {
            Line(new { type = "assistant", message = new { role = "assistant", content = new object[] { new { type = "tool_use", name = "Bash" } } } }),
            Line(new { type = "user", message = new { role = "user", content = new object[] { new { type = "text", text = "[Request interrupted by user for tool use]" } } } }),
            Line(new { type = "system", subtype = "turn_duration" }),
            Line(new { type = "last-prompt" }),
        });
        StartWorking(transcript);
        _clock.Advance(TimeSpan.FromSeconds(10));
        _t.Tick(new Dictionary<string, LiveClaude>());
        Assert.Equal(SessionState.Working, S.State); // too soon to judge

        _clock.Advance(TimeSpan.FromSeconds(30));
        _t.Tick(new Dictionary<string, LiveClaude>());
        Assert.Equal(SessionState.Done, S.State);
        Assert.Equal("You stopped it", S.Timeline[^1].Text);
    }

    [Fact]
    public void A_long_running_command_is_not_mistaken_for_an_interrupt()
    {
        var transcript = Path.Combine(Dir, "t.jsonl");
        File.WriteAllLines(transcript, new[]
        {
            Line(new { type = "user", message = new { role = "user", content = "[Request interrupted by user]" } }),
            Line(new { type = "user", message = new { role = "user", content = "run the full build" } }),
            Line(new { type = "assistant", message = new { role = "assistant", content = new object[] { new { type = "tool_use", name = "Bash" } } } }),
        });
        StartWorking(transcript);
        _clock.Advance(TimeSpan.FromMinutes(20));
        _t.Tick(new Dictionary<string, LiveClaude>());
        Assert.Equal(SessionState.Working, S.State);
    }

    [Fact]
    public void A_long_command_Claude_says_it_is_busy_with_is_not_called_quiet()
    {
        StartWorking();
        _clock.Advance(TimeSpan.FromMinutes(50));
        _t.Tick(Live("busy", _clock.Now - TimeSpan.FromMinutes(50)));
        Assert.Equal(SessionState.Working, S.State);
        _t.Tick(new Dictionary<string, LiveClaude>());
        Assert.Equal(SessionState.Quiet, S.State);
    }

    [Fact]
    public void After_a_restart_a_session_Claude_says_is_busy_keeps_working()
    {
        var saved = new Session { Id = "s1", State = SessionState.Working, StateSince = _clock.Now, LastEventAt = _clock.Now - TimeSpan.FromMinutes(20), TranscriptPath = @"C:
ope	.jsonl" };
        saved.Turns.Add(new Turn { StartedAt = _clock.Now - TimeSpan.FromMinutes(25), Prompt = "build" });
        _t.Load(new[] { saved });
        Assert.Equal(SessionState.Quiet, S.State); // Vigil alone can't tell

        _t.Tick(Live("busy", _clock.Now - TimeSpan.FromMinutes(21)));
        Assert.Equal(SessionState.Working, S.State);
    }

    [Fact]
    public void Busy_is_not_trusted_forever()
    {
        StartWorking();
        _clock.Advance(TimeSpan.FromHours(3) + TimeSpan.FromMinutes(1));
        _t.Tick(Live("busy", _clock.Now - TimeSpan.FromHours(3)));
        Assert.Equal(SessionState.Quiet, S.State);
        _t.Tick(Live("busy", _clock.Now - TimeSpan.FromHours(3)));
        Assert.Equal(SessionState.Quiet, S.State); // and not revived
    }

    [Fact]
    public void Codex_sessions_are_left_alone()
    {
        _t.Handle(Hook.Event("UserPromptSubmit", extra: new { prompt = "x" }), Session.Codex);
        _clock.Advance(TimeSpan.FromMinutes(2));
        _t.Tick(Live("idle", _clock.Now - TimeSpan.FromMinutes(1), alive: false));
        Assert.Equal(SessionState.Working, S.State);
    }

    [Fact]
    public void Reads_Claudes_session_list_and_skips_leftovers_from_closed_windows()
    {
        var dir = Path.Combine(Dir, "sessions");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "100.json"), """{"pid":100,"sessionId":"a","procStart":"5","status":"idle","statusUpdatedAt":1792309878625}""");
        // The same session resumed in a new window; the old window's file was left behind.
        File.WriteAllText(Path.Combine(dir, "200.json"), """{"pid":200,"sessionId":"b","procStart":"7","status":"busy","statusUpdatedAt":1792309000000}""");
        File.WriteAllText(Path.Combine(dir, "300.json"), """{"pid":300,"sessionId":"b","procStart":"9","status":"waiting","statusUpdatedAt":1792309100000}""");
        File.WriteAllText(Path.Combine(dir, "400.json"), "not json");
        File.WriteAllText(Path.Combine(dir, "400.abc.key"), "secret");

        var live = ClaudeRegistry.Read(dir, (pid, start) => pid != 200);

        Assert.Equal(2, live.Count);
        Assert.Equal("idle", live["a"].Status);
        Assert.True(live["a"].Alive);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1792309878625).LocalDateTime, live["a"].StatusAt);
        Assert.Equal("waiting", live["b"].Status);
        Assert.True(live["b"].Alive);
    }

    [Fact]
    public void A_closed_window_is_still_noticed_after_its_file_is_cleaned_up()
    {
        // Claude Code tidies away files left by closed windows, sometimes before Vigil looks twice.
        var dir = Path.Combine(Dir, "sessions");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "100.json");
        File.WriteAllText(file, """{"pid":100,"sessionId":"a","procStart":"5","status":"busy","statusUpdatedAt":1792309878625}""");
        var alive = true;
        var registry = new ClaudeProcesses();

        Assert.True(registry.Read(dir, (_, _) => alive)["a"].Alive);
        alive = false;
        File.Delete(file);
        var after = registry.Read(dir, (_, _) => alive);
        Assert.False(after["a"].Alive);
        Assert.Equal("busy", after["a"].Status);
    }

    [Fact]
    public void A_file_removed_by_a_clean_exit_is_not_called_closed_while_the_process_runs()
    {
        var dir = Path.Combine(Dir, "sessions");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "100.json");
        File.WriteAllText(file, """{"pid":100,"sessionId":"a","procStart":"5","status":"idle","statusUpdatedAt":1792309878625}""");
        var registry = new ClaudeProcesses();
        registry.Read(dir, (_, _) => true);
        File.Delete(file);
        Assert.False(registry.Read(dir, (_, _) => true).ContainsKey("a"));
    }

    [Fact]
    public void The_transcript_is_read_again_once_it_changes()
    {
        var transcript = Path.Combine(Dir, "t2.jsonl");
        File.WriteAllLines(transcript, new[] { Line(new { type = "assistant", message = new { role = "assistant", content = new object[] { new { type = "tool_use", name = "Bash" } } } }) });
        Assert.False(TranscriptScanner.EndsWithInterrupt(transcript));
        File.AppendAllLines(transcript, new[] { Line(new { type = "user", message = new { role = "user", content = "[Request interrupted by user]" } }) });
        Assert.True(TranscriptScanner.EndsWithInterrupt(transcript));
    }

    [Fact]
    public void A_missing_folder_reads_as_nothing_known()
    {
        Assert.Empty(ClaudeRegistry.Read(Path.Combine(Dir, "nope")));
    }

    private static string Line(object o) => JsonSerializer.Serialize(o);
}
