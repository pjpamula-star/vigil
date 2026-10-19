namespace Vigil.Core;

public enum HoldKind { None, Working, WaitingForYou, UsageLimit, Manual, Grace }

public sealed record HoldDecision(bool Hold, HoldKind Kind, string Headline, string Detail, DateTime? Until = null);

/// <summary>
/// Decides whether the PC should be held awake. Pure: same inputs, same answer, easy to test.
/// The rule: only while something is actually happening, and never open-ended.
/// </summary>
public sealed class KeepAwakePolicy
{
    public TimeSpan WaitForYou { get; set; } = TimeSpan.FromMinutes(60);
    public TimeSpan UsageLimitWait { get; set; } = TimeSpan.FromHours(5);
    public TimeSpan Grace { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How long to stay awake for a background task Claude left running (it picks up when the task ends).
    /// Bounded, because some tasks, like watchers, never end.
    /// </summary>
    public TimeSpan BackgroundTaskWait { get; set; } = TimeSpan.FromMinutes(60);

    private DateTime? _lastNeeded;

    public HoldDecision Decide(IReadOnlyList<Session> sessions, DateTime? manualUntil, DateTime now)
    {
        var working = sessions.Where(s => s.State == SessionState.Working).ToList();
        var waiting = sessions.Where(s => s.State == SessionState.NeedsYou && now - s.StateSince < WaitForYou).ToList();
        var paused = sessions.Where(s => s.State == SessionState.Paused && now - s.StateSince < UsageLimitWait).ToList();
        var tasks = sessions.Where(s => s.WaitingOnBackgroundTask && now - s.StateSince < BackgroundTaskWait).ToList();

        HoldDecision? need = null;
        if (working.Count > 0)
        {
            var extra = waiting.Count > 0 ? $", {waiting.Count} waiting on you" : "";
            need = new HoldDecision(true, HoldKind.Working, "Safe to close the lid.",
                $"Staying awake while {Count(working.Count, "session")} work{(working.Count == 1 ? "s" : "")}{extra}. Sleeps on its own after.");
        }
        else if (tasks.Count > 0)
        {
            var until = tasks.Min(s => s.StateSince) + BackgroundTaskWait;
            need = new HoldDecision(true, HoldKind.Working, "Safe to close the lid.",
                $"A background task is still running, and Claude picks up when it ends. Staying awake until {until:h:mm tt} at most.", until);
        }
        else if (waiting.Count > 0)
        {
            var until = waiting.Min(s => s.StateSince) + WaitForYou;
            need = new HoldDecision(true, HoldKind.WaitingForYou, "Claude is waiting on you.",
                $"Staying awake until {until:h:mm tt} so you can answer from your phone.", until);
        }
        else if (paused.Count > 0)
        {
            var until = paused.Min(s => s.StateSince) + UsageLimitWait;
            need = new HoldDecision(true, HoldKind.UsageLimit, "Waiting for the usage limit to reset.",
                $"Claude picks up where it left off. Staying awake until {until:h:mm tt} at most.", until);
        }
        else if (manualUntil is { } m && m > now)
        {
            need = new HoldDecision(true, HoldKind.Manual, "Keeping your PC awake.",
                $"You asked for this. Ends at {m:h:mm tt}.", m);
        }

        if (need is not null)
        {
            _lastNeeded = now;
            return need;
        }

        if (_lastNeeded is { } last && now - last < Grace)
        {
            return new HoldDecision(true, HoldKind.Grace, "All done.",
                $"Your PC can sleep again in {Math.Max(1, (int)Math.Ceiling((Grace - (now - last)).TotalMinutes))} min.", last + Grace);
        }

        _lastNeeded = null;
        return new HoldDecision(false, HoldKind.None, "Your PC sleeps normally.",
            "Nothing is running. When Claude starts working, the eye lights up and your PC stays awake until it's done.");
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";
}
