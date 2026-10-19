using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Vigil.Core;

namespace Vigil.UI;

/// <summary>
/// The Vigil eye as a live drawing: almond, crescent-moon pupil, and lids that blink. Every EyeView in the
/// app follows one shared <see cref="EyeLife"/>, so they all blink and glance together, and each looks
/// toward the mouse when it comes near.
/// </summary>
public sealed class EyeView : FrameworkElement
{
    internal static readonly Geometry Almond = Freeze(Geometry.Parse("M8,50 C24,27 38,21 50,21 C62,21 76,27 92,50 C76,73 62,79 50,79 C38,79 24,73 8,50Z"));

    public static readonly DependencyProperty InkProperty = DependencyProperty.Register(nameof(Ink), typeof(Brush), typeof(EyeView),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FilledProperty = DependencyProperty.Register(nameof(Filled), typeof(bool), typeof(EyeView),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(double), typeof(EyeView),
        new FrameworkPropertyMetadata(7.5, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BadgeProperty = DependencyProperty.Register(nameof(Badge), typeof(Brush), typeof(EyeView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The color of the eye.</summary>
    public Brush Ink { get => (Brush)GetValue(InkProperty); set => SetValue(InkProperty, value); }

    /// <summary>A solid pupil (awake) or an outlined one (asleep when idle).</summary>
    public bool Filled { get => (bool)GetValue(FilledProperty); set => SetValue(FilledProperty, value); }

    /// <summary>Line width in the 100 x 100 design space. Thicker reads better when small.</summary>
    public double Stroke { get => (double)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    /// <summary>A dot at the top right that means something (amber = needs you, red = too hot).</summary>
    public Brush? Badge { get => (Brush?)GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }

    private EyePose _shown = EyePose.Rest;
    private double _dx, _dy;
    private DateTime _openFrom = DateTime.MinValue;
    private TimeSpan _openFor;

    public EyeView()
    {
        Loaded += (_, _) => EyeMotion.Add(this);
        Unloaded += (_, _) => EyeMotion.Remove(this);
    }

    /// <summary>Open the eye from shut, as a window appears.</summary>
    public void OpenUp(int ms = 320, int delayMs = 60)
    {
        // Held still (screenshots, animation effects off): it's simply open.
        if (!EyeMotion.Running) return;
        _openFrom = DateTime.Now + TimeSpan.FromMilliseconds(delayMs);
        _openFor = TimeSpan.FromMilliseconds(ms);
        EyeMotion.Wake();
        Step(EyeMotion.Pose, null, DateTime.Now);
    }

    /// <summary>True while this eye's own opening is still playing.</summary>
    internal bool Opening(DateTime now) => now < _openFrom + _openFor;

    /// <summary>
    /// Move to the shared pose, or toward the mouse if it's near. Returns true while it's still easing
    /// toward the mouse (the driver then keeps ticking quickly).
    /// </summary>
    internal bool Step(EyePose shared, Point? cursor, DateTime now)
    {
        double tx = shared.Dx, ty = shared.Dy;
        if (cursor is { } c && Looking(c) is { } look) (tx, ty) = look;
        // Ease toward where it's looking: the pupil glides instead of jumping.
        _dx += (tx - _dx) * 0.35;
        _dy += (ty - _dy) * 0.35;
        if (Math.Abs(tx - _dx) < 0.05) _dx = tx;
        if (Math.Abs(ty - _dy) < 0.05) _dy = ty;

        var open = shared.Open;
        if (now < _openFrom) open = 0.06;
        else if (now < _openFrom + _openFor)
        {
            var k = (now - _openFrom).TotalMilliseconds / _openFor.TotalMilliseconds;
            open = Math.Min(open, 0.06 + 0.94 * (1 - Math.Pow(1 - k, 3)));
        }

        var pose = new EyePose(open, _dx, _dy);
        if (Math.Abs(pose.Open - _shown.Open) > 0.005 || Math.Abs(pose.Dx - _shown.Dx) > 0.05 || Math.Abs(pose.Dy - _shown.Dy) > 0.05)
        {
            _shown = pose;
            InvalidateVisual();
        }
        return _dx != tx || _dy != ty;
    }

    /// <summary>Where the pupil goes to look at the mouse, or null if the mouse is too far away to bother.</summary>
    private (double, double)? Looking(Point cursor)
    {
        if (PresentationSource.FromVisual(this) is not { CompositionTarget: { } target } || ActualWidth <= 0) return null;
        Point center;
        try { center = PointToScreen(new Point(ActualWidth / 2, ActualHeight / 2)); }
        catch (InvalidOperationException) { return null; }
        var scale = target.TransformToDevice.M11;
        var vx = cursor.X - center.X;
        var vy = cursor.Y - center.Y;
        var d = Math.Sqrt(vx * vx + vy * vy);
        // Notices the mouse within about 320 px; looks straight ahead when it's right on top.
        if (d > 320 * scale || d < 1) return null;
        var reach = Math.Min(1, d / (90 * scale));
        return (vx / d * EyePose.MaxDx * reach, vy / d * EyePose.MaxDy * 1.6 * reach);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var s = size / 100;
        dc.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        dc.PushTransform(new ScaleTransform(s, s));
        Draw(dc, _shown, Ink, Filled, Stroke, Badge);
        dc.Pop();
        dc.Pop();
    }

    /// <summary>Draws the eye in the 100 x 100 design space. Shared with the tray icon.</summary>
    internal static void Draw(DrawingContext dc, EyePose pose, Brush ink, bool filled, double stroke, Brush? badge)
    {
        // The lids close toward the middle: the shape flattens into a line, but the line keeps its width,
        // so a shut eye still reads as a clear stroke.
        var lid = new ScaleTransform(1, Math.Max(0.02, pose.Open), 50, 50);
        var almond = Almond.CloneCurrentValue();
        almond.Transform = lid;
        dc.DrawGeometry(null, new Pen(ink, stroke) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, almond);
        if (pose.Open > 0.15)
        {
            var pupil = Crescent.CloneCurrentValue();
            pupil.Transform = new TransformGroup { Children = { new TranslateTransform(pose.Dx, pose.Dy), lid } };
            dc.PushClip(almond);
            if (filled) dc.DrawGeometry(ink, null, pupil);
            else dc.DrawGeometry(null, new Pen(ink, stroke * 0.6), pupil);
            dc.Pop();
        }
        if (badge is not null)
            dc.DrawEllipse(badge, new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)), 3), new Point(80, 22), 17, 17);
    }

    internal static readonly Geometry Crescent = Freeze(new CombinedGeometry(GeometryCombineMode.Exclude,
        new EllipseGeometry(new Point(50, 50), 17, 17), new EllipseGeometry(new Point(57, 45), 13, 13)));

    private static Geometry Freeze(Geometry g)
    {
        g.Freeze();
        return g;
    }
}

/// <summary>
/// Drives every eye: one shared life (blinks, glances), each eye's look toward the mouse, and the tray icon.
/// Ticks at 30 fps only while something moves, about 8 times a second otherwise (to notice the mouse), and
/// not at all while the screen is off, the PC is locked, or Windows' animation effects are turned off.
/// </summary>
internal static class EyeMotion
{
    private static readonly List<WeakReference<EyeView>> Views = new();
    private static EyeLife _life = new(new Random(), DateTime.Now);
    private static DispatcherTimer? _timer;
    private static bool _paused;
    private static bool _enabled = true;
    private static Point _lastCursor;
    private static DateTime _cursorMovedAt = DateTime.MinValue;

    /// <summary>The shared pose (the tray icon follows this one).</summary>
    public static EyePose Pose { get; private set; } = EyePose.Rest;

    /// <summary>Raised when the shared pose changes.</summary>
    public static event Action? Moved;

    /// <summary>Off: every eye holds still, open (Settings, Windows' animation effects, screenshots).</summary>
    public static bool Enabled
    {
        get => _enabled;
        set { _enabled = value; Restart(); }
    }

    /// <summary>Nobody's looking (screen off or PC locked): stop ticking.</summary>
    public static bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value) return;
            _paused = value;
            Restart();
        }
    }

