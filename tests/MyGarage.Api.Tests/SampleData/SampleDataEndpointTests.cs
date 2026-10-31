using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.SampleData;

namespace MyGarage.Api.Tests.SampleData;

public class SampleDataEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private HttpClient SignedIn() => factory.CreateClientWithToken(TestJwt.Create());

    [Fact]
    public async Task Default_ReturnsThreeFixedItems()
    {
        var response = await SignedIn().GetAsync("/api/sample-data");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<SampleItemDto>>();
        Assert.NotNull(items);
        Assert.Equal([1, 2, 3], items.Select(i => i.Id));
        Assert.Equal("Sample item 1", items[0].Name);
    }

    [Fact]
    public async Task Count_ReturnsRequestedNumberOfItems()
    {
        var items = await SignedIn().GetFromJsonAsync<List<SampleItemDto>>("/api/sample-data?count=5");

        Assert.Equal(5, items!.Count);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("11")]
    [InlineData("abc")]
    public async Task InvalidCount_Returns400ProblemDetails(string count)
    {
        var response = await SignedIn().GetAsync($"/api/sample-data?count={count}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task OutOfRangeCount_ReportsValidationErrorOnCount()
    {
        var response = await SignedIn().GetAsync("/api/sample-data?count=11");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("Count", out _));
    }

    [Fact]
    public async Task NoToken_Returns401()
    {
        var response = await factory.CreateClientWithToken(null).GetAsync("/api/sample-data");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRoute_Returns404ProblemDetails()
    {
        var response = await SignedIn().GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
