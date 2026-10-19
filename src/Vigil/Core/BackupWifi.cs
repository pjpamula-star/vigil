using System.Text.RegularExpressions;

namespace Vigil.Core;

public enum WifiAction { None, Join, Return, NotInRange }

public readonly record struct WifiStep(WifiAction Action, string? Profile = null)
{
    public static readonly WifiStep None = new(WifiAction.None);
}

/// <summary>
/// If the internet drops while Claude is working, join your phone's hotspot; when the work is done,
/// go back to the network you were on. Only networks Windows already knows (saved, with their password)
/// are ever joined.
///
/// It never looks at which networks are nearby: on Windows 11 that list counts as location data and
/// needs location permission, which Vigil has no business asking for. Instead it simply tries the saved
/// hotspot, and if going home fails (home isn't in range), it goes back to the hotspot.
/// </summary>
public sealed class BackupWifiPolicy
{
    /// <summary>A blip isn't an outage: wait this long before switching networks.</summary>
    public TimeSpan OfflineGrace { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>Between tries, so a hotspot that won't connect isn't hammered.</summary>
    public TimeSpan RetryEvery { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>After a failed trip home, stay on the hotspot this long before trying again.</summary>
    public TimeSpan ReturnBackoff { get; init; } = TimeSpan.FromMinutes(15);

    private DateTime? _offlineSince;
    private DateTime _lastTry = DateTime.MinValue;
    private DateTime _noReturnUntil = DateTime.MinValue;
    private DateTime _returnedAt;
    private string? _returnedFrom;
    private int _attempt;
    private bool _toldNotInRange;

    /// <summary>The hotspot Vigil switched to, while it's still on it.</summary>
    public string? Joined { get; private set; }

    /// <summary>The last network that worked before Vigil switched; where it goes back to.</summary>
    public string? Home { get; private set; }

    /// <summary>Vigil just tried to go home and is waiting to see if it worked.</summary>
    public bool Returning => _returnedFrom is not null;

    /// <param name="needed">Vigil is keeping the PC awake for Claude.</param>
    /// <param name="online">The internet is reachable.</param>
    /// <param name="connected">The network we're on (when online), or null.</param>
    /// <param name="backups">Saved hotspots, best first.</param>
    public WifiStep Step(bool needed, bool online, string? connected, IReadOnlyList<string> backups, DateTime now)
    {
        // You (or Windows) moved to another network: it's no longer ours to undo.
        if (online && Joined is not null && connected is not null && !Same(connected, Joined)) Joined = null;

        if (online)
        {
            _offlineSince = null;
            _toldNotInRange = false;
            _attempt = 0;
            _returnedFrom = null; // made it home
            if (Joined is null)
            {
                if (connected is not null && !backups.Any(b => Same(b, connected))) Home = connected;
                return WifiStep.None;
            }
            // Done with the work: back to the normal network, so the hotspot doesn't eat phone data.
            if (!needed && Home is { } home && now >= _noReturnUntil)
            {
                _returnedFrom = Joined;
                _returnedAt = now;
                Joined = null;
                return new WifiStep(WifiAction.Return, home);
            }
            return WifiStep.None;
        }

        _offlineSince ??= now;

        // Going home didn't work (it isn't in range): straight back to the hotspot, and leave it be for a while.
        if (_returnedFrom is { } hotspot)
        {
            if (now - _returnedAt < OfflineGrace) return WifiStep.None;
            _returnedFrom = null;
            _noReturnUntil = now + ReturnBackoff;
            _lastTry = now;
            Joined = hotspot;
            return new WifiStep(WifiAction.Join, hotspot);
        }

        if (!needed || backups.Count == 0 || now - _offlineSince < OfflineGrace || now - _lastTry < RetryEvery) return WifiStep.None;

        // Tried every saved hotspot once and still offline: say so, once, then keep trying quietly.
        if (_attempt == backups.Count && !_toldNotInRange)
        {
            _toldNotInRange = true;
            _lastTry = now;
            return new WifiStep(WifiAction.NotInRange, backups[0]);
        }
        var pick = backups[_attempt % backups.Count];
        _attempt++;
        _lastTry = now;
        Joined = pick;
        return new WifiStep(WifiAction.Join, pick);
    }

    /// <summary>
    /// Windows' network list names a network "Maple Street WiFi 2" when the saved Wi-Fi profile is
    /// "Maple Street WiFi" (it numbers networks it has seen more than once), so compare without that number.
    /// </summary>
    public static bool Same(string? a, string? b)
    {
        if (a is null || b is null) return false;
        static bool Eq(string x, string y) => string.Equals(x.Trim(), y.Trim(), StringComparison.OrdinalIgnoreCase);
        // Only one extra number counts, so "iPhone 11" and "iPhone 12" stay different networks.
        return Eq(a, b) || Eq(DropNumber(a), b) || Eq(a, DropNumber(b));
    }

    private static string DropNumber(string name) => Regex.Replace(name.Trim(), @"\s+\d+$", "");

    /// <summary>The saved profile for a network name: an exact match first, then one that differs only by that number.</summary>
    public static string? Resolve(string network, IEnumerable<string> saved)
    {
        var list = saved.ToList();
        return list.FirstOrDefault(p => string.Equals(p, network, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(p => Same(p, network));
    }

    private static readonly Regex PhoneName = new(
        @"iphone|ipad|android|pixel|galaxy|hotspot|oneplus|motorola|\bmoto\b|redmi|xiaomi|huawei|nokia|['’]s phone|\bphone\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The backups, best first: the one you picked, else every saved network that looks like a phone.</summary>
    public static List<string> Backups(IEnumerable<string> savedProfiles, string? chosen)
    {
        var saved = savedProfiles.ToList();
        if (!string.IsNullOrWhiteSpace(chosen) && saved.Contains(chosen, StringComparer.OrdinalIgnoreCase)) return new List<string> { chosen };
        return saved.Where(p => PhoneName.IsMatch(p)).ToList();
    }
}
