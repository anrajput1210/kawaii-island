using System.IO;
using KawaiiIsland.Services.Mail;

namespace KawaiiIsland.Tests;

public sealed class MailTests
{
    private static MailHeader M(string id) => new(id, "Alex", "Hi", DateTimeOffset.Now);

    [Fact]
    public void Inbox_first_update_is_not_news_then_only_arrivals_count()
    {
        var inbox = new MailInbox();
        Assert.Empty(inbox.Update(new([M("1"), M("2")], 2)));            // already unread at start
        Assert.Equal(["3"], inbox.Update(new([M("3"), M("1"), M("2")], 3)).Select(m => m.Id));
        Assert.Empty(inbox.Update(new([M("3")], 1)));                    // read elsewhere: no new mail
        Assert.Equal(1, inbox.Current.Unread);
    }

    [Fact]
    public void Password_round_trips_through_dpapi_and_is_not_stored_in_plain_text()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kawaii-mail-" + Guid.NewGuid().ToString("N"));
        try
        {
            MailSecret.Save(dir, "app-pass-123");
            Assert.Equal("app-pass-123", MailSecret.Load(dir));
            Assert.DoesNotContain("app-pass-123", File.ReadAllText(Path.Combine(dir, "mail.secret")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
