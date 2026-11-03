using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Drafts;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Web.Drafts;
using MyGarage.Web.Tests.Common;

namespace MyGarage.Web.Tests.Drafts;

// The review page through the real static-SSR pipeline: the form it renders is posted back the way a browser would,
// which proves the field names map onto the form model (nested line items and parts included).
public class DraftReviewSsrTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly Guid DraftId = Guid.NewGuid();
    private static readonly Guid VehicleId = Guid.NewGuid();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            return respond(request);
        }
    }

    private static MaintenanceDraftDto Draft() =>
        new()
        {
            Id = DraftId,
            State = DraftState.NeedsReview,
            Kind = DraftKind.ServiceRecord,
            Proposal = new DraftProposal
            {
                Kind = DraftKind.ServiceRecord,
                ServiceRecord = new ServiceRecordProposal
                {
                    DateServiced = new DateOnly(2026, 10, 12),
                    OdometerReading = 45_210,
                    ShopOrPerson = "Joe's Garage",
                    DocumentTotal = 200.5m,
                    LineItems =
                    [
                        new ServiceLineItemRequest { Description = "Oil change", FlatCost = 40m },
                        new ServiceLineItemRequest
                        {
                            Description = "Brake job",
                            LaborHours = 1.5m,
                            LaborRate = 90m,
                            Parts = [new ServicePartRequest { Name = "Brake pad", Quantity = 2m, UnitPrice = 12.5m }],
                        },
                    ],
                },
            },
            Vehicle = new DraftVehicleDto
            {
                VehicleId = VehicleId,
                Match = VehicleMatch.Vin,
                Candidates = [new VehicleSuggestionDto { VehicleId = VehicleId, Name = "2020 Ford F-150", Match = VehicleMatch.Vin }],
            },
            Source = new DraftSourceDto { Kind = "attachment", FileName = "invoice.pdf", ContentType = "application/pdf", SizeBytes = 1000 },
            Email = new DraftEmailDto { ArrivedAt = new DateTimeOffset(2026, 11, 3, 15, 30, 0, TimeSpan.Zero), Sender = "shop@example.com", Subject = "Invoice" },
            Compatibility = new CompatibilityDto { Kind = DraftKind.ServiceRecord, IsAcceptable = true, Fields = [] },
            HasAttachment = true,
            ExpiresAt = new DateTimeOffset(2026, 12, 3, 15, 30, 0, TimeSpan.Zero),
        };

    private (HttpClient Client, StubHandler Handler) CreateClient()
    {
        var handler = new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == $"/api/drafts/{DraftId}")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Draft()) };
            }

            if (path.EndsWith("/compatibility", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Draft().Compatibility) };
            }

            if (path.EndsWith("/accept", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = JsonContent.Create(new AcceptedDraftDto { Kind = DraftKind.ServiceRecord, VehicleId = VehicleId, RecordId = Guid.NewGuid() }),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton(new DraftsApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }))))
            .CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "Alice Garage");
        return (client, handler);
    }

    private static async Task<string> Describe(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return $"{(int)response.StatusCode}: {(body.Length > 2500 ? body[..2500] : body)}";
    }

    private static async Task<IHtmlDocument> ParseAsync(HttpResponseMessage response) =>
        await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());

    // What a browser would submit for the form: every enabled named control, plus the button pressed.
    private static List<KeyValuePair<string, string>> SubmittedFields(IHtmlDocument document, string action)
    {
        var form = document.QuerySelector("form[data-testid='review-form']")!;
        var fields = new List<KeyValuePair<string, string>>();
        foreach (var control in form.QuerySelectorAll("input[name], select[name], textarea[name]"))
        {
            var name = control.GetAttribute("name")!;
            if (control.HasAttribute("disabled"))
            {
                continue;
            }

            switch (control)
            {
                case IHtmlInputElement { Type: "checkbox" or "radio" } input:
                    if (input.IsChecked)
                    {
                        fields.Add(new(name, input.Value));
                    }

                    break;
                case IHtmlInputElement { Type: "submit" or "button" }:
                    break;
                case IHtmlInputElement input:
                    fields.Add(new(name, input.GetAttribute("value") ?? string.Empty));
                    break;
                case IHtmlSelectElement select:
                    fields.Add(new(name, select.QuerySelector("option[selected]")?.GetAttribute("value") ?? select.QuerySelector("option")?.GetAttribute("value") ?? string.Empty));
                    break;
            }
        }

        fields.RemoveAll(field => field.Key == "action");
        fields.Add(new("action", action));
        return fields;
    }

    private static void Set(List<KeyValuePair<string, string>> fields, string name, string value)
    {
        var index = fields.FindIndex(field => field.Key == name);
        Assert.True(index >= 0, $"The form has no field named {name}.");
        fields[index] = new(name, value);
    }

    [Fact]
    public async Task TheRenderedForm_HasTheNamesTheModelBindsTo()
    {
        var (client, _) = CreateClient();

        var response = await client.GetAsync($"/drafts/{DraftId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fields = SubmittedFields(await ParseAsync(response), "save").Select(field => field.Key).ToList();
        foreach (var name in new[]
        {
            "Model.KindValue", "Model.VehicleId", "Model.Service.DateServiced", "Model.Service.OdometerReading", "Model.Service.ShopOrPerson",
            "Model.Service.DocumentTotal", "Model.Service.Lines[0].Description", "Model.Service.Lines[0].Pricing", "Model.Service.Lines[0].FlatCost",
            "Model.Service.Lines[1].LaborHours", "Model.Service.Lines[1].Parts[0].Name", "Model.Service.Lines[1].Parts[0].UnitPrice", "_handler",
        })
        {
            Assert.Contains(name, fields);
        }
    }

    [Fact]
    public async Task SubmittingTheForm_BindsTheEditedValues_IntoTheAcceptRequest_AndRedirectsToTheVehicle()
    {
        var (client, handler) = CreateClient();
        var page = await client.GetAsync($"/drafts/{DraftId}");
        var fields = SubmittedFields(await ParseAsync(page), "save");
        Set(fields, "Model.Service.OdometerReading", "46000");
        Set(fields, "Model.Service.Lines[0].Description", "Oil and filter");
        Set(fields, "Model.Service.Lines[1].Parts[0].Quantity", "3");

        var response = await client.PostAsync($"/drafts/{DraftId}", new FormUrlEncodedContent(fields));

        Assert.True(response.StatusCode == HttpStatusCode.Redirect, await Describe(response));
        Assert.StartsWith($"/vehicles/{VehicleId}?tab=service-records&saved=draft", response.Headers.Location!.PathAndQuery);
        var accept = handler.Calls.Single(call => call.Path.EndsWith("/accept", StringComparison.Ordinal));
        var body = JsonDocument.Parse(accept.Body).RootElement;
        Assert.Equal(VehicleId, body.GetProperty("vehicleId").GetGuid());
        var section = body.GetProperty("proposal").GetProperty("serviceRecord");
        Assert.Equal(46000, section.GetProperty("odometerReading").GetInt32());
        Assert.Equal("Joe's Garage", section.GetProperty("shopOrPerson").GetString());
        Assert.Equal(200.5m, section.GetProperty("documentTotal").GetDecimal());
        var lines = section.GetProperty("lineItems");
        Assert.Equal(2, lines.GetArrayLength());
        Assert.Equal("Oil and filter", lines[0].GetProperty("description").GetString());
        Assert.Equal(40m, lines[0].GetProperty("flatCost").GetDecimal());
        Assert.Equal(JsonValueKind.Null, lines[0].GetProperty("laborHours").ValueKind);
        Assert.Equal(1.5m, lines[1].GetProperty("laborHours").GetDecimal());
        Assert.Equal(JsonValueKind.Null, lines[1].GetProperty("flatCost").ValueKind);
        Assert.Equal(3m, lines[1].GetProperty("parts")[0].GetProperty("quantity").GetDecimal());
        Assert.Equal(12.5m, lines[1].GetProperty("parts")[0].GetProperty("unitPrice").GetDecimal());
    }

    [Fact]
    public async Task AddLine_RerendersTheFormWithAnotherBlankLine()
    {
        var (client, handler) = CreateClient();
        var page = await client.GetAsync($"/drafts/{DraftId}");
        var fields = SubmittedFields(await ParseAsync(page), "add-line");

        var response = await client.PostAsync($"/drafts/{DraftId}", new FormUrlEncodedContent(fields));

        Assert.True(response.StatusCode == HttpStatusCode.OK, await Describe(response));
        var document = await ParseAsync(response);
        Assert.Equal(3, document.QuerySelectorAll("[data-testid='line-item']").Length);
        Assert.Contains(handler.Calls, call => call.Path.EndsWith("/compatibility", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Calls, call => call.Path.EndsWith("/accept", StringComparison.Ordinal));
    }
}
