using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Web.Common;
using MyGarage.Web.Tests.Common;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class FeatureSuggestionEndpointsTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static (VehiclesApiClient Client, StubHandler Handler) CreateClient(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        return (new VehiclesApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }), handler);
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        return new DefaultHttpContext { RequestServices = services, Response = { Body = new MemoryStream() } };
    }

    private static async Task<(int Status, string Body, DefaultHttpContext Context)> SuggestAsync(string? query, VehiclesApiClient client)
    {
        var context = CreateHttpContext();
        var result = await FeatureSuggestionEndpoints.SuggestAsync(query, client, context, CancellationToken.None);
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync(), context);
    }

    private static HttpResponseMessage Names(params string[] names) => new(HttpStatusCode.OK) { Content = JsonContent.Create(names) };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankQuery_ReturnsAnEmptyListWithoutCallingTheApi(string? query)
    {
        var (client, handler) = CreateClient(_ => Names("sunroof"));

        var (status, body, context) = await SuggestAsync(query, client);

        Assert.Equal(200, status);
        Assert.Equal("[]", body);
        Assert.Empty(handler.Requests);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task Query_ReturnsTheNamesTheApiAnswersWith()
    {
        var (client, handler) = CreateClient(_ => Names("sunroof", "sun visor"));

        var (status, body, context) = await SuggestAsync("sun", client);

        Assert.Equal(200, status);
        Assert.Equal("""["sunroof","sun visor"]""", body);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/api/features", request.RequestUri!.AbsolutePath);
        Assert.Equal("?query=sun", request.RequestUri.Query);
    }

    [Fact]
    public async Task Query_IsTrimmedAndUrlEscapedBeforeItReachesTheApi()
    {
        var (client, handler) = CreateClient(_ => Names());

        await SuggestAsync("  a&b c#d=e  ", client);

        Assert.Equal("?query=a%26b%20c%23d%3De", Assert.Single(handler.Requests).RequestUri!.Query);
    }

    [Fact]
    public async Task ApiAnsweringWithNoBody_ReturnsAnEmptyList()
    {
        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var (status, body, _) = await SuggestAsync("sun", client);

        Assert.Equal(200, status);
        Assert.Equal("[]", body);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ApiFailure_ReturnsAnEmptyListAndNeverTheError(HttpStatusCode failure)
    {
        var (client, _) = CreateClient(_ => new HttpResponseMessage(failure));

        var (status, body, context) = await SuggestAsync("sun", client);

        Assert.Equal(200, status);
        Assert.Equal("[]", body);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task ApiOutage_ReturnsAnEmptyList()
    {
        var (client, _) = CreateClient(_ => throw new HttpRequestException("boom"));

        var (status, body, _) = await SuggestAsync("sun", client);

        Assert.Equal(200, status);
        Assert.Equal("[]", body);
    }

    [Fact]
    public async Task SuggestFeaturesAsync_ReturnsTheNamesFromTheApi()
    {
        var (client, handler) = CreateClient(_ => Names("sunroof"));

        var result = await client.SuggestFeaturesAsync("sun", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["sunroof"], result.Value!);
        Assert.Equal("/api/features?query=sun", Assert.Single(handler.Requests).RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task SuggestFeaturesAsync_EscapesTheQuery()
    {
        var (client, handler) = CreateClient(_ => Names());

        await client.SuggestFeaturesAsync("tow & hitch", CancellationToken.None);

        Assert.Equal("?query=tow%20%26%20hitch", Assert.Single(handler.Requests).RequestUri!.Query);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, GarageApiFailure.NotAccepted)]
    [InlineData(HttpStatusCode.Forbidden, GarageApiFailure.NotAccepted)]
    [InlineData(HttpStatusCode.ServiceUnavailable, GarageApiFailure.Unavailable)]
    public async Task SuggestFeaturesAsync_ThrowsAGarageApiExceptionWhenTheApiRefuses(HttpStatusCode status, GarageApiFailure expected)
    {
        var (client, _) = CreateClient(_ => new HttpResponseMessage(status));

        var exception = await Assert.ThrowsAsync<GarageApiException>(() => client.SuggestFeaturesAsync("sun", CancellationToken.None));

        Assert.Equal(expected, exception.Failure);
    }
}

public class FeatureSuggestionRouteTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private HttpClient CreateClient(bool signedIn)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        if (signedIn)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "Alice Garage");
        }

        return client;
    }

    [Theory]
    [InlineData("/features/suggest")]
    [InlineData("/features/suggest?query=sun")]
    public async Task Anonymous_IsChallengedAndNeverServedData(string route)
    {
        var response = await CreateClient(signedIn: false).GetAsync(route);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain("/features/suggest", response.Headers.Location!.AbsolutePath);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SignedIn_BlankQuery_ReturnsAnEmptyJsonListThatIsNotCached()
    {
        var response = await CreateClient(signedIn: true).GetAsync("/features/suggest");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }
}
