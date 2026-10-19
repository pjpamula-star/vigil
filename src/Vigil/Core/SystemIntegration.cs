using System.Diagnostics;
using Microsoft.Win32;

namespace Vigil.Core;

/// <summary>Start with Windows via the per-user Run key (no admin).</summary>
public static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "Vigil";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is string;
        }
    }

    public static void Set(bool on, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(Name, $"\"{exePath}\" --background");
        else key.DeleteValue(Name, throwOnMissingValue: false);
    }
}

/// <summary>
/// Belt and braces: a per-user scheduled task that runs every 10 minutes and restores the power plan
/// if Vigil is gone but its journal is still there (e.g. Vigil was killed). No admin needed.
/// </summary>
public static class Watchdog
{
    public const string TaskName = "Vigil power-settings watchdog";

    public static void Install(string exePath)
    {
        Run("schtasks.exe", "/Create", "/F", "/TN", TaskName, "/SC", "MINUTE", "/MO", "10",
            "/TR", $"\"{exePath}\" --restore-if-orphaned");
    }

    public static void Uninstall() => Run("schtasks.exe", "/Delete", "/F", "/TN", TaskName);

    public static bool IsInstalled() => Run("schtasks.exe", "/Query", "/TN", TaskName) == 0;

    private static int Run(string file, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            p.WaitForExit(15000);
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            Log.Warn($"{file} failed: {ex.Message}");
            return -1;
        }
    }
}

/// <summary>Optional short notes to the ntfy phone app. Off unless a topic is set.</summary>
public static class PhoneNotes
{
    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static async Task SendAsync(string topic, string title, string message, string priority = "default")
    {
        if (string.IsNullOrWhiteSpace(topic)) return;
        try
        {
            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"https://ntfy.sh/{Uri.EscapeDataString(topic.Trim())}")
            {
                Content = new System.Net.Http.StringContent(message, Encoding.UTF8),
            };
            req.Headers.Add("Title", title);
            req.Headers.Add("Priority", priority);
            req.Headers.Add("Tags", "crescent_moon");
            await Http.SendAsync(req);
        }
        catch (Exception ex)
        {
            Log.Warn($"Phone note failed: {ex.Message}");
        }
    }
}

/// <summary>Things worth telling you about later (safety actions, sleeps), shown in Welcome back.</summary>
public enum ActivityKind { Info, Awake, Sleep, Session, NeedsYou, Done, Lid, You, Warning }

public sealed class HistoryEvent
{
    public DateTime At { get; set; }
    public string Text { get; set; } = "";
    public bool Important { get; set; }
    public ActivityKind Kind { get; set; } = ActivityKind.Info;
}

public static class History
{
    public static event Action? Added;

    public static void Add(string text, bool important = false, ActivityKind kind = ActivityKind.Info)
    {
        try
        {
            if (important && kind == ActivityKind.Info) kind = ActivityKind.Warning;
            var e = new HistoryEvent { At = DateTime.Now, Text = text, Important = important, Kind = kind };
            File.AppendAllText(Paths.History, System.Text.Json.JsonSerializer.Serialize(e, Json.Options).ReplaceLineEndings("") + Environment.NewLine);
            Added?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Warn($"History write failed: {ex.Message}");
        }
    }

    public static List<HistoryEvent> Since(DateTime since)
    {
        var list = new List<HistoryEvent>();
        try
        {
            if (!File.Exists(Paths.History)) return list;
            foreach (var line in File.ReadLines(Paths.History).Reverse().Take(500))
            {
                try
                {
                    var e = System.Text.Json.JsonSerializer.Deserialize<HistoryEvent>(line, Json.Options);
                    if (e is not null && e.At >= since) list.Add(e);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"History read failed: {ex.Message}");
        }
        list.Reverse();
        return list;
    }
}
