using System.Text.Json;
using Vigil.Core;

namespace Vigil.Tests;

internal sealed class Clock
{
    public DateTime Now { get; set; } = new(2026, 10, 16, 23, 0, 0);
    public void Advance(TimeSpan d) => Now += d;
}

internal static class Hook
{
    public static JsonElement Event(string evt, string session = "s1", object? extra = null)
    {
        var dict = new Dictionary<string, object?>
        {
            ["session_id"] = session,
            ["hook_event_name"] = evt,
            ["cwd"] = @"C:\Users\sam\replay-tool",
            ["transcript_path"] = @"C:\nope\t.jsonl",
        };
        if (extra is not null)
            foreach (var p in JsonSerializer.SerializeToElement(extra).EnumerateObject())
                dict[p.Name] = p.Value;
        return JsonSerializer.SerializeToElement(dict);
    }
}

/// <summary>Points Vigil's data folder at a throwaway temp dir for each test class.</summary>
public abstract class TempRoot : IDisposable
{
    protected readonly string Dir = Path.Combine(Path.GetTempPath(), "vigil-tests-" + Guid.NewGuid().ToString("N")[..8]);

    protected TempRoot()
    {
        Directory.CreateDirectory(Dir);
        Paths.Root = Dir;
    }

    public void Dispose()
    {
        try { Directory.Delete(Dir, recursive: true); } catch { }
    }
}
