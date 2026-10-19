using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Vigil.Core;
using static Vigil.UI.Kit;

namespace Vigil.UI;

/// <summary>
/// One lane per session across the hours shown, like the iPhone's Screen Time and battery charts:
/// blue while Claude worked, orange while it waited on you. Shows at a glance what ran at the same time.
/// </summary>
internal static class ActivityChart
{
    private const double LaneHeight = 24;
    private const double BarHeight = 8;

    public static FrameworkElement? Build(IEnumerable<Session> sessions, DateTime from, DateTime to, DateTime now, int maxLanes = 6, double labelWidth = 116)
    {
        if (to <= from) return null;
        var lanes = sessions
            .Select(s => (Session: s, Spans: Activity.Spans(s, from, to, now)))
            .Where(l => l.Spans.Count > 0)
            .Take(maxLanes)
            .ToList();
        if (lanes.Count == 0) return null;

        // Zoom to when something actually ran (with a little room), so short sessions aren't slivers.
        var earliest = lanes.Min(l => l.Spans[0].Start);
        var room = TimeSpan.FromTicks(Math.Max(TimeSpan.FromMinutes(10).Ticks, (to - earliest).Ticks / 12));
        var zoomed = earliest - room;
        if (zoomed > from) from = to - zoomed < TimeSpan.FromHours(1) ? to - TimeSpan.FromHours(1) : zoomed;

        double Frac(DateTime t) => Math.Clamp((t - from) / (to - from), 0, 1);
        var ticks = Activity.Ticks(from, to);

        var root = new StackPanel();

        // Summary, like an Apple Watch ring: how much of this stretch Claude was at work, and how many at once.
        var (busy, most) = Activity.Overlap(lanes.Select(l => (IReadOnlyList<Span>)l.Spans));
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12), LastChildFill = false };
        var summary = new StackPanel { Orientation = Orientation.Horizontal };
        summary.Children.Add(Ring(busy / (to - from), 40));
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        words.Children.Add(T(Text.Duration(busy), "BodyStrong", size: 16));
        words.Children.Add(T(most > 1 ? $"Claude at work · up to {most} at once" : "Claude at work", "Caption", size: 12, color: Palette.Res("Text2")));
        summary.Children.Add(words);
        DockPanel.SetDock(summary, Dock.Left);
        header.Children.Add(summary);

        // Legend: the words that go with the colors.
        var legend = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        legend.Children.Add(Key("Working", "Blue"));
        if (lanes.Any(l => l.Spans.Any(s => s.Kind == SpanKind.Waiting))) { var w = Key("Waiting on you", "Orange"); ((FrameworkElement)w).Margin = new Thickness(0, 5, 0, 0); legend.Children.Add(w); }
        DockPanel.SetDock(legend, Dock.Right);
        header.Children.Add(legend);
        root.Children.Add(header);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        foreach (var _ in lanes) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(LaneHeight) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Faint hour lines behind the bars.
        var lines = new FractionPanel();
        foreach (var t in ticks)
        {
            var line = new Border { Width = 1, Background = Palette.Res("Hairline") };
            FractionPanel.SetAt(line, Frac(t));
            lines.Children.Add(line);
        }
        Grid.SetColumn(lines, 1);
        Grid.SetRowSpan(lines, lanes.Count);
        grid.Children.Add(lines);

        for (var i = 0; i < lanes.Count; i++)
        {
            var (session, spans) = lanes[i];
            var label = new DockPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var (hueFrom, hueTo) = Avatars.HueFor(session.AvatarKey);
            label.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = Sky.Diagonal(hueFrom, hueTo), Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            var name = T(session.Name, "Caption", size: 12.5, color: Palette.Res("Text2"));
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.TextWrapping = TextWrapping.NoWrap;
            name.VerticalAlignment = VerticalAlignment.Center;
            label.Children.Add(name);
            Grid.SetRow(label, i);
            grid.Children.Add(label);

            var track = new FractionPanel { Height = BarHeight, VerticalAlignment = VerticalAlignment.Center };
            var rail = new Border { CornerRadius = new CornerRadius(BarHeight / 2), Background = Palette.Res("Hairline") };
            FractionPanel.SetStart(rail, 0);
            FractionPanel.SetEnd(rail, 1);
            track.Children.Add(rail);

            foreach (var span in spans)
            {
                var live = span.End >= now - TimeSpan.FromSeconds(30);
                var bar = new Border
                {
                    CornerRadius = new CornerRadius(BarHeight / 2),
                    Background = span.Kind == SpanKind.Working ? Sky.Working : Sky.Waiting,
                    ToolTip = $"{(span.Kind == SpanKind.Working ? "Working" : "Waiting on you")}, {span.Start:h:mm tt} to {(live ? "now" : span.End.ToString("h:mm tt"))} ({Text.Duration(span.Length)})",
                };
                FractionPanel.SetStart(bar, Frac(span.Start));
                FractionPanel.SetEnd(bar, Frac(span.End));
                if (live) Breathe(bar);
                track.Children.Add(bar);
            }
            Grid.SetRow(track, i);
            Grid.SetColumn(track, 1);
            grid.Children.Add(track);
        }

        // Hour labels under the lanes.
        var axis = new FractionPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var t in ticks)
        {
            var label = T(Hour(t), "Caption", size: 11.5, color: Palette.Res("Text3"));
            FractionPanel.SetAt(label, Frac(t));
            axis.Children.Add(label);
        }
        Grid.SetRow(axis, lanes.Count);
        Grid.SetColumn(axis, 1);
        grid.Children.Add(axis);

        root.Children.Add(grid);
        return root;
    }

    /// <summary>An activity ring: a gradient arc over a faint track, drawing itself in when shown.</summary>
    internal static FrameworkElement Ring(double fraction, double size)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        const double thick = 6;
        var r = (size - thick) / 2;
        var c = size / 2;
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(new System.Windows.Shapes.Ellipse { Width = size - thick, Height = size - thick, StrokeThickness = thick, Stroke = new SolidColorBrush(Sky.A(Color.FromRgb(0x0A, 0x84, 0xFF), 0x30)) });

        var arc = new System.Windows.Shapes.Path
        {
            StrokeThickness = thick,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stroke = new LinearGradientBrush(Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0x2D, 0xD4, 0xE8), new Point(0, 1), new Point(1, 0)),
        };
        grid.Children.Add(arc);

        // Draw the arc in, from 0 to its value, the first time it appears.
        void Draw(double f)
        {
            f = Math.Min(f, 0.9995);
            if (f <= 0.001) { arc.Data = null; return; }
            var angle = f * 2 * Math.PI;
            var end = new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle));
            arc.Data = new PathGeometry(new[] { new PathFigure(new Point(c, c - r), new PathSegment[] { new ArcSegment(end, new Size(r, r), 0, f > 0.5, SweepDirection.Clockwise, true) }, false) });
        }
        Draw(fraction);
        grid.Loaded += (_, _) =>
        {
            var started = DateTime.Now;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (_, _) =>
            {
                var t = Math.Min(1, (DateTime.Now - started).TotalMilliseconds / 900);
                var eased = 1 - Math.Pow(1 - t, 3);
                Draw(fraction * eased);
                if (t >= 1) timer.Stop();
            };
            timer.Start();
        };
        return grid;
    }

    /// <summary>"9 PM", "12 AM", "2:30 PM".</summary>
    private static string Hour(DateTime t) => t.Minute == 0 ? t.ToString("h tt") : t.ToString("h:mm tt");

    private static UIElement Key(string text, string color)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
        p.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = color == "Blue" ? Sky.Working : Sky.Waiting, VerticalAlignment = VerticalAlignment.Center });
        var label = T(text, "Caption", size: 12, color: Palette.Res("Text2"));
        label.Margin = new Thickness(6, 0, 0, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        p.Children.Add(label);
        return p;
    }

    /// <summary>The bar that is still growing breathes, like the working dot.</summary>
    private static void Breathe(UIElement e) =>
        e.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.55, TimeSpan.FromSeconds(1.1)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
}

