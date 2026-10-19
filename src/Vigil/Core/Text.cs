namespace Vigil.Core;

internal static class Text
{
    public static string S(int n) => n == 1 ? "" : "s";

    public static string Clip(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = Collapse(s);
        return s.Length <= max ? s : s[..Math.Max(0, max - 1)].TrimEnd() + "…";
    }

    /// <summary>Single-line, single-spaced.</summary>
    public static string Collapse(string s)
    {
        var sb = new StringBuilder(s.Length);
        var space = false;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!space && sb.Length > 0) sb.Append(' ');
                space = true;
            }
            else
            {
                sb.Append(ch);
                space = false;
            }
        }
        return sb.ToString().Trim();
    }

    /// <summary>First sentence of a message, without markdown noise.</summary>
    public static string FirstSentence(string? message, int max = 160)
    {
        if (string.IsNullOrWhiteSpace(message)) return "";
        var raw = message.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        // Headings ("## Summary") are labels, not content; skip them when there's anything else.
        if (raw.Any(l => !l.StartsWith('#'))) raw = raw.Where(l => !l.StartsWith('#')).ToList();
        var lines = raw
            .Select(l => l.TrimStart('#', '*', '-', '>', ' ').Replace("**", "").Replace("`", ""))
            .Where(l => l.Length > 0);
        var flat = Collapse(string.Join(" ", lines));
        var cut = -1;
        for (var i = 0; i < flat.Length - 1; i++)
        {
            if (flat[i] is '.' or '!' or '?' && flat[i + 1] == ' ' && i > 12)
            {
                cut = i + 1;
                break;
            }
        }
        var sentence = cut > 0 ? flat[..cut] : flat;
        return Clip(sentence, max);
    }

    /// <summary>Did Claude end its message by asking the user something?</summary>
    public static string? TrailingQuestion(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        var lines = message.Trim().Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0) return null;
        var last = lines[^1].Replace("**", "").Replace("`", "");
        if (!last.EndsWith('?')) return null;
        // Take the last sentence only.
        var idx = Math.Max(last.LastIndexOf(". ", last.Length - 1, StringComparison.Ordinal),
                           last.LastIndexOf("! ", last.Length - 1, StringComparison.Ordinal));
        var q = idx >= 0 ? last[(idx + 2)..] : last;
        return Clip(q.TrimStart('-', '*', ' '), 140);
    }

    public static string Ago(DateTime then, DateTime now)
    {
        var d = now - then;
        if (d < TimeSpan.FromSeconds(45)) return "just now";
        if (d < TimeSpan.FromMinutes(1.5)) return "1 min ago";
        if (d < TimeSpan.FromMinutes(60)) return $"{(int)Math.Round(d.TotalMinutes)} min ago";
        if (d < TimeSpan.FromHours(24)) return then.Date == now.Date ? then.ToString("h:mm tt") : "yesterday " + then.ToString("h:mm tt");
        return then.ToString("MMM d, h:mm tt");
    }

    public static string Duration(TimeSpan d)
    {
        if (d < TimeSpan.FromMinutes(1)) return "<1m";
        if (d < TimeSpan.FromHours(1)) return $"{(int)d.TotalMinutes}m";
        return d.Minutes == 0 ? $"{(int)d.TotalHours}h" : $"{(int)d.TotalHours}h {d.Minutes}m";
    }
}
