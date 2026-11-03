using System.Net;
using System.Net.Http.Json;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Notifications;

namespace MyGarage.Api.Tests.Notifications;

public class NotificationsEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient AliceClient() => factory.CreateClientForUser(Alice, "alice@contoso.test");

    private async Task<Guid> SeedDraftAsync(string ownerId, string state = "needs_review")
    {
        var submission = await factory.InsertSubmissionAsync(ownerId, status: "ready");
        return await factory.InsertDraftAsync(ownerId, submission, state, state == "manual_entry" ? null : "service_record");
    }

    private async Task<bool> IsReadAsync(Guid notificationId) =>
        await factory.ExecuteScalarAsync("SELECT read_at FROM notifications WHERE id = @id", ("id", notificationId)) is not null;

    [Fact]
    public async Task List_DefaultsToTheLatestFive_NewestFirst_AndOnlyTheCallers()
    {
        var now = DateTimeOffset.UtcNow;
        var ids = new List<Guid>();
        for (var index = 0; index < 7; index++)
        {
            ids.Add(await factory.InsertNotificationAsync(Alice, await SeedDraftAsync(Alice), createdAt: now.AddMinutes(-index)));
        }

        await factory.InsertNotificationAsync(Bob, await SeedDraftAsync(Bob), createdAt: now);

        var response = await AliceClient().GetAsync("/api/notifications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<List<NotificationDto>>())!;
        Assert.Equal(ids.Take(5), items.Select(item => item.Id));
        Assert.All(items, item =>
        {
            Assert.Equal("draft_ready", item.Kind);
            Assert.Equal("New draft service record ready to review", item.Title);
            Assert.NotNull(item.DraftId);
            Assert.False(item.IsRead);
        });
        var more = (await (await AliceClient().GetAsync("/api/notifications?take=7")).Content.ReadFromJsonAsync<List<NotificationDto>>())!;
        Assert.Equal(7, more.Count);
    }

    [Fact]
    public async Task List_UnreadOnlyLeavesOutReadOnes_AndTakeIsBounded()
    {
        var unread = await factory.InsertNotificationAsync(Alice, await SeedDraftAsync(Alice));
        var read = await factory.InsertNotificationAsync(Alice, await SeedDraftAsync(Alice), read: true);

        var all = (await (await AliceClient().GetAsync("/api/notifications")).Content.ReadFromJsonAsync<List<NotificationDto>>())!;
        var onlyUnread = (await (await AliceClient().GetAsync("/api/notifications?unreadOnly=true")).Content.ReadFromJsonAsync<List<NotificationDto>>())!;

        Assert.Equal(2, all.Count);
        Assert.True(all.Single(item => item.Id == read).IsRead);
        Assert.Equal([unread], onlyUnread.Select(item => item.Id));
        Assert.Equal(HttpStatusCode.BadRequest, (await AliceClient().GetAsync("/api/notifications?take=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AliceClient().GetAsync("/api/notifications?take=51")).StatusCode);
    }

    [Fact]
    public async Task UnreadCount_ReturnsTheUnreadNotificationsAndTheDraftsAwaitingReview()
    {
        await factory.InsertNotificationAsync(Alice, await SeedDraftAsync(Alice));
        await factory.InsertNotificationAsync(Alice, await SeedDraftAsync(Alice, "manual_entry"));
        await factory.InsertNotificationAsync(Alice, await SeedDraftAsync(Alice, "included"), read: true);
        await factory.InsertNotificationAsync(Bob, await SeedDraftAsync(Bob));

        var response = await AliceClient().GetAsync("/api/notifications/unread-count");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var counts = (await response.Content.ReadFromJsonAsync<UnreadCountDto>())!;
        Assert.Equal(2, counts.Unread);
        Assert.Equal(2, counts.DraftsAwaitingReview);
    }

    [Fact]
    public async Task UnreadCount_ForAUserWithNothing_IsZero()
    {
        var counts = (await (await AliceClient().GetAsync("/api/notifications/unread-count")).Content.ReadFromJsonAsync<UnreadCountDto>())!;

        Assert.Equal(0, counts.Unread);
        Assert.Equal(0, counts.DraftsAwaitingReview);
    }

    [Fact]
    public async Task MarkRead_Returns204ForTheCallersNotification_AndIs404ForAnotherUsersOrUnknown()
    {
        var mine = await factory.InsertNotificationAsync(Alice, await SeedDraftAsync(Alice));
        var theirs = await factory.InsertNotificationAsync(Bob, await SeedDraftAsync(Bob));

        Assert.Equal(HttpStatusCode.NoContent, (await AliceClient().PostAsync($"/api/notifications/{mine}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AliceClient().PostAsync($"/api/notifications/{theirs}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AliceClient().PostAsync($"/api/notifications/{Guid.NewGuid()}/read", null)).StatusCode);

        Assert.True(await IsReadAsync(mine));
        Assert.False(await IsReadAsync(theirs));
    }

    [Fact]
    public async Task ReadAll_Returns204_AndTouchesOnlyTheCallersRows()
    {
        var first = await factory.InsertNotificationAsync(Alice, await SeedDraftAsync(Alice));
        var second = await factory.InsertNotificationAsync(Alice, await SeedDraftAsync(Alice));
        var theirs = await factory.InsertNotificationAsync(Bob, await SeedDraftAsync(Bob));

        var response = await AliceClient().PostAsync("/api/notifications/read-all", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(await IsReadAsync(first));
        Assert.True(await IsReadAsync(second));
        Assert.False(await IsReadAsync(theirs));
    }

    [Fact]
    public async Task EveryRoute_Is401WithoutAToken_AndRefusesTheWorkflowToken()
    {
        var id = Guid.NewGuid();
        (HttpMethod Method, string Path)[] routes =
        [
            (HttpMethod.Get, "/api/notifications"),
            (HttpMethod.Get, "/api/notifications/unread-count"),
            (HttpMethod.Post, $"/api/notifications/{id}/read"),
            (HttpMethod.Post, "/api/notifications/read-all"),
        ];
        var anonymous = factory.CreateClientWithToken(null);
        var workflow = factory.CreateClientForWorkflow();

        foreach (var (method, path) in routes)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(new HttpRequestMessage(method, path))).StatusCode);
            var status = (await workflow.SendAsync(new HttpRequestMessage(method, path))).StatusCode;
            Assert.True(status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized, $"{method} {path} answered {(int)status}");
        }
    }
}
