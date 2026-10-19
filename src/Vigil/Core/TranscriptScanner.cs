using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vigil.Core;

/// <summary>Pulls the session title Claude generated and any Remote Control link from a transcript file.</summary>
public static class TranscriptScanner
{
    private static readonly Regex SessionUrl = new(@"https://claude\.ai/code/(session_[A-Za-z0-9]+)", RegexOptions.Compiled);

    public static (string? title, string? url) Scan(string? transcriptPath)
    {
        if (string.IsNullOrEmpty(transcriptPath) || !File.Exists(transcriptPath)) return (null, null);
        try
        {
            using var fs = new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // Only the tail matters, and transcripts can be huge.
            const long window = 4 * 1024 * 1024;
            if (fs.Length > window) fs.Seek(-window, SeekOrigin.End);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            string? title = null, url = null;
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.Contains("\"ai-title\"", StringComparison.Ordinal))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        title = doc.RootElement.Str("aiTitle") ?? title;
                    }
                    catch (JsonException) { }
                }
                if (line.Contains("claude.ai/code/session_", StringComparison.Ordinal))
                {
                    var m = SessionUrl.Matches(line);
                    if (m.Count > 0) url = m[^1].Value;
                }
            }
            return (title, url);
        }
        catch (Exception ex)
        {
            Log.Warn($"Couldn't scan transcript: {ex.Message}");
            return (null, null);
        }
    }

    /// <summary>
    /// Whether the last message in a transcript is Claude Code's note that you pressed Esc
    /// ("[Request interrupted by user]", or "... for tool use"). Claude Code sends no Stop hook then.
    /// Bookkeeping records written after it (cost, title, turn length) are skipped.
    /// </summary>
    public static bool EndsWithInterrupt(string? transcriptPath)
    {
        if (string.IsNullOrEmpty(transcriptPath) || !File.Exists(transcriptPath)) return false;
        try
        {
            // Asked every few seconds while a session is silent: only read again once the file changes.
            var info = new FileInfo(transcriptPath);
            var stamp = (info.LastWriteTimeUtc, info.Length);
            lock (InterruptCache)
                if (InterruptCache.TryGetValue(transcriptPath, out var hit) && hit.Stamp == stamp) return hit.Result;
            var result = ReadEndsWithInterrupt(transcriptPath);
            lock (InterruptCache)
            {
                if (InterruptCache.Count > 200) InterruptCache.Clear();
                InterruptCache[transcriptPath] = (stamp, result);
            }
            return result;
        }
        catch (Exception ex)
        {
            Log.Warn($"Couldn't read transcript tail: {ex.Message}");
            return false;
        }
    }

    private static readonly Dictionary<string, ((DateTime, long) Stamp, bool Result)> InterruptCache = new(StringComparer.OrdinalIgnoreCase);

    private static bool ReadEndsWithInterrupt(string transcriptPath)
    {
        using var fs = new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        const long window = 256 * 1024;
        if (fs.Length > window) fs.Seek(-window, SeekOrigin.End);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        var lines = reader.ReadToEnd().Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || !(line.Contains("\"type\":\"user\"", StringComparison.Ordinal) || line.Contains("\"type\":\"assistant\"", StringComparison.Ordinal)))
                continue;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch (JsonException) { continue; } // the first line of the window is usually cut off
            using (doc)
            {
                var r = doc.RootElement;
                var type = r.Str("type");
                if (type is not ("user" or "assistant")) continue;
                if (r.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True) continue;
                if (r.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True) continue;
                return type == "user" && IsInterruptNote(r);
            }
        }
        return false;
    }

    private static bool IsInterruptNote(JsonElement record)
    {
        if (!record.TryGetProperty("message", out var m) || !m.TryGetProperty("content", out var c)) return false;
        const string note = "[Request interrupted by user";
        if (c.ValueKind == JsonValueKind.String) return c.GetString()!.StartsWith(note, StringComparison.Ordinal);
        if (c.ValueKind != JsonValueKind.Array) return false;
        foreach (var part in c.EnumerateArray())
            if (part.Str("type") == "text" && part.Str("text")?.StartsWith(note, StringComparison.Ordinal) == true) return true;
        return false;
    }

    /// <summary>
    /// From the start of a transcript: whether it's a background run (its first prompt is tagged
    /// "sessionKind":"bg"), and the folder it started in. Background is null until the first prompt is written.
    /// </summary>
    public static (bool? background, string? startCwd) Head(string? transcriptPath)
    {
        if (string.IsNullOrEmpty(transcriptPath) || !File.Exists(transcriptPath)) return (null, null);
        try
        {
            using var fs = new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            string? cwd = null, line;
            long read = 0;
            while ((line = reader.ReadLine()) is not null && read < 2 * 1024 * 1024)
            {
                read += line.Length;
                if (cwd is null && line.Contains("\"cwd\":", StringComparison.Ordinal))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        cwd = doc.RootElement.Str("cwd");
                    }
                    catch (JsonException) { }
                }
                if (line.Contains("\"type\":\"user\"", StringComparison.Ordinal))
                    return (line.Contains("\"sessionKind\":\"bg\"", StringComparison.Ordinal), cwd);
            }
            return (null, cwd);
        }
        catch (Exception ex)
        {
            Log.Warn($"Couldn't read transcript start: {ex.Message}");
            return (null, null);
        }
    }

    public static string? UrlFromBridgeId(string? id) =>
        id is { Length: > 0 } && id.StartsWith("session_", StringComparison.Ordinal) ? $"https://claude.ai/code/{id}" : null;
}
