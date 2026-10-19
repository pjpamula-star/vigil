using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Vigil.Core;

/// <summary>
/// Windows' own Wi-Fi API (wlanapi.dll): saved networks, and connecting to a saved one. Used instead of
/// netsh so no console window ever flashes, and network names with curly apostrophes ("Sam’s iPhone")
/// come through intact. Deliberately never lists nearby networks: Windows treats that as location data.
/// </summary>
public static class Wifi
{
    /// <summary>Saved Wi-Fi networks on the first Wi-Fi adapter, in Windows' preference order.</summary>
    public static List<string> SavedProfiles()
    {
        var result = new List<string>();
        WithInterface((h, id) =>
        {
            if (WlanGetProfileList(h, ref id, IntPtr.Zero, out var list) != 0) return;
            try
            {
                var n = Marshal.ReadInt32(list);
                for (var i = 0; i < n; i++)
                {
                    // WLAN_PROFILE_INFO: WCHAR strProfileName[256]; DWORD dwFlags  (516 bytes)
                    var name = Marshal.PtrToStringUni(list + 8 + i * 516);
                    if (!string.IsNullOrEmpty(name)) result.Add(name);
                }
            }
            finally { WlanFreeMemory(list); }
        });
        return result;
    }

    /// <summary>
    /// The name of the network we're on, from Windows' Network List Manager (the same names the
    /// network icon shows). Unlike the Wi-Fi scan APIs, this isn't location data and needs no permission.
    /// </summary>
    public static string? ConnectedNetworkName()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"));
            if (type is null) return null;
            dynamic nlm = Activator.CreateInstance(type)!;
            foreach (dynamic network in nlm.GetNetworks(1)) // NLM_ENUM_NETWORK_CONNECTED
            {
                string name = network.GetName();
                if (!string.IsNullOrEmpty(name)) return name;
            }
        }
        catch (Exception ex) { Log.Warn($"Network name: {ex.Message}"); }
        return null;
    }

    /// <summary>Connect to a saved network. Returns once Windows accepted the request, not when it's online.</summary>
    public static bool Connect(string profile)
    {
        var ok = false;
        WithInterface((h, id) =>
        {
            var p = new ConnectionParameters
            {
                Mode = 0,               // wlan_connection_mode_profile
                Profile = profile,
                BssType = 1,            // infrastructure
            };
            ok = WlanConnect(h, ref id, ref p, IntPtr.Zero) == 0;
        });
        return ok;
    }

    /// <summary>Can we actually reach the internet? The same check Windows uses for its globe icon.</summary>
    public static async Task<bool> OnlineAsync()
    {
        if (!NetworkInterface.GetIsNetworkAvailable()) return false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            var text = await Http.GetStringAsync("http://www.msftconnecttest.com/connecttest.txt", cts.Token);
            return text.StartsWith("Microsoft Connect Test", StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private static void WithInterface(Action<IntPtr, Guid> action)
    {
        if (WlanOpenHandle(2, IntPtr.Zero, out _, out var h) != 0) return;
        try
        {
            if (WlanEnumInterfaces(h, IntPtr.Zero, out var list) != 0) return;
            Guid id;
            try
            {
                if (Marshal.ReadInt32(list) == 0) return; // no Wi-Fi adapter
                id = Marshal.PtrToStructure<Guid>(list + 8);
            }
            finally { WlanFreeMemory(list); }
            action(h, id);
        }
        finally { WlanCloseHandle(h, IntPtr.Zero); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ConnectionParameters
    {
        public int Mode;
        [MarshalAs(UnmanagedType.LPWStr)] public string Profile;
        public IntPtr Ssid;
        public IntPtr BssidList;
        public int BssType;
        public uint Flags;
    }

    [DllImport("wlanapi.dll")] private static extern int WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")] private static extern int WlanCloseHandle(IntPtr handle, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern int WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern int WlanGetProfileList(IntPtr handle, ref Guid iface, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern int WlanConnect(IntPtr handle, ref Guid iface, ref ConnectionParameters p, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr memory);
}
