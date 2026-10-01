using System.IO;
using System.Text;
using KawaiiIsland.Services.Mail;

namespace KawaiiIsland.Tests;

public sealed class OAuthTests
{
    [Fact]
    public void Pkce_challenge_is_base64url_sha256_of_the_verifier() => // expected value from Python's hashlib, independently
        Assert.Equal("qAJmDfz0AZLpKM3bSuKKiylvlVzqAianZYKUC1jB63g", OAuth.Challenge("dBjftJeZ4CVP-mJ92K1t6lZvsZ0mZQWO_EnKEoyHtbM"));

    [Fact]
    public void Authorize_url_carries_pkce_state_scope_and_redirect()
    {
        string url = OAuth.AuthorizeUrl(OAuthProvider.Google, "abc.apps.googleusercontent.com", "http://127.0.0.1:5000/", "CH", "ST");
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?response_type=code", url);
        Assert.Contains("client_id=abc.apps.googleusercontent.com", url);
        Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A5000%2F", url);
        Assert.Contains("scope=https%3A%2F%2Fmail.google.com%2F%20openid%20email", url);
        Assert.Contains("code_challenge=CH&code_challenge_method=S256&state=ST", url);
        Assert.Contains("access_type=offline", url);
    }

    [Fact]
    public void Microsoft_uses_localhost_redirect_google_uses_127()
    {
        Assert.Equal("http://localhost:5000/", OAuthProvider.Microsoft.RedirectUri(5000));
        Assert.Equal("http://127.0.0.1:5000/", OAuthProvider.Google.RedirectUri(5000));
        Assert.Equal("outlook.office365.com", OAuthProvider.For("microsoft")!.ImapHost);
        Assert.Null(OAuthProvider.For("imap"));
    }

    [Theory]
    [InlineData("""{"email":"kiko@gmail.com","sub":"1"}""", "kiko@gmail.com")]
    [InlineData("""{"preferred_username":"me@outlook.com"}""", "me@outlook.com")]
    public void Email_comes_from_the_id_token(string claims, string email)
    {
        string token = "eyJhbGciOiJub25lIn0." + OAuth.Base64Url(Encoding.UTF8.GetBytes(claims)) + ".sig";
        Assert.Equal(email, OAuth.EmailFromIdToken(token));
        Assert.Equal("", OAuth.EmailFromIdToken("not-a-jwt"));
    }

    [Fact]
    public void Account_round_trips_encrypted()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kawaii-oauth-" + Guid.NewGuid().ToString("N"));
        try
        {
            OAuth.Save(dir, new OAuthAccount("google", "kiko@gmail.com", "1//refresh-token"));
            Assert.Equal(new OAuthAccount("google", "kiko@gmail.com", "1//refresh-token"), OAuth.Load(dir));
            Assert.DoesNotContain("refresh-token", File.ReadAllText(Path.Combine(dir, "mail.oauth")));
            OAuth.Delete(dir);
            Assert.Null(OAuth.Load(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
