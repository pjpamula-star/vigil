using System.Runtime.InteropServices;

namespace Vigil.Core;

internal static class Native
{
    // ---- Power requests ----
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct REASON_CONTEXT
    {
        public uint Version;
        public uint Flags;
        [MarshalAs(UnmanagedType.LPWStr)] public string SimpleReasonString;
    }

    public const uint POWER_REQUEST_CONTEXT_VERSION = 0;
    public const uint POWER_REQUEST_CONTEXT_SIMPLE_STRING = 0x1;

    public enum PowerRequestType
    {
        DisplayRequired = 0,
        SystemRequired = 1,
        AwayModeRequired = 2,
        ExecutionRequired = 3,
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr PowerCreateRequest(ref REASON_CONTEXT context);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool PowerSetRequest(IntPtr handle, PowerRequestType type);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool PowerClearRequest(IntPtr handle, PowerRequestType type);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [Flags]
    public enum ExecutionState : uint
    {
        SystemRequired = 0x00000001,
        Continuous = 0x80000000,
    }

    [DllImport("kernel32.dll")]
    public static extern ExecutionState SetThreadExecutionState(ExecutionState flags);

    // ---- Power schemes ----
    [DllImport("powrprof.dll")]
    public static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    public static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll")]
    public static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, out uint value);

    [DllImport("powrprof.dll")]
    public static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, out uint value);

    [DllImport("powrprof.dll")]
    public static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, uint value);

    [DllImport("powrprof.dll")]
    public static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid settingGuid, uint value);

    [DllImport("kernel32.dll")]
    public static extern IntPtr LocalFree(IntPtr mem);

    [DllImport("powrprof.dll", SetLastError = true)]
    public static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    [DllImport("powrprof.dll")]
    public static extern bool IsPwrHibernateAllowed();

    [DllImport("powrprof.dll")]
    private static extern bool GetPwrCapabilities(byte[] capabilities);

    /// <summary>
    /// Modern Standby ("S0 low power idle"): the laptop sleeps when its screen goes off and nothing holds it awake.
    /// Reads SYSTEM_POWER_CAPABILITIES.AoAc (byte 20).
    /// </summary>
    public static bool IsModernStandby()
    {
        try
        {
            var caps = new byte[256];
            return GetPwrCapabilities(caps) && caps[20] != 0;
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    /// <summary>How long since the last keyboard or mouse input, or null if Windows won't say.</summary>
    public static TimeSpan? IdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return null;
        var ms = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(ms);
    }

    // ---- Notifications ----
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid powerSettingGuid, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterPowerSettingNotification(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public byte Data;
    }

    public const int WM_POWERBROADCAST = 0x0218;
    public const int PBT_APMSUSPEND = 0x0004;
    public const int PBT_APMRESUMEAUTOMATIC = 0x0012;
    public const int PBT_POWERSETTINGCHANGE = 0x8013;
    public const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;

    public static readonly Guid GUID_LIDSWITCH_STATE_CHANGE = new("BA3E0F4D-B817-4094-A2D1-D56379E6A0F3");
    public static readonly Guid GUID_CONSOLE_DISPLAY_STATE = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    [DllImport("user32.dll")]
    public static extern bool LockWorkStation();

    // ---- Is this session locked right now? ----
    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQuerySessionInformationW(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    /// <summary>True if the Windows session is locked (lock screen showing). False if unknown.</summary>
    public static bool IsSessionLocked()
    {
        const int WTSSessionInfoEx = 25, WTS_CURRENT_SESSION = -1, WTS_SESSIONSTATE_LOCK = 0;
        try
        {
            if (!WTSQuerySessionInformationW(IntPtr.Zero, WTS_CURRENT_SESSION, WTSSessionInfoEx, out var buf, out _)) return false;
            try
            {
                // WTSINFOEX { DWORD Level; union { WTSINFOEX_LEVEL1 { ULONG SessionId; WTS_CONNECTSTATE_CLASS SessionState; LONG SessionFlags; ... } } }
                // The union holds LARGE_INTEGERs, so it is 8-byte aligned: SessionId at 8, SessionState at 12, SessionFlags at 16.
                // (Reading 12 gets SessionState, which is 0 = WTSActive for a normal session and looked like "locked".)
                var level = Marshal.ReadInt32(buf);
                if (level != 1) return false;
                var flags = Marshal.ReadInt32(buf, 16);
                return flags == WTS_SESSIONSTATE_LOCK;
            }
            finally
            {
                WTSFreeMemory(buf);
            }
        }
        catch
        {
            return false;
        }
    }

    // ---- Window helpers ----
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_BORDER_COLOR = 34;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const int DWMWCP_ROUND = 2;
    public const int DWMSBT_MAINWINDOW = 2;      // Mica
    public const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hwnd, int cmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? className, string windowName);
}
