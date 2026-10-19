namespace Vigil.Core;

public enum SessionState
{
    Ready,      // open, nothing asked yet
    Working,
    NeedsYou,
    Paused,     // waiting for a usage limit to reset
    Done,
    Failed,
    Quiet,      // said it was working, but nothing has happened for a long time
    Ended,
}

public enum EntryKind
{
    Prompt, Step, Edit, Test, Commit, Push, Install, Delete, Deploy, Error, Question, NeedsYou, Done, Failed, Paused,
}

public sealed class TimelineEntry
{
    public DateTime At { get; set; }
    public EntryKind Kind { get; set; }
    public string Text { get; set; } = "";
    public int Count { get; set; } = 1;
}

/// <summary>Hard facts read from hook payloads. Never guessed, so the numbers in the UI are exact.</summary>
public sealed class Facts
{
    public const int UnknownCount = -1;

    public HashSet<string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Commits { get; set; } = new();
    public int Pushes { get; set; }
    public List<string> Installs { get; set; } = new();
    public List<string> Deletes { get; set; } = new();
    public int Deploys { get; set; }
    public int? TestsPassed { get; set; }
    public int? TestsFailed { get; set; }
    public int Errors { get; set; }
    public string? Question { get; set; }

    public bool IsEmpty =>
        Files.Count == 0 && Commits.Count == 0 && Pushes == 0 && Installs.Count == 0 && Deletes.Count == 0 &&
        Deploys == 0 && TestsPassed is null && TestsFailed is null && Errors == 0 && Question is null;

    public void Merge(Facts other)
    {
        Files.UnionWith(other.Files);
        Commits.AddRange(other.Commits);
        Pushes += other.Pushes;
        Installs.AddRange(other.Installs);
        Deletes.AddRange(other.Deletes);
        Deploys += other.Deploys;
        if (other.TestsPassed is not null || other.TestsFailed is not null)
        {
            // The most recent test run is the one that matters.
            TestsPassed = other.TestsPassed;
            TestsFailed = other.TestsFailed;
        }
        Errors += other.Errors;
        Question = other.Question ?? Question;
    }

    /// <summary>Short chips, most important first. Problems lead.</summary>
    public List<Chip> Chips()
    {
        var chips = new List<Chip>();
        if (Question is not null) chips.Add(new Chip($"Asked: {Text.Clip(Question, 60)}", ChipTone.Attention));
        if (TestsFailed == UnknownCount) chips.Add(new Chip("Tests failing", ChipTone.Danger));
        else if (TestsFailed is > 0) chips.Add(new Chip($"{TestsFailed} test{Text.S(TestsFailed.Value)} failing", ChipTone.Danger));
        else if (TestsPassed is > 0) chips.Add(new Chip($"{TestsPassed} tests passing", ChipTone.Good));
        if (Files.Count > 0) chips.Add(new Chip($"{Files.Count} file{Text.S(Files.Count)} changed", ChipTone.Neutral));
        if (Commits.Count > 0) chips.Add(new Chip($"{Commits.Count} commit{Text.S(Commits.Count)}", ChipTone.Neutral));
        if (Pushes > 0) chips.Add(new Chip("Pushed", ChipTone.Neutral));
        if (Deploys > 0) chips.Add(new Chip("Deployed", ChipTone.Neutral));
        if (Installs.Count > 0) chips.Add(new Chip($"Installed {Text.Clip(string.Join(", ", Installs.Distinct()), 40)}", ChipTone.Attention));
        if (Deletes.Count > 0) chips.Add(new Chip($"Deleted {Text.Clip(string.Join(", ", Deletes.Distinct()), 40)}", ChipTone.Attention));
        if (Errors > 0) chips.Add(new Chip($"{Errors} error{Text.S(Errors)} hit", ChipTone.Danger));
        return chips;
    }
}

public enum ChipTone { Neutral, Good, Attention, Danger }

public sealed record Chip(string Text, ChipTone Tone);

/// <summary>One prompt → response cycle.</summary>
public sealed class Turn
{
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string Prompt { get; set; } = "";
    public string? LastMessage { get; set; }
    public string? Headline { get; set; }

    /// <summary>Set when the turn ended without Claude finishing it: "You stopped it" (Esc) or "Stopped".</summary>
    public string? EndNote { get; set; }

    public SessionState Outcome { get; set; } = SessionState.Working;
    public Facts Facts { get; set; } = new();
}

public sealed class Session
{
    public const string Claude = "claude";
    public const string Codex = "codex";

