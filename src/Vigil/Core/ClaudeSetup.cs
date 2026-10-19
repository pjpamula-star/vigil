using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vigil.Core;

/// <summary>
/// Connects Vigil to Claude Code by adding hooks to ~/.claude/settings.json.
/// Every write is preceded by a timestamped backup, and uninstall removes exactly what was added.
/// </summary>
public sealed class ClaudeSetup
{
    public const string Marker = "127.0.0.1:47823/hook";

    /// <summary>Events that change a session's state. Run inline (they're rare and order matters).</summary>
    public static readonly string[] SyncEvents =
        { "SessionStart", "UserPromptSubmit", "PermissionRequest", "Notification", "Stop", "StopFailure", "SubagentStart", "SubagentStop", "SessionEnd" };

    /// <summary>Tool events fire constantly. Run in the background so Claude never waits on Vigil.</summary>
    public static readonly string[] AsyncEvents = { "PreToolUse", "PostToolUse", "PostToolUseFailure" };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _settingsPath;
    private readonly string _backupDir;

    public ClaudeSetup(string settingsPath, string backupDir)
    {
        _settingsPath = settingsPath;
        _backupDir = backupDir;
    }

    /// <summary>
    /// cmd + curl start in a few milliseconds and always exit 0, so Claude never shows a hook error.
    /// The short connect timeout matters: Windows retries a refused localhost connection for ~2 s,
    /// which would slow Claude down whenever Vigil isn't running.
    /// cmd expands CLAUDE_CODE_BRIDGE_SESSION_ID, which Claude Code sets while Remote Control is
    /// connected, so Vigil learns each session's claude.ai link.
    /// </summary>
    public static JsonObject HookHandler(bool isAsync, int port = HookServer.DefaultPort)
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var curl = Path.Combine(system, "curl.exe");
        var handler = new JsonObject
        {
            ["type"] = "command",
            ["command"] = Path.Combine(system, "cmd.exe"),
            ["args"] = new JsonArray("/d", "/s", "/c",
                // No inner quotes: the spawner would escape them in a way cmd doesn't understand.
                $"{curl} -s -m 2 --connect-timeout 0.25 --json @- http://127.0.0.1:{port}/hook?rc=%CLAUDE_CODE_BRIDGE_SESSION_ID% >nul 2>&1 & exit /b 0"),
            ["timeout"] = 5,
        };
        if (isAsync) handler["async"] = true;
        return handler;
    }

    public bool IsInstalled()
    {
        var root = Read();
        if (root?["hooks"] is not JsonObject hooks) return false;
        return SyncEvents.Concat(AsyncEvents).All(e => hooks[e] is JsonArray groups && groups.Any(IsVigilGroup));
    }

    public bool RemoteControlAtStartup => Read()?["remoteControlAtStartup"]?.GetValue<bool>() == true;

    public void Install(int port = HookServer.DefaultPort)
    {
        var root = Read() ?? new JsonObject();
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        root["hooks"] = hooks;

        foreach (var evt in SyncEvents.Concat(AsyncEvents))
        {
            var groups = hooks[evt] as JsonArray ?? new JsonArray();
            hooks[evt] = groups;
            RemoveVigil(groups);
            groups.Add(new JsonObject
            {
                ["hooks"] = new JsonArray(HookHandler(AsyncEvents.Contains(evt), port)),
            });
        }
        Write(root);
        Log.Info("Installed Claude Code hooks");
    }

    public void Uninstall()
    {
        var root = Read();
        if (root?["hooks"] is not JsonObject hooks) return;
        foreach (var (name, value) in hooks.ToList())
        {
            if (value is not JsonArray groups) continue;
            RemoveVigil(groups);
            if (groups.Count == 0) hooks.Remove(name);
        }
        if (hooks.Count == 0) root.Remove("hooks");
        Write(root);
        Log.Info("Removed Claude Code hooks");
    }

    /// <summary>Turns on Remote Control for every new session (so the phone can see it).</summary>
    public void SetRemoteControlAtStartup(bool on)
    {
        var root = Read() ?? new JsonObject();
        if (on) root["remoteControlAtStartup"] = true;
        else root.Remove("remoteControlAtStartup");
        Write(root);
    }

    /// <summary>Tells Claude Code to skip phone pushes while this file exists (Vigil creates it while you're at the PC).</summary>
    public void SetPresenceFile(string? path)
    {
        var root = Read() ?? new JsonObject();
        var env = root["env"] as JsonObject ?? new JsonObject();
        if (path is null) env.Remove("CLAUDE_CLIENT_PRESENCE_FILE");
        else env["CLAUDE_CLIENT_PRESENCE_FILE"] = path;
        if (env.Count == 0) root.Remove("env");
        else root["env"] = env;
        Write(root);
    }

    public string? PresenceFile => Read()?["env"]?["CLAUDE_CLIENT_PRESENCE_FILE"]?.GetValue<string>();

    private static bool IsVigilGroup(JsonNode? group) =>
        group?["hooks"] is JsonArray handlers && handlers.Any(IsVigilHandler);

    private static bool IsVigilHandler(JsonNode? handler) =>
        handler is not null && handler.ToJsonString().Contains(Marker, StringComparison.Ordinal);

    private static void RemoveVigil(JsonArray groups)
    {
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            if (groups[i]?["hooks"] is not JsonArray handlers) continue;
            for (var j = handlers.Count - 1; j >= 0; j--)
                if (IsVigilHandler(handlers[j])) handlers.RemoveAt(j);
            if (handlers.Count == 0) groups.RemoveAt(i);
        }
    }

    private JsonObject? Read()
    {
        if (!File.Exists(_settingsPath)) return null;
        var text = File.ReadAllText(_settingsPath);
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
        // Refuse to touch a file we can't parse rather than risk clobbering it.
        return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
               ?? throw new InvalidOperationException("Claude settings.json isn't a JSON object.");
    }

    private void Write(JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        if (File.Exists(_settingsPath))
        {
            Directory.CreateDirectory(_backupDir);
            File.Copy(_settingsPath, Path.Combine(_backupDir, $"settings-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json"), overwrite: true);
            // Keep the 20 most recent backups.
            foreach (var old in new DirectoryInfo(_backupDir).GetFiles("settings-*.json").OrderByDescending(f => f.Name).Skip(20))
                old.Delete();
        }
        var tmp = _settingsPath + ".vigil.tmp";
        File.WriteAllText(tmp, root.ToJsonString(WriteOptions) + Environment.NewLine);
        File.Move(tmp, _settingsPath, overwrite: true);
    }
}
