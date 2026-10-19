using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Vigil.Core;

namespace Vigil.UI;

/// <summary>The tray card. One word for the state, one sentence for why, the sessions, one action.</summary>
public partial class PopoverWindow : Window
{
    private readonly App _app;
    private readonly DispatcherTimer _clock;
    private List<SessionVM> _rows = new();
    private string? _washKey;

    private DateTime _hiddenAt = DateTime.MinValue;

    public PopoverWindow(App app)
    {
        _app = app;
        InitializeComponent();
        WindowStyling.Apply(this, backdrop: Backdrop.Acrylic);
        EndHold.Confirmed += () => { _app.Engine.StopNow(); Refresh(); };
        Deactivated += (_, _) => HidePopover();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) HidePopover(); };
        // The countdown ticks every second while the card is open.
        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
        {
            UpdateCountdown();
            foreach (var vm in _rows) vm.Tick();
        }, Dispatcher);
        IsVisibleChanged += (_, _) => { if (IsVisible) _clock.Start(); else _clock.Stop(); };
    }

    /// <summary>Clicking the tray icon while open closes it (the click deactivates us first).</summary>
    public bool JustHidden => DateTime.Now - _hiddenAt < TimeSpan.FromMilliseconds(300);

    public void ShowNearTray()
    {
        Refresh();
        Opacity = 0;
        // Show it off-screen first: it only knows its height once laid out, and showing it in place
        // would flash it at the old spot for a frame before it moves into the corner.
        Left = -32000;
        Top = -32000;
        Show();
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 12;
        Top = area.Bottom - ActualHeight - 12;
        Activate();
        BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        // The eye opens as the card appears (then blinks and looks around with all the others).
        Eye.OpenUp();
    }

    public void HidePopover()
    {
        if (!IsVisible) return;
        _hiddenAt = DateTime.Now;
        Hide();
        // A hidden window's endless animations (the color sway, the avatars' working rings) would keep
        // WPF drawing 60 times a second for nobody. Let go of them; showing the card builds them again.
        Sessions.ItemsSource = null;
        Wash.Background = null;
        _washKey = null;
    }

    public void Refresh()
    {
        var engine = _app.Engine;
        var status = Status.Of(_app);

        // Awake states get a colored tile with a white eye; asleep and paused stay quiet and gray.
        var active = status.Icon is not (TrayState.Idle or TrayState.Paused);
        var (from, to) = Sky.ForState(status.Icon, status.ColorKey);
        // Awake: a solid white eye on the color. Idle: an outlined eye in the text color; paused, fainter.
        Eye.Ink = active ? Brushes.White : Palette.Res(status.Icon == TrayState.Paused ? "Text3" : "Text2");
        Eye.Filled = active;
        // Only repaint the color when the state changes, so its slow sway isn't restarted on every update.
        var washKey = $"{active}{from}{to}";
        if (washKey != _washKey)
        {
        _washKey = washKey;
        EyeTile.Background = active ? Sky.Diagonal(from, to) : Palette.Res("Card");
        Wash.Background = active
            ? new LinearGradientBrush(new GradientStopCollection { new(Sky.A(from, 0x4A), 0), new(Sky.A(to, 0x14), 0.6), new(Sky.A(to, 0), 1) }, new Point(0, 0), new Point(0.35, 1))
            : null;
        // The color slowly sways, so the card feels alive without asking for attention.
        if (Wash.Background is LinearGradientBrush wash)
            wash.BeginAnimation(LinearGradientBrush.EndPointProperty, new System.Windows.Media.Animation.PointAnimation(new Point(0.35, 1), new Point(1, 0.8), TimeSpan.FromSeconds(7))
                { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever, EasingFunction = new System.Windows.Media.Animation.SineEase() });
        }
        StateTitle.Text = status.Title;
        StateLine.Text = status.Line;
        ConnectButton.Visibility = _app.HooksInstalled ? Visibility.Collapsed : Visibility.Visible;

        var sessions = engine.Tracker.Snapshot()
            .Where(s => s.Background != true)
            .Where(s => s.State is SessionState.Working or SessionState.NeedsYou or SessionState.Paused or SessionState.Failed
                        || (s.State == SessionState.Done && DateTime.Now - s.StateSince < TimeSpan.FromHours(3)))
            .OrderBy(SessionVM.Rank).ThenByDescending(s => s.LastEventAt)
            .Take(4)
            .Select(SessionVM.From)
            .ToList();
        _rows = sessions;
        Sessions.ItemsSource = sessions;
        Sessions.Visibility = sessions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Exactly one action at the bottom.
        var holding = engine.Power.Holding;
        var manual = engine.Decision.Kind == HoldKind.Manual;
        EndHold.Visibility = holding && !engine.Snoozed ? Visibility.Visible : Visibility.Collapsed;
        EndHold.Label = manual ? "Hold to end" : "Hold to let it sleep";
        ResumeLink.Visibility = engine.Snoozed ? Visibility.Visible : Visibility.Collapsed;
        KeepAwakeRow.Visibility = !holding && !engine.Snoozed && _app.HooksInstalled ? Visibility.Visible : Visibility.Collapsed;

        Facts.Text = Status.Facts(engine);
        UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        var engine = _app.Engine;
        if (engine.Decision.Kind == HoldKind.Manual && engine.ManualUntil is { } until && !engine.Snoozed)
        {
            var left = until - DateTime.Now;
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            Countdown.Text = $"{(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00} left";
            Countdown.Visibility = Visibility.Visible;
        }
        else
        {
            Countdown.Visibility = Visibility.Collapsed;
        }
    }

    private void OnHoursKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (Status.ParseDuration(HoursBox.Text) is { } d)
        {
            _app.Engine.KeepAwakeFor(d);
            HoursBox.Text = "";
            Refresh();
        }
        else
        {
            HoursBox.Text = "";
            HoursBox.ToolTip = "Try 2, 30m or 1:30";
        }
    }

    private void OnSessionClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            HidePopover();
            _app.ShowHappened(id);
        }
    }

    private void OnHappened(object sender, RoutedEventArgs e)
    {
        HidePopover();
        _app.ShowHappened(null);
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        HidePopover();
        _app.ShowSettings();
    }

    private void OnConnect(object sender, RoutedEventArgs e)
    {
        try { _app.Connect(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Vigil"); }
        Refresh();
    }

    private void OnResume(object sender, RoutedEventArgs e)
    {
        _app.Engine.Resume();
        Refresh();
    }
}
