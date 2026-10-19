using Vigil.Core;
using Vigil.UI;

namespace Vigil.Tests;

public class BackgroundRunTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("vigil-bg").FullName;
    private static readonly DateTime Now = new(2026, 10, 17, 12, 40, 0);

    private string Transcript(string name, string userLine)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllLines(path, new[]
        {
            """{"type":"mode","mode":"normal"}""",
            """{"type":"attachment","cwd":"C:\\Users\\sam\\apply-agent\\logs"}""",
            userLine,
        });
        return path;
    }

    [Fact]
    public void A_scheduled_or_bg_run_is_recognized_from_its_first_prompt()
    {
        var bg = Transcript("bg.jsonl", """{"type":"user","sessionKind":"bg","message":{}}""");
        var tab = Transcript("tab.jsonl", """{"type":"user","message":{}}""");
        Assert.Equal((true, @"C:\Users\sam\apply-agent\logs"), TranscriptScanner.Head(bg));
        Assert.False(TranscriptScanner.Head(tab).background);
        Assert.Null(TranscriptScanner.Head(Path.Combine(_dir, "missing.jsonl")).background);
    }

    [Fact]
    public void Background_runs_stay_off_the_pill_and_out_of_the_list_but_are_counted()
    {
        Session S(string id, SessionState state, bool bg)
        {
            var s = new Session { Id = id, State = state, Background = bg, StateSince = Now.AddMinutes(-5), LastEventAt = Now.AddMinutes(-5) };
            s.Turns.Add(new Turn { StartedAt = Now.AddMinutes(-10), EndedAt = Now.AddMinutes(-5), Outcome = SessionState.Done });
            return s;
        }
        var all = new[] { S("tab", SessionState.Done, false), S("bg1", SessionState.Done, true), S("bg2", SessionState.Working, true) };
        Assert.Equal(new[] { "tab" }, Glance.Of(all, Now).All.Select(s => s.Id));
        var recap = Recap.Build(all, Now.AddHours(-24), Now);
        Assert.Equal(new[] { "tab" }, recap.Items.Select(i => i.Session.Id));
        Assert.Equal(2, recap.BackgroundRuns);
    }

    [Fact]
    public void The_avatar_stands_for_the_project_or_for_the_session_itself()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var project = new Session { Cwd = Path.Combine(home, "portfolio-site", "src"), StartCwd = Path.Combine(home, "portfolio-site"), AiTitle = "Personal website redesign" };
        var fromHome = new Session { Cwd = Path.Combine(home, ".claude", "memory"), StartCwd = home, AiTitle = "Bakery website redesign" };
        Assert.Equal("portfolio-site", project.AvatarKey);
        Assert.Equal("Bakery website redesign", fromHome.AvatarKey);
        Assert.Equal("BW", Avatars.Initials(fromHome.AvatarKey));
        Assert.Equal("PS", Avatars.Initials(project.AvatarKey));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
