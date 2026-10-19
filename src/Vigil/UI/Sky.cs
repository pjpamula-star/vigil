using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Vigil.Core;

namespace Vigil.UI;

/// <summary>
/// Vigil's color comes from the sky outside, like the iPhone Weather app: deep navy at night,
/// coral at dawn, blue in the day, rose and orange at dusk. Coming back plays a sunrise from night
/// into whatever the sky is now. Status has its own small gradients (blue working, amber needs you,
/// green done, red hot), always next to the word.
/// </summary>
internal static class Sky
{
    public readonly record struct Palette3(Color Top, Color Middle, Color Bottom);

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static readonly Palette3 Night = new(C("#070B1C"), C("#0F1A3A"), C("#1A2A55"));
    public static readonly Palette3 Dawn = new(C("#1B2D5E"), C("#B35E6B"), C("#F4A06A"));
    public static readonly Palette3 Day = new(C("#0E4FA8"), C("#2F86D8"), C("#79C3EE"));
    public static readonly Palette3 Dusk = new(C("#1C2350"), C("#B04E62"), C("#F08A4B"));

    /// <summary>The clock the sky follows. VIGIL_SKY_HOUR=7 previews dawn (screenshots, tuning).</summary>
    public static DateTime Now =>
        FixedNow ?? (int.TryParse(Environment.GetEnvironmentVariable("VIGIL_SKY_HOUR"), out var h) && h is >= 0 and < 24
            ? DateTime.Today.AddHours(h).AddMinutes(30) : DateTime.Now);

    /// <summary>A stopped clock for --demo-playback, so every recording shows the same morning.</summary>
    public static DateTime? FixedNow { get; set; }

    public static Palette3 For(DateTime t) => t.Hour switch
    {
        >= 5 and < 8 => Dawn,
        >= 8 and < 17 => Day,
        >= 17 and < 21 => Dusk,
        _ => Night,
    };

    /// <summary>"dawn", "day", "dusk", "night": shared with the phone.</summary>
    public static string Name(DateTime t) => t.Hour switch { >= 5 and < 8 => "dawn", >= 8 and < 17 => "day", >= 17 and < 21 => "dusk", _ => "night" };

    /// <summary>Is there a sun to rise? (At night the sky just deepens into a lighter navy.)</summary>
    public static bool HasSun(DateTime t) => t.Hour is >= 5 and < 21;

    public static Color SunColor(DateTime t) => t.Hour switch
    {
        >= 5 and < 8 => C("#FFC27A"),
        >= 17 and < 21 => C("#FFB067"),
        >= 8 and < 17 => C("#FFF3C4"),
        _ => C("#9FB8FF"),
    };

    /// <summary>A vertical gradient with three stops, ready to animate.</summary>
    public static LinearGradientBrush Brush(Palette3 p) => new(new GradientStopCollection
    {
        new(p.Top, 0), new(p.Middle, 0.62), new(p.Bottom, 1),
    }, new Point(0, 0), new Point(0, 1));

    /// <summary>Sunrise: move each stop from one sky to the other.</summary>
    public static void Animate(LinearGradientBrush brush, Palette3 to, TimeSpan duration, TimeSpan delay)
    {
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        var targets = new[] { to.Top, to.Middle, to.Bottom };
        for (var i = 0; i < 3; i++)
            brush.GradientStops[i].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(targets[i], duration) { BeginTime = delay, EasingFunction = ease });
    }

    /// <summary>
    /// What drifts across the sky once it's light: aurora ribbons at night, warm clouds at dawn and dusk,
    /// soft white clouds by day. Each is (color, alpha, x, y, width, height) on a 540-wide window.
    /// </summary>
    public static (Color Color, byte Alpha, double X, double Y, double W, double H)[] Drift(DateTime t) => Name(t) switch
    {
        "night" => new[] { (C("#2DD4BF"), (byte)0x48, -80.0, 10.0, 420.0, 170.0), (C("#34D399"), (byte)0x30, 220.0, 70.0, 380.0, 140.0), (C("#3B82F6"), (byte)0x38, 60.0, 140.0, 460.0, 200.0) },
        "dawn" => new[] { (C("#FFB4A2"), (byte)0x40, -60.0, 60.0, 380.0, 150.0), (C("#FFD6A5"), (byte)0x34, 240.0, 20.0, 360.0, 130.0), (C("#FF8FA3"), (byte)0x28, 80.0, 180.0, 460.0, 180.0) },
        "dusk" => new[] { (C("#FF9A76"), (byte)0x3C, -40.0, 40.0, 400.0, 160.0), (C("#FFC48C"), (byte)0x30, 250.0, 110.0, 360.0, 140.0), (C("#E8618C"), (byte)0x2C, 60.0, 190.0, 440.0, 180.0) },
        _ => new[] { (Colors.White, (byte)0x2C, -60.0, 30.0, 360.0, 120.0), (Colors.White, (byte)0x22, 260.0, 90.0, 320.0, 110.0), (C("#BFE6FF"), (byte)0x26, 40.0, 170.0, 480.0, 170.0) },
    };

    // ---------- status gradients ----------

    public static (Color From, Color To) ForState(TrayState state, string colorKey) => state switch
    {
        TrayState.NeedsYou => (C("#FFB340"), C("#FF7A2F")),
        TrayState.Hot => colorKey == "Red" ? (C("#FF5A4F"), C("#FF8A3D")) : (C("#FFB340"), C("#FF7A2F")),
        TrayState.Working when colorKey == "Green" => (C("#34D399"), C("#14B8A6")),
        TrayState.Working => (C("#0A84FF"), C("#2DD4E8")),
        TrayState.Manual => (C("#5B8DEF"), C("#2DD4E8")),
        _ => (C("#3A3A44"), C("#26262E")),
    };

    public static LinearGradientBrush Diagonal(Color from, Color to) =>
        new(from, to, new Point(0, 0), new Point(1, 1));

    public static LinearGradientBrush Horizontal(Color from, Color to) =>
        new(from, to, new Point(0, 0.5), new Point(1, 0.5));

    /// <summary>Activity-chart bars: a little light runs along them.</summary>
    public static Brush Working { get; } = Frozen(Horizontal(C("#0A84FF"), C("#2DD4E8")));
    public static Brush Waiting { get; } = Frozen(Horizontal(C("#FF9F0A"), C("#FF6A3D")));

    private static Brush Frozen(Brush b) { b.Freeze(); return b; }

    /// <summary>The same color with a new alpha.</summary>
    public static Color A(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
}
