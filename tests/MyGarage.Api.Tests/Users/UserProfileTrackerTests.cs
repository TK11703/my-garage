using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyGarage.Api.Tests.Common;

namespace MyGarage.Api.Tests.Users;

public class UserProfileTrackerTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow;

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private (HttpClient Client, TestTimeProvider Clock) Host(string? token)
    {
        var clock = new TestTimeProvider(Start);
        var derived = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        var client = derived.CreateClient();
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return (client, clock);
    }

    private static string UserToken(string oid, string? email = "tracker@contoso.test", string? name = "Tracker Test") =>
        TestJwt.Create(
            includeUserClaims: false,
            claims: new Dictionary<string, object?>
            {
                ["oid"] = oid,
                ["email"] = email,
                ["name"] = name,
            }.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value!));

    private async Task<DateTimeOffset> LastSeenAsync(string ownerId) =>
        new((DateTime)(await factory.ExecuteScalarAsync("SELECT last_seen_at FROM user_profiles WHERE owner_id = @ownerId", ("ownerId", ownerId)))!);

    [Fact]
    public async Task AuthenticatedUserRequest_UpsertsTheProfileFromTheClaims()
    {
        var (client, _) = Host(UserToken("tracker-1", "tracker1@contoso.test", "Tracker One"));

        var response = await client.GetAsync("/api/users/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "tracker1@contoso.test|Tracker One",
            await factory.ExecuteScalarAsync("SELECT email || '|' || display_name FROM user_profiles WHERE owner_id = 'tracker-1'"));
        Assert.Equal(Start.UtcDateTime, (await LastSeenAsync("tracker-1")).UtcDateTime, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task PreferredUsername_IsUsedWhenThereIsNoEmailClaim()
    {
        var token = TestJwt.Create(includeUserClaims: false, claims: new Dictionary<string, object>
        {
            ["oid"] = "tracker-2", ["preferred_username"] = "upn@contoso.test", ["name"] = "Upn User",
        });
        var (client, _) = Host(token);

        await client.GetAsync("/api/users/me");

        Assert.Equal("upn@contoso.test", await factory.ExecuteScalarAsync("SELECT email FROM user_profiles WHERE owner_id = 'tracker-2'"));
    }

    [Fact]
    public async Task SecondRequestWithinAnHour_DoesNotWriteAgain_ButOneAfterAnHourDoes()
    {
        var (client, clock) = Host(UserToken("tracker-3"));
        await client.GetAsync("/api/users/me");

        clock.Advance(TimeSpan.FromMinutes(59));
        await client.GetAsync("/api/users/me");
        Assert.Equal(Start.UtcDateTime, (await LastSeenAsync("tracker-3")).UtcDateTime, TimeSpan.FromMilliseconds(1));

        clock.Advance(TimeSpan.FromMinutes(2));
        await client.GetAsync("/api/users/me");
        Assert.Equal(Start.AddMinutes(61).UtcDateTime, (await LastSeenAsync("tracker-3")).UtcDateTime, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task AppOnlyToken_WritesNoProfile()
    {
        var (client, _) = Host(TestJwt.Create(
            scope: null,
            includeUserClaims: false,
            claims: new Dictionary<string, object>
            {
                ["oid"] = TestJwt.WorkflowAppId, ["azp"] = TestJwt.WorkflowAppId, ["roles"] = new[] { TestJwt.EmailIngestRole }, ["email"] = "app@contoso.test",
            }));

        await IntakeClient.PostAsync(client, IntakeClient.NewEmail("stranger@nowhere.test"));
        await client.GetAsync("/api/users/me");

        Assert.Equal(0L, await factory.CountRowsAsync("user_profiles"));
    }

    [Fact]
    public async Task AnonymousRequest_WritesNoProfile()
    {
        var (client, _) = Host(null);

        var response = await client.GetAsync("/api/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0L, await factory.CountRowsAsync("user_profiles"));
    }

    [Fact]
    public async Task TokenWithoutAnEmail_StoresANullEmail()
    {
        var (client, _) = Host(UserToken("tracker-4", email: null, name: "No Email"));

        await client.GetAsync("/api/users/me");

        Assert.Null(await factory.ExecuteScalarAsync("SELECT email FROM user_profiles WHERE owner_id = 'tracker-4'"));
        Assert.Equal("No Email", await factory.ExecuteScalarAsync("SELECT display_name FROM user_profiles WHERE owner_id = 'tracker-4'"));
    }
}
