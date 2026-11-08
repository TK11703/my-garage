using System.Net;
using System.Net.Http.Json;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Tests.Common;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

// The archive and restore forms through the real static-SSR pipeline, posted as a browser would with the antiforgery field.
public class VehicleArchiveSsrTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly Guid VehicleId = Guid.NewGuid();

    private sealed class StubHandler(bool archived) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path)> Calls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Calls.Add((request.Method, path));
            if (path == $"/api/vehicles/{VehicleId}" && request.Method == HttpMethod.Get)
            {
                return Task.FromResult(Json(Detail(archived)));
            }

            if (request.Method == HttpMethod.Post && path == $"/api/vehicles/{VehicleId}/archive")
            {
                return Task.FromResult(Json(Detail(true)));
            }

            if (request.Method == HttpMethod.Post && path == $"/api/vehicles/{VehicleId}/restore")
            {
                return Task.FromResult(Json(Detail(false)));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static VehicleDetailDto Detail(bool isArchived) =>
            new()
            {
                Id = VehicleId, Make = "Ford", Model = "F-150", Year = 2020, Tags = [], Images = [],
                CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
                ArchivedAt = isArchived ? DateTimeOffset.UtcNow : null,
            };

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private (HttpClient Client, StubHandler Handler) CreateClient(bool archived)
    {
        var handler = new StubHandler(archived);
        var client = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(new VehiclesApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
                // Tab components use other typed clients; send them to the stub too, without token acquisition.
                services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(handlerBuilder =>
                {
                    handlerBuilder.AdditionalHandlers.Clear();
                    handlerBuilder.PrimaryHandler = handler;
                }));
            }))
            .CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "Alice Garage");
        return (client, handler);
    }

    private static async Task<IHtmlDocument> ParseAsync(HttpResponseMessage response) =>
        await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());

    private static FormUrlEncodedContent PostedForm(IHtmlDocument page, string formName, bool withToken)
    {
        var form = page.QuerySelectorAll("form").OfType<IHtmlFormElement>()
            .SingleOrDefault(candidate => candidate.QuerySelector($"input[name='_handler'][value='{formName}']") is not null)
            ?? throw new InvalidOperationException(page.Body?.TextContent.Trim());
        var fields = form.QuerySelectorAll("input[type='hidden'][name]").OfType<IHtmlInputElement>()
            .Where(input => withToken || (!input.Name!.Contains("antiforgery", StringComparison.OrdinalIgnoreCase) && input.Name != "__RequestVerificationToken"))
            .Select(input => new KeyValuePair<string, string>(input.Name!, input.Value));
        return new FormUrlEncodedContent(fields);
    }

    private static string LocationOf(HttpResponseMessage response) =>
        response.Headers.Location!.IsAbsoluteUri ? response.Headers.Location.PathAndQuery : response.Headers.Location.OriginalString;

    [Fact]
    public async Task APostedArchiveForm_CallsTheApi_AndRedirectsToTheVehicleWithANotice()
    {
        var (client, handler) = CreateClient(archived: false);
        var page = await ParseAsync(await client.GetAsync($"/vehicles/{VehicleId}"));

        var response = await client.PostAsync($"/vehicles/{VehicleId}", PostedForm(page, "archive-vehicle", withToken: true));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/vehicles/{VehicleId}?saved=archived", LocationOf(response));
        Assert.Contains(handler.Calls, call => call.Method == HttpMethod.Post && call.Path == $"/api/vehicles/{VehicleId}/archive");
    }

    [Fact]
    public async Task APostedRestoreForm_CallsTheApi_AndRedirectsToTheVehicleWithANotice()
    {
        var (client, handler) = CreateClient(archived: true);
        var page = await ParseAsync(await client.GetAsync($"/vehicles/{VehicleId}"));

        var response = await client.PostAsync($"/vehicles/{VehicleId}", PostedForm(page, "restore-vehicle", withToken: true));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/vehicles/{VehicleId}?saved=restored", LocationOf(response));
        Assert.Contains(handler.Calls, call => call.Method == HttpMethod.Post && call.Path == $"/api/vehicles/{VehicleId}/restore");
    }

    [Theory]
    [InlineData(false, "archive-vehicle")]
    [InlineData(true, "restore-vehicle")]
    public async Task APostWithoutTheAntiforgeryToken_IsRefused_AndNothingIsSent(bool archived, string formName)
    {
        var (client, handler) = CreateClient(archived);
        var page = await ParseAsync(await client.GetAsync($"/vehicles/{VehicleId}"));

        var response = await client.PostAsync($"/vehicles/{VehicleId}", PostedForm(page, formName, withToken: false));

        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain(handler.Calls, call => call.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task AnArchivedVehiclePage_ShowsTheArchivedAlert_AndNoEditLink()
    {
        var (client, _) = CreateClient(archived: true);

        var page = await ParseAsync(await client.GetAsync($"/vehicles/{VehicleId}?saved=archived"));

        Assert.NotNull(page.QuerySelector("[data-testid='archived-alert']"));
        Assert.NotNull(page.QuerySelector("[data-testid='vehicle-saved'][role='status']"));
        Assert.Null(page.QuerySelector("a[data-testid='edit-vehicle']"));
    }
}