    public static void Add(EyeView v)
    {
        Views.RemoveAll(w => !w.TryGetTarget(out var x) || x == v);
        Views.Add(new WeakReference<EyeView>(v));
        Wake();
    }

    public static void Remove(EyeView v) => Views.RemoveAll(w => !w.TryGetTarget(out var x) || x == v);

    /// <summary>Something to show (a window opening, the tray listening): make sure the driver runs.</summary>
    public static void Wake()
    {
        if (!Running) return;
        if (_timer is null)
        {
            if (Application.Current?.Dispatcher is not { } ui) return;
            _timer = new DispatcherTimer(DispatcherPriority.Render, ui) { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += (_, _) => Tick();
        }
        _timer.Interval = TimeSpan.FromMilliseconds(33);
        _timer.Start();
    }

    internal static bool Running => _enabled && !_paused && SystemParameters.ClientAreaAnimation;

    private static void Restart()
    {
        if (Running)
        {
            _life = new EyeLife(new Random(), DateTime.Now);
            Wake();
            return;
        }
        _timer?.Stop();
        SetPose(EyePose.Rest);
        foreach (var v in Live()) v.Step(EyePose.Rest, null, DateTime.Now);
    }

    private static IEnumerable<EyeView> Live()
    {
        Views.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (var w in Views.ToList())
            if (w.TryGetTarget(out var v) && v.IsVisible) yield return v;
    }

    private static void Tick()
    {
        if (!Running)
        {
            Restart();
            return;
        }
        var now = DateTime.Now;
        SetPose(_life.PoseAt(now));

        // The mouse: only while it's moving (and a few seconds after), so a parked cursor isn't stared at forever.
        Point? cursor = null;
        if (GetCursorPos(out var p))
        {
            var here = new Point(p.X, p.Y);
            if (here != _lastCursor) { _lastCursor = here; _cursorMovedAt = now; }
            if (now - _cursorMovedAt < TimeSpan.FromSeconds(3)) cursor = here;
        }

        var easing = false;
        var opening = false;
        foreach (var v in Live())
        {
            easing |= v.Step(Pose, cursor, now);
            opening |= v.Opening(now);
        }

        // Fast only while something actually moves: a move playing, a pupil following the mouse, an eye opening.
        var busy = _life.Animating(now) || easing || opening;
        _timer!.Interval = TimeSpan.FromMilliseconds(busy ? 33 : 120);
    }

    private static void SetPose(EyePose pose)
    {
        if (pose == Pose) return;
        Pose = pose;
        Moved?.Invoke();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT p);
}
