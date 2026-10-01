using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace KawaiiIsland.Services.Mail;

/// <summary>A mail service with OAuth sign-in (Gmail, Outlook.com / Microsoft 365). Client IDs come from Settings.</summary>
public sealed record OAuthProvider(string Key, string Name, string AuthUrl, string TokenUrl, string Scope, string ImapHost, string WebMail, string ExtraAuth)
{
    public static readonly OAuthProvider Google = new("google", "Google",
        "https://accounts.google.com/o/oauth2/v2/auth", "https://oauth2.googleapis.com/token",
        "https://mail.google.com/ openid email", "imap.gmail.com", "https://mail.google.com/", "access_type=offline&prompt=consent");

    public static readonly OAuthProvider Microsoft = new("microsoft", "Microsoft",
        "https://login.microsoftonline.com/common/oauth2/v2.0/authorize", "https://login.microsoftonline.com/common/oauth2/v2.0/token",
        "https://outlook.office.com/IMAP.AccessAsUser.All offline_access openid email", "outlook.office365.com", "https://outlook.office.com/mail/", "prompt=select_account");

    public static OAuthProvider? For(string key) => key switch { "google" => Google, "microsoft" => Microsoft, _ => null };

    /// <summary>Client ID/secret built into this copy of the app (see csproj); empty when the build has none.</summary>
    public (string Id, string Secret) BuiltInClient => (Metadata($"{Name}ClientId"), Metadata($"{Name}ClientSecret"));

    private static string Metadata(string key) =>
        typeof(OAuthProvider).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value?.Trim() ?? "";

    /// <summary>Microsoft only accepts http://localhost for desktop apps; Google prefers the 127.0.0.1 literal.</summary>
    public string RedirectUri(int port) => Key == "microsoft" ? $"http://localhost:{port}/" : $"http://127.0.0.1:{port}/";
}

/// <summary>Signed-in account kept on this PC, DPAPI-encrypted (mail.oauth). Never in config.json.</summary>
public sealed record OAuthAccount(string Provider, string Email, string RefreshToken);

