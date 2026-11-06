using System.Net;
using System.Net.Http.Json;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Reminders;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Reminders;
using MyGarage.Web.Tests.Common;
using MyGarage.Web.Vehicles;
using static MyGarage.Web.Tests.Reminders.ReminderTestData;

namespace MyGarage.Web.Tests.Reminders;

// The reminder pages through the real static-SSR pipeline: forms are posted as a browser would, with the antiforgery field.
public class ReminderSsrTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly Guid VehicleId = Guid.NewGuid();

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            Calls.Add((request.Method, path, body));

            if (request.Method == HttpMethod.Get && path == $"/api/vehicles/{VehicleId}")
            {
                return Json(new VehicleDetailDto
                {
                    Id = VehicleId, Make = "Ford", Model = "F-150", Year = 2020, Mileage = 42_000, Tags = [], Images = [],
                    CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
                });
            }

            if (path == $"/api/vehicles/{VehicleId}/reminders")
            {
                return request.Method == HttpMethod.Get ? Json(new List<ReminderDto>()) : Json(Reminder(VehicleId), HttpStatusCode.Created);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };
    }

    private (HttpClient Client, StubHandler Handler) CreateClient()
    {
        var handler = new StubHandler();
        var client = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(new RemindersApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
                services.AddSingleton(new VehiclesApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
            }))
            .CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "Alice Garage");
        return (client, handler);
    }

    private static async Task<IHtmlDocument> ParseAsync(HttpResponseMessage response) =>
        await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());

    // The named form's hidden fields (handler name and antiforgery token) plus the typed values, as a browser would send them.
    private static FormUrlEncodedContent PostedForm(IHtmlDocument page, string formName, bool withToken, params (string Name, string Value)[] values)
    {
        var form = page.QuerySelectorAll("form").OfType<IHtmlFormElement>()
            .Single(candidate => candidate.QuerySelector($"input[name='_handler'][value='{formName}']") is not null);
        var fields = new List<KeyValuePair<string, string>>();
        foreach (var input in form.QuerySelectorAll("input[type='hidden'][name]").OfType<IHtmlInputElement>())
        {
            if (withToken || !input.Name!.Contains("antiforgery", StringComparison.OrdinalIgnoreCase) && input.Name != "__RequestVerificationToken")
            {
                fields.Add(new(input.Name!, input.Value));
            }
        }

        fields.AddRange(values.Select(pair => new KeyValuePair<string, string>(pair.Name, pair.Value)));
        return new FormUrlEncodedContent(fields);
    }

    private static (string, string)[] OilChange() =>
    [
        ("Model.Name", "Oil change"),
        ("Model.AppliesTo", "Vehicle"),
        ("Model.IntervalMiles", "5000"),
        ("Model.TimeValue", "6"),
        ("Model.TimeUnitText", "months"),
        ("Model.LastDoneOn", "2026-10-01"),
        ("Model.LastDoneMileage", "40000"),
    ];

    [Fact]
    public async Task ThePage_RendersTheNamedForm_WithTheChosenPreset()
    {
        var (client, _) = CreateClient();

        var response = await client.GetAsync($"/vehicles/{VehicleId}/reminders/new?preset=oil-change");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await ParseAsync(response);
        Assert.NotNull(page.QuerySelector("form input[name='_handler'][value='create-reminder']"));
        Assert.Equal("Oil change", ((IHtmlInputElement)page.QuerySelector("#name")!).Value);
        Assert.Equal("5000", ((IHtmlInputElement)page.QuerySelector("#interval-miles")!).Value);
    }

    [Fact]
    public async Task APostedForm_IsSentToTheApi_AndTheUserIsRedirectedToTheRemindersTab()
    {
        var (client, handler) = CreateClient();
        var page = await ParseAsync(await client.GetAsync($"/vehicles/{VehicleId}/reminders/new"));

        var response = await client.PostAsync($"/vehicles/{VehicleId}/reminders/new", PostedForm(page, "create-reminder", withToken: true, OilChange()));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.IsAbsoluteUri ? response.Headers.Location.PathAndQuery : response.Headers.Location.OriginalString;
        Assert.Equal($"/vehicles/{VehicleId}?tab=reminders&saved=created", location);
        var call = Assert.Single(handler.Calls, call => call.Method == HttpMethod.Post);
        Assert.Contains("\"name\":\"Oil change\"", call.Body);
        Assert.Contains("\"intervalMiles\":5000", call.Body);
    }

    [Fact]
    public async Task APostWithoutTheAntiforgeryToken_IsRefused_AndNothingIsSent()
    {
        var (client, handler) = CreateClient();
        var page = await ParseAsync(await client.GetAsync($"/vehicles/{VehicleId}/reminders/new"));

        var response = await client.PostAsync($"/vehicles/{VehicleId}/reminders/new", PostedForm(page, "create-reminder", withToken: false, OilChange()));

        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain(handler.Calls, call => call.Method == HttpMethod.Post);
    }
}
