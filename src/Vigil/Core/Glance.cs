namespace Vigil.Core;

/// <summary>
/// Your sessions at a glance, the three things worth knowing while you work on something else:
/// what needs you, what's still working, and what's done. Used by the desktop pill.
/// </summary>
public sealed record Glance(IReadOnlyList<Session> NeedsYou, IReadOnlyList<Session> Working, IReadOnlyList<Session> Done)
{
    /// <summary>A finished session stays on the pill this long (if you leave it open), then it's old news.</summary>
    public static readonly TimeSpan DoneFor = TimeSpan.FromHours(12);

    public bool Empty => NeedsYou.Count == 0 && Working.Count == 0 && Done.Count == 0;

    /// <summary>Everything on the pill, in the order you'd act on it.</summary>
    public IEnumerable<Session> All => NeedsYou.Concat(Working).Concat(Done);

    public static Glance Of(IEnumerable<Session> sessions, DateTime now)
    {
        var recent = sessions.Where(s => s.Background != true)
            .Where(s => now - s.LastEventAt < DoneFor || s.State is SessionState.Working or SessionState.NeedsYou)
            .OrderByDescending(s => s.LastEventAt).ToList();
        return new Glance(
            // Stopped, out of usage, or gone silent: all of them want a look from you.
            recent.Where(s => s.State is SessionState.NeedsYou or SessionState.Failed or SessionState.Paused or SessionState.Quiet).ToList(),
            recent.Where(s => s.State == SessionState.Working).ToList(),
            recent.Where(IsDone).ToList());
    }

    /// <summary>
    /// Done and still open: Claude finished and the session is waiting for your next message.
    /// Closed sessions (and the quick automated runs that start and end by themselves) are history,
    /// not something to check on, so they're left to What happened.
    /// </summary>
    public static bool IsDone(Session s) => s.State == SessionState.Done;
}
