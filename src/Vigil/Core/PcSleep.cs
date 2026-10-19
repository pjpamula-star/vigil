namespace Vigil.Core;

public enum PcPower { Awake, Asleep, Off }

/// <summary>
/// Whether the PC is awake, asleep or off, for your phone.
///
/// On a Modern Standby laptop (most new Windows laptops) there's no separate "sleep": once the screen is off
/// and nothing holds the PC awake, Windows suspends every app within seconds, Vigil included. So asleep means
/// screen off while Vigil isn't holding it. On a PC with classic sleep, it's the suspend Windows announces.
/// A moment's wake with the screen still off (Windows does maintenance now and then) doesn't count as awake.
/// </summary>
public sealed class PcSleep
{
    private readonly Func<DateTime> _now;
    private readonly object _gate = new();
    private bool? _displayOn;
    private bool _holding;
    private bool _suspended;
    private bool _off;
    private bool _runningScreenOff;
    private DateTime? _holdEndedAt;

    public const string ShuttingDown = "shutdown";
    public const string VigilClosed = "closed";

    /// <summary>How soon after a hold ends a sleep still counts as "Claude finished, so it went to sleep".</summary>
    public static readonly TimeSpan AfterWork = TimeSpan.FromMinutes(5);

    public PcSleep(bool modernStandby, Func<DateTime>? now = null)
    {
        ModernStandby = modernStandby;
        _now = now ?? (() => DateTime.Now);
        Since = _now();
    }

    public bool ModernStandby { get; }
    public PcPower State { get; private set; } = PcPower.Awake;

    /// <summary>When the current state began.</summary>
    public DateTime Since { get; private set; }

    public DateTime? LastSleptAt { get; private set; }
    public DateTime? LastWokeAt { get; private set; }

    /// <summary>This sleep began soon after Vigil stopped holding the PC: Claude finished, then it slept.</summary>
    public bool SleptAfterWork { get; private set; }

    public bool? DisplayOn { get { lock (_gate) return _displayOn; } }

    /// <summary>Awake with the screen off (Vigil is holding it, or something else is).</summary>
    public bool ScreenOff { get { lock (_gate) return _displayOn == false; } }

    /// <summary>Why it's off: <see cref="ShuttingDown"/> or <see cref="VigilClosed"/>.</summary>
    public string? OffReason { get; private set; }

    /// <summary>Raised when it goes to sleep (true) or wakes (false), on the thread that reported the change.</summary>
    public event Action<bool>? Changed;

    public void Display(bool on) => Update(() =>
    {
        _displayOn = on;
        _runningScreenOff = false;
    });

    /// <summary>
    /// Vigil has kept running for minutes with the screen off, so something (music, a download) keeps the PC
    /// awake: Windows would have suspended Vigil otherwise.
    /// </summary>
    public void StillRunning()
    {
        lock (_gate)
        {
            _runningScreenOff = true;
            if (State != PcPower.Asleep || _suspended) return;
            // It never really slept: take back the sleep instead of recording a wake.
            State = PcPower.Awake;
            Since = _awakeBefore;
            LastSleptAt = _sleptBefore;
            LastWokeAt = _wokeBefore;
            SleptAfterWork = false;
        }
        Changed?.Invoke(false);
    }

    private DateTime _awakeBefore;
    private DateTime? _sleptBefore, _wokeBefore;

    public void Hold(bool holding) => Update(() =>
    {
        if (_holding && !holding)
        {
            _holdEndedAt = _now();
            _runningScreenOff = false; // whether it now sleeps is a new question
        }
        _holding = holding;
    });

    public void Suspend() => Update(() => _suspended = true);
    public void Resume() => Update(() => _suspended = false);

    /// <summary>Windows is shutting down or restarting, or Vigil is closing.</summary>
    public void TurningOff(string why) => Update(() =>
    {
        _off = true;
        OffReason ??= why;
    });

    private void Update(Action change)
    {
        bool? raised = null;
        lock (_gate)
        {
            change();
            var next = _off ? PcPower.Off
                : _suspended || (ModernStandby && _displayOn == false && !_holding && !_runningScreenOff) ? PcPower.Asleep
                : PcPower.Awake;
            if (next == State) return;
            var now = _now();
            var wasAwake = State == PcPower.Awake;
            if (next == PcPower.Awake)
            {
                LastWokeAt = now;
                SleptAfterWork = false;
            }
            else if (wasAwake)
            {
                _awakeBefore = Since;
                _sleptBefore = LastSleptAt;
                _wokeBefore = LastWokeAt;
                LastSleptAt = now;
                SleptAfterWork = _holdEndedAt is { } ended && now - ended <= AfterWork;
                if (SleptAfterWork) _holdEndedAt = null; // only the first sleep after the work counts
            }
            State = next;
            Since = now;
            // Asleep and off are both "gone" to the phone; going from one to the other isn't news.
            if (wasAwake || next == PcPower.Awake) raised = next != PcPower.Awake;
        }
        if (raised is { } asleep) Changed?.Invoke(asleep);
    }
}
