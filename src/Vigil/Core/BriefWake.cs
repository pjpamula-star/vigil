namespace Vigil.Core;

/// <summary>
/// Keeps the PC from suspending for a few seconds, separately from the main keep-awake. On a Modern Standby
/// laptop Windows suspends apps within seconds of the screen going off, so telling the phone "it's asleep"
/// needs this to finish sending. Released on Dispose, or after <c>max</c> at the latest.
/// </summary>
public sealed class BriefWake : IDisposable
{
    private IntPtr _request;
    private readonly Timer _cap;
    private int _released;

    private BriefWake(string reason, TimeSpan max)
    {
        var ctx = new Native.REASON_CONTEXT
        {
            Version = Native.POWER_REQUEST_CONTEXT_VERSION,
            Flags = Native.POWER_REQUEST_CONTEXT_SIMPLE_STRING,
            SimpleReasonString = reason,
        };
        _request = Native.PowerCreateRequest(ref ctx);
        if (Valid)
        {
            Native.PowerSetRequest(_request, Native.PowerRequestType.SystemRequired);
            Native.PowerSetRequest(_request, Native.PowerRequestType.ExecutionRequired);
        }
        _cap = new Timer(_ => Dispose(), null, max, Timeout.InfiniteTimeSpan);
    }

    private bool Valid => _request != IntPtr.Zero && _request != new IntPtr(-1);

    public static BriefWake Hold(string reason, TimeSpan? max = null) => new(reason, max ?? TimeSpan.FromSeconds(15));

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 1) return;
        _cap.Dispose();
        if (Valid)
        {
            Native.PowerClearRequest(_request, Native.PowerRequestType.SystemRequired);
            Native.PowerClearRequest(_request, Native.PowerRequestType.ExecutionRequired);
            Native.CloseHandle(_request);
        }
        _request = IntPtr.Zero;
    }
}
