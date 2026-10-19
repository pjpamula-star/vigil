using System.Windows.Media;
using Vigil.Core;

namespace Vigil.UI;

/// <summary>
/// The single source of truth for "what is Vigil doing, in plain words".
/// The tray card, tooltip and right-click menu all say exactly the same thing.
/// </summary>
internal sealed record Status(string Title, string Line, TrayState Icon, string ColorKey)
{
    public static Status Of(App app)
    {
        var e = app.Engine;
        var d = e.Decision;

        if (!app.HooksInstalled)
            return new("Not connected", "Connect Vigil to Claude Code to get started.", TrayState.Idle, "Text2");

        if (e.Power.Holding && e.Safety.Level == SafetyLevel.Hot)
            return new("Too hot", e.Safety.Detail, TrayState.Hot, "Red");
        if (e.Power.Holding && e.Safety.Level == SafetyLevel.Warm)
            return new("Cooling down", e.Safety.Detail, TrayState.Hot, "Orange");

        if (e.Snoozed)
            return new("Paused", $"Your PC sleeps normally. Vigil turns back on at {e.SnoozedUntil:h:mm tt}.", TrayState.Paused, "Text3");

        var needsYou = e.Tracker.Snapshot().Any(s => s.State == SessionState.NeedsYou && s.Background != true);
        return d.Kind switch
        {
            HoldKind.Working when needsYou => new("Awake", "Claude is working, and a session needs you.", TrayState.NeedsYou, "Blue"),
            HoldKind.Working => new("Awake", "Claude is working. Safe to close the lid.", TrayState.Working, "Blue"),
            HoldKind.WaitingForYou => new("Needs you", $"Claude is waiting for your answer. Staying awake until {d.Until:h:mm tt}.", TrayState.NeedsYou, "Orange"),
            HoldKind.UsageLimit => new("Awake", $"Waiting for Claude's usage limit to reset. At most until {d.Until:h:mm tt}.", TrayState.Working, "Blue"),
            HoldKind.Manual => new("Awake", $"You asked for this. Sleeps at {d.Until:h:mm tt}.", TrayState.Manual, "Text1"),
            HoldKind.Grace => new("All done", d.Detail, TrayState.Working, "Green"),
            _ => new("Asleep when idle", "Turns on by itself when Claude starts working.", TrayState.Idle, "Text2"),
        };
    }

    public Color Color => ((SolidColorBrush)Palette.Res(ColorKey)).Color;

    /// <summary>"Lid open · Plugged in · 87%"</summary>
    public static string Facts(Engine e)
    {
        var parts = new List<string>();
        if (e.LidOpen is { } lid) parts.Add(lid ? "Lid open" : "Lid closed");
        var p = System.Windows.Forms.SystemInformation.PowerStatus;
        if (p.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)
        {
            parts.Add(p.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline ? "On battery" : "Plugged in");
            if (p.BatteryLifePercent is >= 0 and <= 1) parts.Add($"{Math.Round(p.BatteryLifePercent * 100)}%");
        }
        parts.Add(e.Safety.Level == SafetyLevel.Ok ? "Temperature fine" : e.Safety.Headline);
        return string.Join("  ·  ", parts);
    }

    /// <summary>Accepts "2", "2h", "1.5", "30m", "90 min", "1:30". Returns null if it can't tell.</summary>
    public static TimeSpan? ParseDuration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim().ToLowerInvariant().Replace(" ", "");
        if (t.Contains(':'))
        {
            var parts = t.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m))
                return Valid(TimeSpan.FromHours(h) + TimeSpan.FromMinutes(m));
            return null;
        }
        foreach (var (suffix, minutes) in new[] { ("minutes", 1.0), ("minute", 1.0), ("mins", 1.0), ("min", 1.0), ("m", 1.0), ("hours", 60.0), ("hour", 60.0), ("hrs", 60.0), ("hr", 60.0), ("h", 60.0) })
        {
            if (t.EndsWith(suffix) && double.TryParse(t[..^suffix.Length], System.Globalization.CultureInfo.InvariantCulture, out var n))
                return Valid(TimeSpan.FromMinutes(n * minutes));
        }
        return double.TryParse(t, System.Globalization.CultureInfo.InvariantCulture, out var hours) ? Valid(TimeSpan.FromHours(hours)) : null;

        static TimeSpan? Valid(TimeSpan d) => d > TimeSpan.Zero && d <= TimeSpan.FromHours(24) ? d : null;
    }
}
