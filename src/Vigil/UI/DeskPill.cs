using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Vigil.Core;
using Path = System.Windows.Shapes.Path;

namespace Vigil.UI;

/// <summary>
/// A tiny pill that lives in the empty part of the taskbar: the eye, then a breathing amber dot,
/// a turning blue arc and a check mark, each with a number. Hover and the pill itself grows into a
/// panel (like the Dynamic Island) with every session, toward whichever side has room. It never takes
/// focus, never covers a window, stays out of Alt+Tab, and hides for full-screen apps.
/// If the taskbar fills up, it waits just above it; you can drag it anywhere.
/// </summary>
internal sealed class DeskPill : Window
{
    // The window is a fixed transparent canvas; only the capsule inside it is drawn, and clicks on the
    // transparent rest fall through to whatever is underneath.
    private const double WinW = 400, WinH = 480, Pad = 14;
    private const double PillH = 30, PanelW = 340, Radius = 15;

    private static readonly Color Amber = Color.FromRgb(0xFF, 0xB3, 0x40);
    private static readonly Color Blue = Color.FromRgb(0x3B, 0xA9, 0xFF);
    private static readonly Color Green = Color.FromRgb(0x34, 0xD3, 0x99);
    private static readonly Color Fill = Color.FromArgb(0xF5, 0x24, 0x24, 0x28);
    private static readonly Color FillHover = Color.FromArgb(0xFA, 0x30, 0x30, 0x35);
    private static readonly Brush White = Brushes.White;
    private static readonly Brush Dim = Frozen(Color.FromArgb(0x9E, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Faint = Frozen(Color.FromArgb(0x5C, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Hair = Frozen(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));

    private readonly App _app;
    private readonly Border _capsule;
    private readonly SolidColorBrush _fill = new(Fill);
    private readonly Grid _inner = new();
    private readonly Grid _compactHost = new() { Height = PillH };
    private readonly StackPanel _list = new() { Width = PanelW - 8 };
    private readonly Border _listHost;
    private readonly DispatcherTimer _tick, _hover, _news, _upkeep;
    private readonly List<(TextBlock Text, Session Session)> _times = new();
    private readonly Dictionary<string, SessionState> _last = new();

    private string _compactKey = "", _listKey = "";
    private (Session Session, string Text, Color Color)? _headline;
    private DateTime _seenDoneAt = DateTime.Now;
    private bool _expanded, _primed, _hiddenForFullscreen, _dragged, _dragging, _probing, _started;
    private Point? _down;     // where a press began, in screen pixels
    private Vector _grab;     // from the pill's center to the pointer while dragging, in screen units
    private double _compactW = PillH;

    // Where the pill sits: the screen point of its anchored corner, and which way it grows.
    private Point _anchor;
    private bool _up = true, _fromRight = true, _docked = true;
    private Rect? _gap; // the taskbar's empty stretch, in screen units (DIPs)
    private double? _home; // docked: the pill's right edge, chosen once and kept until something would overlap it
    private double? _wantHome; // docked: where you dropped it in the taskbar; it returns there when there's room
    private bool _inGap, _shown;
    private WinEventProc? _onForeground;
    private IntPtr _hook;

    public DeskPill(App app)
    {
        _app = app;
        Title = "Vigil status";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = WinW;
        Height = WinH;
        Left = -32000;
        FontFamily = (FontFamily)app.FindResource("BodyFont");
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        _listHost = new Border { Child = _list, Width = PanelW, Opacity = 0 };
        _inner.Children.Add(_listHost);
        _inner.Children.Add(_compactHost);

        _capsule = new Border
        {
            Background = _fill,
            BorderBrush = Frozen(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Radius),
            Width = PillH, Height = PillH,
            Child = _inner,
            Margin = new Thickness(Pad),
            Cursor = Cursors.Hand,
            Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Direction = 270, Opacity = 0.4, Color = Colors.Black },
        };
        // Children stay inside the rounded corners while the capsule changes shape.
        _capsule.SizeChanged += (_, _) =>
            _inner.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, _capsule.ActualWidth - 2), Math.Max(0, _capsule.ActualHeight - 2)), Radius - 1, Radius - 1);
        Content = new Grid { Children = { _capsule } };

