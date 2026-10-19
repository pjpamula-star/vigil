using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Vigil.Core;
using static Vigil.UI.Kit;
using Path = System.Windows.Shapes.Path;

namespace Vigil.UI;

/// <summary>
/// "Welcome back." Shown when you come back and Claude did something while you were away:
/// the eye opens, a greeting, how long you were gone, one sentence, one line per session.
/// The only motion is the eye opening and the content settling in.
/// </summary>
public sealed class WelcomeBackWindow : Window
{
    private readonly App _app;
    private readonly Recap _recap;
    private readonly bool _example;
    private readonly bool _playback;

    private readonly EyeView _eye = new() { Width = 44, Height = 44, Ink = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly List<(FrameworkElement Element, double At)> _stagger = new();
    private readonly LinearGradientBrush _sky;
    private readonly TranslateTransform _sunRise = new(0, 320);
    private Ellipse? _sun;
    private Canvas? _stars;
    private Canvas? _drift;

    // Text over the sky: white, a little softer for secondary lines. Readable on night navy and day blue alike.
    private static readonly Brush W100 = Frozen(Colors.White, 0xFF);
    private static readonly Brush W80 = Frozen(Colors.White, 0xD0);
    private static readonly Brush W65 = Frozen(Colors.White, 0xA6);
    private static readonly Brush Glass = Frozen(Colors.Black, 0x40);
    private static readonly Brush GlassLine = Frozen(Colors.White, 0x22);
    private static Brush Frozen(Color c, byte a) { var b = new SolidColorBrush(Sky.A(c, a)); b.Freeze(); return b; }

    /// <param name="playback">The demo recording: the entrance runs on a fixed timeline (see Play).</param>
    public WelcomeBackWindow(App app, Recap recap, bool example = false, bool playback = false)
    {
        _app = app;
        _recap = recap;
        _example = example;
        _playback = playback;

        Title = "Welcome back";
        Width = 540;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        // The window is the sky: it starts at night and brightens into the sky outside right now.
        _sky = Sky.Brush(Sky.Night);
        Background = _sky;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        WindowStyling.Apply(this, backdrop: Backdrop.None);
        MouseLeftButtonDown += (_, e) => { if (e.ChangedButton == MouseButton.Left) try { DragMove(); } catch { } };
        // It appears on its own, often while you're typing elsewhere: it never takes the keyboard,
        // so a stray Enter can't dismiss it before you've seen it. Escape works once you've clicked it.
        ShowActivated = false;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        Content = Build();
        Opacity = 0;
        Loaded += (_, _) => Play();
    }

    // ---------- layout ----------

    private UIElement Build()
    {
        var layers = new Grid { ClipToBounds = true };
        var now = Sky.Now;
        // Stars: out when you come back, fading as the sky brightens (they stay at night).
        _stars = new Canvas { IsHitTestVisible = false };
        var rng = new Random(7);
        for (var i = 0; i < 34; i++)
        {
            var size = rng.NextDouble() < 0.15 ? 2.6 : 1.6;
            var star = new Ellipse { Width = size, Height = size, Fill = Brushes.White, Opacity = 0.25 + rng.NextDouble() * 0.6 };
            Canvas.SetLeft(star, rng.NextDouble() * 540);
            Canvas.SetTop(star, rng.NextDouble() * 360);
            _stars.Children.Add(star);
        }
        layers.Children.Add(_stars);

        // Then the sky keeps moving, slowly: aurora at night, clouds by day.
        _drift = new Canvas { IsHitTestVisible = false, Opacity = 0 };
        var k = 0;
        foreach (var (color, alpha, x, y, w, h) in Sky.Drift(now))
        {
            var blob = new Ellipse
            {
                Width = w, Height = h,
                Fill = new RadialGradientBrush(new GradientStopCollection { new(Sky.A(color, alpha), 0), new(Sky.A(color, (byte)(alpha / 3)), 0.55), new(Sky.A(color, 0), 1) }),
                RenderTransform = new TranslateTransform(),
            };
            Canvas.SetLeft(blob, x);
            Canvas.SetTop(blob, y);
            var move = (TranslateTransform)blob.RenderTransform;
            var seconds = 16 + k * 5;
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-50 + k * 20, 70 - k * 15, TimeSpan.FromSeconds(seconds)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-10, 14, TimeSpan.FromSeconds(seconds * 0.7)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            _drift.Children.Add(blob);
            k++;
        }
        layers.Children.Add(_drift);
        _sun = new Ellipse
        {
            Width = 640, Height = 640,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(-200, 0, -200, -420),
            Fill = new RadialGradientBrush(new GradientStopCollection
            {
                new(Sky.A(Sky.SunColor(now), Sky.HasSun(now) ? (byte)0xA0 : (byte)0x55), 0),
                new(Sky.A(Sky.SunColor(now), 0x30), 0.45),
                new(Sky.A(Sky.SunColor(now), 0x00), 1),
            }),
            RenderTransform = _sunRise,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        layers.Children.Add(_sun);
        layers.Children.Add(Page());
        return layers;
    }

    private UIElement Page()
    {
        var root = new StackPanel { Margin = new Thickness(32, 28, 32, 26) };

        var top = new Grid();
        top.Children.Add(Eye());
        var close = new Button { Style = S("IconButton"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -6, -10, 0), ToolTip = "Close" };
        close.Content = new TextBlock { Text = Icons.Close, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 11, Foreground = W80 };
        close.Click += (_, _) => Close();
        top.Children.Add(close);
        root.Children.Add(top);

        root.Children.Add(Stagger(T(_example ? "Welcome back." : $"{Greeting()}, {FirstName(_app)}.", "TitleXL", size: 32, color: W100, margin: new Thickness(0, 18, 0, 0)), at: 0.0));
        root.Children.Add(Stagger(T(AwayLine(), "Body", size: 15, color: W80, margin: new Thickness(0, 6, 0, 0)), at: 0.0));

        if (_recap.Items.Count > 0)
        {
            root.Children.Add(Stagger(T(_recap.Title, "TitleL", color: W100, margin: new Thickness(0, 22, 0, 10)), at: 0.4));

            if (ActivityChart.Build(_recap.Items.Select(i => i.Session), _recap.From, _recap.To, Sky.Now, maxLanes: 5, labelWidth: 104) is { } chart)
                root.Children.Add(Stagger(new Border { Background = Glass, CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12, 16, 10), Margin = new Thickness(0, 0, 0, 12), Child = chart }, at: 0.4));

            var list = new StackPanel();
            var shown = _recap.Items.Take(5).ToList();
            for (var i = 0; i < shown.Count; i++)
            {
                // Playback: the first row (what needs you) arrives with the card, the rest follow one by one.
                var rowAt = 2.8 + 0.4 * (i - 1);
                if (i > 0) list.Children.Add(Row(new Border { Height = 1, Background = GlassLine, Margin = new Thickness(16, 0, 0, 0) }, i, rowAt));
                list.Children.Add(Row(Highlight(shown[i]), i, rowAt));
            }
            if (_recap.Items.Count > shown.Count)
                list.Children.Add(T($"and {_recap.Items.Count - shown.Count} more in What happened", "Caption", margin: new Thickness(16, 4, 16, 12)));
            root.Children.Add(Stagger(new Border { Background = Glass, CornerRadius = new CornerRadius(14), Child = list }, at: 0.4));
        }

        root.Children.Add(Stagger(T(PcLine(), "Caption", color: W65, margin: new Thickness(0, 16, 0, 0)), at: 4.0));

        var buttons = new Grid { Margin = new Thickness(0, 22, 0, 0) };
        var see = Button("See everything", () =>
        {
            if (_example) _app.ShowWelcomeBackExampleDetails();
            else _app.ShowHappenedSince(_recap.From);
            Close();
        });
        see.HorizontalAlignment = HorizontalAlignment.Left;
        see.Background = Glass;
        var go = Button("Done", Close, primary: true);
        go.HorizontalAlignment = HorizontalAlignment.Right;
        go.Padding = new Thickness(26, 9, 26, 9);
        buttons.Children.Add(see);
        buttons.Children.Add(go);
        root.Children.Add(Stagger(buttons, at: 4.0));
        return root;
    }

    /// <summary>The Vigil mark in white: an eye with a crescent-moon pupil. It opens as you come back, then lives.</summary>
    private UIElement Eye() => _eye;

    /// <summary>One session: name and status in words, then what it did or needs.</summary>
    private FrameworkElement Highlight(RecapItem item)
    {
        var s = item.Session;
        var needs = item.State == SessionState.NeedsYou;
        var g = new Grid { Margin = new Thickness(12, 12, 16, 12) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var avatar = Avatars.Make(s.AvatarKey, item.State, 32);
        avatar.VerticalAlignment = VerticalAlignment.Top;
        avatar.Margin = new Thickness(0, -2, 10, 0);
        g.Children.Add(avatar);

        var text = new StackPanel();
        var head = new TextBlock { Style = S("BodyStrong"), FontSize = 14.5, TextTrimming = TextTrimming.CharacterEllipsis };
        head.Inlines.Add(new System.Windows.Documents.Run(s.Name));
        head.Inlines.Add(new System.Windows.Documents.Run("   " + StateWords(item)) { Foreground = Palette.ForState(item.State == SessionState.Ended ? SessionState.Done : item.State), FontSize = 12.5 });
        text.Children.Add(head);
        var what = needs ? item.Reason ?? item.Headline
                 : item.State == SessionState.Working && s.NowLine is { } now ? $"Now: {now}"
                 : item.Headline;
        text.Children.Add(T(what, "Body", color: W80, margin: new Thickness(0, 3, 0, 0)));
        var facts = string.Join(" · ", item.Chips.Where(c => !c.Text.StartsWith("Asked:", StringComparison.Ordinal)).Select(c => c.Text));
        if (facts.Length > 0) text.Children.Add(T(facts, "Caption", color: W65, margin: new Thickness(0, 3, 0, 0)));
        if (!needs && s.Turns.LastOrDefault()?.Facts.Question is { } q)
            text.Children.Add(T($"Asked you: {q}", "Caption", color: Palette.Res("Orange"), margin: new Thickness(0, 3, 0, 0)));
        Grid.SetColumn(text, 1);
        g.Children.Add(text);

        if (needs)
        {
            var answer = Button(s.RemoteUrl is not null ? "Answer" : "Open", () =>
            {
                if (_example) { Close(); return; }
                if (s.RemoteUrl is { } url) App.OpenUrl(url);
                else _app.ShowHappened(s.Id);
                Close();
            }, primary: true, margin: new Thickness(12, 0, 0, 0));
            answer.Padding = new Thickness(16, 6, 16, 6);
            answer.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(answer, 2);
            g.Children.Add(answer);
        }
        return g;
    }

    private static string StateWords(RecapItem i) => i.State switch
    {
        SessionState.NeedsYou => "Needs you",
        SessionState.Working => "Still working",
        SessionState.Done or SessionState.Ended => $"Done {i.LastAt:h:mm tt}",
        SessionState.Failed => "Stopped",
        SessionState.Paused => "Usage limit",
        SessionState.Quiet => "No activity",
        _ => Palette.StateLabel(i.State),
    };

    /// <summary>Joins the entrance. at: when it arrives in playback, in seconds; otherwise it follows the one before.</summary>
    private T Stagger<T>(T e, double at = 0) where T : FrameworkElement
    {
        e.Opacity = 0;
        e.RenderTransform = new TranslateTransform(0, _playback ? 12 : 10);
        _stagger.Add((e, at));
        return e;
    }

    /// <summary>List rows only stagger on their own in playback; normally the card brings them in together.</summary>
    private FrameworkElement Row(FrameworkElement e, int index, double at) => _playback && index > 0 ? Stagger(e, at) : e;

    // ---------- motion: the eye opens, the content settles ----------

    private void Play()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        // Playback appears at once, fully opaque: a recording never shows what was behind the window.
        if (_playback) Opacity = 1;
        else BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));