    public string Id { get; set; } = "";

    /// <summary>Which coding agent this is: "claude" (Claude Code) or "codex" (OpenAI Codex CLI).</summary>
    public string Agent { get; set; } = Claude;

    /// <summary>The current folder. Follows Claude around when it changes directory.</summary>
    public string Cwd { get; set; } = "";

    /// <summary>The folder the session started in. Doesn't move, so it can name the project.</summary>
    public string? StartCwd { get; set; }

    /// <summary>
    /// Started in the background (claude --bg, or by a script on a schedule), so there's no terminal
    /// tab for it. Kept off the pill, the lists and notifications. Null until the transcript says.
    /// </summary>
    public bool? Background { get; set; }

    public string? TranscriptPath { get; set; }
    public string? AiTitle { get; set; }
    public string? RemoteUrl { get; set; }

    public SessionState State { get; set; } = SessionState.Ready;
    public DateTime StateSince { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime LastEventAt { get; set; }

    /// <summary>What Claude is doing right now, in plain words.</summary>
    public string? NowLine { get; set; }

    /// <summary>Why the session needs you (for NeedsYou), or why it failed or paused.</summary>
    public string? Reason { get; set; }

    public int ActiveSubagents { get; set; }
    public List<string> BackgroundTasks { get; set; } = new();
    public List<Turn> Turns { get; set; } = new();
    public List<TimelineEntry> Timeline { get; set; } = new();

    public string Name => AiTitle is { Length: > 0 } ? AiTitle : FolderName;

    public string FolderName
    {
        get
        {
            var trimmed = Cwd.TrimEnd('\\', '/');
            var name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? (trimmed.Length > 0 ? trimmed : "Claude session") : name;
        }
    }

    /// <summary>
    /// What the avatar stands for: the project folder the session started in, or, for a session started
    /// in your home folder (where most sessions start), the session itself, by its title.
    /// </summary>
    public string AvatarKey
    {
        get
        {
            var start = (StartCwd ?? Cwd).TrimEnd('\\', '/');
            var folder = Path.GetFileName(start);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\', '/');
            var generic = string.IsNullOrEmpty(folder)
                || start.Equals(home, StringComparison.OrdinalIgnoreCase)
                || start.Contains(@"\AppData\", StringComparison.OrdinalIgnoreCase)
                || start.Contains(@"\.claude\", StringComparison.OrdinalIgnoreCase);
            return generic && AiTitle is { Length: > 0 } ? AiTitle : string.IsNullOrEmpty(folder) ? Name : folder;
        }
    }

    /// <summary>
    /// True once BackgroundTasks holds only work Claude comes back for. Lists saved by Vigil 0.3.0 and
    /// earlier also held servers and watchers, so those are screened again by their labels.
    /// </summary>
    public bool TasksScreened { get; set; }

    /// <summary>The background work Claude picks up again when it ends (not servers, watchers or monitors).</summary>
    public IReadOnlyList<string> AwaitedTasks =>
        TasksScreened ? BackgroundTasks : BackgroundTasks.Where(t => !SessionTracker.LooksEndless(t)).ToList();

    /// <summary>Claude finished its turn but left a background task running (it picks up when that ends).</summary>
    public bool WaitingOnBackgroundTask => State == SessionState.Done && AwaitedTasks.Count > 0;

    public Turn? CurrentTurn => Turns.Count > 0 ? Turns[^1] : null;

    public string? Headline => Turns.LastOrDefault(t => t.Headline is not null)?.Headline;

    /// <summary>
    /// What the latest turn came to, in one line: how it ended if it was stopped, else its summary, else the
    /// first sentence of Claude's last message. Only falls back to an earlier turn's summary when the latest
    /// turn has nothing to say, so a stopped turn never shows the previous turn's result.
    /// </summary>
    public string? Summary => CurrentTurn is { } t
        ? t.EndNote ?? t.Headline ?? (Text.FirstSentence(t.LastMessage, 200) is { Length: > 0 } first ? first : Headline)
        : Headline;

    public Facts AllFacts
    {
        get
        {
            var f = new Facts();
            foreach (var t in Turns) f.Merge(t.Facts);
            return f;
        }
    }

    public Facts FactsSince(DateTime since)
    {
        var f = new Facts();
        foreach (var t in Turns.Where(t => (t.EndedAt ?? DateTime.MaxValue) >= since)) f.Merge(t.Facts);
        return f;
    }

    public bool IsLive => State is not SessionState.Ended;
}
