using KawaiiIsland.Services;

namespace KawaiiIsland.Tests;

public sealed class CalendarTests
{
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
