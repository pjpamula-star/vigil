using System.Diagnostics;
using System.Text.Json;

namespace Vigil.Core;

/// <summary>What Claude Code itself says about one of its sessions.</summary>
/// <param name="Status">"busy" while a turn runs, "waiting" while a prompt or dialog is open, "idle" or "shell" otherwise.</param>
/// <param name="Alive">The Claude process that wrote it is still running.</param>
public sealed record LiveClaude(string SessionId, string Status, DateTime StatusAt, bool Alive)
{
    public int Pid { get; init; }
    public long? ProcStart { get; init; }
}

/// <summary>
/// Reads the list of running sessions that Claude Code keeps in ~/.claude/sessions (one file per process).
/// Hooks miss two things: Claude sends no Stop when you press Esc, and nothing at all when its window is
/// closed. This list catches both. It isn't a documented interface, so Vigil only ever uses it to end
/// something that has already stopped, never to start anything, and anything unreadable counts as unknown.
/// </summary>
public static class ClaudeRegistry
{
    public static string Dir => Path.Combine(Paths.ClaudeHome, "sessions");

    public static IReadOnlyDictionary<string, LiveClaude> Read(string dir, Func<int, long?, bool>? isAlive = null)
    {
        isAlive ??= ProcessAlive;
        var found = new Dictionary<string, LiveClaude>();
        string[] files;
        try { files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json") : Array.Empty<string>(); }
        catch { return found; }

        foreach (var file in files)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var r = doc.RootElement;
                if (r.ValueKind != JsonValueKind.Object) continue;
                var id = r.Str("sessionId");
                if (id is null || !r.TryGetProperty("pid", out var pidEl) || !pidEl.TryGetInt32(out var pid)) continue;
                long? start = long.TryParse(r.Str("procStart"), out var ps) ? ps : null;
                var status = r.Str("status") ?? "";
                var at = r.TryGetProperty("statusUpdatedAt", out var atEl) && atEl.TryGetInt64(out var ms)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime
                    : DateTime.MinValue;
                var entry = new LiveClaude(id, status, at, isAlive(pid, start)) { Pid = pid, ProcStart = start };

                // The same session can have a file from an old window next to the one it was resumed in.
                if (!found.TryGetValue(id, out var other) || Better(entry, other)) found[id] = entry;
            }
            catch
            {
                // Half-written or unreadable: skip it this time.
            }
        }
        return found;
    }

    private static bool Better(LiveClaude a, LiveClaude b) =>
        a.Alive != b.Alive ? a.Alive : a.StatusAt > b.StatusAt;

    /// <summary>The process is running, and is the same one (Windows reuses process ids).</summary>
    internal static bool ProcessAlive(int pid, long? startFileTime)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.HasExited) return false;
            if (startFileTime is null) return true;
            try { return p.StartTime.ToFileTimeUtc() == startFileTime; }
            catch { return true; } // can't read its start time: it exists, so assume it's the one
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Reads Claude's list, and remembers which process each session ran in. Claude Code tidies away the file
/// a closed window left behind, sometimes within seconds, so a session that drops off the list is checked
/// by its last known process: gone means closed; still running means it's exiting cleanly (its SessionEnd
/// hook covers that).
/// </summary>
public sealed class ClaudeProcesses
{
    private readonly Dictionary<string, (LiveClaude Last, DateTime SeenAt)> _seen = new();

    public IReadOnlyDictionary<string, LiveClaude> Read(string dir, Func<int, long?, bool>? isAlive = null)
    {
        isAlive ??= ClaudeRegistry.ProcessAlive;
        var now = DateTime.Now;
        var found = new Dictionary<string, LiveClaude>(ClaudeRegistry.Read(dir, isAlive));
        foreach (var (id, entry) in found) _seen[id] = (entry, now);

        foreach (var (id, (last, _)) in _seen.ToList())
        {
            if (found.ContainsKey(id)) continue;
            if (!last.Alive || !isAlive(last.Pid, last.ProcStart)) found[id] = last with { Alive = false };
            else _seen.Remove(id);
        }
        // Forget sessions not seen for a day.
        foreach (var id in _seen.Where(e => now - e.Value.SeenAt > TimeSpan.FromDays(1)).Select(e => e.Key).ToList())
            _seen.Remove(id);
        return found;
    }
}
