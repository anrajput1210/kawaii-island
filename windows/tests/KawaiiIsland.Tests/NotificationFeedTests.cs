using KawaiiIsland.Services.Notifications;

namespace KawaiiIsland.Tests;

public sealed class NotificationFeedTests
{
    private readonly List<string> _muted = [];
    private NotificationFeed Feed() => new(() => _muted);
    private static IslandNotification N(uint id, string app = "Chat") => new(id, app, "title", "body", null, DateTimeOffset.Now);

    [Fact]
    public void Newest_first()
    {
        var f = Feed();
        f.Add(N(1)); f.Add(N(2));
        Assert.Equal([2u, 1u], f.Items.Select(i => i.Id));
    }

    [Fact]
    public void Keeps_last_20()
    {
        var f = Feed();
        for (uint i = 1; i <= 25; i++) f.Add(N(i));
        Assert.Equal(20, f.Items.Count);
        Assert.Equal(25u, f.Items[0].Id);
        Assert.Equal(6u, f.Items[^1].Id);
    }

    [Fact]
    public void Skips_duplicates_muted_apps_and_itself()
    {
        var f = Feed();
        _muted.Add("chat"); // case-insensitive
        Assert.False(f.Add(N(1, "Chat")));
        Assert.False(f.Add(N(2, NotificationFeed.Self)));
        Assert.True(f.Add(N(3, "Mail")));
        Assert.False(f.Add(N(3, "Mail")));
        Assert.Single(f.Items);
    }

    [Fact]
    public void Muting_removes_history_of_that_app()
    {
        var f = Feed();
        f.Add(N(1, "Chat")); f.Add(N(2, "Mail"));
        f.RemoveApp("chat");
        Assert.Equal("Mail", Assert.Single(f.Items).App);
    }

    [Fact]
    public void Body_is_first_two_non_empty_lines() =>
        Assert.Equal("a\nb", NotificationFeed.Body(["a", "", "b", "c"]));

    [Theory]
    [InlineData(20, "now")]
    [InlineData(300, "5m")]
    [InlineData(7200, "2h")]
    [InlineData(259200, "3d")]
    public void Relative_time(int seconds, string expected) => Assert.Equal(expected, NotificationFeed.Ago(TimeSpan.FromSeconds(seconds)));
}
