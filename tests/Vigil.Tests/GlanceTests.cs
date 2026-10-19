using Vigil.Core;

namespace Vigil.Tests;

public class GlanceTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 11, 0, 0);

    private static Session S(string id, SessionState state, double hoursAgo) =>
        new() { Id = id, State = state, LastEventAt = Now - TimeSpan.FromHours(hoursAgo), StateSince = Now - TimeSpan.FromHours(hoursAgo) };

    [Fact]
    public void Sorts_sessions_into_needs_you_working_and_done()
    {
        var g = Glance.Of(new[]
        {
            S("a", SessionState.Working, 0),
            S("b", SessionState.NeedsYou, 0.1),
            S("c", SessionState.Done, 1),
            S("d", SessionState.Quiet, 1),
            S("e", SessionState.Paused, 2),
            S("f", SessionState.Ready, 0.5),
        }, Now);
        Assert.Equal(new[] { "b", "d", "e" }, g.NeedsYou.Select(s => s.Id));
        Assert.Equal(new[] { "a" }, g.Working.Select(s => s.Id));
        Assert.Equal(new[] { "c" }, g.Done.Select(s => s.Id));
    }

    [Fact]
    public void Old_finished_work_drops_off_but_waiting_never_does()
    {
        var g = Glance.Of(new[] { S("old", SessionState.Done, 13), S("waiting", SessionState.NeedsYou, 20) }, Now);
        Assert.Empty(g.Done);
        Assert.Equal(new[] { "waiting" }, g.NeedsYou.Select(s => s.Id));
    }

    [Fact]
    public void Closed_sessions_are_not_counted_as_done()
    {
        var closed = S("closed", SessionState.Ended, 0.5);
        closed.Turns.Add(new Turn { StartedAt = Now.AddHours(-1), EndedAt = Now.AddHours(-0.5), Outcome = SessionState.Done });
        var g = Glance.Of(new[] { closed, S("open", SessionState.Done, 0.2) }, Now);
        Assert.Equal(new[] { "open" }, g.Done.Select(s => s.Id));
    }

    [Fact]
    public void Nothing_to_show_is_empty()
    {
        Assert.True(Glance.Of(Array.Empty<Session>(), Now).Empty);
    }
}
