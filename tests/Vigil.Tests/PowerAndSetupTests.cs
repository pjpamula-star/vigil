using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vigil.Core;

namespace Vigil.Tests;

public class PowerControllerTests : TempRoot
{
    private readonly MemoryPowerPlan _plan = new();
    private string JournalPath => Path.Combine(Dir, "journal.json");

    public PowerControllerTests()
    {
        _plan.Values[PowerSettings.LidAction] = (1, 1);        // sleep on lid close
        _plan.Values[PowerSettings.StandbyIdle] = (300, 180);  // 5 min / 3 min
        _plan.Values[PowerSettings.HibernateIdle] = (0, 0);
        _plan.Values[PowerSettings.ProcThrottleMax] = (100, 100);
    }

    [Fact]
    public void Hold_overrides_and_release_restores_exactly()
    {
        var pc = new PowerController(_plan, JournalPath, useRequests: false);
        pc.Hold();
        Assert.True(File.Exists(JournalPath));
        Assert.Equal((0u, 0u), _plan.Values[PowerSettings.LidAction]);
        Assert.Equal((0u, 0u), _plan.Values[PowerSettings.StandbyIdle]);

        pc.Release();
        Assert.False(File.Exists(JournalPath));
        Assert.Equal((1u, 1u), _plan.Values[PowerSettings.LidAction]);
        Assert.Equal((300u, 180u), _plan.Values[PowerSettings.StandbyIdle]);
    }

    [Fact]
    public void Journal_is_written_before_any_change()
    {
        var spy = new SpyPlan(_plan, () => File.Exists(JournalPath));
        new PowerController(spy, JournalPath, useRequests: false).Hold();
        Assert.True(spy.Writes > 0);
        Assert.True(spy.JournalExistedOnEveryWrite);
    }

    [Fact]
    public void A_crashed_run_is_recovered_by_the_next_one()
    {
        new PowerController(_plan, JournalPath, useRequests: false).Hold();
        // Simulate a crash: the first controller vanishes without releasing.
        var next = new PowerController(_plan, JournalPath, useRequests: false);
        Assert.True(next.HasJournal);
        Assert.True(next.Restore());
        Assert.Equal((1u, 1u), _plan.Values[PowerSettings.LidAction]);
    }

    [Fact]
    public void Cpu_cap_is_journaled_and_undone_independently()
    {
        var pc = new PowerController(_plan, JournalPath, useRequests: false);
        pc.Hold();
        pc.CapCpu(60);
        Assert.Equal((60u, 60u), _plan.Values[PowerSettings.ProcThrottleMax]);
        pc.UncapCpu();
        Assert.Equal((100u, 100u), _plan.Values[PowerSettings.ProcThrottleMax]);
        Assert.Equal((0u, 0u), _plan.Values[PowerSettings.LidAction]); // still holding
        pc.CapCpu(60);
        pc.Release();
        Assert.Equal((100u, 100u), _plan.Values[PowerSettings.ProcThrottleMax]);
        Assert.Equal((1u, 1u), _plan.Values[PowerSettings.LidAction]);
    }

    [Fact]
    public void Holding_twice_keeps_the_true_originals()
    {
        var pc = new PowerController(_plan, JournalPath, useRequests: false);
        pc.Hold();
        pc.Release();
        pc.Hold();
        pc.Hold();
        pc.Release();
        Assert.Equal((300u, 180u), _plan.Values[PowerSettings.StandbyIdle]);
    }

    private sealed class SpyPlan(IPowerPlan inner, Func<bool> journalExists) : IPowerPlan
    {
        public int Writes;
        public bool JournalExistedOnEveryWrite = true;
        public Guid ActiveScheme() => inner.ActiveScheme();
        public (uint ac, uint dc) Read(Guid s, PowerSettings.Setting x) => inner.Read(s, x);
        public void Write(Guid s, PowerSettings.Setting x, uint ac, uint dc)
        {
            Writes++;
            JournalExistedOnEveryWrite &= journalExists();
            inner.Write(s, x, ac, dc);
        }
        public void Apply(Guid s) => inner.Apply(s);
    }
}

public class ClaudeSetupTests : TempRoot
{
    private string SettingsPath => Path.Combine(Dir, "settings.json");
    private ClaudeSetup Setup => new(SettingsPath, Path.Combine(Dir, "backups"));

    private const string Existing = """
    {
      "permissions": { "defaultMode": "auto" },
      "enabledPlugins": { "superpowers@x": true },
      "hooks": {
        "Stop": [ { "hooks": [ { "type": "command", "command": "say done" } ] } ]
      },
      "theme": "dark"
    }
    """;

    [Fact]
    public void Install_adds_hooks_and_keeps_everything_else()
    {
        File.WriteAllText(SettingsPath, Existing);
        Setup.Install();
        Assert.True(Setup.IsInstalled());

        var root = JsonNode.Parse(File.ReadAllText(SettingsPath))!;
        Assert.Equal("auto", root["permissions"]!["defaultMode"]!.GetValue<string>());
        Assert.Equal("dark", root["theme"]!.GetValue<string>());
        var stop = root["hooks"]!["Stop"]!.AsArray();
        Assert.Equal(2, stop.Count);
        Assert.Contains("say done", stop[0]!.ToJsonString());

        // Tool hooks run in the background; state hooks run inline.
        Assert.True(root["hooks"]!["PostToolUse"]![0]!["hooks"]![0]!["async"]!.GetValue<bool>());
        Assert.Null(root["hooks"]!["Stop"]![1]!["hooks"]![0]!["async"]);
        Assert.Single(Directory.GetFiles(Path.Combine(Dir, "backups")));
    }

