using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace Vigil.UI;

/// <summary>
/// Insomnia's "hold × to end": a small ring fills while you hold. Let go early and nothing happens.
/// </summary>
public sealed class RingHold : StackPanel
{
    public static readonly TimeSpan HoldTime = TimeSpan.FromMilliseconds(600);
    private const double Size = 26, Thickness = 2.4;

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(RingHold), new PropertyMetadata(0.0, (d, _) => ((RingHold)d).UpdateArc()));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    private readonly Path _arc;
    private readonly TextBlock _label;
    private bool _armed;

    public event Action? Confirmed;

    public string Label
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    public RingHold()
    {
        Orientation = Orientation.Horizontal;
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent;
        Focusable = true;

        var ring = new Grid { Width = Size, Height = Size };
        ring.Children.Add(new Ellipse { Stroke = Palette.Res("Control"), StrokeThickness = Thickness });
        _arc = new Path { Stroke = Palette.Res("Red"), StrokeThickness = Thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        ring.Children.Add(_arc);
        ring.Children.Add(new TextBlock
        {
            Text = "\uE711", FontFamily = (FontFamily)Application.Current.Resources["IconFont"], FontSize = 10, Foreground = Palette.Res("Text2"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        Children.Add(ring);
        _label = new TextBlock
        {
            Text = "Hold to end", Margin = new System.Windows.Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            FontFamily = (FontFamily)Application.Current.Resources["BodyFont"], FontSize = 12.5, Foreground = Palette.Res("Text2"),
        };
        Children.Add(_label);

        MouseLeftButtonDown += (_, e) => { Begin(); CaptureMouse(); e.Handled = true; };
        MouseLeftButtonUp += (_, _) => { Cancel(); ReleaseMouseCapture(); };
        LostMouseCapture += (_, _) => Cancel();
        KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter && !e.IsRepeat) Begin(); };
        KeyUp += (_, e) => { if (e.Key is Key.Space or Key.Enter) Cancel(); };
    }

    private void Begin()
    {
        _armed = true;
        var anim = new DoubleAnimation(0, 1, new Duration(HoldTime));
        anim.Completed += (_, _) =>
        {
            if (!_armed) return;
            _armed = false;
            Confirmed?.Invoke();
            BeginAnimation(ProgressProperty, null);
            Progress = 0;
        };
        BeginAnimation(ProgressProperty, anim);
    }

    private void Cancel()
    {
        if (!_armed) return;
        _armed = false;
        BeginAnimation(ProgressProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));
    }

    private void UpdateArc()
    {
        var p = Math.Clamp(Progress, 0, 0.9999);
        if (p <= 0) { _arc.Data = null; return; }
        var r = (Size - Thickness) / 2;
        var c = new Point(Size / 2, Size / 2);
        var start = new Point(c.X, c.Y - r);
        var angle = p * 2 * Math.PI;
        var end = new Point(c.X + r * Math.Sin(angle), c.Y - r * Math.Cos(angle));
        var fig = new PathFigure { StartPoint = start, IsClosed = false };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, p > 0.5, SweepDirection.Clockwise, true));
        _arc.Data = new PathGeometry(new[] { fig });
    }
}
