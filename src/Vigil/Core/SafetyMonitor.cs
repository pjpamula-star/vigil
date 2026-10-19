using System.Diagnostics;

namespace Vigil.Core;

public enum SafetyLevel { Ok, Warm, Hot }

public sealed record ThermalReading(bool Available, int PassiveLimit, bool Throttling);

public sealed record BatteryReading(bool OnBattery, int Percent);

public sealed record SafetyStatus(SafetyLevel Level, string Headline, string Detail, bool ShouldEaseOff, bool ShouldHibernate);

public interface ISensors
{
    ThermalReading Thermal();
    BatteryReading Battery();
}

/// <summary>
/// Reads Windows' own thermal-throttling counters. Real temperatures need admin rights on this laptop,
/// but the firmware's throttling signal is the better alarm anyway: it fires when the hardware itself
/// decides it is too hot.
/// </summary>
public sealed class WindowsSensors : ISensors, IDisposable
{
    private readonly List<PerformanceCounter> _passive = new();
    private readonly List<PerformanceCounter> _reasons = new();
    private bool _available;

    public WindowsSensors()
    {
        try
        {
            var category = new PerformanceCounterCategory("Thermal Zone Information");
            foreach (var instance in category.GetInstanceNames())
            {
                _passive.Add(new PerformanceCounter("Thermal Zone Information", "% Passive Limit", instance, readOnly: true));
                _reasons.Add(new PerformanceCounter("Thermal Zone Information", "Throttle Reasons", instance, readOnly: true));
            }
            _available = _passive.Count > 0;
        }
        catch (Exception ex)
        {
            Log.Warn($"Thermal counters unavailable: {ex.Message}");
            _available = false;
        }
    }

    public ThermalReading Thermal()
    {
        if (!_available) return new ThermalReading(false, 100, false);
        try
        {
            var passive = _passive.Select(c => (int)Math.Round(c.NextValue())).DefaultIfEmpty(100).Min();
            var throttling = _reasons.Any(c => c.NextValue() > 0);
            return new ThermalReading(true, passive is <= 0 or > 100 ? 100 : passive, throttling);
        }
        catch (Exception ex)
        {
            Log.Warn($"Thermal read failed: {ex.Message}");
            return new ThermalReading(false, 100, false);
        }
    }

    public BatteryReading Battery()
    {
        var s = System.Windows.Forms.SystemInformation.PowerStatus;
        var onBattery = s.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline;
        var pct = s.BatteryLifePercent is >= 0 and <= 1 ? (int)Math.Round(s.BatteryLifePercent * 100) : 100;
        return new BatteryReading(onBattery, pct);
    }

    public void Dispose()
    {
        foreach (var c in _passive.Concat(_reasons)) c.Dispose();
    }
}

/// <summary>
/// Two-step protection: ease off first (cap the CPU), then hibernate if that wasn't enough.
/// Pure state machine over readings, so it can be tested with fake sensors and a fake clock.
/// </summary>
public sealed class SafetyMonitor
{
    public TimeSpan WarmAfter { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan HotAfterEasing { get; set; } = TimeSpan.FromMinutes(3);
    public TimeSpan SevereAfter { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan CalmAfter { get; set; } = TimeSpan.FromMinutes(5);
    public int SeverePassiveLimit { get; set; } = 50;
    public int EaseOffBatteryPercent { get; set; } = 20;
    public int HibernateBatteryPercent { get; set; } = 8;

    private DateTime? _hotSince;     // throttling started
    private DateTime? _severeSince;  // deep throttling started
    private DateTime? _easedAt;      // when we capped the CPU
    private DateTime? _calmSince;

    public bool Eased => _easedAt is not null;
    public ThermalReading LastThermal { get; private set; } = new(false, 100, false);
    public BatteryReading LastBattery { get; private set; } = new(false, 100);

    public SafetyStatus Evaluate(ThermalReading t, BatteryReading b, DateTime now)
    {
        LastThermal = t;
        LastBattery = b;

        var throttling = t.Available && (t.PassiveLimit < 100 || t.Throttling);
        var severe = t.Available && t.PassiveLimit <= SeverePassiveLimit;

        _hotSince = throttling ? _hotSince ?? now : null;
        _severeSince = severe ? _severeSince ?? now : null;

        // Battery first: it's simple and certain.
        if (b.OnBattery && b.Percent <= HibernateBatteryPercent)
            return new SafetyStatus(SafetyLevel.Hot, "Battery almost empty", $"{b.Percent}% left. Hibernating so nothing is lost.", true, true);

        if (_severeSince is { } sv && now - sv >= SevereAfter)
            return new SafetyStatus(SafetyLevel.Hot, "Too hot", "Windows is throttling hard to cool down. Hibernating to protect your PC.", true, true);

        if (_easedAt is { } eased && throttling && now - eased >= HotAfterEasing)
            return new SafetyStatus(SafetyLevel.Hot, "Still too hot", $"Easing off for {(int)HotAfterEasing.TotalMinutes} min didn't cool it down. Hibernating to protect your PC.", true, true);

        var warmHeat = _hotSince is { } hs && now - hs >= WarmAfter;
        var lowBattery = b.OnBattery && b.Percent <= EaseOffBatteryPercent;

        if (warmHeat || lowBattery)
        {
            _easedAt ??= now;
            _calmSince = null;
            return warmHeat
                ? new SafetyStatus(SafetyLevel.Warm, "Running warm: easing off", "Windows is slowing the CPU to cool down, so Vigil capped its speed. If it's still throttling in a few minutes, the PC hibernates.", true, false)
                : new SafetyStatus(SafetyLevel.Warm, "Battery low: easing off", $"{b.Percent}% left. Slowed the CPU to stretch it. Hibernates at {HibernateBatteryPercent}%.", true, false);
        }

        if (_easedAt is not null)
        {
            _calmSince ??= now;
            if (now - _calmSince >= CalmAfter)
            {
                _easedAt = null;
                _calmSince = null;
                return Ok(t, b, easeOff: false);
            }
            return new SafetyStatus(SafetyLevel.Warm, "Cooling down", "Keeping the CPU capped until things stay calm for a few minutes.", true, false);
        }

        return Ok(t, b, easeOff: false);
    }

    public void Reset()
    {
        _hotSince = _severeSince = _easedAt = _calmSince = null;
    }

    private static SafetyStatus Ok(ThermalReading t, BatteryReading b, bool easeOff)
    {
        var heat = t.Available ? "Temperature is fine" : "Heat sensor unavailable";
        var battery = b.OnBattery ? $"battery {b.Percent}%" : "plugged in";
        return new SafetyStatus(SafetyLevel.Ok, "All clear", $"{heat} · {battery}", easeOff, false);
    }
}
