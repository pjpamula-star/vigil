using System.Text.Json;
using Vigil.Core;

namespace Vigil.Tests;

public class ToolInterpreterTests : TempRoot
{
    private static (Facts, List<TimelineEntry>) Bash(string cmd, string output = "", bool ok = true)
    {
        var input = JsonSerializer.SerializeToElement(new { command = cmd });
        return ok
            ? ToolInterpreter.FromSuccess("Bash", input, JsonSerializer.SerializeToElement(new { stdout = output }), DateTime.Now)
            : ToolInterpreter.FromFailure("Bash", input, "Exit code 1\n" + output, false, DateTime.Now);
    }

    [Theory]
    [InlineData("git commit -m \"Add frame scrubbing\"", "Add frame scrubbing")]
    [InlineData("git add -A && git commit -q -m 'Fix parser'", "Fix parser")]
    [InlineData("git commit -m \"$(cat <<'EOF'\nAdd research notes\n\nCo-Authored-By: x\nEOF\n)\"", "Add research notes")]
    public void Reads_commit_messages(string cmd, string expected)
    {
        var (facts, entries) = Bash(cmd);
        Assert.Equal(expected, Assert.Single(facts.Commits));
        Assert.Contains(entries, e => e.Kind == EntryKind.Commit);
    }

    [Theory]
    [InlineData("pytest -q", "48 passed in 2.31s", 48, 0)]
    [InlineData("npm test", "Tests:       2 failed, 46 passed, 48 total", 46, 2)]
    [InlineData("dotnet test", "Passed!  - Failed:     0, Passed:    31, Skipped:     0", 31, 0)]
    [InlineData("npx vitest run", " Tests  12 passed (12)", 12, 0)]
    public void Parses_test_results(string cmd, string output, int passed, int failed)
    {
        var (facts, _) = Bash(cmd, output, ok: failed == 0);
        Assert.Equal(passed, facts.TestsPassed);
        Assert.Equal(failed, facts.TestsFailed);
    }

    [Fact]
    public void Failing_tests_without_counts_still_show_as_failing()
    {
        var (facts, entries) = Bash("pytest", "ImportError: boom", ok: false);
        Assert.Equal(Facts.UnknownCount, facts.TestsFailed);
        Assert.Contains(facts.Chips(), c => c.Text == "Tests failing" && c.Tone == ChipTone.Danger);
        Assert.Equal("Tests failed", Assert.Single(entries).Text);
    }

    [Fact]
    public void Flags_installs_deletes_pushes_and_deploys()
    {
        var (facts, _) = Bash("npm install vite@6 && rm -rf dist && git push origin main && vercel --prod");
        Assert.Equal(new[] { "vite@6" }, facts.Installs);
        Assert.Equal(new[] { "dist" }, facts.Deletes);
        Assert.Equal(1, facts.Pushes);
        Assert.Equal(1, facts.Deploys);
    }

    [Fact]
    public void Routine_read_failures_are_not_news()
    {
        var (facts, entries) = ToolInterpreter.FromFailure("Read", JsonSerializer.SerializeToElement(new { file_path = "x" }), "not found", false, DateTime.Now);
        Assert.Equal(0, facts.Errors);
        Assert.Empty(entries);
    }

    [Fact]
    public void Chips_put_problems_first()
    {
        var f = new Facts { TestsFailed = 2, TestsPassed = 40, Files = { "a" }, Question = "Ship it?" };
        var chips = f.Chips();
        Assert.StartsWith("Asked:", chips[0].Text);
        Assert.StartsWith("2 tests failing", chips[1].Text);
    }

    [Theory]
    [InlineData("Done.\n\nWant me to deploy it?", "Want me to deploy it?")]
    [InlineData("All good. Shall I continue?", "Shall I continue?")]
    [InlineData("Finished everything.", null)]
    public void Detects_trailing_questions(string message, string? expected) =>
        Assert.Equal(expected, Text.TrailingQuestion(message));

    [Fact]
    public void First_sentence_strips_markdown()
    {
        Assert.Equal("Fixed the crash.", Text.FirstSentence("## Summary\n**Fixed the crash.** Then I added tests."));
    }
}
