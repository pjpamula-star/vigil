using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Vigil.Core;
using Path = System.Windows.Shapes.Path;

namespace Vigil.UI;

/// <summary>
/// Every project gets its own color, the way Apple Calendar colors calendars: a round tile with its
/// initials, so concurrent sessions are easy to tell apart at a glance. The ring around it shows the
/// state: a spinning blue arc while Claude works, a pulsing amber ring while it waits on you.
/// The phone uses the same hash and palette, so a project is the same color everywhere.
/// </summary>
internal static class Avatars
{
    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    // Apple's system hues, minus orange, red and green, which mean "needs you", "problem" and "done".
    private static readonly (Color From, Color To)[] Hues =
    {
        (C("#64D2FF"), C("#0A84FF")), // sky
        (C("#66D4CF"), C("#1FA2B5")), // teal
        (C("#FF6482"), C("#FF375F")), // pink
        (C("#FFE15C"), C("#F5B800")), // sunflower
        (C("#7D7AFF"), C("#4F6BFF")), // indigo
        (C("#7FE0C2"), C("#20B39A")), // mint
        (C("#D4B48C"), C("#A2845E")), // sand
        (C("#DA8FFF"), C("#AF52DE")), // purple
    };

    private static Dictionary<string, int> _assigned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Give the projects you're looking at different colors. Each starts at its own hashed color and
    /// moves to the next free one if another project already has it. The order doesn't depend on the
    /// list, so every window agrees, and the phone gets the same numbers in its update.
    /// </summary>
    public static void Assign(IEnumerable<string> folders)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<int>();
        foreach (var f in folders.Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(HashIndex).ThenBy(f => f.ToLowerInvariant(), StringComparer.Ordinal))
        {
            var i = HashIndex(f);
            for (var k = 0; k < Hues.Length && used.Contains(i); k++) i = (i + 1) % Hues.Length;
            used.Add(i);
            map[f] = i;
        }
        _assigned = map;
    }

    /// <summary>The project's color: its assigned one, or its hashed one if it isn't on screen.</summary>
    public static int HueIndex(string name) => _assigned.TryGetValue(name, out var i) ? i : HashIndex(name);

    /// <summary>FNV-1a over UTF-16 code units; the phone computes the same number.</summary>
    public static int HashIndex(string name)
    {
        uint h = 2166136261;
        foreach (var ch in name.ToLowerInvariant())
        {
            h ^= ch;
            h *= 16777619;
        }
        return (int)(h % (uint)Hues.Length);
    }

    public static (Color From, Color To) HueFor(string name) => Hues[HueIndex(name)];

    /// <summary>"replay-tool" → "RT", "card-game" → "CG", "Bakery website redesign" → "BW".</summary>
    public static string Initials(string name)
    {
        var parts = name.Split(new[] { '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => char.IsLetterOrDigit(p[0])).ToArray();
        if (parts.Length == 0) return "?";
        var first = char.ToUpperInvariant(parts[0][0]).ToString();
        return parts.Length > 1 ? first + char.ToUpperInvariant(parts[1][0]) : first;
    }

    public static FrameworkElement Make(string name, SessionState state, double size = 30)
    {
        var ring = size + 8;
        var grid = new Grid { Width = ring, Height = ring };
        var (from, to) = HueFor(name);
        var dim = state is SessionState.Ended or SessionState.Quiet or SessionState.Ready;

        grid.Children.Add(new Ellipse
        {
            Width = size, Height = size,
            Fill = Sky.Diagonal(from, to),
            Opacity = dim ? 0.55 : 1,
        });
        var initials = Initials(name);
        grid.Children.Add(new TextBlock
        {
            Text = initials,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = size * (initials.Length > 1 ? 0.36 : 0.44),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = dim ? 0.8 : 1,
        });

        if (state == SessionState.Working) grid.Children.Add(Spinner(ring));
        else if (state == SessionState.NeedsYou) grid.Children.Add(Pulse(ring));
        return grid;
    }

    /// <summary>A third of a ring, fading along its length, turning steadily.</summary>
    private static UIElement Spinner(double d)
    {
        var r = d / 2 - 1.5;
        var c = d / 2;
        var geo = new PathGeometry(new[]
        {
            new PathFigure(new Point(c, c - r), new PathSegment[] { new ArcSegment(new Point(c + r * Math.Sin(2.3), c - r * Math.Cos(2.3)), new Size(r, r), 0, false, SweepDirection.Clockwise, true) }, false),
        });
        var spin = new RotateTransform(0, c, c);
        var path = new Path
        {
            Data = geo,
            StrokeThickness = 2.4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stroke = new LinearGradientBrush(Sky.A(C("#2DD4E8"), 0x10), C("#0A84FF"), new Point(1, 0), new Point(0, 1)),
            RenderTransform = spin,
            IsHitTestVisible = false,
        };
        spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever });
        return path;
    }

    /// <summary>A full amber ring that breathes: it's waiting for you.</summary>
    private static UIElement Pulse(double d)
    {
        var e = new Ellipse
        {
            Width = d - 1, Height = d - 1,
            StrokeThickness = 2.2,
            Stroke = new LinearGradientBrush(C("#FFB340"), C("#FF7A2F"), 45),
            IsHitTestVisible = false,
        };
        e.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromSeconds(1.1)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        return e;
    }
}