/// <summary>
/// Lays children out by fraction of its width: Start..End for bars, or centered on At for labels and lines.
/// </summary>
internal sealed class FractionPanel : Panel
{
    public static readonly DependencyProperty StartProperty = DependencyProperty.RegisterAttached("Start", typeof(double), typeof(FractionPanel), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsParentArrange));
    public static readonly DependencyProperty EndProperty = DependencyProperty.RegisterAttached("End", typeof(double), typeof(FractionPanel), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsParentArrange));
    public static readonly DependencyProperty AtProperty = DependencyProperty.RegisterAttached("At", typeof(double), typeof(FractionPanel), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static void SetStart(UIElement e, double v) => e.SetValue(StartProperty, v);
    public static void SetEnd(UIElement e, double v) => e.SetValue(EndProperty, v);
    public static void SetAt(UIElement e, double v) => e.SetValue(AtProperty, v);

    private const double MinBar = 4;

    protected override Size MeasureOverride(Size available)
    {
        double height = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, available.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(double.IsInfinity(available.Width) ? 0 : available.Width, double.IsInfinity(available.Height) ? height : Math.Min(height, available.Height));
    }

    protected override Size ArrangeOverride(Size final)
    {
        var w = final.Width;
        foreach (UIElement child in InternalChildren)
        {
            var at = (double)child.GetValue(AtProperty);
            if (!double.IsNaN(at))
            {
                var cw = child.DesiredSize.Width;
                var x = Math.Clamp(at * w - cw / 2, 0, Math.Max(0, w - cw));
                child.Arrange(new Rect(x, 0, cw, final.Height));
                continue;
            }
            var start = (double)child.GetValue(StartProperty);
            var end = (double)child.GetValue(EndProperty);
            if (double.IsNaN(start)) start = 0;
            if (double.IsNaN(end)) end = 1;
            var left = start * w;
            var width = Math.Max(MinBar, (end - start) * w);
            if (left + width > w) left = Math.Max(0, w - width);
            child.Arrange(new Rect(left, 0, width, final.Height));
        }
        return final;
    }
}
