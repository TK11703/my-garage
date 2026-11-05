using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MyGarage.Api.Tests.Common;

namespace MyGarage.Api.Tests.Drafts;

public class DraftUploadRateLimitTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Url = "/api/drafts/uploads";

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private WebApplicationFactory<Program> Limited(int perMinute) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["DraftUploads:RequestsPerMinute"] = perMinute.ToString() })));

    private static HttpClient ClientFor(WebApplicationFactory<Program> limited, string oid)
    {
        var client = limited.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.Create(claims: new Dictionary<string, object> { ["oid"] = oid }));
        return client;
    }

    private static MultipartFormDataContent Form()
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(IntakeClient.PdfBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "files", "invoice.pdf");
        return content;
    }

    [Fact]
    public async Task TheUploadOverTheLimitInAMinute_Returns429WithRetryAfter_AndStoresNothingForIt()
    {
        var client = ClientFor(Limited(2), "rate-alice");

        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync(Url, Form())).StatusCode);
        }

        var rejected = await client.PostAsync(Url, Form());

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        Assert.Equal(2L, await factory.CountRowsAsync("email_submissions"));
    }

    [Fact]
    public async Task AnotherUser_IsNotLimitedByTheFirstUsersUploads()
    {
        var limited = Limited(1);
        var alice = ClientFor(limited, "rate-alice-2");
        var bob = ClientFor(limited, "rate-bob-2");
        await alice.PostAsync(Url, Form());

        Assert.Equal(HttpStatusCode.TooManyRequests, (await alice.PostAsync(Url, Form())).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await bob.PostAsync(Url, Form())).StatusCode);
    }

    [Fact]
    public async Task TheDefaultLimit_IsTenAMinute_AndOtherDraftEndpointsAreNotLimitedByUploads()
    {
        var limited = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["DraftUploads:RequestsPerMinute"] = "1" })));
        var client = ClientFor(limited, "rate-alice-3");
        await client.PostAsync(Url, Form());

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync(Url, Form())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/drafts")).StatusCode);
        Assert.Equal(10, new MyGarage.Api.Drafts.Uploads.DraftUploadOptions().RequestsPerMinute);
    }
}