        _capsule.MouseEnter += (_, _) =>
        {
            if (_dragging) return;
            _fill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(FillHover, TimeSpan.FromMilliseconds(150)));
            Intent(expand: true, TimeSpan.FromMilliseconds(170));
        };
        _capsule.MouseLeave += (_, _) =>
        {
            if (_dragging) return;
            _fill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Fill, TimeSpan.FromMilliseconds(220)));
            Intent(expand: false, TimeSpan.FromMilliseconds(260));
        };
        // Press anywhere on the pill (or the open panel) and move to drag it. A press without moving is a click.
        _capsule.PreviewMouseLeftButtonDown += (_, _) => { _down = CursorPx(); _dragged = false; };
        _capsule.PreviewMouseMove += OnDrag;
        _capsule.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!_dragging) return;
            e.Handled = true; // a drag is not a click on whatever row it ended over
            EndDrag();
        };
        _capsule.LostMouseCapture += (_, _) => { if (_dragging) EndDrag(); };
        _capsule.AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler((_, _) => _down = null), handledEventsToo: true);
        _compactHost.MouseLeftButtonUp += (_, _) =>
        {
            if (!_dragged && _down is not null) _app.ShowHappened(null);
            _down = null;
        };
        _capsule.ContextMenu = Menu();

        _hover = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher);
        _hover.Tick += (_, _) =>
        {
            _hover.Stop();
            if (_dragging) return;
            // Don't burst open under a press: that's someone about to drag.
            if (_hover.Tag is true) { if (Mouse.LeftButton == MouseButtonState.Pressed) _hover.Start(); else Expand(); }
            else Collapse();
        };
        _news = new DispatcherTimer(TimeSpan.FromSeconds(6), DispatcherPriority.Normal, (_, _) => { _news!.Stop(); _headline = null; Refresh(); }, Dispatcher);
        _news.Stop();
        _tick = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
        {
            foreach (var (text, s) in _times) text.Text = SessionVM.From(s).Meta;
        }, Dispatcher);
        _tick.Stop();
        // Every 2 seconds: stay above the taskbar, step aside for full-screen apps, and keep up with the taskbar's layout.
        _upkeep = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => Upkeep(), Dispatcher);
        _upkeep.Stop();

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
        Closed += (_, _) => { _tick.Stop(); _upkeep.Stop(); _hover.Stop(); _news.Stop(); };
    }

    /// <summary>Find a spot, then fade and spring in.</summary>
    public void Start()
    {
        _started = true;
        var s = _app.Engine.Settings;
        _docked = s.DeskPillAtX < 0;
        _wantHome = s.DeskPillHomeX >= 0 ? s.DeskPillHomeX : null;
        if (!_docked) SetAnchorFromPoint(new Point(s.DeskPillAtX, s.DeskPillAtY));
        else Place(null);
        _upkeep.Start();
        Upkeep();

        _capsule.Opacity = 0;
        if (!_docked) FadeIn();
        else
        {
            // Docked: wait for the taskbar's layout (or give up after a moment and use the backup spot).
            var wait = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Normal, (t, _) => { ((DispatcherTimer)t!).Stop(); FadeIn(); }, Dispatcher);
            wait.Start();
        }

        // Clicking the taskbar brings it to the front, over the pill. Step back in front the moment that
        // happens, instead of waiting for the next check.
        _onForeground = (_, _, _, _, _, _, _) => { if (!_expanded) Raise(); };
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _onForeground, 0, 0, 0);
        Closed += (_, _) => { if (_hook != IntPtr.Zero) UnhookWinEvent(_hook); };
    }

    private void FadeIn()
    {
        if (_shown) return;
        _shown = true;
        _capsule.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)) { EasingFunction = Smooth() });
    }

    private void Raise()
    {
        if (!IsLoaded) return;
        SetWindowPos(new WindowInteropHelper(this).Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    // ---------- placement ----------

    /// <summary>
    /// Docked: one spot in the taskbar's empty stretch, kept for good. The pill only moves if a new app
    /// button or tray icon would actually run into it, and then it glides to the middle of the new space.
    /// If there's no room, it sits just above the taskbar on the right, also fixed.
    /// </summary>
    private void Place(Rect? gap, bool glide = false)
    {
        var work = SystemParameters.WorkArea;
        const double edge = 6;
        // Leaving the taskbar takes a real squeeze; coming back takes clear room. No flip-flopping
        // when a tray icon blinks in and out.
        var need = _compactW + 2 * edge + (_inGap ? 0 : 36);
        if (gap is { } g && g.Width >= need)
        {
            // The range the pill's right edge can sit in. Start in the middle; after that, only move
            // as little as needed to stay clear.
            var lo = g.Left + edge + _compactW;
            var hi = g.Right - edge;
            _home = Math.Round((_wantHome ?? _home) is { } h ? Math.Clamp(h, lo, hi) : Math.Clamp(g.Left + (g.Width + _compactW) / 2, lo, hi));
            _anchor = new Point(_home.Value, Math.Round(g.Top + (g.Height + PillH) / 2));
            _inGap = true;
        }
        else
        {
            _inGap = false;
            _home = null;
            _anchor = new Point(work.Right - 16, work.Bottom - 12);
        }
        _up = true;
        _fromRight = true;
        Arrange(glide);
    }

    /// <summary>A free spot (after you drag it): grow toward whichever side has more room.</summary>
    private void SetAnchorFromPoint(Point pillCenter)
    {
        var screen = Screen();
        _up = pillCenter.Y > screen.Top + screen.Height / 2;
        _fromRight = pillCenter.X > screen.Left + screen.Width / 2;
        _anchor = new Point(pillCenter.X + (_fromRight ? _compactW / 2 : -_compactW / 2), pillCenter.Y + (_up ? PillH / 2 : -PillH / 2));
        Arrange();
    }

    private static Rect Screen() => new(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

    /// <summary>Line the window up so the capsule's anchored corner lands on the anchor.</summary>
    private void Arrange(bool glide = false)
    {
        _capsule.HorizontalAlignment = _fromRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _capsule.VerticalAlignment = _up ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        _compactHost.VerticalAlignment = _up ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        _compactHost.HorizontalAlignment = _fromRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        foreach (FrameworkElement c in _compactHost.Children) c.HorizontalAlignment = _compactHost.HorizontalAlignment;
        _listHost.VerticalAlignment = _up ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        _listHost.HorizontalAlignment = _fromRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _listHost.Margin = _up ? new Thickness(0, 0, 0, PillH) : new Thickness(0, PillH, 0, 0);

        // The panel opens toward the room, so it is never cut off by the edge of the screen.
        var screen = Screen();
        var left = _fromRight ? _anchor.X + Pad - WinW : _anchor.X - Pad;
        var top = _up ? _anchor.Y + Pad - WinH : _anchor.Y - Pad;
        left = Math.Clamp(left, screen.Left - Pad, screen.Right - WinW + Pad);
        top = Math.Clamp(top, screen.Top - Pad, screen.Bottom - WinH + Pad);
        if (Math.Abs(left - Left) < 0.5 && Math.Abs(top - Top) < 0.5) return; // already there: don't touch it
        StopGlide();
        if (glide && _started && !_dragging && Math.Abs(top - Top) < 0.5)
        {
            var glideTo = new DoubleAnimation(left, TimeSpan.FromMilliseconds(450)) { EasingFunction = Smooth() };
            // Hand the final spot back to the window itself once it gets there, so nothing keeps holding
            // the old animated value (that held value is what used to snap the pill back after a drag).
            glideTo.Completed += (_, _) => { if (Math.Abs(Left - left) < 0.5) { Left = left; BeginAnimation(LeftProperty, null); } };
            BeginAnimation(LeftProperty, glideTo);
        }
        else Left = left;
        Top = top;
    }

    /// <summary>Stop a glide where it is, without a jump.</summary>
    private void StopGlide()
    {
        var at = Left;
        Left = at;
        BeginAnimation(LeftProperty, null);
    }

    /// <summary>The middle of the small pill, in screen units, from where its corner is anchored.</summary>
    private Point PillCenter() => new(
        _anchor.X + (_fromRight ? -_compactW / 2 : _compactW / 2),
        _anchor.Y + (_up ? -PillH / 2 : PillH / 2));

    private Point CursorPx() => GetCursorPos(out var p) ? new Point(p.X, p.Y) : default;

    private Point CursorDip()
    {
        var px = CursorPx();
        return PresentationSource.FromVisual(this)?.CompositionTarget is { } t ? t.TransformFromDevice.Transform(px) : px;
    }

    private int _upkeeps;

    private void Upkeep()
    {
        if (!IsLoaded) return;
        // A backstop for the foreground hook: stay in front of the taskbar.
        if (!_expanded) Raise();

        if (SHQueryUserNotificationState(out var state) == 0)
        {
            var busy = state is 2 or 3 or 4; // full-screen app, full-screen game, presentation
            if (busy && IsVisible) { _hiddenForFullscreen = true; Hide(); }
            else if (!busy && _hiddenForFullscreen) { _hiddenForFullscreen = false; Show(); }
        }

        // The taskbar's layout, every other time (4 s): reading it through UI Automation costs Vigil and
        // Explorer real CPU, and the taskbar rarely changes faster than that.
        if (_docked && !_expanded && !_dragging && !_probing && ++_upkeeps % 2 == 0) _ = ProbeAsync();
    }

    private async Task ProbeAsync()
    {
        _probing = true;
        try
        {
            var px = await Task.Run(() => { try { return TaskbarProbe.FindGap(); } catch { return null; } });
            Rect? dip = null;
            if (px is { } p && PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
            {
                var m = target.TransformFromDevice;
                dip = new Rect(m.Transform(p.TopLeft), m.Transform(p.BottomRight));
            }
            if (_docked && !_expanded && !_dragging && dip != _gap)
            {
                _gap = dip;
                Place(dip, glide: _shown);
            }
            FadeIn();
        }
        finally { _probing = false; }
    }

    // ---------- dragging ----------
    // Our own drag instead of DragMove: the pill shrinks back to its small shape the moment you start,
    // stays under your pointer the whole way, nothing else moves it meanwhile, and it stays exactly
    // where you let go. Drop it on the taskbar and it docks at that spot.

    private void OnDrag(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (_dragging) { DragTo(CursorDip()); return; }
        if (_down is not { } down) return;
        var now = CursorPx();
        if (Math.Abs(now.X - down.X) < 5 && Math.Abs(now.Y - down.Y) < 5) return;
        BeginDrag();
    }

    private void BeginDrag()
    {
        _dragging = _dragged = true;
        _hover.Stop();
        StopGlide();
        if (_expanded) CollapseNow();

        // Keep the spot you grabbed under the pointer; if you grabbed the open panel, the small pill
        // comes to the pointer instead.
        var cursor = CursorDip();
        var off = cursor - PillCenter();
        _grab = new Vector(Math.Clamp(off.X, -_compactW / 2, _compactW / 2), Math.Clamp(off.Y, -PillH / 2, PillH / 2));
        _capsule.CaptureMouse();
        DragTo(cursor);
    }

    private void DragTo(Point cursor) => SetAnchorFromPoint(cursor - _grab);

    private void EndDrag()
    {
        _dragging = false;
        _down = null;
        if (Mouse.Captured == _capsule) _capsule.ReleaseMouseCapture();

        var center = PillCenter();
        var s = _app.Engine.Settings;
        var work = SystemParameters.WorkArea;
        var onTaskbar = center.Y > work.Bottom && center.X >= work.Left && center.X <= work.Right;
        if (onTaskbar)
        {
            // Dock right here. The next look at the taskbar nudges it clear of buttons if it has to.
            var wasDocked = _docked;
            _docked = true;
            _wantHome = _home = Math.Round(center.X + _compactW / 2);
            s.DeskPillAtX = s.DeskPillAtY = -1;
            s.DeskPillHomeX = _wantHome.Value;
            if (wasDocked && _gap is not null) Place(_gap, glide: true);
            else
            {
                _gap = null;
                _up = _fromRight = true;
                _anchor = new Point(_home.Value, Math.Round(center.Y + PillH / 2));
                Arrange();
            }
            _ = ProbeAsync();
        }
        else
        {
            _docked = false;
            _gap = null;
            SetAnchorFromPoint(center);
            s.DeskPillAtX = Math.Round(center.X);
            s.DeskPillAtY = Math.Round(center.Y);
        }
        s.Save();
        _fill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(_capsule.IsMouseOver ? FillHover : Fill, TimeSpan.FromMilliseconds(150)));
    }

    /// <summary>Back to the small pill at once, no animation (for the start of a drag).</summary>
    private void CollapseNow()
    {
        Collapse();
        _capsule.BeginAnimation(WidthProperty, null);
        _capsule.BeginAnimation(HeightProperty, null);
        _capsule.Width = _compactW;
        _capsule.Height = PillH;
        _listHost.BeginAnimation(OpacityProperty, null);
        _listHost.Opacity = 0;
    }

    private ContextMenu Menu()
    {
        var menu = new ContextMenu();
        void Item(string text, Action a)
        {
            var i = new MenuItem { Header = text };
            i.Click += (_, _) => a();
            menu.Items.Add(i);
        }
        Item("Open What happened", () => _app.ShowHappened(null));
        Item("Put it back in the taskbar", () =>
        {
            var s = _app.Engine.Settings;
            s.DeskPillAtX = s.DeskPillAtY = -1;
            s.DeskPillHomeX = -1;
            s.Save();
            _docked = true;
            _gap = null;
            _home = _wantHome = null;
            Place(null);
            _ = ProbeAsync();
        });
        menu.Items.Add(new Separator());
        Item("Hide the pill", () => _app.SetDeskPill(false));
        return menu;
    }

    // ---------- content ----------

    public void Refresh()
    {
        var glance = Glance.Of(_app.Engine.Tracker.Snapshot(), DateTime.Now);
        NoticeNews(glance);

        var fresh = glance.Done.Any(s => s.LastEventAt > _seenDoneAt);
        var key = $"{glance.NeedsYou.Count}|{glance.Working.Count}|{glance.Done.Count}|{fresh}|{_headline?.Session.Id}{_headline?.Text}";
        if (key != _compactKey)
        {
            _compactKey = key;
            SwapCompact(_headline is { } h ? Headline(h.Session, h.Text, h.Color) : Symbols(glance, fresh));
        }
        if (_expanded) BuildList(glance, animate: false);
    }

    /// <summary>Cross-fade the pill's content and let its width follow on a spring.</summary>
    private void SwapCompact(FrameworkElement next)
    {
        next.Measure(new Size(double.PositiveInfinity, PillH));
        _compactW = Math.Max(PillH, Math.Ceiling(next.DesiredSize.Width) + 2);
        var old = _compactHost.Children.OfType<FrameworkElement>().ToList();
        foreach (var o in old)
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110));
            fade.Completed += (_, _) => _compactHost.Children.Remove(o);
            o.BeginAnimation(OpacityProperty, fade);
        }
        // Pinned to the anchored side, so the symbols stay under your pointer while the panel opens around them.
        next.HorizontalAlignment = _fromRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _compactHost.Children.Add(next);
        if (old.Count > 0 && _started)
            next.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { BeginTime = TimeSpan.FromMilliseconds(70) });
        _compactHost.Width = _expanded ? PanelW : _compactW;
        if (!_expanded) Morph(_compactW, PillH, TimeSpan.FromMilliseconds(320));
        if (_docked && _started && !_expanded && !_dragging && _gap is not null) Place(_gap, glide: true);
    }

    /// <summary>Something just finished or started waiting on you: the pill says which, for a few seconds.</summary>
    private void NoticeNews(Glance g)
    {
        var first = !_primed;
        _primed = true;
        foreach (var s in g.All)
        {
            var had = _last.TryGetValue(s.Id, out var before);
            _last[s.Id] = s.State;
            if (first || (had && before == s.State)) continue;
            if (s.State == SessionState.NeedsYou) Announce(s, "needs you", Amber);
            else if (Glance.IsDone(s) && (!had || before == SessionState.Working)) Announce(s, "done", Green);
        }
    }

    private void Announce(Session s, string text, Color color)
    {
        _headline = (s, text, color);
        _news.Stop();
        _news.Start();
    }

    /// <summary>For screenshots: the "done" moment.</summary>
    internal void ShowNews(Session s)
    {
        Announce(s, "done", Green);
        Refresh();
    }

    /// <summary>The whole pill: the eye, then only the symbols that have something to count.</summary>
    private static FrameworkElement Symbols(Glance g, bool fresh)
    {
        var row = Row();
        row.Children.Add(Eye(g.Empty ? Faint : White));
        if (g.NeedsYou.Count > 0) row.Children.Add(Count(Pulse(), g.NeedsYou.Count, Frozen(Amber)));
        if (g.Working.Count > 0) row.Children.Add(Count(Spinner(11), g.Working.Count, White));
        if (g.Done.Count > 0) row.Children.Add(Count(Check(fresh), g.Done.Count, fresh ? Frozen(Green) : Dim));
        return row;
    }

    private static FrameworkElement Headline(Session s, string text, Color color)
    {
        var row = Row();
        var avatar = Avatars.Make(s.AvatarKey, s.State, 16);
        avatar.Margin = new Thickness(-4, -4, 2, -4);
        row.Children.Add(avatar);
        row.Children.Add(Label(s.Name, White, weight: FontWeights.SemiBold, size: 12.5, maxWidth: 150));
        row.Children.Add(Label(" " + text, Frozen(color), weight: FontWeights.SemiBold, size: 12.5));
        return row;
    }

    private void BuildList(Glance g, bool animate)
    {
        var shown = g.All.Take(7).ToList();
        var key = string.Join(";", shown.Select(s => $"{s.Id}:{s.State}:{s.Name}")) + $"|{g.All.Count()}|{_up}";
        if (key == _listKey && !animate) return;
        _listKey = key;
        _list.Children.Clear();
        _times.Clear();

        var words = new List<string>();
        if (g.NeedsYou.Count > 0) words.Add($"{g.NeedsYou.Count} need{(g.NeedsYou.Count == 1 ? "s" : "")} you");
        if (g.Working.Count > 0) words.Add($"{g.Working.Count} working");
        if (g.Done.Count > 0) words.Add($"{g.Done.Count} done");
        var line = new Border { Height = 1, Background = Hair, Margin = new Thickness(12, _up ? 6 : 0, 12, _up ? 0 : 4) };

        if (!_up) _list.Children.Add(line);
        _list.Children.Add(Label(words.Count > 0 ? string.Join(" · ", words) : "Nothing running", Dim, size: 11.5, margin: new Thickness(14, 10, 14, 4)));
        if (shown.Count == 0)
            _list.Children.Add(Label("Claude sessions show up here while they run.", Faint, size: 12, margin: new Thickness(14, 2, 14, 10)));
        foreach (var s in shown) _list.Children.Add(SessionRow(s));
        var more = g.All.Count() - shown.Count;
        if (more > 0) _list.Children.Add(Label($"and {more} more", Faint, size: 11.5, margin: new Thickness(14, 2, 14, 6)));
        // The hairline always sits against the pill row.
        if (_up) _list.Children.Add(line);

        if (!animate) return;
        var i = 0;
        var order = _list.Children.OfType<FrameworkElement>().ToList();
        if (_up) order.Reverse(); // rows nearest the pill appear first
        foreach (var r in order)
        {
            var delay = TimeSpan.FromMilliseconds(80 + 26 * i++);
            var move = new TranslateTransform(0, _up ? 10 : -10);
            r.RenderTransform = move;
            r.Opacity = 0;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            r.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = delay, EasingFunction = ease });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(380)) { BeginTime = delay, EasingFunction = ease });
        }
    }

    private UIElement SessionRow(Session s)
    {
        var grid = new Grid { Margin = new Thickness(8, 2, 12, 2), MinHeight = 38 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var avatar = Avatars.Make(s.AvatarKey, s.State, 22);
        avatar.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(avatar);

        var vm = SessionVM.From(s);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
        text.Children.Add(Label(s.Name, White, weight: FontWeights.SemiBold, size: 13, maxWidth: 200));
        var line = s.State == SessionState.Working ? s.NowLine : vm.Line;
        if (!string.IsNullOrWhiteSpace(line)) text.Children.Add(Label(line!, Dim, size: 12, maxWidth: 200));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var (word, color) = s.State switch
        {
            SessionState.Working => ("Working", Blue),
            SessionState.NeedsYou => ("Needs you", Amber),
            SessionState.Quiet => ("Quiet", Amber),
            SessionState.Paused => ("Usage limit", Amber),
            SessionState.Failed => ("Stopped", Color.FromRgb(0xFF, 0x6B, 0x5E)),
            _ => ("Done", Green),
        };
        var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        right.Children.Add(Label(word, Frozen(color), weight: FontWeights.SemiBold, size: 11.5, align: TextAlignment.Right));
        var time = Label(vm.Meta, Faint, size: 11.5, align: TextAlignment.Right);
        System.Windows.Documents.Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
        _times.Add((time, s));
        right.Children.Add(time);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        var hl = new SolidColorBrush(Colors.Transparent);
        var row = new Border { CornerRadius = new CornerRadius(10), Child = grid, Background = hl, Margin = new Thickness(4, 0, 4, 0) };
        row.MouseEnter += (_, _) => hl.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF), TimeSpan.FromMilliseconds(120)));
        row.MouseLeave += (_, _) => hl.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Colors.Transparent, TimeSpan.FromMilliseconds(200)));
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _app.ShowHappened(s.Id);
        };
        return row;
    }

    // ---------- open and close ----------

    private void Intent(bool expand, TimeSpan after)
    {
        _hover.Stop();
        _hover.Tag = expand;
        _hover.Interval = after;
        _hover.Start();
    }

    internal void Expand()
    {
        if (_expanded) return;
        _expanded = true;
        _seenDoneAt = DateTime.Now;
        if (IsLoaded) SetWindowPos(new WindowInteropHelper(this).Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        Refresh();
        BuildList(Glance.Of(_app.Engine.Tracker.Snapshot(), DateTime.Now), animate: true);
        _tick.Start();

        _compactHost.Width = PanelW;
        _listHost.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        Morph(PanelW, OpenHeight(), TimeSpan.FromMilliseconds(420));
    }

    internal void Collapse()
    {
        if (!_expanded) return;
        _expanded = false;
        _tick.Stop();
        _times.Clear();
        _listHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
        Refresh();
        _compactHost.Width = _compactW;
        Morph(_compactW, PillH, TimeSpan.FromMilliseconds(320), new CubicEase { EasingMode = EasingMode.EaseInOut });
    }

    private double OpenHeight()
    {
        _list.Measure(new Size(PanelW - 8, double.PositiveInfinity));
        return Math.Min(WinH - 2 * Pad, PillH + Math.Ceiling(_list.DesiredSize.Height));
    }

    /// <summary>Change the capsule's shape smoothly, picking up from wherever it is mid-animation.</summary>
    private void Morph(double w, double h, TimeSpan duration, IEasingFunction? ease = null)
    {
        if (!_started)
        {
            _capsule.Width = w;
            _capsule.Height = h;
            return;
        }
        ease ??= Smooth();
        _capsule.BeginAnimation(WidthProperty, new DoubleAnimation(w, duration) { EasingFunction = ease });
        _capsule.BeginAnimation(HeightProperty, new DoubleAnimation(h, duration) { EasingFunction = ease });
    }

    /// <summary>For screenshots: the docked layout (growing up and to the left), without moving the window.</summary>
    internal void LayoutLikeDocked()
    {
        _up = _fromRight = true;
        var (left, top) = (Left, Top);
        Arrange();
        (Left, Top) = (left, top);
    }

    /// <summary>For screenshots, where nothing animates: snap to the open shape.</summary>
    internal void SnapOpen()
    {
        Expand();
        _capsule.BeginAnimation(WidthProperty, null);
        _capsule.BeginAnimation(HeightProperty, null);
        _capsule.Width = PanelW;
        _capsule.Height = OpenHeight();
        _listHost.BeginAnimation(OpacityProperty, null);
        _listHost.Opacity = 1;
        foreach (FrameworkElement r in _list.Children) { r.BeginAnimation(OpacityProperty, null); r.Opacity = 1; r.RenderTransform = null; }
    }

    // ---------- pieces ----------

    private static StackPanel Row() => new()
    {
        Orientation = Orientation.Horizontal,
        Height = PillH,
        Margin = new Thickness(10, 0, 11, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static UIElement Count(UIElement icon, int n, Brush color)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        p.Children.Add(icon);
        var num = Label(n.ToString(), color, weight: FontWeights.SemiBold, size: 13, margin: new Thickness(5, 0, 0, 1));
        System.Windows.Documents.Typography.SetNumeralAlignment(num, FontNumeralAlignment.Tabular);
        p.Children.Add(num);
        return p;
    }

    private static TextBlock Label(string text, Brush color, FontWeight? weight = null, double size = 13, Thickness? margin = null, double maxWidth = double.PositiveInfinity, TextAlignment align = TextAlignment.Left) => new()
    {
        Text = text,
        Foreground = color,
        FontSize = size,
        FontWeight = weight ?? FontWeights.Normal,
        Margin = margin ?? new Thickness(0),
        MaxWidth = maxWidth,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextAlignment = align,
        // With a max width, a stretched text block would center itself; keep the column's edge.
        HorizontalAlignment = align == TextAlignment.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    /// <summary>The Vigil eye, small. It blinks and glances with the others, and watches the mouse go by.</summary>
    private static UIElement Eye(Brush color) =>
        new EyeView { Width = 17, Height = 17, Ink = color, Stroke = 9.5, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>
    /// An endless animation at 8 frames a second. The pill is a see-through window, which Windows redraws
    /// in software every frame: at the usual 60, the dot and spinner alone took about half a CPU core.
    /// </summary>
    private static T Calm<T>(T timeline) where T : Timeline
    {
        Timeline.SetDesiredFrameRate(timeline, 8);
        return timeline;
    }

    /// <summary>An amber dot that breathes: waiting on you.</summary>
    private static UIElement Pulse()
    {
        var e = new Ellipse { Width = 8, Height = 8, Fill = Frozen(Amber), VerticalAlignment = VerticalAlignment.Center };
        e.BeginAnimation(OpacityProperty, Calm(new DoubleAnimation(1, 0.3, TimeSpan.FromSeconds(1.1)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() }));
        return e;
    }

    /// <summary>A small turning arc: still working.</summary>
    private static UIElement Spinner(double d)
    {
        var r = d / 2 - 1;
        var c = d / 2;
        var track = new Ellipse { Width = d, Height = d, Stroke = Frozen(Color.FromArgb(0x40, Blue.R, Blue.G, Blue.B)), StrokeThickness = 2 };
        var arc = new Path
        {
            Data = new PathGeometry(new[]
            {
                new PathFigure(new Point(c, c - r), new PathSegment[] { new ArcSegment(new Point(c + r, c), new Size(r, r), 0, false, SweepDirection.Clockwise, true) }, false),
            }),
            Stroke = Frozen(Blue), StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        };
        var spin = new RotateTransform(0, c, c);
        arc.RenderTransform = spin;
        // Ticks round in 8 steps, like a classic activity indicator: only 8 redraws a turn.
        var steps = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1.2), RepeatBehavior = RepeatBehavior.Forever };
        for (var i = 0; i < 8; i++) steps.KeyFrames.Add(new DiscreteDoubleKeyFrame(i * 45, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.2 * i / 8))));
        spin.BeginAnimation(RotateTransform.AngleProperty, Calm(steps));
        return new Grid { Width = d, Height = d, VerticalAlignment = VerticalAlignment.Center, Children = { track, arc } };
    }

    /// <summary>A check mark: bright green when something finished since you last looked.</summary>
    private static UIElement Check(bool fresh)
    {
        var bg = new Ellipse { Width = 13, Height = 13, Fill = fresh ? Frozen(Green) : Frozen(Color.FromArgb(0x55, Green.R, Green.G, Green.B)) };
        var tick = new Path
        {
            Data = Geometry.Parse("M3.6,6.7 L5.7,8.7 L9.6,4.4"),
            Stroke = fresh ? Frozen(Color.FromRgb(0x05, 0x2E, 0x20)) : White,
            StrokeThickness = 1.7, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        };
        return new Grid { Width = 13, Height = 13, VerticalAlignment = VerticalAlignment.Center, Children = { bg, tick } };
    }

    /// <summary>
    /// Fast at first, then a long, gentle stop, and never past the target: steady, with no wobble.
    /// </summary>
    private static IEasingFunction Smooth()
    {
        var e = new ExponentialEase { Exponent = 6, EasingMode = EasingMode.EaseOut };
        e.Freeze();
        return e;
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);

    private const uint EVENT_SYSTEM_FOREGROUND = 3;
    private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
}
