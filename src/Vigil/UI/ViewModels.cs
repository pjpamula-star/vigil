using System.Windows;
using System.Windows.Media;
using Vigil.Core;

namespace Vigil.UI;

internal static class Palette
{
    public static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    public static Brush ForState(SessionState s) => s switch
    {
        SessionState.Working => Res("Blue"),
        SessionState.NeedsYou => Res("Orange"),
        SessionState.Done => Res("Green"),
        SessionState.Failed => Res("Red"),
        SessionState.Paused => Res("Orange"),
        _ => Res("Text3"),
    };

    public static Brush Tint(Brush b, byte alpha)
    {
        var c = ((SolidColorBrush)b).Color;
        var brush = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }

    public static (Brush bg, Brush fg) ForTone(ChipTone t) => t switch
    {
        ChipTone.Good => (Tint(Res("Green"), 0x24), Res("Green")),
        ChipTone.Attention => (Tint(Res("Orange"), 0x24), Res("Orange")),
        ChipTone.Danger => (Tint(Res("Red"), 0x26), Res("Red")),
        _ => (Res("Control"), Res("Text2")),
    };

    /// <summary>A session that finished its work and then closed reads as "Done", not "Closed".</summary>
    public static string StateLabel(Session s) =>
        s.State == SessionState.Ended && s.CurrentTurn is { Outcome: SessionState.Done } ? "Done" : StateLabel(s.State);

    public static string StateLabel(SessionState s) => s switch
    {
        SessionState.Working => "Working",
        SessionState.NeedsYou => "Needs you",
        SessionState.Done => "Done",
        SessionState.Failed => "Stopped",
        SessionState.Paused => "Usage limit",
        SessionState.Quiet => "Quiet",
        SessionState.Ended => "Closed",
        _ => "Ready",
    };
}

public sealed class ChipVM
{
    public string Text { get; init; } = "";
    public Brush Background { get; init; } = Brushes.Transparent;
    public Brush Foreground { get; init; } = Brushes.White;

    public static ChipVM From(Chip c)
    {
        var (bg, fg) = Palette.ForTone(c.Tone);
        return new ChipVM { Text = c.Text, Background = bg, Foreground = fg };
    }

    public static ChipVM Plain(string text) => From(new Chip(text, ChipTone.Neutral));
}

public sealed class EntryVM
{
    public string Text { get; init; } = "";
    public string When { get; init; } = "";
    public Brush Dot { get; init; } = Brushes.Gray;
    public FontWeight Weight { get; init; } = FontWeights.Normal;

    public static EntryVM From(TimelineEntry e, DateTime now) => new()
    {
        Text = e.Kind == EntryKind.Edit && e.Count > 2 ? $"Edited {e.Count} files" : e.Kind == EntryKind.Edit ? $"Edited {e.Text}" : e.Text,
        When = Core.Text.Ago(e.At, now),
        Dot = e.Kind switch
        {
            EntryKind.Commit or EntryKind.Push or EntryKind.Deploy or EntryKind.Done => Palette.Res("Green"),
            EntryKind.Test => e.Text.Contains("fail", StringComparison.OrdinalIgnoreCase) ? Palette.Res("Red") : Palette.Res("Green"),
            EntryKind.Error or EntryKind.Failed => Palette.Res("Red"),
            EntryKind.NeedsYou or EntryKind.Question or EntryKind.Install or EntryKind.Delete or EntryKind.Paused => Palette.Res("Orange"),
            EntryKind.Prompt => Palette.Res("Text2"),
            _ => Palette.Res("Text3"),
        },
        Weight = e.Kind is EntryKind.Prompt ? FontWeights.SemiBold : FontWeights.Normal,
    };
}

