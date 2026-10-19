using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vigil.Core;

/// <summary>
/// Turns raw tool calls into plain-English lines and hard facts.
/// Deterministic on purpose: no model is involved, so nothing here can be made up.
/// </summary>
internal static class ToolInterpreter
{
    public static string NowLine(string tool, JsonElement input)
    {
        string File(string key = "file_path") => ShortPath(input.Str(key) ?? input.Str("notebook_path") ?? input.Str("path"));

        switch (tool)
        {
            case "Edit":
            case "MultiEdit":
                return $"Editing {File()}";
            case "apply_patch": // Codex
            {
                var files = PatchFiles(input.Str("command"));
                return files.Count switch { 0 => "Editing files", 1 => $"Editing {ShortPath(files[0])}", _ => $"Editing {files.Count} files" };
            }
            case "update_plan": // Codex
                return "Updating its plan";
            case "spawn_agent": // Codex
                return $"Delegating: {Text.Clip(input.Str("message") ?? input.Str("prompt") ?? "a subtask", 70)}";
            case "Write":
                return $"Writing {File()}";
            case "NotebookEdit":
                return $"Editing {File("notebook_path")}";
            case "Read":
                return $"Reading {File()}";
            case "Grep":
            case "Glob":
            case "LS":
                return "Searching the code";
            case "Bash":
            case "PowerShell":
            {
                var desc = input.Str("description");
                if (!string.IsNullOrWhiteSpace(desc)) return Text.Clip(desc, 80);
                return $"Running {Text.Clip(input.Str("command"), 60)}";
            }
            case "WebFetch":
            {
                var url = input.Str("url");
                return Uri.TryCreate(url, UriKind.Absolute, out var u) ? $"Reading {u.Host}" : "Reading a web page";
            }
            case "WebSearch":
                return $"Searching the web for “{Text.Clip(input.Str("query"), 50)}”";
            case "Agent":
            case "Task":
                return $"Delegating: {Text.Clip(input.Str("description") ?? input.Str("prompt"), 70)}";
            case "TodoWrite":
                return "Updating its plan";
            case "AskUserQuestion":
                return "Asking you a question";
            case "ExitPlanMode":
                return "Proposing a plan";
            case "Skill":
                return $"Using the {input.Str("skill") ?? "a"} skill";
            case "ToolSearch":
                return "Loading tools";
        }

        if (tool.StartsWith("mcp__", StringComparison.Ordinal))
        {
            var parts = tool.Split("__");
            var server = parts.Length > 1 ? parts[1].Replace("claude_ai_", "").Replace('_', ' ') : "a tool";
            return $"Using {server}";
        }

        return $"Using {tool}";
    }

    private static readonly Regex PatchFile = new(@"^\*\*\* (?:Add|Update|Delete) File: (.+?)\s*$|^\*\*\* Move to: (.+?)\s*$", RegexOptions.Multiline);

    /// <summary>The files a Codex patch touches ("*** Update File: src/app.ts" and friends).</summary>
    public static List<string> PatchFiles(string? patch)
    {
        var files = new List<string>();
        if (string.IsNullOrEmpty(patch)) return files;
        foreach (Match m in PatchFile.Matches(patch))
        {
            var path = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            if (!files.Contains(path)) files.Add(path);
        }
        return files;
    }

    public static string ShortPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "a file";
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    /// <summary>Facts and timeline lines from a successful tool call.</summary>
    public static (Facts facts, List<TimelineEntry> entries) FromSuccess(string tool, JsonElement input, JsonElement? response, DateTime at)
    {
        var facts = new Facts();
        var entries = new List<TimelineEntry>();

        switch (tool)
        {
            case "Edit":
            case "MultiEdit":
            case "Write":
            case "NotebookEdit":
            {
                var path = input.Str("file_path") ?? input.Str("notebook_path");
                if (path is not null)
                {
                    facts.Files.Add(path);
                    entries.Add(new TimelineEntry { At = at, Kind = EntryKind.Edit, Text = ShortPath(path) });
                }
                break;
            }
            case "Bash":
            case "PowerShell":
            {
                var cmd = input.Str("command") ?? "";
                var output = response is { } r ? FlattenStrings(r) : "";
                if (response is { } resp && resp.Obj("bashEditDiff") is { } diff && diff.Obj("changedFiles") is { ValueKind: JsonValueKind.Array } changed)
                {
                    foreach (var f in changed.EnumerateArray())
                        if (f.ValueKind == JsonValueKind.String && f.GetString() is { } s) facts.Files.Add(s);
                }
                ClassifyCommand(cmd, input.Str("description"), output, succeeded: true, facts, entries, at);
                break;
            }
            case "apply_patch": // Codex
                foreach (var path in PatchFiles(input.Str("command")))
                {
                    facts.Files.Add(path);
                    entries.Add(new TimelineEntry { At = at, Kind = EntryKind.Edit, Text = ShortPath(path) });
                }
                break;
            case "AskUserQuestion":
            {
                var q = FirstQuestion(input);
                if (q is not null)
                {
                    facts.Question = q;
                    entries.Add(new TimelineEntry { At = at, Kind = EntryKind.Question, Text = $"Asked you: {q}" });
                }
                break;
            }
            case "Agent":
            case "Task":
                entries.Add(new TimelineEntry { At = at, Kind = EntryKind.Step, Text = $"Subagent finished: {Text.Clip(input.Str("description"), 70)}" });
                break;
        }
        return (facts, entries);
    }