/// <summary>
/// OAuth 2.0 for desktop apps (RFC 8252): system browser + loopback redirect + PKCE (RFC 7636). Only tokens are
/// stored; the password is typed into Google/Microsoft's own page and never seen by the app.
/// </summary>
public static class OAuth
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly byte[] Entropy = "KawaiiIsland.oauth.v1"u8.ToArray();
    private static (string Token, DateTimeOffset Expires)? _access;

    // ---------------- sign-in ----------------

    /// <returns>The account, or throws <see cref="OAuthException"/> with a friendly message.</returns>
    public static async Task<OAuthAccount> SignInAsync(OAuthProvider provider, string clientId, string clientSecret, CancellationToken ct)
    {
        if (clientId.Length == 0) throw new OAuthException($"Sign in with {provider.Name} isn't available in this build of Kawaii Island.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));

        var listeners = OpenLoopback(out int port);
        try
        {
            string verifier = Base64Url(RandomNumberGenerator.GetBytes(32)), state = Base64Url(RandomNumberGenerator.GetBytes(16));
            string redirect = provider.RedirectUri(port);
            Process.Start(new ProcessStartInfo(AuthorizeUrl(provider, clientId, redirect, Challenge(verifier), state)) { UseShellExecute = true });

            var query = await ReceiveRedirectAsync(listeners, timeout.Token);
            if (query["state"] != state) throw new OAuthException("Sign-in was interrupted. Please try again.");
            if (query["error"] is { } error) throw new OAuthException(error == "access_denied" ? "Sign-in was cancelled." : $"{provider.Name} said: {error}");
            if (query["code"] is not { } code) throw new OAuthException("No sign-in code came back. Please try again.");

            var tokens = await PostAsync(provider.TokenUrl, Form(clientId, clientSecret,
                ("grant_type", "authorization_code"), ("code", code), ("code_verifier", verifier), ("redirect_uri", redirect)), timeout.Token);
            string refresh = Str(tokens, "refresh_token");
            if (refresh.Length == 0) throw new OAuthException($"{provider.Name} didn't allow offline access. Please try again.");
            Remember(tokens);
            return new OAuthAccount(provider.Key, EmailFromIdToken(Str(tokens, "id_token")), refresh);
        }
        catch (OperationCanceledException) { throw new OAuthException("Sign-in timed out. Please try again."); }
        finally { foreach (var l in listeners) l.Stop(); }
    }

    /// <summary>A fresh access token for IMAP (cached until 2 minutes before it expires). Saves a rotated refresh token.</summary>
    public static async Task<string> AccessTokenAsync(OAuthProvider provider, OAuthAccount account, string clientId, string clientSecret, string directory, CancellationToken ct)
    {
        if (_access is { } a && a.Expires > DateTimeOffset.Now.AddMinutes(2)) return a.Token;
        var tokens = await PostAsync(provider.TokenUrl, Form(clientId, clientSecret,
            ("grant_type", "refresh_token"), ("refresh_token", account.RefreshToken), ("scope", provider.Scope)), ct);
        if (Str(tokens, "refresh_token") is { Length: > 0 } rotated && rotated != account.RefreshToken)
            Save(directory, account with { RefreshToken = rotated });
        return Remember(tokens);
    }

    public static void Forget() => _access = null;

    // ---------------- storage (DPAPI, current user) ----------------

    private static string FileIn(string directory) => Path.Combine(directory, "mail.oauth");

    public static void Save(string directory, OAuthAccount account)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(FileIn(directory), ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(account), Entropy, DataProtectionScope.CurrentUser));
    }

    public static OAuthAccount? Load(string directory)
    {
        try { return JsonSerializer.Deserialize<OAuthAccount>(ProtectedData.Unprotect(File.ReadAllBytes(FileIn(directory)), Entropy, DataProtectionScope.CurrentUser)); }
        catch (Exception ex) when (ex is IOException or CryptographicException or JsonException or UnauthorizedAccessException) { return null; }
    }

    public static void Delete(string directory) { File.Delete(FileIn(directory)); _access = null; }

    // ---------------- pieces (unit-tested) ----------------

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>PKCE S256 challenge for a verifier.</summary>
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static string AuthorizeUrl(OAuthProvider p, string clientId, string redirect, string challenge, string state) =>
        $"{p.AuthUrl}?response_type=code&client_id={Uri.EscapeDataString(clientId)}&redirect_uri={Uri.EscapeDataString(redirect)}" +
        $"&scope={Uri.EscapeDataString(p.Scope)}&code_challenge={challenge}&code_challenge_method=S256&state={state}&{p.ExtraAuth}";

    /// <summary>The "email" (or Microsoft "preferred_username") claim from an id_token. Signature isn't checked:
    /// the token came straight from the provider over TLS and is only used as a display name / IMAP user.</summary>
    public static string EmailFromIdToken(string idToken)
    {
        var parts = idToken.Split('.');
        if (parts.Length < 2) return "";
        string payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        try
        {
            using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
            return Str(doc.RootElement, "email") is { Length: > 0 } email ? email : Str(doc.RootElement, "preferred_username");
        }
        catch (Exception ex) when (ex is FormatException or JsonException) { return ""; }
    }

    // ---------------- plumbing ----------------

    /// <summary>Listens on 127.0.0.1 and, when possible, ::1 on the same free port (browsers may use either for "localhost").</summary>
    private static List<TcpListener> OpenLoopback(out int port)
    {
        var v4 = new TcpListener(IPAddress.Loopback, 0);
        v4.Start();
        port = ((IPEndPoint)v4.LocalEndpoint).Port;
        var listeners = new List<TcpListener> { v4 };
        try { var v6 = new TcpListener(IPAddress.IPv6Loopback, port); v6.Start(); listeners.Add(v6); }
        catch (SocketException) { /* no IPv6 loopback: 127.0.0.1 is enough */ }
        return listeners;
    }

    private static async Task<System.Collections.Specialized.NameValueCollection> ReceiveRedirectAsync(List<TcpListener> listeners, CancellationToken ct)
    {
        while (true)
        {
            var accepts = listeners.Select(l => l.AcceptTcpClientAsync(ct).AsTask()).ToList();
            using var client = await await Task.WhenAny(accepts);
            var stream = client.GetStream();
            var buffer = new byte[8192];
            int n = await stream.ReadAsync(buffer, ct);
            string requestLine = Encoding.ASCII.GetString(buffer, 0, n).Split("\r\n")[0]; // GET /?code=…&state=… HTTP/1.1
            string target = requestLine.Split(' ') is [_, var t, ..] ? t : "/";
            if (!target.StartsWith("/?") && target != "/") continue; // favicon etc.
            var query = HttpUtility.ParseQueryString(target.Length > 1 ? target[2..] : "");
            string html = "<!doctype html><meta charset=utf-8><title>Kawaii Island</title><body style=\"font:16px system-ui;background:#111;color:#eee;display:grid;place-items:center;height:90vh\">" +
                          (query["code"] is null ? "Sign-in didn't finish. You can close this tab." : "You're signed in to Kawaii Island. You can close this tab.") + "</body>";
            var body = Encoding.UTF8.GetBytes(html);
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"), ct);
            await stream.WriteAsync(body, ct);
            return query;
        }
    }

    private static FormUrlEncodedContent Form(string clientId, string clientSecret, params (string Key, string Value)[] fields)
    {
        var all = new List<KeyValuePair<string, string>> { new("client_id", clientId) };
        if (clientSecret.Length > 0) all.Add(new("client_secret", clientSecret)); // Google desktop clients send it (it isn't a real secret)
        all.AddRange(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)));
        return new FormUrlEncodedContent(all);
    }

    private static async Task<JsonElement> PostAsync(string url, HttpContent content, CancellationToken ct)
    {
        using var response = await Http.PostAsync(url, content, ct);
        string text = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement.Clone();
        if (!response.IsSuccessStatusCode)
            throw new OAuthException(Str(root, "error_description") is { Length: > 0 } d ? d : Str(root, "error") is { Length: > 0 } e ? e : $"HTTP {(int)response.StatusCode}");
        return root;
    }

    private static string Remember(JsonElement tokens)
    {
        string token = Str(tokens, "access_token");
        int seconds = tokens.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var s) ? s : 3600;
        _access = (token, DateTimeOffset.Now.AddSeconds(seconds));
        return token;
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}

public sealed class OAuthException(string message) : Exception(message);
