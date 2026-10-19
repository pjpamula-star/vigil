using System.Runtime.InteropServices;

namespace Vigil.Core;

public interface IPowerPlan
{
    Guid ActiveScheme();
    (uint ac, uint dc) Read(Guid scheme, PowerSettings.Setting setting);
    void Write(Guid scheme, PowerSettings.Setting setting, uint ac, uint dc);
    void Apply(Guid scheme);
}

/// <summary>The real power plan, through powrprof.dll. Works without admin rights.</summary>
public sealed class WindowsPowerPlan : IPowerPlan
{
    public Guid ActiveScheme()
    {
        Check(Native.PowerGetActiveScheme(IntPtr.Zero, out var ptr), "read active plan");
        try { return Marshal.PtrToStructure<Guid>(ptr); }
        finally { Native.LocalFree(ptr); }
    }

    public (uint ac, uint dc) Read(Guid scheme, PowerSettings.Setting s)
    {
        var sub = s.Sub; var id = s.Id;
        Check(Native.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref id, out var ac), $"read {s.Name} (AC)");
        Check(Native.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref id, out var dc), $"read {s.Name} (battery)");
        return (ac, dc);
    }

    public void Write(Guid scheme, PowerSettings.Setting s, uint ac, uint dc)
    {
        var sub = s.Sub; var id = s.Id;
        Check(Native.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref sub, ref id, ac), $"write {s.Name} (AC)");
        Check(Native.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref sub, ref id, dc), $"write {s.Name} (battery)");
    }

    public void Apply(Guid scheme) => Check(Native.PowerSetActiveScheme(IntPtr.Zero, ref scheme), "apply plan");

    private static void Check(uint result, string what)
    {
        if (result != 0) throw new InvalidOperationException($"Couldn't {what} (error {result}).");
    }
}

/// <summary>What Vigil changed, written to disk BEFORE the change so it can always be undone.</summary>
public sealed class PowerJournal
{
    public Guid Scheme { get; set; }
    public DateTime CreatedAt { get; set; }
    public int Pid { get; set; }
    public List<JournalEntry> Entries { get; set; } = new();
}

public sealed class JournalEntry
{
    public Guid Sub { get; set; }
    public Guid Setting { get; set; }
    public string Name { get; set; } = "";
    public uint Ac { get; set; }
    public uint Dc { get; set; }
}

/// <summary>
/// Holds the PC awake two ways at once, because Modern Standby laptops ignore either one alone:
/// a system power request, and power-plan overrides (never sleep, lid does nothing).
/// Every original value is journaled first and restored on release, on crash recovery, or by the watchdog.
/// </summary>
public sealed class PowerController : IDisposable
{
    private readonly IPowerPlan _plan;
    private readonly string _journalPath;
    private readonly bool _useRequests;
    private IntPtr _request = IntPtr.Zero;
    private readonly object _gate = new();

    public bool Holding { get; private set; }
    public bool CpuCapped { get; private set; }
    public string? LastError { get; private set; }

    public PowerController(IPowerPlan plan, string journalPath, bool useRequests = true)
    {
        _plan = plan;
        _journalPath = journalPath;
        _useRequests = useRequests;
    }

    public bool HasJournal => File.Exists(_journalPath);

    public void Hold()
    {
        lock (_gate)
        {
            if (Holding) return;
            try
            {
                if (_useRequests) SetRequest(true);
                ApplyOverrides(PowerSettings.KeepAwakeOverrides);
                Holding = true;
                LastError = null;
                Log.Info("Holding the PC awake");
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Log.Error("Hold failed", ex);
                // Never leave half-applied changes behind.
                SafeRestore();
                if (_useRequests) SetRequest(false);
                throw;
            }
        }
    }

    public void Release()
    {
        lock (_gate)
        {
            if (_useRequests) SetRequest(false);
            Restore();
            Holding = false;
            CpuCapped = false;
            Log.Info("Released: normal sleep restored");
        }
    }

    /// <summary>Cap CPU speed to cool down. Journaled like everything else.</summary>
    public void CapCpu(uint percent)
    {
        lock (_gate)
        {
            if (CpuCapped) return;
            ApplyOverrides(new[] { (PowerSettings.CpuMax, percent) });
            CpuCapped = true;
            Log.Info($"CPU capped at {percent}%");
        }
    }

    public void UncapCpu()
    {
        lock (_gate)
        {
            if (!CpuCapped) return;
            var journal = Json.Load<PowerJournal>(_journalPath);
            var entry = journal?.Entries.FirstOrDefault(e => e.Setting == PowerSettings.ProcThrottleMax);
            if (journal is not null && entry is not null)
            {
                _plan.Write(journal.Scheme, PowerSettings.CpuMax, entry.Ac, entry.Dc);
                _plan.Apply(_plan.ActiveScheme());
                journal.Entries.Remove(entry);
                Json.SaveAtomic(_journalPath, journal);
            }
            CpuCapped = false;
            Log.Info("CPU cap removed");
        }
    }

