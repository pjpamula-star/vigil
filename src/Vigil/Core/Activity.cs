namespace Vigil.Core;

public enum SpanKind { Working, Waiting }

public readonly record struct Span(DateTime Start, DateTime End, SpanKind Kind)
{
    public TimeSpan Length => End - Start;
}

/// <summary>
/// When a session was busy, for the activity chart: one span per turn while Claude worked,
/// and the wait since it asked you something. Clipped to the window being shown.
/// </summary>
public static class Activity
{
    /// <summary>Spans shorter than this still get drawn, but neighbors closer than this merge.</summary>
    private static readonly TimeSpan MergeGap = TimeSpan.FromMinutes(1);

    public static List<Span> Spans(Session s, DateTime from, DateTime to, DateTime now)
    {
        var raw = new List<Span>();
        var waitingSince = s.State == SessionState.NeedsYou ? s.StateSince : (DateTime?)null;

        for (var i = 0; i < s.Turns.Count; i++)
        {
            var t = s.Turns[i];
            var isLast = i == s.Turns.Count - 1;
            DateTime end = t.EndedAt
                ?? (isLast && s.State == SessionState.Working ? now
                    : isLast && waitingSince is { } w ? w
                    : isLast ? s.LastEventAt
                    : s.Turns[i + 1].StartedAt);
            if (end > t.StartedAt) raw.Add(new Span(t.StartedAt, end, SpanKind.Working));
        }
        if (waitingSince is { } since) raw.Add(new Span(since, now, SpanKind.Waiting));

        var result = new List<Span>();
        foreach (var span in raw.OrderBy(x => x.Start))
        {
            var start = span.Start < from ? from : span.Start;
            var end = span.End > to ? to : span.End;
            if (end <= start) continue;
            if (result.Count > 0 && result[^1].Kind == span.Kind && start - result[^1].End <= MergeGap)
            {
                var prev = result[^1];
                result[^1] = prev with { End = end > prev.End ? end : prev.End };
            }
            else result.Add(new Span(start, end, span.Kind));
        }
        return result;
    }

    /// <summary>Round-hour tick marks for an axis from..to, at most <paramref name="max"/> of them.</summary>
    public static List<DateTime> Ticks(DateTime from, DateTime to, int max = 5)
    {
        var length = to - from;
        var step = new[] { 0.25, 0.5, 1, 2, 3, 4, 6, 12, 24 }
            .Select(TimeSpan.FromHours)
            .FirstOrDefault(st => length / st <= max, TimeSpan.FromHours(24));
        // Land on the clock's own marks: 12, 3, 6, 9 for a 3-hour step.
        var stepHours = Math.Max(1, (int)step.TotalHours);
        var first = from.Date.AddHours(from.Hour - from.Hour % stepHours);
        while (first < from) first += step;
        var ticks = new List<DateTime>();
        for (var t = first; t <= to; t += step) ticks.Add(t);
        return ticks;
    }

    /// <summary>Time with at least one session working, and the most working at once.</summary>
    public static (TimeSpan Busy, int MostAtOnce) Overlap(IEnumerable<IReadOnlyList<Span>> lanes)
    {
        var edges = lanes.SelectMany(l => l.Where(x => x.Kind == SpanKind.Working))
            .SelectMany(x => new[] { (At: x.Start, Delta: 1), (At: x.End, Delta: -1) })
            .OrderBy(e => e.At).ThenBy(e => e.Delta)
            .ToList();
        var busy = TimeSpan.Zero;
        int running = 0, most = 0;
        DateTime? since = null;
        foreach (var (at, delta) in edges)
        {
            if (running > 0 && since is { } s0) busy += at - s0;
            running += delta;
            most = Math.Max(most, running);
            since = at;
        }
        return (busy, most);
    }
}
