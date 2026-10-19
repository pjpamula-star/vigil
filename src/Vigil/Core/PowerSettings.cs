namespace Vigil.Core;

/// <summary>Power plan settings Vigil may touch. All of them are restored from the journal.</summary>
public static class PowerSettings
{
    public static readonly Guid SubButtons = new("4f971e89-eebd-4455-a8de-9e59040e7347");
    public static readonly Guid LidAction = new("5ca83367-6e45-459f-a27b-476b1d01c936");

    public static readonly Guid SubSleep = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    public static readonly Guid StandbyIdle = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");
    public static readonly Guid HibernateIdle = new("9d7815a6-7ee4-497e-8888-515a05f02364");

    public static readonly Guid SubProcessor = new("54533251-82be-4824-96c1-47b60b740d00");
    public static readonly Guid ProcThrottleMax = new("bc5038f7-23e0-4960-96da-33abaf5935ec");

    public sealed record Setting(string Name, Guid Sub, Guid Id);

    public static readonly Setting LidSetting = new("Lid close action", SubButtons, LidAction);
    public static readonly Setting SleepAfter = new("Sleep after", SubSleep, StandbyIdle);
    public static readonly Setting HibernateAfter = new("Hibernate after", SubSleep, HibernateIdle);
    public static readonly Setting CpuMax = new("Maximum processor state", SubProcessor, ProcThrottleMax);

    /// <summary>What "stay awake" changes: never idle-sleep, never idle-hibernate, lid does nothing.</summary>
    public static readonly (Setting setting, uint value)[] KeepAwakeOverrides =
    {
        (SleepAfter, 0),
        (HibernateAfter, 0),
        (LidSetting, 0),
    };
}