    /// <summary>Put back everything in the journal, then delete it. Safe to call any time.</summary>
    public bool Restore()
    {
        lock (_gate)
        {
            var journal = Json.Load<PowerJournal>(_journalPath);
            if (journal is null)
            {
                if (File.Exists(_journalPath)) File.Delete(_journalPath); // unreadable; nothing we can do with it
                return false;
            }
            foreach (var e in journal.Entries)
            {
                try
                {
                    _plan.Write(journal.Scheme, new PowerSettings.Setting(e.Name, e.Sub, e.Setting), e.Ac, e.Dc);
                }
                catch (Exception ex)
                {
                    Log.Error($"Restoring {e.Name} failed", ex);
                }
            }
            try { _plan.Apply(_plan.ActiveScheme()); } catch (Exception ex) { Log.Error("Re-applying plan failed", ex); }
            File.Delete(_journalPath);
            Log.Info($"Restored {journal.Entries.Count} power settings");
            return true;
        }
    }

    private void SafeRestore()
    {
        try { Restore(); } catch (Exception ex) { Log.Error("Restore failed", ex); }
    }

    private void ApplyOverrides(IEnumerable<(PowerSettings.Setting setting, uint value)> overrides)
    {
        var scheme = _plan.ActiveScheme();
        var journal = Json.Load<PowerJournal>(_journalPath) ?? new PowerJournal { Scheme = scheme, CreatedAt = DateTime.Now };
        journal.Pid = Environment.ProcessId;

        var list = overrides.ToList();
        // 1. Journal the originals (only the first time we touch each setting).
        foreach (var (setting, _) in list)
        {
            if (journal.Entries.Any(e => e.Setting == setting.Id)) continue;
            var (ac, dc) = _plan.Read(journal.Scheme, setting);
            journal.Entries.Add(new JournalEntry { Sub = setting.Sub, Setting = setting.Id, Name = setting.Name, Ac = ac, Dc = dc });
        }
        Json.SaveAtomic(_journalPath, journal);

        // 2. Only now change anything.
        foreach (var (setting, value) in list)
            _plan.Write(journal.Scheme, setting, value, value);
        _plan.Apply(scheme);
    }

    private void SetRequest(bool on)
    {
        if (on)
        {
            if (_request == IntPtr.Zero)
            {
                var ctx = new Native.REASON_CONTEXT
                {
                    Version = Native.POWER_REQUEST_CONTEXT_VERSION,
                    Flags = Native.POWER_REQUEST_CONTEXT_SIMPLE_STRING,
                    SimpleReasonString = "Vigil: Claude Code is working",
                };
                _request = Native.PowerCreateRequest(ref ctx);
            }
            if (_request != IntPtr.Zero && _request != new IntPtr(-1))
            {
                Native.PowerSetRequest(_request, Native.PowerRequestType.SystemRequired);
                Native.PowerSetRequest(_request, Native.PowerRequestType.ExecutionRequired);
            }
            Native.SetThreadExecutionState(Native.ExecutionState.Continuous | Native.ExecutionState.SystemRequired);
        }
        else
        {
            if (_request != IntPtr.Zero && _request != new IntPtr(-1))
            {
                Native.PowerClearRequest(_request, Native.PowerRequestType.SystemRequired);
                Native.PowerClearRequest(_request, Native.PowerRequestType.ExecutionRequired);
                Native.CloseHandle(_request);
            }
            _request = IntPtr.Zero;
            Native.SetThreadExecutionState(Native.ExecutionState.Continuous);
        }
    }

    /// <summary>Restore everything, then hibernate (or sleep if hibernate is off).</summary>
    public void HibernateNow()
    {
        Release();
        var hibernate = Native.IsPwrHibernateAllowed();
        Log.Warn(hibernate ? "Hibernating to protect the PC" : "Hibernate unavailable; sleeping instead");
        Native.SetSuspendState(hibernate, false, false);
    }

    public void Dispose()
    {
        try { Release(); } catch (Exception ex) { Log.Error("Release on exit failed", ex); }
    }
}

/// <summary>An in-memory power plan for tests and demo mode. Touches nothing on the real system.</summary>
public sealed class MemoryPowerPlan : IPowerPlan
{
    public Guid Scheme { get; set; } = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
    public Dictionary<Guid, (uint ac, uint dc)> Values { get; } = new();
    public int Applies { get; private set; }

    public Guid ActiveScheme() => Scheme;
    public (uint ac, uint dc) Read(Guid scheme, PowerSettings.Setting setting) => Values.TryGetValue(setting.Id, out var v) ? v : (900, 600);
    public void Write(Guid scheme, PowerSettings.Setting setting, uint ac, uint dc) => Values[setting.Id] = (ac, dc);
    public void Apply(Guid scheme) => Applies++;
}
