using System.Windows.Threading;

namespace Vigil.Core;

/// <summary>
/// Runs <see cref="BackupWifiPolicy"/> in the background: every 10 seconds while Vigil is keeping the PC
/// awake (and while it's on the hotspot), checks the internet and switches networks if needed.
/// Nothing is checked, and nothing goes over the network, while the PC isn't being kept awake.
/// </summary>
internal sealed class BackupWifiMonitor : IDisposable
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(10);

    private readonly Dispatcher _ui;
    private readonly AppSettings _settings;
    private readonly Func<bool> _needed;
    private readonly BackupWifiPolicy _policy = new();
    private readonly CancellationTokenSource _stop = new();
    private string? _announce;

    /// <summary>A line for Vigil's log, and whether it's worth a toast.</summary>
    public event Action<string, bool>? Said;

    /// <summary>Back online through the hotspot: tell the phone.</summary>
    public event Action<PhoneMessage>? Note;

    public BackupWifiMonitor(Dispatcher ui, AppSettings settings, Func<bool> needed)
    {
        _ui = ui;
        _settings = settings;
        _needed = needed;
    }

    public void Start() => _ = Task.Run(LoopAsync);

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try { await Task.Delay(Every, _stop.Token); }
            catch (OperationCanceledException) { return; }
            try { await CheckAsync(); }
            catch (Exception ex) { Log.Warn($"Backup Wi-Fi: {ex.Message}"); }
        }
    }

    private List<string> _backups = new();

    private async Task CheckAsync()
    {
        if (!_settings.BackupWifi) return;
        var needed = await _ui.InvokeAsync(_needed);
        if (!needed && _policy.Joined is null && !_policy.Returning) return;

        var online = await Wifi.OnlineAsync();
        // Only the network's name while online (to know where home is); the saved list only when it matters.
        var connected = online ? Wifi.ConnectedNetworkName() : null;
        if (!online || _policy.Joined is not null || _policy.Returning)
            _backups = BackupWifiPolicy.Backups(Wifi.SavedProfiles(), _settings.BackupWifiProfile);
        var step = _policy.Step(needed, online, connected, _backups, DateTime.Now);
        var look = (Connected: connected, Online: online);

        if (look.Online && _announce is { } joined && Same(look.Connected, joined))
        {
            _announce = null;
            var note = new PhoneMessage("Your PC is on your hotspot",
                $"Wi-Fi dropped while Claude was working, so Vigil joined {joined}. It goes back to {_policy.Home ?? "your Wi-Fi"} when Claude is done.",
                null, false, "wifi");
            _ = _ui.BeginInvoke(() => Note?.Invoke(note));
        }

        switch (step.Action)
        {
            case WifiAction.Join:
                Log.Info($"Backup Wi-Fi: offline while needed; joining '{step.Profile}'");
                if (Wifi.Connect(step.Profile!))
                {
                    _announce = step.Profile;
                    Say($"The internet dropped while Claude was working, so Vigil joined {step.Profile}.", toast: true);
                }
                else Say($"The internet dropped, and Windows wouldn't join {step.Profile}. Vigil will try again in a minute.", toast: false);
                break;
            case WifiAction.NotInRange:
                Say($"The internet dropped while Claude was working, and Vigil couldn't reach {step.Profile}. On iPhone, open Settings › Personal Hotspot and turn on Allow Others to Join; Vigil keeps trying.", toast: true);
                break;
            case WifiAction.Return:
                Log.Info($"Backup Wi-Fi: work done; back to '{step.Profile}'");
                // The network list's name for home may carry a number ("… 2"); connect by the saved profile's name.
                var home = BackupWifiPolicy.Resolve(step.Profile!, Wifi.SavedProfiles()) ?? step.Profile!;
                if (Wifi.Connect(home)) Say($"Claude is done, so Vigil left the hotspot and went back to {home}.", toast: false);
                break;
        }
    }

    private void Say(string text, bool toast) => _ui.BeginInvoke(() => Said?.Invoke(text, toast));

    private static bool Same(string? a, string? b) => BackupWifiPolicy.Same(a, b);

    public void Dispose() => _stop.Cancel();
}
