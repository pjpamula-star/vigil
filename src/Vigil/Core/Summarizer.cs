using System.Diagnostics;

namespace Vigil.Core;

/// <summary>
/// Writes the one-line headline for a finished turn using Claude's fastest model (about 3 seconds).
/// Runs Claude in an empty folder with only project settings, so Vigil's own hooks don't fire for it.
/// Falls back to the first sentence of Claude's message if anything goes wrong.
/// </summary>
public sealed class Summarizer
{
    private const string SystemPrompt =
        "You summarize a coding agent's finished work for a busy developer glancing at a status panel. " +
        "Output ONE plain sentence, at most 20 words: what got done and where things stand now. " +
        "Always keep: failures, failing tests, anything left unfinished, and any question for the user. " +
        "Use past tense for completed work. Do not start with or repeat the project name; the app already shows it. " +
        "No preamble, no quotes, no markdown, no emoji.";

    private readonly SemaphoreSlim _one = new(1, 1);
    private readonly string _workDir;

    public bool Enabled { get; set; } = true;
    public string? ClaudeExe { get; set; } = FindClaude();

    /// <summary>claude.exe from PATH, or the default install location.</summary>
    public static string? FindClaude()
    {
        var candidates = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(d => Path.Combine(d.Trim('"'), "claude.exe"))
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(45);

    public Summarizer(string workDir)
    {
        _workDir = workDir;
        Directory.CreateDirectory(workDir);
    }

    public static string Fallback(Turn turn)
    {
        if (turn.EndNote is { } note) return $"{note}.";
        var first = Text.FirstSentence(turn.LastMessage, 140);
        return first.Length > 0 ? first : turn.Outcome switch
        {
            SessionState.Failed => "Stopped with an error.",
            SessionState.Paused => "Paused at the usage limit.",
            _ => "Finished.",
        };
    }

    internal static string BuildInput(Session session, Turn turn)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PROJECT: {session.FolderName}");
        if (!string.IsNullOrWhiteSpace(turn.Prompt)) sb.AppendLine($"USER ASKED: {Text.Clip(turn.Prompt, 600)}");
        var f = turn.Facts;
        var facts = new List<string>();
        if (f.Files.Count > 0) facts.Add($"changed {f.Files.Count} file(s): {string.Join(", ", f.Files.Select(ToolInterpreter.ShortPath).Distinct().Take(8))}");
        if (f.TestsFailed is > 0) facts.Add($"{f.TestsFailed} tests failing");
        else if (f.TestsFailed == Facts.UnknownCount) facts.Add("tests failing");
        else if (f.TestsPassed is > 0) facts.Add($"{f.TestsPassed} tests passing");
        if (f.Commits.Count > 0) facts.Add($"committed: {string.Join("; ", f.Commits.Take(3))}");
        if (f.Pushes > 0) facts.Add("pushed");
        if (f.Deploys > 0) facts.Add("deployed");
        if (f.Installs.Count > 0) facts.Add($"installed {string.Join(", ", f.Installs.Distinct())}");
        if (f.Deletes.Count > 0) facts.Add($"deleted {string.Join(", ", f.Deletes.Distinct())}");
        if (f.Errors > 0) facts.Add($"{f.Errors} error(s) along the way");
        if (facts.Count > 0) sb.AppendLine($"FACTS: {string.Join("; ", facts)}");
        sb.AppendLine($"OUTCOME: {turn.Outcome}");
        var msg = turn.LastMessage ?? "";
        if (msg.Length > 6000) msg = msg[..3000] + "\n…\n" + msg[^3000..];
        sb.AppendLine("CLAUDE'S FINAL MESSAGE:");
        sb.AppendLine(msg);
        return sb.ToString();
    }

    public async Task<string> SummarizeAsync(Session session, Turn turn)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(turn.LastMessage) && turn.Facts.IsEmpty)
            return Fallback(turn);

        // Short replies don't need a model: they already are the summary.
        var plain = Text.Collapse(turn.LastMessage ?? "");
        if (plain.Length <= 140 && turn.Facts.IsEmpty) return Text.Clip(plain, 140);

        await _one.WaitAsync();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ClaudeExe ?? throw new FileNotFoundException("claude.exe not found"),
                WorkingDirectory = _workDir,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardInputEncoding = new UTF8Encoding(false),
            };
            foreach (var a in new[]
                     {
                         "-p", "--model", "haiku",
                         "--setting-sources", "project", "--no-session-persistence", "--strict-mcp-config",
                         "--tools", "", "--system-prompt", SystemPrompt, "Summarize this:",
                     })
                psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Couldn't start claude");
            await proc.StandardInput.WriteAsync(BuildInput(session, turn));
            proc.StandardInput.Close();

            var output = proc.StandardOutput.ReadToEndAsync();
            var wait = proc.WaitForExitAsync();
            if (await Task.WhenAny(wait, Task.Delay(Timeout)) != wait)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                Log.Warn("Summary timed out");
                return Fallback(turn);
            }
            var text = Text.Collapse((await output).Trim().Trim('"'));
            if (proc.ExitCode != 0 || text.Length == 0 || text.Length > 300 || LooksLikeRefusal(text))
            {
                Log.Warn($"Summary unusable (exit {proc.ExitCode}): {Text.Clip(text, 120)}");
                return Fallback(turn);
            }
            return text;
        }
        catch (Exception ex)
        {
            Log.Warn($"Summary failed: {ex.Message}");
            return Fallback(turn);
        }
        finally
        {
            _one.Release();
        }
    }

    private static bool LooksLikeRefusal(string s) =>
        s.StartsWith("I can't", StringComparison.OrdinalIgnoreCase) ||
        s.StartsWith("I cannot", StringComparison.OrdinalIgnoreCase) ||
        s.Contains("Please provide", StringComparison.OrdinalIgnoreCase);
}
