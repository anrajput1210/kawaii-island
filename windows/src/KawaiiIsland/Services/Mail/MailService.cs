using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;

namespace KawaiiIsland.Services.Mail;

/// <summary>One unread message, headers only (spec §3.1: bodies are never downloaded).</summary>
public sealed record MailHeader(string Id, string From, string Subject, DateTimeOffset Date);

/// <summary>Latest unread headers (newest first) and the total unread count.</summary>
public sealed record MailSnapshot(IReadOnlyList<MailHeader> Latest, int Unread)
{
    public static readonly MailSnapshot Empty = new([], 0);
}

/// <summary>Where mail comes from: the demo inbox (default, no credentials) or a real IMAP account.</summary>
internal interface IMailProvider : IDisposable
{
    /// <summary>Raised on a background thread whenever the unread set changes.</summary>
    event Action<MailSnapshot>? Changed;

    /// <returns>null when running, otherwise a friendly explanation (bad password, server unreachable…).</returns>
    Task<string?> StartAsync();
}

/// <summary>Remembers which messages were already shown so only real arrivals count as "new". Unit-tested.</summary>
public sealed class MailInbox
{
    private readonly HashSet<string> _seen = [];
    private bool _primed;

    public MailSnapshot Current { get; private set; } = MailSnapshot.Empty;

    /// <returns>Messages that weren't there before. The first update only primes (what's already unread isn't news).</returns>
    public IReadOnlyList<MailHeader> Update(MailSnapshot snapshot)
    {
        Current = snapshot;
        var fresh = snapshot.Latest.Where(m => _seen.Add(m.Id)).ToList();
        if (_primed) return fresh;
        _primed = true;
        return [];
    }
}

/// <summary>Demo inbox: reads mock_mail.json from the settings folder if present, otherwise built-in samples,
/// and "receives" one message every 45 s.</summary>
internal sealed class MockMailProvider(string directory) : IMailProvider
{
    private sealed record Sample(string From, string Subject);

    private static readonly Sample[] BuiltIn =
    [
        new("Kawaii Island", "Demo inbox: add your mail account in Settings → Mail"),
        new("Design team", "Mockups for the new onboarding flow"),
        new("Calendar", "Reminder: stand-up in 10 minutes"),
        new("Build server", "Nightly build passed (54 tests)"),
        new("Alex", "Lunch on Friday?"),
    ];

    private readonly List<MailHeader> _unread = [];
    private Timer? _timer;
    private int _next;

    public event Action<MailSnapshot>? Changed;

    public Task<string?> StartAsync()
    {
        var samples = Load();
        lock (_unread) { Add(samples); Add(samples); } // two waiting already
        _timer = new Timer(_ => { lock (_unread) Add(samples); }, null, TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(45));
        return Task.FromResult<string?>(null);
    }

    private void Add(Sample[] samples)
    {
        var s = samples[_next % samples.Length];
        _unread.Insert(0, new MailHeader($"mock-{_next++}", s.From, s.Subject, DateTimeOffset.Now));
        if (_unread.Count > 20) _unread.RemoveAt(20);
        Changed?.Invoke(new MailSnapshot([.. _unread], _unread.Count));
    }

    private Sample[] Load()
    {
        try
        {
            var path = Path.Combine(directory, "mock_mail.json");
            if (File.Exists(path) && JsonSerializer.Deserialize<Sample[]>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) is { Length: > 0 } custom)
                return custom;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return BuiltIn;
    }

    public void Dispose() => _timer?.Dispose();
}

/// <summary>
/// IMAP via MailKit: INBOX read-only, unread headers only. Waits with IDLE when the server supports it (re-issued
/// every 9 min), otherwise polls every PollSeconds. Reconnects with backoff (5 s → 5 min) after errors.
/// </summary>
internal sealed class ImapMailProvider(string host, int port, bool ssl, int pollSeconds, Func<ImapClient, CancellationToken, Task> authenticate) : IMailProvider
{
    private const int Latest = 20;
    private readonly CancellationTokenSource _cts = new();

    public event Action<MailSnapshot>? Changed;

