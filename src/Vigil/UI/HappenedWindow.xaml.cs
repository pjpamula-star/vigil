using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Vigil.Core;
using static Vigil.UI.Kit;

namespace Vigil.UI;

/// <summary>
/// "What happened": the one window. A sentence, three numbers, a card per Claude session
/// (needs-you first), and Vigil's own log at the bottom. Nothing else.
/// </summary>
public partial class HappenedWindow : Window
{
    private readonly App _app;
    private readonly HashSet<string> _expanded = new();
    private DateTime? _awayFrom;
    private bool _sinceAway;
    private Recap? _example;
    private bool _showLog;
    private Recap? _current;

    public HappenedWindow(App app)
    {
        _app = app;
        InitializeComponent();
        WindowStyling.Apply(this);
        Logo.Ink = Palette.Res("Text1");
        var sky = Sky.For(Sky.Now);
        SkyWash.Background = new System.Windows.Media.LinearGradientBrush(new System.Windows.Media.GradientStopCollection
        {
            new(Sky.A(sky.Top, 0xE0), 0), new(Sky.A(sky.Middle, 0x70), 0.55), new(Sky.A(sky.Bottom, 0), 1),
        }, new Point(0, 0), new Point(0, 1));
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        History.Added += () => Dispatcher.BeginInvoke(() => { if (IsVisible && _showLog) Render(); });
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Closing just hides; reopening is instant and keeps what you expanded.
        if (!_app.Quitting)
        {
            e.Cancel = true;
            Hide();
            _example = null;
        }
        base.OnClosing(e);
    }

    /// <summary>Open on the last 24 hours, optionally focused on one session.</summary>
    public void ShowRecent(string? focusId)
    {
        _example = null;
        _sinceAway = false;
        if (focusId is not null) _expanded.Add(focusId);
        Render();
        Present();
        if (focusId is not null) ScrollTo(focusId);
    }

    /// <summary>Open on everything since you left (called when you unlock).</summary>
    public void ShowSince(DateTime from)
    {
        _example = null;
        _awayFrom = from;
        _sinceAway = true;
        Render();
        Present();
    }

    public void ShowExample(Recap recap)
    {
        _example = recap;
        Render();
        Present();
    }

    public void Refresh()
    {
        if (IsVisible && _example is null) Render();
    }

    private void Present()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void Render()
    {
        var now = DateTime.Now;
        var from = _sinceAway && _awayFrom is { } a ? a : now - TimeSpan.FromHours(24);
        var recap = _example ?? Recap.Build(_app.Engine.Tracker.Snapshot(), from, now);
        _current = recap;
        Body.Children.Clear();

        // Range: "Since you left 11:42 PM" vs "Last 24 hours"
        if (_example is not null)
        {
            Body.Children.Add(T("Example: what you'll see after a night away", "Caption", size: 13));
        }
        else if (_awayFrom is { } away)
        {
            var seg = new Segmented(new[] { ($"Since you left at {away:h:mm tt}", 1), ("Last 24 hours", 0) }, v => { _sinceAway = v == 1; Render(); });
            seg.Select(_sinceAway ? 1 : 0);
            Body.Children.Add(seg);
        }
        else
        {
            Body.Children.Add(T("Last 24 hours", "Caption", size: 13));
        }

        // The sentence
        Body.Children.Add(T(recap.Items.Count == 0 ? "Nothing yet." : recap.Title, "TitleXL", size: 30, margin: new Thickness(0, 10, 0, 0)));

        if (recap.Items.Count == 0)
        {
            Body.Children.Add(T("When Claude finishes something, or needs you, it shows up here with a one-line summary.",
                "Caption", size: 14, margin: new Thickness(0, 8, 0, 0)));
        }
        else
        {
            if (recap.FilesChanged > 0)
                Body.Children.Add(T($"{recap.FilesChanged} file{(recap.FilesChanged == 1 ? "" : "s")} changed in total.", "Body", color: Palette.Res("Text2"), margin: new Thickness(0, 6, 0, 4)));

            if (ActivityChart.Build(recap.Items.Select(i => i.Session), recap.From, recap.To, DateTime.Now) is { } chart)
                Body.Children.Add(new Border { Background = Palette.Res("Card"), CornerRadius = new CornerRadius(18), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 12, 0, 4), Child = chart });

            foreach (var item in recap.Items) Body.Children.Add(CardFor(item, from));
            if (recap.BackgroundRuns > 0)
                Body.Children.Add(T($"Not listed: {recap.BackgroundRuns} background run{(recap.BackgroundRuns == 1 ? "" : "s")} (scheduled scripts and claude --bg), which have no tab to go back to.",
                    "Caption", size: 12.5, color: Palette.Res("Text3"), margin: new Thickness(2, 14, 0, 0)));
        }

