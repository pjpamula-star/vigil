namespace Vigil.Core;

/// <summary>Tiny append-only log. Rolls over at 2 MB so it never grows without bound.</summary>
internal static class Log
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                var path = Paths.Log;
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 2_000_000)
                {
                    File.Copy(path, path + ".1", overwrite: true);
                    File.WriteAllText(path, "");
                }
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
