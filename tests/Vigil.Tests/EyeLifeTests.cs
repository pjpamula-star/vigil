using Vigil.Core;

namespace Vigil.Tests;

public class EyeLifeTests
{
    private static readonly DateTime T0 = new(2026, 10, 18, 12, 0, 0);

    [Fact]
    public void Rests_open_until_its_first_move()
    {
        var life = new EyeLife(new Random(1), T0);
        Assert.Equal(EyePose.Rest, life.PoseAt(T0));
        Assert.InRange((life.NextAt - T0).TotalSeconds, 1.5, 3.5);
    }

    [Fact]
    public void A_blink_closes_and_opens_again_within_a_third_of_a_second()
    {
        var life = new EyeLife(new Random(1), T0);
        life.Begin(T0, "blink");
        Assert.InRange(life.PoseAt(T0.AddMilliseconds(120)).Open, 0, 0.1);
        Assert.Equal(1, life.PoseAt(T0.AddMilliseconds(290)).Open, 3);
        Assert.Equal(EyePose.Rest, life.PoseAt(T0.AddMilliseconds(400)));
    }

    [Fact]
    public void A_glance_looks_away_and_comes_back()
    {
        var life = new EyeLife(new Random(7), T0);
        life.Begin(T0, "glance");
        var away = life.PoseAt(T0.AddMilliseconds(400));
        Assert.True(Math.Abs(away.Dx) + Math.Abs(away.Dy) > 3, $"pupil barely moved: {away}");
        Assert.InRange(Math.Abs(away.Dx), 0, EyePose.MaxDx);
        Assert.InRange(Math.Abs(away.Dy), 0, EyePose.MaxDy);
        Assert.Equal(EyePose.Rest, life.PoseAt(T0.AddSeconds(3)));
    }

    [Fact]
    public void A_scan_looks_left_then_right()
    {
        var life = new EyeLife(new Random(1), T0);
        life.Begin(T0, "scan");
        Assert.Equal(-EyePose.MaxDx, life.PoseAt(T0.AddMilliseconds(500)).Dx, 3);
        Assert.Equal(EyePose.MaxDx, life.PoseAt(T0.AddMilliseconds(1400)).Dx, 3);
        Assert.Equal(EyePose.Rest, life.PoseAt(T0.AddMilliseconds(2100)));
    }

    [Fact]
    public void Moves_keep_coming_every_few_seconds_and_vary()
    {
        var life = new EyeLife(new Random(3), T0);
        var moves = new List<string>();
        var starts = new List<DateTime>();
        string? last = null;
        for (var t = T0; t < T0.AddMinutes(10); t = t.AddMilliseconds(50))
        {
            life.PoseAt(t);
            if (life.Move is { } m && last is null) { moves.Add(m); starts.Add(t); }
            last = life.Move;
        }
        Assert.InRange(moves.Count, 60, 200);
        Assert.Contains("blink", moves);
        Assert.Contains("glance", moves);
        Assert.Contains("double", moves);
        Assert.Contains("scan", moves);
        // Never two moves on top of each other: at least the shortest gap apart.
        for (var i = 1; i < starts.Count; i++) Assert.True((starts[i] - starts[i - 1]).TotalSeconds >= EyeLife.Gap.Min);
    }

    [Fact]
    public void Quantized_poses_snap_to_a_small_grid()
    {
        Assert.Equal(new EyePose(0.5, 4, -2), new EyePose(0.47, 3.6, -2.4).Quantized());
    }
}