    /// <summary>Facts and timeline lines from a failed tool call.</summary>
    public static (Facts facts, List<TimelineEntry> entries) FromFailure(string tool, JsonElement input, string? error, bool interrupted, DateTime at)
    {
        var facts = new Facts();
        var entries = new List<TimelineEntry>();
        if (interrupted) return (facts, entries);

        if (tool is "Bash" or "PowerShell")
        {
            var cmd = input.Str("command") ?? "";
            ClassifyCommand(cmd, input.Str("description"), error ?? "", succeeded: false, facts, entries, at);
            if (entries.Count == 0 || entries.All(e => e.Kind != EntryKind.Test))
            {
                facts.Errors++;
                entries.Add(new TimelineEntry { At = at, Kind = EntryKind.Error, Text = $"Command failed: {Text.Clip(input.Str("description") ?? cmd, 70)}" });
            }
        }
        else if (tool is not ("Read" or "Grep" or "Glob" or "LS"))
        {
            // Failed reads and searches are routine exploration, not news.
            facts.Errors++;
            entries.Add(new TimelineEntry { At = at, Kind = EntryKind.Error, Text = $"{tool} failed: {Text.Clip(FirstLine(error), 80)}" });
        }
        return (facts, entries);
    }

    private static readonly Regex CommitMsg = new(@"git\s+(?:-C\s+\S+\s+)?commit\b.*?-m\s+(?:""((?:[^""\\]|\\.)*)""|'([^']*)'|(\S+))", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex HeredocMsg = new(@"cat\s*<<-?\s*'?EOF'?\s*\r?\n(.*?)\r?\n", RegexOptions.Singleline);
    private static readonly Regex Install = new(@"\b(?:npm|pnpm|yarn|bun)\s+(?:i|install|add)\s+([^\s&|;]+)|\bpip3?\s+install\s+([^\s&|;]+)|\bwinget\s+install\s+(?:--id\s+)?([^\s&|;]+)|\bdotnet\s+add\s+(?:\S+\s+)?package\s+([^\s&|;]+)|\bcargo\s+add\s+([^\s&|;]+)", RegexOptions.IgnoreCase);
    private static readonly Regex Delete = new(@"(?:^|[;&|]\s*)(?:rm\s+(?:-\w+\s+)*|del\s+(?:/\w\s+)*|rmdir\s+(?:/\w\s+)*|Remove-Item\s+(?:-\w+\s+)*)([^\s;&|]+)", RegexOptions.IgnoreCase);
    private static readonly Regex TestCmd = new(@"\b(pytest|jest|vitest|mocha|playwright\s+test|cargo\s+test|go\s+test|dotnet\s+test|npm\s+(?:run\s+)?test|pnpm\s+(?:run\s+)?test|yarn\s+test|bun\s+test|unittest|rspec|phpunit)\b", RegexOptions.IgnoreCase);
    private static readonly Regex Deploy = new(@"\b(vercel(\s+--prod|\s+deploy)?|netlify\s+deploy|fly\s+deploy|wrangler\s+(deploy|publish)|firebase\s+deploy|gh\s+release\s+create)\b", RegexOptions.IgnoreCase);

    internal static void ClassifyCommand(string cmd, string? description, string output, bool succeeded, Facts facts, List<TimelineEntry> entries, DateTime at)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return;

        if (TestCmd.IsMatch(cmd))
        {
            var (passed, failed) = ParseTestCounts(output);
            // A failing run with no parsable count is still a failing run: -1 means "failing, count unknown".
            int failedNow = failed is > 0 ? failed.Value : (succeeded ? 0 : Facts.UnknownCount);
            facts.TestsPassed = passed ?? 0;
            facts.TestsFailed = failedNow;
            var text = failedNow switch
            {
                > 0 => $"Tests: {failedNow} failing{(passed is > 0 ? $", {passed} passing" : "")}",
                Facts.UnknownCount => "Tests failed",
                _ => passed is > 0 ? $"Tests pass ({passed})" : "Ran the tests",
            };
            entries.Add(new TimelineEntry { At = at, Kind = EntryKind.Test, Text = text });
            return;
        }

        if (!succeeded) return;

        if (Regex.IsMatch(cmd, @"\bgit\b[^&;|]*\bcommit\b", RegexOptions.IgnoreCase))
        {
            var msg = CommitMessage(cmd);
            facts.Commits.Add(msg ?? "commit");
            entries.Add(new TimelineEntry { At = at, Kind = EntryKind.Commit, Text = msg is null ? "Committed" : $"Committed “{Text.Clip(msg, 70)}”" });
        }
        if (Regex.IsMatch(cmd, @"\bgit\b[^&;|]*\bpush\b", RegexOptions.IgnoreCase))
        {
            facts.Pushes++;
            entries.Add(new TimelineEntry { At = at, Kind = EntryKind.Push, Text = "Pushed to GitHub" });
        }
        foreach (Match m in Install.Matches(cmd))
        {
            var pkg = m.Groups.Cast<Group>().Skip(1).FirstOrDefault(g => g.Success)?.Value;
            if (pkg is null || pkg.StartsWith('-')) continue;
            facts.Installs.Add(pkg);
            entries.Add(new TimelineEntry { At = at, Kind = EntryKind.Install, Text = $"Installed {pkg}" });
        }
        foreach (Match m in Delete.Matches(cmd))
        {
            var target = m.Groups[1].Value.Trim('"', '\'');
            if (target.Length == 0) continue;
            facts.Deletes.Add(ShortPath(target));
            entries.Add(new TimelineEntry { At = at, Kind = EntryKind.Delete, Text = $"Deleted {ShortPath(target)}" });
        }
        if (Deploy.IsMatch(cmd))
        {
            facts.Deploys++;
            entries.Add(new TimelineEntry { At = at, Kind = EntryKind.Deploy, Text = "Deployed" });
        }
    }

