using Vigil.Core;

namespace Vigil.Tests;

public class KeepAwakePolicyTests
{
    private static readonly DateTime T0 = new(2026, 10, 16, 23, 0, 0);

    private static Session Make(SessionState state, DateTime since) =>
        new() { Id = Guid.NewGuid().ToString(), State = state, StateSince = since, LastEventAt = since };

    [Fact]
    public void Holds_while_working_and_says_so_plainly()
    {
        var d = new KeepAwakePolicy().Decide(new[] { Make(SessionState.Working, T0) }, null, T0);
        Assert.True(d.Hold);
        Assert.Equal(HoldKind.Working, d.Kind);
        Assert.Equal("Safe to close the lid.", d.Headline);
    }

    [Fact]
    public void A_background_task_holds_for_a_while_but_never_forever()
    {
        var p = new KeepAwakePolicy { BackgroundTaskWait = TimeSpan.FromMinutes(60), Grace = TimeSpan.Zero };
        var s = Make(SessionState.Done, T0);
        s.BackgroundTasks.Add("npm run build");
        Assert.True(p.Decide(new[] { s }, null, T0 + TimeSpan.FromMinutes(30)).Hold);
        Assert.False(p.Decide(new[] { s }, null, T0 + TimeSpan.FromMinutes(61)).Hold);
    }

    [Fact]
    public void Waiting_on_you_holds_only_up_to_the_limit()
    {
        var p = new KeepAwakePolicy { WaitForYou = TimeSpan.FromMinutes(60), Grace = TimeSpan.Zero };
        var s = new[] { Make(SessionState.NeedsYou, T0) };
        Assert.True(p.Decide(s, null, T0 + TimeSpan.FromMinutes(59)).Hold);
        Assert.False(p.Decide(s, null, T0 + TimeSpan.FromMinutes(61)).Hold);
    }

    [Fact]
    public void Grace_period_then_sleep()
    {
        var p = new KeepAwakePolicy { Grace = TimeSpan.FromMinutes(2) };
        Assert.True(p.Decide(new[] { Make(SessionState.Working, T0) }, null, T0).Hold);
        var done = new[] { Make(SessionState.Done, T0) };
        var d1 = p.Decide(done, null, T0 + TimeSpan.FromMinutes(1));
        Assert.True(d1.Hold);
        Assert.Equal(HoldKind.Grace, d1.Kind);
        Assert.False(p.Decide(done, null, T0 + TimeSpan.FromMinutes(3)).Hold);
    }

    [Fact]
    public void Manual_hold_always_expires()
    {
        var p = new KeepAwakePolicy { Grace = TimeSpan.Zero };
        var until = T0 + TimeSpan.FromHours(1);
        Assert.Equal(HoldKind.Manual, p.Decide(Array.Empty<Session>(), until, T0).Kind);
        Assert.False(p.Decide(Array.Empty<Session>(), until, until + TimeSpan.FromSeconds(1)).Hold);
    }

    [Fact]
    public void Nothing_running_means_normal_sleep()
    {
        var d = new KeepAwakePolicy().Decide(new[] { Make(SessionState.Done, T0), Make(SessionState.Quiet, T0) }, null, T0);
        Assert.False(d.Hold);
    }
}

public class SafetyMonitorTests
{
    private static readonly DateTime T0 = new(2026, 10, 16, 23, 0, 0);
    private static readonly BatteryReading Plugged = new(false, 100);
    private static readonly ThermalReading Cool = new(true, 100, false);
    private static readonly ThermalReading Throttling = new(true, 80, true);

    [Fact]
    public void Brief_throttling_is_ignored()
    {
        var m = new SafetyMonitor();
        m.Evaluate(Throttling, Plugged, T0);
        var s = m.Evaluate(Throttling, Plugged, T0 + TimeSpan.FromSeconds(20));
        Assert.Equal(SafetyLevel.Ok, s.Level);
    }

    [Fact]
    public void Sustained_throttling_eases_off_then_hibernates_if_it_persists()
    {
        var m = new SafetyMonitor();
        m.Evaluate(Throttling, Plugged, T0);
        var warm = m.Evaluate(Throttling, Plugged, T0 + TimeSpan.FromSeconds(30));
        Assert.Equal(SafetyLevel.Warm, warm.Level);
        Assert.True(warm.ShouldEaseOff);
        Assert.False(warm.ShouldHibernate);

        var still = m.Evaluate(Throttling, Plugged, T0 + TimeSpan.FromMinutes(2));
        Assert.False(still.ShouldHibernate);
        var hot = m.Evaluate(Throttling, Plugged, T0 + TimeSpan.FromSeconds(30) + TimeSpan.FromMinutes(3));
        Assert.True(hot.ShouldHibernate);
    }

    [Fact]
    public void Cooling_down_releases_the_cap_after_calm_period()
    {
        var m = new SafetyMonitor();
        m.Evaluate(Throttling, Plugged, T0);
        m.Evaluate(Throttling, Plugged, T0 + TimeSpan.FromSeconds(30));
        var cooling = m.Evaluate(Cool, Plugged, T0 + TimeSpan.FromMinutes(1));
        Assert.True(cooling.ShouldEaseOff);
        var calm = m.Evaluate(Cool, Plugged, T0 + TimeSpan.FromMinutes(6.5));
        Assert.Equal(SafetyLevel.Ok, calm.Level);
        Assert.False(calm.ShouldEaseOff);
    }

    [Fact]
    public void Deep_throttling_hibernates_quickly()
    {
        var m = new SafetyMonitor();
        var severe = new ThermalReading(true, 40, true);
        m.Evaluate(severe, Plugged, T0);
        Assert.True(m.Evaluate(severe, Plugged, T0 + TimeSpan.FromSeconds(30)).ShouldHibernate);
    }

    [Fact]
    public void Battery_thresholds()
    {
        var m = new SafetyMonitor { EaseOffBatteryPercent = 20, HibernateBatteryPercent = 8 };
        Assert.Equal(SafetyLevel.Ok, m.Evaluate(Cool, new BatteryReading(true, 50), T0).Level);
        Assert.True(m.Evaluate(Cool, new BatteryReading(true, 19), T0).ShouldEaseOff);
        Assert.True(m.Evaluate(Cool, new BatteryReading(true, 8), T0).ShouldHibernate);
        // Plugged in at low charge is fine.
        var fresh = new SafetyMonitor();
        Assert.Equal(SafetyLevel.Ok, fresh.Evaluate(Cool, new BatteryReading(false, 5), T0).Level);
    }

    [Fact]
    public void Missing_sensors_never_trigger()
    {
        var m = new SafetyMonitor();
        var none = new ThermalReading(false, 100, false);
        Assert.Equal(SafetyLevel.Ok, m.Evaluate(none, Plugged, T0 + TimeSpan.FromHours(1)).Level);
    }
}
