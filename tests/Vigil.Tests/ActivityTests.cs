using Vigil.Core;

namespace Vigil.Tests;

public class ActivityTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 3, 0, 0);
    private static DateTime Ago(int minutes) => Now - TimeSpan.FromMinutes(minutes);

    [Fact]
    public void A_working_turn_runs_until_now()
    {
        var s = new Session { State = SessionState.Working, StateSince = Ago(30) };
        s.Turns.Add(new Turn { StartedAt = Ago(30) });
        var spans = Activity.Spans(s, Ago(120), Now, Now);
        Assert.Equal(new[] { new Span(Ago(30), Now, SpanKind.Working) }, spans);
    }

    [Fact]
    public void Waiting_on_you_follows_the_work()
    {
        var s = new Session { State = SessionState.NeedsYou, StateSince = Ago(10), LastEventAt = Ago(10) };
        s.Turns.Add(new Turn { StartedAt = Ago(50) });
        var spans = Activity.Spans(s, Ago(120), Now, Now);
        Assert.Equal(new[] { new Span(Ago(50), Ago(10), SpanKind.Working), new Span(Ago(10), Now, SpanKind.Waiting) }, spans);
    }

    [Fact]
    public void Finished_turns_are_clipped_to_the_window_and_close_ones_merge()
    {
        var s = new Session { State = SessionState.Done, StateSince = Ago(5), LastEventAt = Ago(5) };
        s.Turns.Add(new Turn { StartedAt = Ago(200), EndedAt = Ago(100) });
        s.Turns.Add(new Turn { StartedAt = Ago(60), EndedAt = Ago(40) });
        s.Turns.Add(new Turn { StartedAt = Ago(40), EndedAt = Ago(5) });
        var spans = Activity.Spans(s, Ago(120), Now, Now);
        Assert.Equal(new[] { new Span(Ago(120), Ago(100), SpanKind.Working), new Span(Ago(60), Ago(5), SpanKind.Working) }, spans);
    }

    [Fact]
    public void Ticks_land_on_round_hours()
    {
        var ticks = Activity.Ticks(new DateTime(2026, 10, 16, 19, 26, 0), new DateTime(2026, 10, 17, 2, 56, 0));
        Assert.Equal(new[] { 20, 22, 0, 2 }, ticks.Select(t => t.Hour));
        Assert.All(ticks, t => Assert.Equal(0, t.Minute));
    }

    [Fact]
    public void Overlap_counts_busy_time_once_and_the_most_at_once()
    {
        var a = new List<Span> { new(Ago(60), Ago(30), SpanKind.Working) };
        var b = new List<Span> { new(Ago(40), Ago(10), SpanKind.Working), new(Ago(10), Now, SpanKind.Waiting) };
        var c = new List<Span> { new(Ago(35), Ago(32), SpanKind.Working) };
        var (busy, most) = Activity.Overlap(new[] { a, b, c });
        Assert.Equal(TimeSpan.FromMinutes(50), busy);
        Assert.Equal(3, most);
    }
}