    internal static string? CommitMessage(string cmd)
    {
        var heredoc = HeredocMsg.Match(cmd);
        if (heredoc.Success) return Text.Collapse(heredoc.Groups[1].Value);
        var m = CommitMsg.Match(cmd);
        if (!m.Success) return null;
        var msg = m.Groups.Cast<Group>().Skip(1).FirstOrDefault(g => g.Success)?.Value;
        if (msg is null) return null;
        var firstLine = msg.Replace("\\n", "\n").Split('\n')[0];
        return Text.Collapse(firstLine);
    }

    private static readonly Regex[] PassedPatterns =
    {
        new(@"Passed:\s*(\d+)", RegexOptions.IgnoreCase),                 // dotnet test
        new(@"Tests:\s+(?:\d+\s+\w+,\s+)*?(\d+)\s+passed", RegexOptions.IgnoreCase), // jest
        new(@"Tests\s+(\d+)\s+passed", RegexOptions.IgnoreCase),          // vitest
        new(@"(\d+)\s+(?:passed|passing)\b", RegexOptions.IgnoreCase),    // pytest, mocha
        new(@"test result: \w+\. (\d+) passed", RegexOptions.IgnoreCase), // cargo
    };

    private static readonly Regex[] FailedPatterns =
    {
        new(@"Failed:\s*(\d+)", RegexOptions.IgnoreCase),
        new(@"(\d+)\s+(?:failed|failing)\b", RegexOptions.IgnoreCase),
    };

    internal static (int? passed, int? failed) ParseTestCounts(string output)
    {
        if (string.IsNullOrEmpty(output)) return (null, null);
        int? Find(Regex[] patterns)
        {
            foreach (var p in patterns)
            {
                var ms = p.Matches(output);
                if (ms.Count > 0 && int.TryParse(ms[^1].Groups[1].Value, out var n)) return n;
            }
            return null;
        }
        return (Find(PassedPatterns), Find(FailedPatterns));
    }

    private static string? FirstQuestion(JsonElement input)
    {
        if (input.Obj("questions") is { ValueKind: JsonValueKind.Array } qs)
            foreach (var q in qs.EnumerateArray())
                if (q.Str("question") is { } text) return Text.Clip(text, 120);
        return input.Str("question");
    }

    private static string FirstLine(string? s) =>
        string.IsNullOrWhiteSpace(s) ? "" : s.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";

    /// <summary>All string values in a JSON tree, joined. Used to scan tool output for test counts.</summary>
    internal static string FlattenStrings(JsonElement e)
    {
        var sb = new StringBuilder();
        void Walk(JsonElement x, int depth)
        {
            if (depth > 6 || sb.Length > 200_000) return;
            switch (x.ValueKind)
            {
                case JsonValueKind.String: sb.AppendLine(x.GetString()); break;
                case JsonValueKind.Object: foreach (var p in x.EnumerateObject()) Walk(p.Value, depth + 1); break;
                case JsonValueKind.Array: foreach (var i in x.EnumerateArray()) Walk(i, depth + 1); break;
            }
        }
        Walk(e, 0);
        return sb.ToString();
    }
}
