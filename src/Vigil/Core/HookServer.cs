using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Vigil.Core;

/// <summary>
/// A deliberately tiny HTTP endpoint on 127.0.0.1 that Claude Code's hooks post to.
/// Loopback only: nothing on the network can reach it.
/// </summary>
public sealed class HookServer : IDisposable
{
    public const int DefaultPort = 47823;
    private const int MaxBody = 32 * 1024 * 1024;

    private readonly TcpListener _listener;
    private readonly Action<JsonElement, string?, string> _onEvent;
    private readonly Func<string>? _status;
    private readonly Action<string>? _test;
    private readonly CancellationTokenSource _cts = new();

    public int Port { get; }
    public long EventsReceived;

    /// <param name="test">Test copies only: POST /test/&lt;what&gt; simulates something Windows would report (screen off, lid closed).</param>
    public HookServer(Action<JsonElement, string?, string> onEvent, int port = DefaultPort, Func<string>? status = null, Action<string>? test = null)
    {
        _test = test;
        Port = port;
        _onEvent = onEvent;
        _status = status;
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    public void Start()
    {
        _listener.Start(backlog: 64);
        _ = Task.Run(AcceptLoop);
        Log.Info($"Listening for Claude Code hooks on 127.0.0.1:{Port}");
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (Exception ex)
            {
                Log.Warn($"Accept failed: {ex.Message}");
                continue;
            }
            _ = Task.Run(() => Serve(client));
        }
    }

    private async Task Serve(TcpClient client)
    {
        using var _ = client;
        try
        {
            client.ReceiveTimeout = 5000;
            var stream = client.GetStream();
            var (method, path, body) = await ReadRequest(stream);
            if (method == "POST" && path.StartsWith("/hook", StringComparison.Ordinal))
            {
                await Respond(stream, 200, "");
                if (body.Length > 0)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(body);
                        Interlocked.Increment(ref EventsReceived);
                        _onEvent(doc.RootElement.Clone(), RemoteId(path), Agent(path));
                    }
                    catch (JsonException ex)
                    {
                        Log.Warn($"Bad hook payload: {ex.Message}");
                    }
                }
            }
            else if (method == "POST" && _test is not null && path.StartsWith("/test/", StringComparison.Ordinal))
            {
                _test(path["/test/".Length..]);
                await Respond(stream, 200, "");
            }
            else if (method == "GET" && path == "/status")
            {
                await Respond(stream, 200, _status?.Invoke() ?? "{}", "application/json");
            }
            else
            {
                await Respond(stream, 404, "");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Hook request failed: {ex.Message}");
        }
    }

    private static async Task<(string method, string path, byte[] body)> ReadRequest(NetworkStream stream)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int headerEnd;
        while (true)
        {
            var n = await stream.ReadAsync(chunk);
            if (n == 0) throw new IOException("Connection closed before headers");
            buffer.Write(chunk, 0, n);
            headerEnd = IndexOf(buffer.GetBuffer(), (int)buffer.Length, "\r\n\r\n"u8);
            if (headerEnd >= 0) break;
            if (buffer.Length > 64 * 1024) throw new IOException("Headers too large");
        }

        var headerText = Encoding.ASCII.GetString(buffer.GetBuffer(), 0, headerEnd);
        var lines = headerText.Split("\r\n");
        var requestLine = lines[0].Split(' ');
        var method = requestLine.Length > 0 ? requestLine[0] : "";
        var path = requestLine.Length > 1 ? requestLine[1] : "/";
        var contentLength = 0;
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0 && line[..colon].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                int.TryParse(line[(colon + 1)..].Trim(), out contentLength);
        }
        if (contentLength < 0 || contentLength > MaxBody) throw new IOException("Body too large");

        var bodyStart = headerEnd + 4;
        var body = new byte[contentLength];
        var have = (int)buffer.Length - bodyStart;
        Array.Copy(buffer.GetBuffer(), bodyStart, body, 0, Math.Min(have, contentLength));
        while (have < contentLength)
        {
            var n = await stream.ReadAsync(body.AsMemory(have, contentLength - have));
            if (n == 0) break;
            have += n;
        }
        return (method, path, body);
    }

    private static async Task Respond(NetworkStream stream, int status, string body, string type = "text/plain")
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var reason = status == 200 ? "OK" : "Not Found";
        var head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {type}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        if (bytes.Length > 0) await stream.WriteAsync(bytes);
    }

    /// <summary>Which coding agent posted: Codex's hooks post to /hook/codex, Claude Code's to /hook.</summary>
    internal static string Agent(string path) =>
        path.StartsWith(CodexSetup.Marker, StringComparison.Ordinal) ? Session.Codex : Session.Claude;

    /// <summary>The Remote Control session id from "?rc=...", if cmd expanded it.</summary>
    internal static string? RemoteId(string path)
    {
        var q = path.IndexOf("?rc=", StringComparison.Ordinal);
        if (q < 0) return null;
        var value = Uri.UnescapeDataString(path[(q + 4)..].Split('&')[0]).Trim();
        // An unset variable comes through literally as %NAME%.
        if (value.Length == 0 || value.Contains('%') || value.Length > 200) return null;
        return value.All(c => char.IsLetterOrDigit(c) || c is '_' or '-') ? value : null;
    }

    private static int IndexOf(byte[] haystack, int length, ReadOnlySpan<byte> needle)
    {
        var idx = haystack.AsSpan(0, length).IndexOf(needle);
        return idx;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
    }
}