        // Sunrise: night brightens into the sky outside, and the light comes up from below.
        Sky.Animate(_sky, Sky.For(Sky.Now), TimeSpan.FromMilliseconds(2200), TimeSpan.FromMilliseconds(250));
        _drift?.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(2400)) { BeginTime = TimeSpan.FromMilliseconds(900) });
        if (_stars is not null)
            _stars.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Sky.HasSun(Sky.Now) ? 0 : 0.7, TimeSpan.FromMilliseconds(1800)) { BeginTime = TimeSpan.FromMilliseconds(600) });
        if (_sun is not null)
        {
            _sun.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(1400)) { BeginTime = TimeSpan.FromMilliseconds(500) });
            _sunRise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(320, 60, TimeSpan.FromMilliseconds(2600)) { BeginTime = TimeSpan.FromMilliseconds(400), EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } });
        }

        _eye.OpenUp(ms: 420, delayMs: 150);

        // Normally each piece follows the one before. In playback each arrives at its own second:
        // header 0.0, title, chart and what needs you 0.4, done rows 2.8 / 3.2 / 3.6, the last line 4.0.
        for (var i = 0; i < _stagger.Count; i++)
        {
            var (e, at) = _stagger[i];
            var begin = _playback ? TimeSpan.FromSeconds(at) : TimeSpan.FromMilliseconds(220 + i * 50);
            var fade = TimeSpan.FromMilliseconds(_playback ? 300 : 260);
            var slide = TimeSpan.FromMilliseconds(_playback ? 300 : 320);
            var from = _playback ? 12 : 10;
            e.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, fade) { BeginTime = begin });
            ((TranslateTransform)e.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(from, 0, slide) { BeginTime = begin, EasingFunction = ease });
        }
    }

    // ---------- words ----------

    private string AwayLine()
    {
        var away = _recap.To - _recap.From;
        var from = _recap.From.Date == _recap.To.Date ? _recap.From.ToString("h:mm tt") : _recap.From.ToString("ddd h:mm tt");
        var lead = _recap.Items.Count == 0 ? "Nothing new from Claude." : "Here's what Claude did.";
        return $"You were away for {Long(away)}, from {from} to {_recap.To:h:mm tt}. {lead}";
    }

    internal static string Long(TimeSpan d)
    {
        if (d < TimeSpan.FromMinutes(1)) return "under a minute";
        var h = (int)d.TotalHours;
        var m = d.Minutes;
        if (h == 0) return $"{m} minute{Text.S(m)}";
        return m == 0 ? $"{h} hour{Text.S(h)}" : $"{h} hour{Text.S(h)} {m} minute{Text.S(m)}";
    }

    private static string Greeting()
    {
        var h = Sky.Now.Hour;
        return h < 5 ? "Still up" : h < 12 ? "Good morning" : h < 17 ? "Good afternoon" : h < 22 ? "Good evening" : "Welcome back";
    }

    /// <summary>Settings name, else your Git name, else your Windows name.</summary>
    internal static string FirstName(App app)
    {
        // Demo data (and screenshots) greets a stand-in, not whoever is running it.
        if (app.Engine.Demo) return "Sam";
        var name = app.Engine.Settings.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            try
            {
                var cfg = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gitconfig");
                if (File.Exists(cfg))
                {
                    var inUser = false;
                    foreach (var raw in File.ReadLines(cfg))
                    {
                        var line = raw.Trim();
                        if (line.StartsWith('[')) inUser = line.StartsWith("[user", StringComparison.OrdinalIgnoreCase);
                        else if (inUser && line.StartsWith("name", StringComparison.OrdinalIgnoreCase) && line.Contains('='))
                        {
                            name = line[(line.IndexOf('=') + 1)..].Trim();
                            break;
                        }
                    }
                }
            }
            catch { }
        }
        if (string.IsNullOrWhiteSpace(name)) name = Environment.UserName;
        var first = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";
        return char.ToUpper(first[0]) + first[1..];
    }

    private string PcLine()
    {
        var parts = new List<string>();
        var awake = AwakeTime();
        if (awake > TimeSpan.FromMinutes(1)) parts.Add($"Vigil kept your PC awake for {Long(awake)}.");
        var warnings = _recap.Events.Count(e => e.Important);
        parts.Add(warnings == 0 ? "No heat or battery issues." : $"{warnings} safety note{Text.S(warnings)}. See everything for details.");
        parts.Add(_app.Engine.Power.Holding && !_example && !_app.Engine.Demo ? "It is still keeping your PC awake." : "Sleep settings are back to normal.");
        return string.Join(" ", parts);
    }

    /// <summary>Adds up the "kept awake" stretches from Vigil's log for this window.</summary>
    private TimeSpan AwakeTime()
    {
        if (_example) return TimeSpan.FromHours(4.85);
        if (_app.Engine.Demo) return DemoData.AwakeFor;
        var total = TimeSpan.Zero;
        DateTime? since = null;
        foreach (var e in History.Since(_recap.From - TimeSpan.FromHours(12)))
        {
            if (e.Kind == ActivityKind.Awake) since ??= e.At < _recap.From ? _recap.From : e.At;
            else if (e.Kind == ActivityKind.Sleep && since is { } s)
            {
                if (e.At > _recap.From) total += e.At - s;
                since = null;
            }
        }
        if (since is { } open) total += _recap.To - open;
        return total;
    }
}
