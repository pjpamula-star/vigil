using Vigil.Core;

namespace Vigil.Tests;

public class NudgeTests
{
    private static readonly TimeSpan Hour = TimeSpan.FromMinutes(60);
    private static TimeSpan M(int m) => TimeSpan.FromMinutes(m);

    [Fact]
    public void Nothing_in_the_first_20_minutes() => Assert.Equal(0, Engine.NextNudge(M(19), Hour, 0));

    [Fact]
    public void Still_waiting_after_20_minutes_once() 
    {
        Assert.Equal(1, Engine.NextNudge(M(20), Hour, 0));
        Assert.Equal(1, Engine.NextNudge(M(35), Hour, 1));
    }

    [Fact]
    public void Ten_minute_warning_before_the_PC_sleeps()
    {
        Assert.Equal(2, Engine.NextNudge(M(50), Hour, 1));
        Assert.Equal(2, Engine.NextNudge(M(55), Hour, 2));
    }

    [Fact]
    public void Jumps_straight_to_the_warning_if_the_first_was_missed() => Assert.Equal(2, Engine.NextNudge(M(52), Hour, 0));

    [Fact]
    public void Nothing_after_the_window_ends() => Assert.Equal(0, Engine.NextNudge(M(61), Hour, 0));

    [Fact]
    public void Short_windows_only_get_the_nudge_when_it_fits()
    {
        var fifteen = M(15);
        Assert.Equal(0, Engine.NextNudge(M(10), fifteen, 0));
        Assert.Equal(0, Engine.NextNudge(M(14), fifteen, 0));
    }
}
