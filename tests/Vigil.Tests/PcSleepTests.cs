using Vigil.Core;

namespace Vigil.Tests;

/// <summary>Whether the PC is awake, asleep or off, as your phone shows it.</summary>
public class PcSleepTests
{
    private readonly Clock _clock = new();
    private readonly List<bool> _changes = new();

    private PcSleep Make(bool modernStandby = true)
    {
        var s = new PcSleep(modernStandby, () => _clock.Now);
        s.Changed += asleep => _changes.Add(asleep);
        return s;
    }

    [Fact]
    public void Starts_awake()
    {
        var s = Make();
        Assert.Equal(PcPower.Awake, s.State);
        Assert.Equal(_clock.Now, s.Since);
    }

    [Fact]
    public void Screen_off_while_nothing_holds_it_is_asleep_on_a_modern_standby_laptop()
    {
        var s = Make();
        s.Display(on: true);
        _clock.Advance(TimeSpan.FromMinutes(3));
        var off = _clock.Now;
        s.Display(on: false);
        Assert.Equal(PcPower.Asleep, s.State);
        Assert.Equal(off, s.Since);
        Assert.Equal(new[] { true }, _changes);
    }

    [Fact]
    public void Screen_off_while_Vigil_holds_it_is_still_awake()
    {
        var s = Make();
        s.Hold(true);
        s.Display(on: false);
        Assert.Equal(PcPower.Awake, s.State);

        // Claude finishes with the lid closed: it goes to sleep the moment Vigil lets go.
        _clock.Advance(TimeSpan.FromMinutes(40));
        s.Hold(false);
        Assert.Equal(PcPower.Asleep, s.State);
        Assert.Equal(_clock.Now, s.Since);
        Assert.True(s.SleptAfterWork);
    }

    [Fact]
    public void Only_the_first_sleep_after_the_work_counts()
    {
        var s = Make();
        s.Display(on: false);
        s.Hold(true);
        s.Hold(false);
        Assert.True(s.SleptAfterWork);
        _clock.Advance(TimeSpan.FromMinutes(1));
        s.Display(on: true);
        s.Display(on: false);
        Assert.Equal(PcPower.Asleep, s.State);
        Assert.False(s.SleptAfterWork);
    }

    [Fact]
    public void Sleeping_long_after_the_last_hold_is_not_after_work()
    {
        var s = Make();
        s.Hold(true);
        s.Hold(false);
        _clock.Advance(TimeSpan.FromMinutes(30));
        s.Display(on: false);
        Assert.Equal(PcPower.Asleep, s.State);
        Assert.False(s.SleptAfterWork);
    }

    [Fact]
    public void Screen_off_is_not_sleep_on_a_PC_with_classic_sleep()
    {
        var s = Make(modernStandby: false);
        s.Display(on: false);
        Assert.Equal(PcPower.Awake, s.State);
        s.Suspend();
        Assert.Equal(PcPower.Asleep, s.State);
        s.Resume();
        Assert.Equal(PcPower.Awake, s.State);
    }

    [Fact]
    public void Waking_records_how_long_it_slept()
    {
        var s = Make();
        s.Display(on: false);
        var slept = _clock.Now;
        _clock.Advance(TimeSpan.FromHours(8) + TimeSpan.FromMinutes(43));
        s.Display(on: true);
        Assert.Equal(PcPower.Awake, s.State);
        Assert.Equal(slept, s.LastSleptAt);
        Assert.Equal(_clock.Now, s.LastWokeAt);
        Assert.Equal(_clock.Now, s.Since);
        Assert.Equal(new[] { true, false }, _changes);
    }

    [Fact]
    public void A_brief_wake_with_the_screen_still_off_does_not_count_as_awake()
    {
        // Windows wakes a sleeping laptop for a moment now and then (or Vigil's own sleep call returns
        // at once on a Modern Standby laptop). The screen stays off, so it's still asleep, since the same time.
        var s = Make();
        s.Display(on: false);
        var since = _clock.Now;
        _clock.Advance(TimeSpan.FromMinutes(28));
        s.Suspend();
        s.Resume();
        Assert.Equal(PcPower.Asleep, s.State);
        Assert.Equal(since, s.Since);
        Assert.Equal(new[] { true }, _changes);
    }

    [Fact]
    public void Work_starting_with_the_lid_closed_wakes_it()
    {
        // A scheduled Claude run starts while the laptop sleeps: Vigil holds it, so it's awake (screen still off).
        var s = Make();
        s.Display(on: false);
        _clock.Advance(TimeSpan.FromHours(1));
        s.Hold(true);
        Assert.Equal(PcPower.Awake, s.State);
        Assert.Equal(new[] { true, false }, _changes);
    }

    [Fact]
    public void Shutting_down_is_off_and_stays_off()
    {
        var s = Make();
        s.TurningOff(PcSleep.ShuttingDown);
        Assert.Equal(PcPower.Off, s.State);
        s.Display(on: true);
        Assert.Equal(PcPower.Off, s.State);
        Assert.Equal(new[] { true }, _changes);
    }

    [Fact]
    public void Still_running_with_the_screen_off_means_something_else_keeps_it_awake()
    {
        var s = Make();
        var awakeSince = s.Since;
        _clock.Advance(TimeSpan.FromMinutes(10));
        s.Display(on: false);
        Assert.Equal(PcPower.Asleep, s.State);
        _clock.Advance(TimeSpan.FromMinutes(3));
        s.StillRunning();
        Assert.Equal(PcPower.Awake, s.State);
        Assert.True(s.ScreenOff);
        // It never slept, so there's no sleep or wake to report.
        Assert.Equal(awakeSince, s.Since);
        Assert.Null(s.LastSleptAt);
        Assert.Null(s.LastWokeAt);

        // The screen coming on and going off again starts over.
        s.Display(on: true);
        s.Display(on: false);
        Assert.Equal(PcPower.Asleep, s.State);
    }

    [Fact]
    public void After_work_ends_with_the_screen_off_it_is_asleep_again_even_if_it_was_running()
    {
        var s = Make();
        s.Display(on: false);
        _clock.Advance(TimeSpan.FromMinutes(3));
        s.StillRunning();
        s.Hold(true);
        s.Hold(false);
        Assert.Equal(PcPower.Asleep, s.State);
    }

    [Fact]
    public void Off_says_why()
    {
        var s = Make();
        s.TurningOff(PcSleep.VigilClosed);
        Assert.Equal(PcSleep.VigilClosed, s.OffReason);
    }

    [Fact]
    public void Nothing_known_about_the_screen_yet_means_awake()
    {
        var s = Make();
        s.Hold(false);
        Assert.Equal(PcPower.Awake, s.State);
        Assert.Empty(_changes);
    }
}
