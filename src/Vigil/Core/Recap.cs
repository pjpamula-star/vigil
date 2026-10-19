namespace Vigil.Core;

/// <summary>A per-session slice of what happened in a time window.</summary>
public sealed class RecapItem
{
    public required Session Session { get; init; }
    public required SessionState State { get; init; }
    public required string Headline { get; init; }
    public required List<Chip> Chips { get; init; }
    public string? Reason { get; init; }
    public TimeSpan Worked { get; init; }
    public DateTime LastAt { get; init; }
}

/// <summary>"While you were away": what each session did since a moment in time. Needs-you first.</summary>
public sealed class Recap
{
    public DateTime From { get; init; }
    public DateTime To { get; init; }
    public List<RecapItem> Items { get; init; } = new();
    public List<HistoryEvent> Events { get; init; } = new();

    public int DoneCount => Items.Count(i => i.State == SessionState.Done);
    public int NeedsYouCount => Items.Count(i => i.State is SessionState.NeedsYou);
    public int WorkingCount => Items.Count(i => i.State == SessionState.Working);
    public int ProblemCount => Items.Count(i => i.State is SessionState.Failed or SessionState.Paused or SessionState.Quiet);
    public int FilesChanged { get; init; }
    public bool HasNews => Items.Count > 0 || Events.Any(e => e.Important);

    /// <summary>Background runs in the window, left out of the list.</summary>
    public int BackgroundRuns { get; init; }

    public string Title
    {
        get
        {
            var parts = new List<string>();
            if (DoneCount > 0) parts.Add($"{DoneCount} done");
            if (NeedsYouCount > 0) parts.Add($"{NeedsYouCount} need{(NeedsYouCount == 1 ? "s" : "")} you");
            if (WorkingCount > 0) parts.Add($"{WorkingCount} still working");
            if (ProblemCount > 0) parts.Add($"{ProblemCount} stopped");
            if (parts.Count == 0) return "Nothing new.";
            var s = string.Join(". ", parts) + ".";
            return char.ToUpper(s[0]) + s[1..];
        }
    }

    public static Recap Build(IEnumerable<Session> sessions, DateTime from, DateTime to, IEnumerable<HistoryEvent>? history = null)
    {
        var items = new List<RecapItem>();
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hidden = 0;
        foreach (var s in sessions)
        {
            var turns = s.Turns.Where(t => t.StartedAt >= from || (t.EndedAt ?? to) >= from).ToList();
            // Background runs (scheduled scripts, claude --bg) have no tab to go back to: count, don't list.
            if (s.Background == true)
            {
                if (s.LastEventAt >= from && turns.Count > 0) hidden++;
                continue;
            }
            // Anything that happened while you were away, plus anything still running or waiting on you now.
            var touched = (s.LastEventAt >= from && turns.Count > 0)
                          || s.State is SessionState.NeedsYou or SessionState.Working;
            if (!touched) continue;

            var facts = new Facts();
            foreach (var t in turns) facts.Merge(t.Facts);
            files.UnionWith(facts.Files);
            var last = turns.LastOrDefault(t => t.Headline is not null) ?? turns.LastOrDefault();
            var headline = last?.Headline ?? (last is not null ? Summarizer.Fallback(last) : s.NowLine ?? "Started working.");
            // The latest turn was stopped before it finished: say so after the last real result.
            if (turns.LastOrDefault() is { EndNote: { } note, Headline: null } latest && latest != last)
                headline = $"{headline.TrimEnd('.')}. Later: {char.ToLowerInvariant(note[0])}{note[1..]}.";
            if (s.State == SessionState.Working && s.NowLine is not null) headline = $"Now: {s.NowLine}";
            var worked = TimeSpan.FromTicks(turns.Sum(t =>
                ((t.EndedAt ?? (s.State == SessionState.Working ? to : s.LastEventAt)) - (t.StartedAt < from ? from : t.StartedAt)).Ticks));
            items.Add(new RecapItem
            {
                Session = s,
                State = s.State,
                Headline = headline,
                Chips = facts.Chips(),
                Reason = s.State is SessionState.NeedsYou or SessionState.Failed or SessionState.Paused or SessionState.Quiet ? s.Reason : null,
                Worked = worked < TimeSpan.Zero ? TimeSpan.Zero : worked,
                LastAt = s.LastEventAt,
            });
        }

        int Rank(RecapItem i) => i.State switch
        {
            SessionState.NeedsYou => 0,
            SessionState.Failed or SessionState.Paused or SessionState.Quiet => 1,
            SessionState.Done when i.Session.Turns.LastOrDefault()?.Facts.Question is not null => 2,
            SessionState.Working => 3,
            _ => 4,
        };

        return new Recap
        {
            From = from,
            To = to,
            Items = items.OrderBy(Rank).ThenByDescending(i => i.LastAt).ToList(),
            Events = (history ?? History.Since(from)).Where(e => e.Important).ToList(),
            FilesChanged = files.Count,
            BackgroundRuns = hidden,
        };
    }
}
