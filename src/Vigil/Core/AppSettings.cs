namespace Vigil.Core;

internal sealed class AppSettings
{
    /// <summary>How long Vigil keeps the PC awake while a session waits on you.</summary>
    public int WaitForYouMinutes { get; set; } = 60;

    /// <summary>Keep awake while Claude waits for a usage limit to reset, up to this many hours (0 = never).</summary>
    public int UsageLimitWaitHours { get; set; } = 5;

    /// <summary>Minutes of silence after which a "working" session is treated as quiet.</summary>
    public int QuietAfterMinutes { get; set; } = 45;

    /// <summary>Grace period before letting the PC sleep once nothing needs it.</summary>
    public int ReleaseGraceMinutes { get; set; } = 2;

    public bool LockOnLidClose { get; set; } = true;

    /// <summary>When everything's done and the lid is closed: sleep right away (hibernate on battery), so it's bag-safe.</summary>
    public bool SleepWhenDoneLidClosed { get; set; } = true;
    public bool HeatProtection { get; set; } = true;
    public int EaseOffBatteryPercent { get; set; } = 20;
    public int HibernateBatteryPercent { get; set; } = 8;
    public int CpuCapPercent { get; set; } = 60;

    /// <summary>If the internet drops while Claude works, join a saved phone hotspot; go back when done.</summary>
    public bool BackupWifi { get; set; } = true;

    /// <summary>The saved network to use as the backup. Empty = any saved network that looks like a phone.</summary>
    public string BackupWifiProfile { get; set; } = "";

    /// <summary>The little status pill on the desktop: needs you / working / done.</summary>
    public bool DeskPill { get; set; } = true;

    /// <summary>Where you dragged the pill: its center, in screen units. -1 = docked in the taskbar's empty space.</summary>
    public double DeskPillAtX { get; set; } = -1;
    public double DeskPillAtY { get; set; } = -1;

    /// <summary>Docked: where in the taskbar you dropped it (its right edge, in screen units). -1 = the middle of the free space.</summary>
    public double DeskPillHomeX { get; set; } = -1;

    public bool ShowWelcomeBack { get; set; } = true;
    public int WelcomeBackMinAwayMinutes { get; set; } = 1;
    public bool AiHeadlines { get; set; } = true;

    /// <summary>Optional: ntfy.sh topic for short phone notes. Empty = off.</summary>
    public string NtfyTopic { get; set; } = "";

    /// <summary>"Vigil on your phone": the shared secret from the pairing QR code. Empty = not set up.</summary>
    public string PhoneKey { get; set; } = "";

    /// <summary>Vigil has received an event from Codex, so its hooks are approved and working.</summary>
    public bool CodexSeen { get; set; }
    public string PhoneServer { get; set; } = "https://vigil-phone.vercel.app";
    public bool PhonePaired { get; set; }

    /// <summary>Only buzz the phone while you're away (locked or lid closed).</summary>
    public bool PhoneOnlyWhenAway { get; set; } = true;

    /// <summary>The eye blinks now and then, glances around and watches the mouse.</summary>
    public bool EyeAlive { get; set; } = true;

    /// <summary>Tell the phone when the PC goes to sleep after Claude finishes.</summary>
    public bool PhoneNotifySleep { get; set; } = true;

    public bool FirstRunDone { get; set; }

    /// <summary>What "Welcome back, ___" calls you. Empty = your Git name, then your Windows name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Demo/screenshot mode: never write to disk.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Ephemeral { get; set; }

    public static AppSettings Load() => Json.Load<AppSettings>(Paths.Settings) ?? new AppSettings();

    public void Save()
    {
        if (Ephemeral) return;
        try { Json.SaveAtomic(Paths.Settings, this); }
        catch (Exception ex) { Log.Error("Saving settings failed", ex); }
    }
}
