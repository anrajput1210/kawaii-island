using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace KawaiiIsland.Services.ClaudeCode;

/// <summary>
/// Minimal HTTP/1.1 receiver on 127.0.0.1 only (never reachable from the network). Claude Code's hooks and status
/// line POST their JSON here via curl:
///   POST /kawaii/hook    → <see cref="Hook"/>   (empty reply: hook stdout can reach Claude's context, so say nothing)
///   POST /kawaii/status  → <see cref="Status"/> (reply text becomes Claude Code's status bar)
///   POST /kawaii/permission → <see cref="Hook"/> + <see cref="Permission"/>: held open until the user answers on the
///                             island; the reply is the hook's decision JSON ("" = ask in the terminal as usual)
/// A raw TcpListener avoids HttpListener's URL-ACL/admin requirements on Windows.
/// </summary>
public sealed class HookServer(int port) : IDisposable
{
    private const int MaxHeader = 16 * 1024, MaxBody = 2 * 1024 * 1024;
    private readonly TcpListener _listener = new(IPAddress.Loopback, port);
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Raised on a thread-pool thread.</summary>
    public event Action<JsonElement>? Hook;
    /// <summary>Called on a thread-pool thread; returns the status-line text.</summary>
    public Func<JsonElement, string>? Status { get; set; }
    /// <summary>Called on a thread-pool thread; completes with the PermissionRequest hook output.</summary>
    public Func<JsonElement, Task<string>>? Permission { get; set; }

    /// <exception cref="SocketException">The port is already in use.</exception>
    public void Start()
    {
        _listener.Start();
        _ = AcceptLoop();
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch (Exception) { return; } // stopped
            _ = Handle(client);
        }
    }

    private async Task Handle(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                var stream = client.GetStream();
                var (path, body) = await ReadRequest(stream, timeout.Token);
                string reply = "";
                switch (path)
                {
                    case "/kawaii/hook":
                        using (var doc = JsonDocument.Parse(body)) Hook?.Invoke(doc.RootElement.Clone());
                        break;
                    case "/kawaii/status":
                        using (var doc = JsonDocument.Parse(body)) reply = Status?.Invoke(doc.RootElement.Clone()) ?? "";
                        break;
                    case "/kawaii/permission":
                        JsonElement request;
                        using (var doc = JsonDocument.Parse(body)) request = doc.RootElement.Clone();
                        Hook?.Invoke(request);
                        reply = Permission is null ? "" : await Permission(request);
                        await Respond(stream, "200 OK", reply, _cts.Token); // the 3 s budget is for reading only
                        return;
                    default:
                        await Respond(stream, "404 Not Found", "", timeout.Token);
                        return;
                }
                await Respond(stream, "200 OK", reply, timeout.Token);
            }
            catch (Exception) { /* malformed, oversized or timed-out request: drop it */ }
        }
    }

    private static async Task<(string Path, byte[] Body)> ReadRequest(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[MaxHeader];
        int filled = 0, headerEnd;
        while ((headerEnd = IndexOf(buffer, filled, "\r\n\r\n"u8)) < 0)
        {
            if (filled == buffer.Length) throw new InvalidDataException("headers too large");
            int n = await stream.ReadAsync(buffer.AsMemory(filled), ct);
            if (n == 0) throw new EndOfStreamException();
            filled += n;
        }

        var head = Encoding.ASCII.GetString(buffer, 0, headerEnd).Split("\r\n");
        var requestLine = head[0].Split(' ');
        if (requestLine.Length < 2 || requestLine[0] != "POST") throw new InvalidDataException("POST only");
        int length = head.Skip(1)
            .Select(h => h.Split(':', 2))
            .Where(kv => kv.Length == 2 && kv[0].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            .Select(kv => int.Parse(kv[1].Trim()))
            .FirstOrDefault();
        if (length is < 0 or > MaxBody) throw new InvalidDataException("bad length");

        var body = new byte[length];
        int already = Math.Min(filled - (headerEnd + 4), length);
        Array.Copy(buffer, headerEnd + 4, body, 0, already);
        await stream.ReadExactlyAsync(body.AsMemory(already), ct);
        return (requestLine[1], body);
    }

    private static async Task Respond(NetworkStream stream, string status, string text, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, ct);
        await stream.WriteAsync(payload, ct);
    }

    private static int IndexOf(byte[] haystack, int length, ReadOnlySpan<byte> needle) =>
        haystack.AsSpan(0, length).IndexOf(needle);

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
    }
}