    public async Task<string?> StartAsync()
    {
        try { using var probe = await ConnectAsync(_cts.Token); }
        catch (AuthenticationException) { return "The mail server rejected the sign-in. For IMAP, Gmail and Outlook need an app password; or use Sign in with Google / Microsoft."; }
        catch (OAuthException ex) { return "Sign-in expired or was revoked: " + ex.Message + " Sign in again in Settings → Mail."; }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or SslHandshakeException or ImapProtocolException or ImapCommandException or System.Net.Http.HttpRequestException)
        {
            return $"Couldn't reach {host}:{port} ({ex.Message}).";
        }
        _ = Task.Run(() => RunAsync(_cts.Token));
        return null;
    }

    private async Task<ImapClient> ConnectAsync(CancellationToken ct)
    {
        var client = new ImapClient();
        try
        {
            await client.ConnectAsync(host, port, ssl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable, ct);
            await authenticate(client, ct); // app password, or XOAUTH2 with a fresh token (Sign in with Google / Microsoft)
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        int backoff = 5;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var client = await ConnectAsync(ct);
                var inbox = client.Inbox;
                await inbox.OpenAsync(FolderAccess.ReadOnly, ct);
                backoff = 5;
                while (!ct.IsCancellationRequested)
                {
                    await ReportAsync(inbox, ct);
                    if (client.Capabilities.HasFlag(ImapCapabilities.Idle)) await IdleAsync(client, inbox, ct);
                    else await Task.Delay(TimeSpan.FromSeconds(pollSeconds), ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception) // dropped connection, server hiccup, sleep/resume: wait and reconnect
            {
                try { await Task.Delay(TimeSpan.FromSeconds(backoff), ct); } catch (OperationCanceledException) { return; }
                backoff = Math.Min(backoff * 2, 300);
            }
        }
    }

    /// <summary>Waits until something changes in INBOX (new, read elsewhere, deleted) or 9 minutes pass.</summary>
    private static async Task IdleAsync(ImapClient client, IMailFolder inbox, CancellationToken ct)
    {
        using var done = new CancellationTokenSource(TimeSpan.FromMinutes(9));
        void Wake(object? sender, EventArgs e) => done.Cancel();
        inbox.CountChanged += Wake;
        inbox.MessageFlagsChanged += Wake;
        inbox.MessageExpunged += Wake;
        try { await client.IdleAsync(done.Token, ct); }
        finally
        {
            inbox.CountChanged -= Wake;
            inbox.MessageFlagsChanged -= Wake;
            inbox.MessageExpunged -= Wake;
        }
    }

    private async Task ReportAsync(IMailFolder inbox, CancellationToken ct)
    {
        var unread = await inbox.SearchAsync(SearchQuery.NotSeen, ct);
        var newest = unread.OrderByDescending(u => u.Id).Take(Latest).ToList();
        IList<IMessageSummary> summaries = newest.Count == 0 ? [] : await inbox.FetchAsync(newest, MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId, ct);
        var headers = summaries
            .Select(s => new MailHeader(s.UniqueId.ToString(), Sender(s.Envelope), s.Envelope?.Subject ?? "", s.Envelope?.Date ?? DateTimeOffset.Now))
            .OrderByDescending(h => h.Date)
            .ToList();
        Changed?.Invoke(new MailSnapshot(headers, unread.Count));
    }

    private static string Sender(Envelope? e)
    {
        var from = e?.From.Mailboxes.FirstOrDefault();
        return string.IsNullOrWhiteSpace(from?.Name) ? from?.Address ?? "Unknown sender" : from.Name;
    }

    public void Dispose() => _cts.Cancel();
}

/// <summary>The IMAP password, encrypted with DPAPI for this Windows user only (never in config.json).</summary>
internal static class MailSecret
{
    private static readonly byte[] Entropy = "KawaiiIsland.mail.v1"u8.ToArray();

    private static string PathIn(string directory) => Path.Combine(directory, "mail.secret");

    public static void Save(string directory, string password)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(PathIn(directory), ProtectedData.Protect(Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser));
    }

    public static string? Load(string directory)
    {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(PathIn(directory)), Entropy, DataProtectionScope.CurrentUser)); }
        catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException) { return null; }
    }

    public static bool Exists(string directory) => File.Exists(PathIn(directory));
}
