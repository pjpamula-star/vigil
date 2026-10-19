using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vigil.Core;

/// <summary>
/// Connects Vigil to OpenAI's Codex CLI by adding hooks to ~/.codex/hooks.json. Codex's hooks carry the
/// same events and fields as Claude Code's, so the same tracker reads both.
///
/// Codex only runs a hook after you approve it (type /hooks in Codex), and it records that approval
/// against a fingerprint of the hook. Vigil leaves that to you: approving is the point of the check.
/// Every write is preceded by a backup, and Disconnect removes exactly what was added.
/// </summary>
public sealed class CodexSetup
{
    public const string Marker = "/hook/codex";

    public static readonly string[] SyncEvents =
        { "SessionStart", "UserPromptSubmit", "PermissionRequest", "Stop", "Interrupt", "SubagentStart", "SubagentStop", "SessionEnd" };

    public static readonly string[] AsyncEvents = { "PreToolUse", "PostToolUse" };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _home;
    private readonly string _backupDir;

    public CodexSetup(string codexHome, string backupDir)
    {
        _home = codexHome;
        _backupDir = backupDir;
    }

    private string HooksPath => Path.Combine(_home, "hooks.json");

    /// <summary>Codex has been run on this PC (it creates its home folder on first run).</summary>
    public bool Available => Directory.Exists(_home);

    /// <summary>
    /// Codex runs a hook's command through cmd /C on Windows. curl starts in milliseconds; the output is
    /// thrown away (Codex reads a Stop hook's stdout as JSON) and the exit code is always 0, so Codex
    /// never shows a hook error, even when Vigil isn't running.
    /// </summary>
    public static JsonObject HookHandler(string evt, int port = HookServer.DefaultPort)
    {
        var curl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe");
        var handler = new JsonObject
        {
            ["type"] = "command",
            ["command"] = $"{curl} -s -m 2 --connect-timeout 0.25 --json @- http://127.0.0.1:{port}{Marker} >nul 2>&1 & exit /b 0",
            // SessionEnd and Interrupt allow at most 3 seconds.
            ["timeout"] = evt is "SessionEnd" or "Interrupt" ? 3 : 5,
        };
        if (AsyncEvents.Contains(evt)) handler["async"] = true;
        return handler;
    }

    public bool IsInstalled()
    {
        if (Read()?["hooks"] is not JsonObject hooks) return false;
        return SyncEvents.Concat(AsyncEvents).All(e => hooks[e] is JsonArray groups && groups.Any(IsVigilGroup));
    }

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
            groups.Add(new JsonObject { ["hooks"] = new JsonArray(HookHandler(evt, port)) });
        }
        Write(root);
        Log.Info("Installed Codex hooks");
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
        // Leave no empty file behind if Vigil's hooks were all it held.
        if (root.Count == 0 && File.Exists(HooksPath))
        {
            Backup();
            File.Delete(HooksPath);
        }
        else
        {
            Write(root);
        }
        Log.Info("Removed Codex hooks");
    }

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
        if (!File.Exists(HooksPath)) return null;
        var text = File.ReadAllText(HooksPath);
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
        // Refuse to touch a file we can't parse rather than risk clobbering it.
        return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
               ?? throw new InvalidOperationException("Codex hooks.json isn't a JSON object.");
    }

    private void Backup()
    {
        if (!File.Exists(HooksPath)) return;
        Directory.CreateDirectory(_backupDir);
        File.Copy(HooksPath, Path.Combine(_backupDir, $"codex-hooks-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json"), overwrite: true);
        foreach (var old in new DirectoryInfo(_backupDir).GetFiles("codex-hooks-*.json").OrderByDescending(f => f.Name).Skip(20))
            old.Delete();
    }

    private void Write(JsonObject root)
    {
        Directory.CreateDirectory(_home);
        Backup();
        var tmp = HooksPath + ".vigil.tmp";
        File.WriteAllText(tmp, root.ToJsonString(WriteOptions) + Environment.NewLine);
        File.Move(tmp, HooksPath, overwrite: true);
    }
}
