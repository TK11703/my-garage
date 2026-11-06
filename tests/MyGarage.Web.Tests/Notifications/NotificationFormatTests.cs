using MyGarage.Web.Notifications;

namespace MyGarage.Web.Tests.Notifications;

public class NotificationFormatTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(59, "just now")]
    [InlineData(60, "1 minute ago")]
    [InlineData(300, "5 minutes ago")]
    [InlineData(3599, "59 minutes ago")]
    [InlineData(3600, "1 hour ago")]
    [InlineData(10_800, "3 hours ago")]
    [InlineData(86_400, "1 day ago")]
    [InlineData(172_800, "2 days ago")]
    [InlineData(604_800, "Oct 27, 2026")]
    public void Relative_SaysHowLongAgo_ThenGivesTheDate(int secondsAgo, string expected) =>
        Assert.Equal(expected, NotificationFormat.Relative(Now - TimeSpan.FromSeconds(secondsAgo), Now));

    [Fact]
    public void ANotificationFromTheFuture_IsJustNow() =>
        Assert.Equal("just now", NotificationFormat.Relative(Now + TimeSpan.FromMinutes(5), Now));
}
