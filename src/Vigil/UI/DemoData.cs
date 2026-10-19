using Vigil.Core;

namespace Vigil.UI;

/// <summary>
/// Fixed fake sessions for demo mode, screenshots and the launch video (vigil.exe --demo, --demo-playback).
/// One night: four projects start at 11:52 PM, three finish, one stops to ask. Nothing here reads this PC.
/// </summary>
internal static class DemoData
{
    /// <summary>The morning you come back. Fixed for --demo-playback, so every recording is identical.</summary>
    public static readonly DateTime PlaybackMorning = new(2026, 10, 20, 7, 14, 0);

    /// <summary>The most recent 7:14 AM, so --demo times read naturally today.</summary>
    public static DateTime RecentMorning()
    {
        var m = DateTime.Today.AddHours(7).AddMinutes(14);
        return DateTime.Now < m ? m.AddDays(-1) : m;
    }

    /// <summary>When the night started: 11:52 PM the evening before.</summary>
    public static DateTime NightStart(DateTime morning) => morning.Date.AddDays(-1).AddHours(23).AddMinutes(52);

    /// <summary>How long Vigil held the PC awake: 11:52 PM until 4:43 AM.</summary>
    /// <remarks>
    /// The last session asked for you at 3:41 AM. Vigil waits up to an hour for an answer
    /// (KeepAwakePolicy.WaitForYou), then 2 more minutes (Grace), and lets the PC sleep at 4:43 AM.
    /// </remarks>
    public static readonly TimeSpan AwakeFor = new(4, 51, 0);

    public static void Load(SessionTracker tracker, DateTime? morning = null)
    {
        foreach (var s in Night(morning ?? RecentMorning())) tracker.Put(s);
    }

    /// <summary>The four projects, as they stand at 7:14 AM.</summary>
    public static List<Session> Night(DateTime morning)
    {
        var start = NightStart(morning);
        DateTime At(int hour, int minute) => morning.Date.AddHours(hour).AddMinutes(minute);

        Session Make(string name, SessionState state, DateTime end, string prompt, string? headline = null, string? reason = null)
        {
            var s = new Session
            {
                Id = $"demo-{name}", Cwd = $@"C:\Users\sam\{name}", StartCwd = $@"C:\Users\sam\{name}", AiTitle = name,
                State = state, StateSince = end, StartedAt = start, LastEventAt = end,
                Reason = reason,
                RemoteUrl = state == SessionState.NeedsYou ? "https://claude.ai/code/session_demo" : null,
            };
            s.Turns.Add(new Turn
            {
                StartedAt = start, Prompt = prompt,
                EndedAt = state == SessionState.Done ? end : null,
                Outcome = state == SessionState.Done ? SessionState.Done : SessionState.Working,
                Headline = headline,
            });
            s.Timeline.Add(new TimelineEntry { At = start, Kind = EntryKind.Prompt, Text = $"You asked: “{prompt}”" });
            s.Timeline.Add(state == SessionState.Done
                ? new TimelineEntry { At = end, Kind = EntryKind.Done, Text = $"Finished: {headline}" }
                : new TimelineEntry { At = end, Kind = EntryKind.NeedsYou, Text = reason ?? "" });
            return s;
        }

        return new List<Session>
        {
            // Needs you
            Make("landing-page", SessionState.NeedsYou, At(3, 41), "add the pricing section and install what it needs",
                reason: "Waiting for your approval to run npm install."),
            // Done, newest first
            Make("api-server", SessionState.Done, At(2, 58), "add rate limiting with tests",
                headline: "Added rate limiting and 12 tests, all passing."),
            Make("data-pipeline", SessionState.Done, At(1, 47), "fix the crash on empty CSV rows",
                headline: "Fixed crash on empty CSV rows."),
            Make("docs-site", SessionState.Done, At(0, 36), "rewrite the setup guide",
                headline: "Rewrote the setup guide."),
        };
    }

    /// <summary>What Vigil's log says about the night. The text matches what the engine writes.</summary>
    public static List<HistoryEvent> NightEvents(DateTime morning) => new()
    {
        new HistoryEvent
        {
            At = morning.Date.AddHours(2).AddMinutes(10),
            Important = true,
            Text = "Running warm: easing off. Windows is slowing the CPU to cool down, so Vigil capped its speed. If it's still throttling in a few minutes, the PC hibernates.",
        },
    };

    /// <summary>The recap you see at 7:14 AM.</summary>
    public static Recap OvernightRecap(DateTime? morning = null)
    {
        var m = morning ?? RecentMorning();
        var night = Night(m);
        // Demo data never reads this PC's own history.
        return Recap.Build(night, NightStart(m), m, history: NightEvents(m));
    }
}
