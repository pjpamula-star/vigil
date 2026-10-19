namespace Vigil.Core;

/// <summary>Every file Vigil reads or writes lives under %LOCALAPPDATA%\Vigil.</summary>
internal static class Paths
{
    public static string Root { get; set; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vigil");

    public static string Settings => Combine("settings.json");
    public static string Journal => Combine("power-journal.json");
    public static string State => Combine("sessions.json");
    public static string History => Combine("history.jsonl");
    public static string Log => Combine("vigil.log");
    public static string PresenceFile => Combine("present.marker");
    public static string Away => Combine("away.json");
    public static string SummarizerDir => Combine("summarizer");
    public static string WebViewData => Combine("webview");
    public static string ClaudeSettingsBackupDir => Combine("claude-settings-backups");

    public static string ClaudeHome =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    public static string ClaudeSettings => System.IO.Path.Combine(ClaudeHome, "settings.json");

    /// <summary>Codex's home: CODEX_HOME if set, else ~/.codex.</summary>
    public static string CodexHome =>
        Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } home
            ? home
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

    public static string Combine(string name)
    {
        Directory.CreateDirectory(Root);
        return System.IO.Path.Combine(Root, name);
    }
}
