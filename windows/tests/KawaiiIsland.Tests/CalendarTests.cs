using System.Text.Json;
using KawaiiIsland.Services;
using KawaiiIsland.Services.Mail;

namespace KawaiiIsland.Tests;

public sealed class CalendarTests
{
    [Fact]
    public void Google_events_parse_timed_and_all_day()
    {
        using var doc = JsonDocument.Parse("""
            { "items": [
              { "id": "a", "summary": "Standup", "start": { "dateTime": "2026-10-01T15:00:00-04:00" } },
              { "id": "b", "start": { "date": "2026-10-02" } } ] }
            """);
        var items = CalendarService.ParseGoogle(doc.RootElement);
        Assert.Equal("Standup", items[0].Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 19, 0, 0, TimeSpan.Zero), items[0].Start!.Value.ToUniversalTime());
        Assert.False(items[0].AllDay);
        Assert.Equal("(No title)", items[1].Title);
        Assert.True(items[1].AllDay);
    }

    [Fact]
    public void Outlook_events_parse_as_utc()
    {
        using var doc = JsonDocument.Parse("""
            { "value": [ { "id": "x", "subject": "Lab", "isAllDay": false, "start": { "dateTime": "2026-10-01T18:30:00.0000000", "timeZone": "UTC" } } ] }
            """);
        var item = Assert.Single(CalendarService.ParseMicrosoft(doc.RootElement));
        Assert.Equal("Lab", item.Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 18, 30, 0, TimeSpan.Zero), item.Start);
    }

    [Fact]
    public void Calendar_scope_follows_the_signed_in_provider()
    {
        Assert.Contains("calendar.readonly", OAuthProvider.Google.CalendarScope);
        Assert.StartsWith("https://graph.microsoft.com/Calendars.Read", OAuthProvider.Microsoft.CalendarScope);
        Assert.Contains("Calendars.Read", OAuthProvider.Microsoft.ConsentScope);       // consented at sign-in
        Assert.DoesNotContain("graph.microsoft.com", OAuthProvider.Microsoft.Scope);   // IMAP token stays single-resource
    }

    [Theory]
    [InlineData("Gym 5:30 PM", "Gym", "5:30 PM")]
    [InlineData("Project sync 14:00", "Project sync", "14:00")]
    [InlineData("Read a book", "Read a book", "")]
    public void Tasks_split_name_and_time(string text, string name, string time) =>
        Assert.Equal((name, time), CalendarService.SplitTask(text));

    [Fact]
    public void Month_progress()
    {
        Assert.Equal(0, CalendarService.MonthProgress(new DateTime(2026, 10, 1)), 3);
        Assert.Equal(0.5, CalendarService.MonthProgress(new DateTime(2026, 9, 16)), 3);
    }

    [Theory]
    [InlineData(512, "512 B/s")]
    [InlineData(20480, "20 KB/s")]
    [InlineData(1572864, "1.5 MB/s")]
    public void Network_rate(double bps, string text) => Assert.Equal(text, SystemStats.Rate(bps));
}
