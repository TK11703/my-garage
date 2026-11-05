using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using MyGarage.Api.Tests.Common;

namespace MyGarage.Api.Tests.Vehicles.StickerImport;

public class StickerImportRateLimitTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Url = "/api/vehicles/sticker-extractions";

    public async Task InitializeAsync()
    {
        await factory.ResetDataAsync();
        factory.StickerExtractor.Reset();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private WebApplicationFactory<Program> Limited(int perMinute) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["StickerImport:RequestsPerMinute"] = perMinute.ToString() })));

    private static HttpClient ClientFor(WebApplicationFactory<Program> limited, string oid)
    {
        var client = limited.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwt.Create(claims: new Dictionary<string, object> { ["oid"] = oid }));
        return client;
    }

    private static MultipartFormDataContent Form()
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7 sticker"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "sticker.pdf");
        return content;
    }

    [Fact]
    public async Task TheRequestOverTheLimitInAMinute_Returns429WithRetryAfter_AndTheExtractorIsNotCalledForIt()
    {
        var limited = Limited(3);
        var client = ClientFor(limited, "rate-alice");

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(Url, Form())).StatusCode);
        }

        var rejected = await client.PostAsync(Url, Form());

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        Assert.Equal(3, factory.StickerExtractor.Calls.Count);
    }

    [Fact]
    public async Task AnotherUser_IsNotLimitedByTheFirstUsersCalls()
    {
        var limited = Limited(2);
        var alice = ClientFor(limited, "rate-alice-2");
        var bob = ClientFor(limited, "rate-bob-2");
        await alice.PostAsync(Url, Form());
        await alice.PostAsync(Url, Form());

        Assert.Equal(HttpStatusCode.TooManyRequests, (await alice.PostAsync(Url, Form())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.PostAsync(Url, Form())).StatusCode);
    }

    [Fact]
    public async Task TheDefaultLimitIs6AMinute()
    {
        Assert.Equal(6, new MyGarage.Api.Vehicles.StickerImport.StickerImportOptions().RequestsPerMinute);
        var limited = Limited(6);
        var client = ClientFor(limited, "rate-carol");

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 7; i++)
        {
            statuses.Add((await client.PostAsync(Url, Form())).StatusCode);
        }

        Assert.Equal(6, statuses.Count(status => status == HttpStatusCode.OK));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    [Fact]
    public async Task TheLimit_DoesNotApplyToTheOtherVehicleEndpoints()
    {
        var limited = Limited(1);
        var client = ClientFor(limited, "rate-dave");
        await client.PostAsync(Url, Form());
        await client.PostAsync(Url, Form());

        var response = await client.PostAsJsonAsync("/api/vehicles", new MyGarage.Shared.Vehicles.VehicleRequest { Make = "Ford", Model = "F-150", Year = 2020 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