    [Fact]
    public void Install_twice_does_not_duplicate()
    {
        File.WriteAllText(SettingsPath, Existing);
        Setup.Install();
        Setup.Install();
        var root = JsonNode.Parse(File.ReadAllText(SettingsPath))!;
        Assert.Equal(2, root["hooks"]!["Stop"]!.AsArray().Count);
        Assert.Single(root["hooks"]!["PreToolUse"]!.AsArray());
    }

    [Fact]
    public void Uninstall_removes_exactly_what_was_added()
    {
        File.WriteAllText(SettingsPath, Existing);
        Setup.Install();
        Setup.Uninstall();
        var root = JsonNode.Parse(File.ReadAllText(SettingsPath))!;
        Assert.False(Setup.IsInstalled());
        var hooks = root["hooks"]!.AsObject();
        Assert.Single(hooks);
        Assert.Contains("say done", hooks["Stop"]!.ToJsonString());
    }

    [Fact]
    public void Remote_control_and_presence_settings_round_trip()
    {
        File.WriteAllText(SettingsPath, Existing);
        Setup.SetRemoteControlAtStartup(true);
        Setup.SetPresenceFile(@"C:\x\present.marker");
        Assert.True(Setup.RemoteControlAtStartup);
        Assert.Equal(@"C:\x\present.marker", Setup.PresenceFile);
        Setup.SetPresenceFile(null);
        Assert.Null(JsonNode.Parse(File.ReadAllText(SettingsPath))!["env"]);
    }

    [Fact]
    public void Refuses_to_overwrite_a_broken_settings_file()
    {
        File.WriteAllText(SettingsPath, "{ this is not json");
        Assert.ThrowsAny<Exception>(() => Setup.Install());
        Assert.Equal("{ this is not json", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Hook_command_always_succeeds_and_targets_loopback()
    {
        var h = ClaudeSetup.HookHandler(isAsync: true).ToJsonString();
        Assert.Contains("127.0.0.1:47823/hook", h);
        Assert.Contains("exit /b 0", h);
        Assert.Contains("CLAUDE_CODE_BRIDGE_SESSION_ID", h);
    }
}

public class HookServerTests : TempRoot
{
    [Theory]
    [InlineData("/hook?rc=session_01AbC", "session_01AbC")]
    [InlineData("/hook?rc=%CLAUDE_CODE_BRIDGE_SESSION_ID%", null)]
    [InlineData("/hook", null)]
    [InlineData("/hook?rc=bad%20id", null)]
    public void Reads_remote_id(string path, string? expected) => Assert.Equal(expected, HookServer.RemoteId(path));

    [Fact]
    public async Task Receives_a_posted_hook_event()
    {
        var port = 48000 + Random.Shared.Next(1000);
        var got = new TaskCompletionSource<(JsonElement, string?)>();
        using var server = new HookServer((e, rc, _) => got.TrySetResult((e, rc)), port);
        server.Start();

        using var http = new HttpClient();
        var body = new StringContent("""{"session_id":"abc","hook_event_name":"Stop"}""", System.Text.Encoding.UTF8, "application/json");
        var resp = await http.PostAsync($"http://127.0.0.1:{port}/hook?rc=session_42", body);
        Assert.True(resp.IsSuccessStatusCode);
        Assert.Equal("", await resp.Content.ReadAsStringAsync());

        var (evt, rc) = await got.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("abc", evt.Str("session_id"));
        Assert.Equal("session_42", rc);
    }
}

public class RecapTests : TempRoot
{
    [Fact]
    public void Needs_you_comes_first_and_counts_are_right()
    {
        var from = new DateTime(2026, 10, 16, 23, 0, 0);
        var to = from + TimeSpan.FromHours(8);
        Session Make(string id, SessionState state, DateTime last, int files)
        {
            var s = new Session { Id = id, Cwd = $@"C:\p\{id}", State = state, StateSince = last, LastEventAt = last, StartedAt = from };
            var t = new Turn { StartedAt = from + TimeSpan.FromMinutes(5), EndedAt = last, Headline = $"{id} headline" };
            for (var i = 0; i < files; i++) t.Facts.Files.Add($"{id}{i}");
            s.Turns.Add(t);
            return s;
        }
        var recap = Recap.Build(new[]
        {
            Make("done1", SessionState.Done, from + TimeSpan.FromHours(2), 3),
            Make("needs", SessionState.NeedsYou, from + TimeSpan.FromHours(3), 1),
            Make("old", SessionState.Done, from - TimeSpan.FromHours(3), 9),
        }, from, to);

        Assert.Equal(new[] { "needs", "done1" }, recap.Items.Select(i => i.Session.Id));
        Assert.Equal(4, recap.FilesChanged);
        Assert.Equal("1 done. 1 needs you.", recap.Title);
    }

    [Fact]
    public void A_session_still_working_counts_even_if_it_was_quiet_while_you_were_away()
    {
        var from = new DateTime(2026, 10, 16, 23, 0, 0);
        var to = from + TimeSpan.FromHours(2);
        var s = new Session { Id = "long", Cwd = @"C:\p\long", State = SessionState.Working, StateSince = from - TimeSpan.FromHours(1), LastEventAt = from - TimeSpan.FromMinutes(5), StartedAt = from - TimeSpan.FromHours(1) };
        s.Turns.Add(new Turn { StartedAt = from - TimeSpan.FromHours(1), Prompt = "long build" });
        var recap = Recap.Build(new[] { s }, from, to);
        Assert.True(recap.HasNews);
        Assert.Equal(1, recap.WorkingCount);
    }
}
