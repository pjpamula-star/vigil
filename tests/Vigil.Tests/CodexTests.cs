using System.Text.Json.Nodes;
using Vigil.Core;
using Vigil.UI;

namespace Vigil.Tests;

public class CodexSetupTests : TempRoot
{
    private string Home => Path.Combine(Dir, ".codex");
    private string HooksFile => Path.Combine(Home, "hooks.json");
    private CodexSetup Setup() => new(Home, Path.Combine(Dir, "backups"));

    [Fact]
    public void Not_available_until_Codex_has_run_once()
    {
        Assert.False(Setup().Available);
        Directory.CreateDirectory(Home);
        Assert.True(Setup().Available);
    }

    [Fact]
    public void Install_adds_every_hook_and_keeps_yours()
    {
        Directory.CreateDirectory(Home);
        File.WriteAllText(HooksFile, """{ "hooks": { "PreToolUse": [ { "matcher": "Bash", "hooks": [ { "type": "command", "command": "python policy.py" } ] } ] } }""");
        var setup = Setup();
        setup.Install();
        Assert.True(setup.IsInstalled());

        var root = JsonNode.Parse(File.ReadAllText(HooksFile))!;
        var pre = root["hooks"]!["PreToolUse"]!.AsArray();
        Assert.Equal(2, pre.Count);
        Assert.Equal("python policy.py", (string?)pre[0]!["hooks"]![0]!["command"]);
        Assert.True((bool?)pre[1]!["hooks"]![0]!["async"]);
        // Codex allows SessionEnd and Interrupt 3 seconds at most.
        Assert.Equal(3, (int?)root["hooks"]!["Interrupt"]![0]!["hooks"]![0]!["timeout"]);
        Assert.Single(Directory.GetFiles(Path.Combine(Dir, "backups"), "codex-hooks-*.json"));

        setup.Install();
        Assert.Equal(2, JsonNode.Parse(File.ReadAllText(HooksFile))!["hooks"]!["PreToolUse"]!.AsArray().Count);
    }

    [Fact]
    public void Uninstall_removes_only_Vigils_hooks_and_no_empty_file_is_left()
    {
        Directory.CreateDirectory(Home);
        var setup = Setup();
        setup.Install();
        setup.Uninstall();
        Assert.False(File.Exists(HooksFile));

        File.WriteAllText(HooksFile, """{ "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "notify.exe" } ] } ] } }""");
        setup.Install();
        setup.Uninstall();
        var stop = JsonNode.Parse(File.ReadAllText(HooksFile))!["hooks"]!["Stop"]!.AsArray();
        Assert.Single(stop);
        Assert.False(setup.IsInstalled());
    }

    [Fact]
    public void Hook_command_runs_under_cmd_always_succeeds_and_prints_nothing()
    {
        var cmd = (string)CodexSetup.HookHandler("Stop")["command"]!;
        Assert.Contains("http://127.0.0.1:47823/hook/codex", cmd);
        Assert.EndsWith(">nul 2>&1 & exit /b 0", cmd);
        Assert.Contains("--connect-timeout 0.25", cmd);
    }

    [Theory]
    [InlineData("/hook/codex", "codex")]
    [InlineData("/hook?rc=abc", "claude")]
    [InlineData("/hook", "claude")]
    public void The_url_says_which_agent_posted(string path, string agent) => Assert.Equal(agent, HookServer.Agent(path));
}

public class CodexTrackingTests
{
    private readonly Clock _clock = new();
    private readonly SessionTracker _t;

    public CodexTrackingTests() => _t = new SessionTracker(() => _clock.Now);

    private const string Patch = "*** Begin Patch\n*** Update File: src/app.ts\n@@\n-a\n+b\n*** Add File: src/new.ts\n+x\n*** End Patch\n";

    [Fact]
    public void A_Codex_turn_reads_like_a_Claude_one()
    {
        _t.Handle(Hook.Event("SessionStart", "c1", new { source = "startup", model = "gpt-5.5" }), Session.Codex);
        _t.Handle(Hook.Event("UserPromptSubmit", "c1", new { turn_id = "t1", prompt = "fix the build" }), Session.Codex);
        var s = _t.Get("c1")!;
        Assert.Equal(Session.Codex, s.Agent);
        Assert.False(s.Background);
        Assert.Equal(SessionState.Working, s.State);

        _t.Handle(Hook.Event("PreToolUse", "c1", new { turn_id = "t1", tool_name = "apply_patch", tool_input = new { command = Patch } }), Session.Codex);
        Assert.Equal("Editing 2 files", s.NowLine);
        _t.Handle(Hook.Event("PostToolUse", "c1", new { turn_id = "t1", tool_name = "apply_patch", tool_input = new { command = Patch }, tool_response = "Success" }), Session.Codex);
        Assert.Equal(new[] { "src/app.ts", "src/new.ts" }, s.CurrentTurn!.Facts.Files.OrderBy(f => f));

        _t.Handle(Hook.Event("PreToolUse", "c1", new { turn_id = "t1", tool_name = "Bash", tool_input = new { command = "npm test" } }), Session.Codex);
        Assert.Equal("Running npm test", s.NowLine);

        _t.Handle(Hook.Event("PermissionRequest", "c1", new { turn_id = "t1", tool_name = "Bash", tool_input = new { command = "npm install vite@6" } }), Session.Codex);
        Assert.Equal(SessionState.NeedsYou, s.State);

        _clock.Advance(TimeSpan.FromMinutes(1));
        _t.Handle(Hook.Event("Stop", "c1", new { turn_id = "t1", stop_hook_active = false, last_assistant_message = "Fixed the build; tests pass." }), Session.Codex);
        Assert.Equal(SessionState.Done, s.State);
        Assert.True(Glance.IsDone(s));
    }

    [Fact]
    public void Pressing_Esc_in_Codex_ends_the_turn()
    {
        _t.Handle(Hook.Event("UserPromptSubmit", "c2", new { turn_id = "t1", prompt = "refactor" }), Session.Codex);
        _t.Handle(Hook.Event("Interrupt", "c2", new { turn_id = "t1" }), Session.Codex);
        var s = _t.Get("c2")!;
        Assert.Equal(SessionState.Done, s.State);
        Assert.NotNull(s.CurrentTurn!.EndedAt);
        Assert.Equal("You stopped it", s.Timeline[^1].Text);
        Assert.Equal("You stopped it", SessionVM.From(s).Line);
    }

    [Fact]
    public void Codex_sessions_are_labeled_and_Claude_ones_are_not()
    {
        _t.Handle(Hook.Event("Stop", "c3", new { last_assistant_message = "Done." }), Session.Codex);
        _t.Handle(Hook.Event("Stop", "k1", new { last_assistant_message = "Done." }));
        Assert.StartsWith("Codex · ", SessionVM.From(_t.Get("c3")!).Meta);
        Assert.DoesNotContain("Codex", SessionVM.From(_t.Get("k1")!).Meta);
        Assert.Equal(Session.Claude, _t.Get("k1")!.Agent);
    }

    [Fact]
    public void Settings_tell_you_the_one_step_Codex_needs()
    {
        Assert.Contains("run it once", SettingsView.CodexText(available: false, on: false, seen: false));
        Assert.Contains("/hooks", SettingsView.CodexText(available: true, on: true, seen: false));
        Assert.DoesNotContain("/hooks", SettingsView.CodexText(available: true, on: true, seen: true));
    }
}
