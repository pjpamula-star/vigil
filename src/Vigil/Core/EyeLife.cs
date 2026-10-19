namespace Vigil.Core;

/// <summary>
/// Where the eye is looking and how open it is. <see cref="Open"/> runs from 0 (shut) to 1 (open);
/// <see cref="Dx"/> and <see cref="Dy"/> move the pupil, in the eye's 100 x 100 design space.
/// </summary>
public readonly record struct EyePose(double Open, double Dx, double Dy)
{
    public static readonly EyePose Rest = new(1, 0, 0);

    /// <summary>How far the pupil can move and still sit inside the almond.</summary>
    public const double MaxDx = 11, MaxDy = 5;

    /// <summary>Snapped to a coarse grid, so a tray icon can be drawn once per pose and reused.</summary>
    public EyePose Quantized() => new(Math.Round(Open * 10) / 10, Math.Round(Dx), Math.Round(Dy));
}

/// <summary>
/// The eye's little life: every few seconds it blinks, sometimes twice, glances somewhere and back,
/// or looks left and right. One brain for every eye in the app, so all of them move together.
/// Pure: give it a random source and a time, ask for the pose.
/// </summary>
public sealed class EyeLife
{
    private readonly Random _rng;
    private readonly List<(double Ms, EyePose Pose)> _keys = new();
    private DateTime _start;

    /// <summary>Quiet seconds between moves.</summary>
    public static readonly (double Min, double Max) Gap = (3.5, 9);

    public EyeLife(Random rng, DateTime now)
    {
        _rng = rng;
        // The first move comes soon, so a freshly opened card shows it's alive.
        NextAt = now + TimeSpan.FromSeconds(1.5 + rng.NextDouble() * 2);
    }

    /// <summary>When the next move starts (or the current one started, while it plays).</summary>
    public DateTime NextAt { get; private set; }

    /// <summary>The name of the move playing now, for tests and logs ("blink", "glance", ...), or null.</summary>
    public string? Move { get; private set; }

    public bool Animating(DateTime now) => _keys.Count > 0 && now >= _start && (now - _start).TotalMilliseconds <= _keys[^1].Ms;

    public EyePose PoseAt(DateTime now)
    {
        if (_keys.Count > 0 && (now - _start).TotalMilliseconds > _keys[^1].Ms)
        {
            // The move is over: rest until the next one.
            _keys.Clear();
            Move = null;
            NextAt = now + TimeSpan.FromSeconds(Gap.Min + _rng.NextDouble() * (Gap.Max - Gap.Min));
        }
        if (_keys.Count == 0 && now >= NextAt) Begin(now);
        if (_keys.Count == 0) return EyePose.Rest;

        var t = (now - _start).TotalMilliseconds;
        for (var i = 1; i < _keys.Count; i++)
        {
            if (t > _keys[i].Ms) continue;
            var (t0, a) = _keys[i - 1];
            var (t1, b) = _keys[i];
            var k = t1 <= t0 ? 1 : Ease((t - t0) / (t1 - t0));
            return new EyePose(Lerp(a.Open, b.Open, k), Lerp(a.Dx, b.Dx, k), Lerp(a.Dy, b.Dy, k));
        }
        return _keys[^1].Pose;
    }

    /// <summary>Start a move now (also used to make the eye react, say when you open the card).</summary>
    public void Begin(DateTime now, string? move = null)
    {
        _keys.Clear();
        _start = now;
        NextAt = now;
        move ??= Pick();
        Move = move;
        var at = new EyePose(1, 0, 0);
        void Key(double ms, EyePose p) => _keys.Add((ms, p));
        double Blink(double from, EyePose look)
        {
            Key(from + 90, look with { Open = 0.06 });
            Key(from + 150, look with { Open = 0.06 });
            Key(from + 290, look);
            return from + 290;
        }

        Key(0, at);
        switch (move)
        {
            case "double":
            {
                var end = Blink(0, at);
                Key(end + 110, at);
                Blink(end + 110, at);
                break;
            }
            case "glance":
            {
                // Somewhere in the almond, never a tiny twitch.
                var angle = _rng.NextDouble() * Math.PI * 2;
                var look = new EyePose(1, Math.Round(Math.Cos(angle) * EyePose.MaxDx, 1), Math.Round(Math.Sin(angle) * EyePose.MaxDy, 1));
                Key(220, look);
                var hold = 700 + _rng.NextDouble() * 900;
                var end = 220 + hold;
                // Sometimes it blinks while it's looking away.
                if (_rng.NextDouble() < 0.35) end = Blink(220 + hold / 2, look) + hold / 2;
                Key(end, look);
                Key(end + 280, at);
                break;
            }
            case "scan":
            {
                var left = new EyePose(1, -EyePose.MaxDx, 0);
                var right = new EyePose(1, EyePose.MaxDx, 0);
                Key(220, left);
                Key(800, left);
                Key(1150, right);
                Key(1750, right);
                Key(2050, at);
                break;
            }
            default: // "blink"
                Blink(0, at);
                break;
        }
    }

    private string Pick()
    {
        var r = _rng.NextDouble();
        return r < 0.5 ? "blink" : r < 0.62 ? "double" : r < 0.92 ? "glance" : "scan";
    }

    private static double Lerp(double a, double b, double k) => a + (b - a) * k;

    /// <summary>Ease in and out, so lids and pupil start and stop softly.</summary>
    private static double Ease(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }
}