        // Safety notes (heat, battery, recovery) — only if any
        foreach (var e in recap.Events)
            Body.Children.Add(Note(e.Text, "Orange"));
        if (recap.Items.Count > 0 && recap.Events.Count == 0 && _example is null)
            Body.Children.Add(Note(_app.Engine.Power.Holding ? "Your PC is being kept awake right now." : "No heat or battery problems. Your sleep settings are normal.", "Green"));

        // Vigil's own log
        if (_example is null) Body.Children.Add(LogSection(from));
    }

    private UIElement Stat(int n, string label, string color)
    {
        var p = new StackPanel();
        p.Children.Add(new TextBlock { Text = n.ToString(), FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = 30, FontWeight = FontWeights.SemiBold, Foreground = Palette.Res(color) });
        p.Children.Add(T(label, "Caption", margin: new Thickness(0, 4, 0, 0)));
        return new Border { Background = Palette.Res("Card"), CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(5, 0, 5, 0), Child = p };
    }

    private UIElement CardFor(RecapItem item, DateTime from)
    {
        var s = item.Session;
        var needs = item.State == SessionState.NeedsYou;
        var panel = new StackPanel();

        // Name + state
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = T(s.Name, "BodyStrong", size: 15);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.TextWrapping = TextWrapping.NoWrap;
        name.VerticalAlignment = VerticalAlignment.Center;
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        var avatar = Avatars.Make(s.AvatarKey, item.State, 28);
        avatar.Margin = new Thickness(-4, -4, 8, -4);
        nameRow.Children.Add(avatar);
        nameRow.Children.Add(name);
        top.Children.Add(nameRow);
        var vm = new RecapItemVM { Item = item };
        var pill = new TextBlock { Text = vm.StateText, Foreground = vm.StateBrush, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(pill, 1);
        top.Children.Add(pill);
        panel.Children.Add(top);

        // The one-line summary
        panel.Children.Add(T(vm.Headline, "Body", size: 14.5, color: Palette.Res("Text2"), margin: new Thickness(0, 6, 0, 0)));

        // Exact facts
        var factLine = string.Join(" · ", item.Chips.Where(c => !c.Text.StartsWith("Asked:", StringComparison.Ordinal)).Select(c => c.Text)
            .Concat(item.Worked > TimeSpan.FromMinutes(1) ? new[] { $"worked {Text.Duration(item.Worked)}" } : Array.Empty<string>()));
        var hasFacts = factLine.Length > 0;
        if (hasFacts) panel.Children.Add(T(factLine, "Caption", color: Palette.Res("Text3"), margin: new Thickness(0, 8, 0, 0)));
        if (s.Turns.LastOrDefault()?.Facts.Question is { } q && item.State != SessionState.NeedsYou)
            panel.Children.Add(T($"Asked you: {q}", "Caption", color: Palette.Res("Orange"), margin: new Thickness(0, 4, 0, 0)));

        // Actions
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        if (s.RemoteUrl is { } url)
            actions.Children.Add(Button(needs ? "Answer in Claude" : "Open in Claude", () => App.OpenUrl(url), primary: needs, margin: new Thickness(0, 0, 8, 0)));
        else if (needs)
            actions.Children.Add(Button("Copy command to resume", () => CopyResume(s), primary: true, margin: new Thickness(0, 0, 8, 0)));
        var expanded = _expanded.Contains(s.Id);
        var steps = StepsFor(s, from);
        if (steps.Count > 0)
        {
            var toggle = new Button { Style = S("LinkButton"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            toggle.Content = Icons.Label(expanded ? "Hide steps" : $"Show {steps.Count} step{(steps.Count == 1 ? "" : "s")}", expanded ? Icons.ChevronUp : Icons.ChevronDown, Palette.Res("Blue"));
            toggle.Click += (_, _) =>
            {
                if (!_expanded.Add(s.Id)) _expanded.Remove(s.Id);
                Render();
            };
            actions.Children.Add(toggle);
        }
        if (actions.Children.Count > 0) panel.Children.Add(actions);

        if (expanded && steps.Count > 0)
        {
            var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            foreach (var e in steps) list.Children.Add(StepRow(e));
            panel.Children.Add(list);
        }

        return new Border
        {
            Tag = s.Id,
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18, 16, 18, 16),
            Margin = new Thickness(0, 12, 0, 0),
            Background = Palette.Res("Card"),
            Child = panel,
        };
    }

    /// <summary>The steps inside the window, newest first; plus "now" for a working session.</summary>
    private static List<EntryVM> StepsFor(Session s, DateTime from)
    {
        var now = DateTime.Now;
        var list = s.Timeline.Where(t => t.At >= from).Reverse().Take(40).Select(e => EntryVM.From(e, now)).ToList();
        if (s.State == SessionState.Working && s.NowLine is { } line)
            list.Insert(0, new EntryVM { Text = line, When = "now", Dot = Palette.Res("Blue"), Weight = FontWeights.SemiBold });
        return list;
    }

    private static UIElement StepRow(EntryVM e)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = e.Dot, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 7, 0, 0) });
        var text = T(e.Text, "Body", size: 13.5, weight: e.Weight);
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        var when = T(e.When, "Caption", size: 12, margin: new Thickness(12, 1, 0, 0));
        Grid.SetColumn(when, 2);
        g.Children.Add(when);
        return g;
    }

    private static UIElement Note(string text, string color)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 16, 0, 0) };
        row.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = Palette.Res(color), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(T(text, "Caption", size: 13, margin: new Thickness(8, 0, 0, 0)));
        return row;
    }

    private UIElement LogSection(DateTime from)
    {
        var events = _app.Engine.Demo ? new List<HistoryEvent>() : History.Since(from);
        var panel = new StackPanel { Margin = new Thickness(0, 26, 0, 0) };
        var toggle = new Button { Style = S("LinkButton"), HorizontalAlignment = HorizontalAlignment.Left };
        toggle.Content = new TextBlock
        {
            Text = events.Count == 0 ? "Vigil's log is empty so far" : _showLog ? "Hide Vigil's log" : $"Show Vigil's log ({events.Count})",
            Foreground = Palette.Res(events.Count == 0 ? "Text3" : "Text2"), FontSize = 12.5,
        };
        toggle.Click += (_, _) => { if (events.Count == 0) return; _showLog = !_showLog; Render(); };
        panel.Children.Add(toggle);
        panel.Children.Add(T("Everything Vigil did and why: when it kept your PC awake, the lid, and when sleep came back.", "Caption", size: 12, color: Palette.Res("Text3"), margin: new Thickness(0, 2, 0, 0)));

        if (_showLog && events.Count > 0)
        {
            var list = new StackPanel();
            foreach (var e in events.AsEnumerable().Reverse().Take(150))
            {
                var g = new Grid { Margin = new Thickness(0, 5, 0, 5) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.Children.Add(T(e.At.ToString("h:mm tt"), "Caption", size: 12.5));
                var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = LogDot(e.Kind), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
                Grid.SetColumn(dot, 1);
                g.Children.Add(dot);
                var text = T(e.Text, "Body", size: 13.5, weight: e.Important ? FontWeights.SemiBold : FontWeights.Normal);
                Grid.SetColumn(text, 2);
                g.Children.Add(text);
                list.Children.Add(g);
            }
            panel.Children.Add(Card(list, padding: new Thickness(16, 8, 16, 8), margin: new Thickness(0, 10, 0, 0)));
        }
        return panel;
    }

    private static Brush LogDot(ActivityKind k) => k switch
    {
        ActivityKind.Awake => Palette.Res("Blue"),
        ActivityKind.Done => Palette.Res("Green"),
        ActivityKind.NeedsYou => Palette.Res("Orange"),
        ActivityKind.Warning => Palette.Res("Red"),
        ActivityKind.You => Palette.Res("Text2"),
        ActivityKind.Lid => Palette.Res("Text2"),
        _ => Palette.Res("Text3"),
    };

    private void ScrollTo(string id)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var card = Body.Children.OfType<Border>().FirstOrDefault(b => b.Tag as string == id);
            card?.BringIntoView();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void CopyResume(Session s)
    {
        try
        {
            Clipboard.SetText($"cd \"{s.Cwd}\"; claude --resume {s.Id}");
            _app.Toast("Copied", "Paste it into a terminal to pick this session back up.");
        }
        catch (Exception ex) { Log.Warn($"Clipboard: {ex.Message}"); }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_current is not { } r) return;
        var sb = new StringBuilder();
        sb.AppendLine($"{r.Title} ({r.From:h:mm tt} – {r.To:h:mm tt})");
        foreach (var i in r.Items)
        {
            sb.AppendLine($"- {i.Session.Name} [{Palette.StateLabel(i.State)}]: {i.Headline}");
            if (i.Chips.Count > 0) sb.AppendLine($"  {string.Join(" · ", i.Chips.Select(c => c.Text))}");
        }
        try
        {
            Clipboard.SetText(sb.ToString());
            CopyText.Text = "Copied";
        }
        catch (Exception ex) { Log.Warn($"Clipboard: {ex.Message}"); }
    }

    private void OnHow(object sender, RoutedEventArgs e) => _app.ShowWelcome();

    private void OnSettings(object sender, RoutedEventArgs e) => _app.ShowSettings();

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