/// <summary>One session, shaped for display. Rebuilt on every change (cheap, and never stale).</summary>
public sealed class SessionVM : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Called every second while the card is open, so working timers tick like a Live Activity.</summary>
    public void Tick()
    {
        if (IsWorking) PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Meta)));
    }

    public required Session Session { get; init; }
    public string Id => Session.Id;
    public string Name => Session.Name;
    public string Folder => Session.FolderName;
    public string StateLabel => Palette.StateLabel(Session);
    public Brush StateBrush => Palette.ForState(Session.State);
    public Brush StateTint => Palette.Tint(StateBrush, 0x26);

    /// <summary>The project's own color and initials, with a ring for its state.</summary>
    public FrameworkElement Avatar => _avatar ??= Avatars.Make(Session.AvatarKey, Session.State);
    private FrameworkElement? _avatar;

    public bool IsWorking => Session.State == SessionState.Working;
    public bool IsDim => Session.State is SessionState.Ended or SessionState.Quiet or SessionState.Ready;
    public double RowOpacity => IsDim ? 0.6 : 1.0;
    public string? RemoteUrl => Session.RemoteUrl;
    public bool HasRemote => Session.RemoteUrl is not null;

    /// <summary>The one line that matters most right now.</summary>
    public string Line
    {
        get
        {
            var s = Session;
            return s.State switch
            {
                SessionState.Working => s.NowLine ?? "Working",
                SessionState.NeedsYou => s.Reason ?? "Waiting for your answer",
                SessionState.Failed or SessionState.Paused or SessionState.Quiet => s.Reason ?? StateLabel,
                SessionState.Done when s.Reason is { } q => $"Asked you: {q}",
                SessionState.Done when s.WaitingOnBackgroundTask => $"Finished; still running in the background: {s.AwaitedTasks[0]}",
                SessionState.Done => s.Summary ?? "Finished",
                SessionState.Ended => s.Summary ?? "Session closed",
                _ => "Waiting for your first prompt",
            };
        }
    }

    /// <summary>Right-hand meta: elapsed time while working, otherwise how long ago.</summary>
    public string Meta => Session.Agent == Session.Codex ? $"Codex · {When}" : When; // Claude is the default; Codex says so

    /// <summary>Elapsed time while working, otherwise how long ago.</summary>
    public string When
    {
        get
        {
            var now = DateTime.Now;
            return Session.State == SessionState.Working
                ? Clock(now - (Session.CurrentTurn?.StartedAt ?? Session.StateSince))
                : Text.Ago(Session.StateSince, now);
        }
    }

    /// <summary>A running stopwatch: "4:07", "1:02:33".</summary>
    public static string Clock(TimeSpan d)
    {
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        return d.TotalHours >= 1 ? $"{(int)d.TotalHours}:{d.Minutes:00}:{d.Seconds:00}" : $"{d.Minutes}:{d.Seconds:00}";
    }

    public string Glyph => Session.State switch
    {
        SessionState.NeedsYou => "!",
        SessionState.Done => "",
        SessionState.Failed => "",
        SessionState.Paused => "‖",
        _ => "",
    };

    public string Headline
    {
        get
        {
            var s = Session;
            switch (s.State)
            {
                case SessionState.Working:
                    return s.CurrentTurn?.Prompt is { Length: > 0 } p ? $"Working on: {Text.Clip(p, 140)}" : "Working";
                case SessionState.NeedsYou:
                    return s.Reason ?? "Waiting for you";
                default:
                    return s.Summary is { Length: > 0 } summary ? summary : "Nothing yet.";
            }
        }
    }

    public string SubHeadline =>
        Session.State == SessionState.Working && Session.NowLine is { } n ? $"Right now: {n}" :
        Session.State is SessionState.Failed or SessionState.Paused or SessionState.Quiet ? Session.Reason ?? "" : "";

    public string Started => $"{Session.FolderName} · started {Session.StartedAt:h:mm tt}";

    public List<ChipVM> Chips => Session.AllFacts.Chips().Select(ChipVM.From).ToList();

    public List<EntryVM> Timeline
    {
        get
        {
            var now = DateTime.Now;
            var list = Session.Timeline.AsEnumerable().Reverse().Take(60).Select(e => EntryVM.From(e, now)).ToList();
            if (Session.State == SessionState.Working && Session.NowLine is { } nowLine)
                list.Insert(0, new EntryVM { Text = nowLine, When = "now", Dot = Palette.Res("Blue"), Weight = FontWeights.SemiBold });
            return list;
        }
    }

    public static SessionVM From(Session s) => new() { Session = s };

    /// <summary>Sort: needs you, working, then most recent.</summary>
    public static int Rank(Session s) => s.State switch
    {
        SessionState.NeedsYou => 0,
        SessionState.Working => 1,
        SessionState.Paused or SessionState.Failed => 2,
        SessionState.Done => 3,
        SessionState.Ready => 4,
        SessionState.Quiet => 5,
        _ => 6,
    };
}

public sealed class RecapItemVM
{
    public required RecapItem Item { get; init; }
    public string Id => Item.Session.Id;
    public string Name => Item.Session.Name;
    /// <summary>Needs you: just what Claude is asking. Stopped: the reason. Otherwise: the summary.</summary>
    public string Headline => Item.State switch
    {
        SessionState.NeedsYou => Item.Reason ?? Item.Headline,
        SessionState.Failed or SessionState.Paused or SessionState.Quiet when Item.Reason is { } r => r,
        _ => Item.Headline,
    };
    public List<ChipVM> Chips
    {
        get
        {
            var chips = Item.Chips.Select(ChipVM.From).ToList();
            if (Item.Worked > TimeSpan.FromMinutes(1)) chips.Add(ChipVM.Plain($"worked {Text.Duration(Item.Worked)}"));
            return chips;
        }
    }
    public bool NeedsYou => Item.State == SessionState.NeedsYou;
    public string StateText => Item.State switch
    {
        SessionState.NeedsYou => $"Needs you · since {Item.Session.StateSince:h:mm tt}",
        SessionState.Done => $"Done {Item.LastAt:h:mm tt}",
        SessionState.Working => "Still working",
        SessionState.Failed => $"Stopped {Item.LastAt:h:mm tt}",
        SessionState.Paused => "Usage limit",
        SessionState.Quiet => "Went quiet",
        _ => Palette.StateLabel(Item.State),
    };
    public Brush StateBrush => Palette.ForState(Item.State);
    public Brush StateTint => Palette.Tint(StateBrush, 0x26);
    public Brush CardBackground => Palette.Res("Card");
    public Brush CardBorder => Brushes.Transparent;
    public string PrimaryAction => Item.Session.RemoteUrl is not null ? "Open conversation" : "Open session";
    public Visibility ActionsVisibility => NeedsYou ? Visibility.Visible : Visibility.Collapsed;
}
