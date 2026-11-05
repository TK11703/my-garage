using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyGarage.Shared.Lookups;
using MyGarage.Shared.Vehicles;
using MyGarage.Shared.Vehicles.StickerImport;
using MyGarage.Web.Tests.Common;
using MyGarage.Web.Lookups;
using MyGarage.Web.Vehicles;
using MyGarage.Web.Vehicles.StickerImport;

namespace MyGarage.Web.Tests.Vehicles.StickerImport;

// The add and edit pages through the real static-SSR pipeline: the import form is posted as a browser would (multipart, with the
// antiforgery field), and the form it renders is posted back, which proves the sticker values and features survive to the save.
public class StickerImportSsrTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly Guid VehicleId = Guid.NewGuid();
    private static readonly Guid SunroofTagId = Guid.NewGuid();
    private static readonly Guid TowingTagId = Guid.NewGuid();
    private static readonly LookupItemDto Gasoline = new() { Id = Guid.NewGuid(), Name = "Gasoline", IsDefault = true, IsHidden = false };
    private static readonly LookupItemDto Automatic = new() { Id = Guid.NewGuid(), Name = "Automatic", IsDefault = true, IsHidden = false };
    private static readonly LookupItemDto FourWd = new() { Id = Guid.NewGuid(), Name = "4WD", IsDefault = true, IsHidden = false };
    private static readonly LookupItemDto Suv = new() { Id = Guid.NewGuid(), Name = "SUV", IsDefault = true, IsHidden = false };

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Body)> Calls { get; } = [];

        public Func<HttpResponseMessage> Extraction { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Sticker()) };

        // When set, saving a new vehicle is rejected with a StickerFeatures validation problem.
        public bool RejectSave { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;
            Calls.Add((request.Method, path, body));

            if (request.Method == HttpMethod.Get && path == "/api/lookups")
            {
                return Json(new LookupsDto { VehicleTypes = [Suv], FuelTypes = [Gasoline], Transmissions = [Automatic], Drivetrains = [FourWd] });
            }

            if (request.Method == HttpMethod.Get && path == $"/api/vehicles/{VehicleId}")
            {
                return Json(StoredVehicle());
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/notes", StringComparison.Ordinal))
            {
                return Json(new PagedResultDto<JournalNoteDto> { Items = [], Page = 1, PageSize = 10, TotalCount = 0, TotalPages = 0 });
            }

            if (request.Method == HttpMethod.Post && path == "/api/vehicles/sticker-extractions")
            {
                return Extraction();
            }

            if (request.Method == HttpMethod.Post && path == "/api/vehicles")
            {
                return RejectSave
                    ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = JsonContent.Create(new { title = "One or more validation errors occurred.", status = 400, errors = new Dictionary<string, string[]> { ["StickerFeatures"] = ["A vehicle can have at most 100 features."] } }),
                    }
                    : Json(StoredVehicle(), HttpStatusCode.Created);
            }

            if (request.Method == HttpMethod.Put && path == $"/api/vehicles/{VehicleId}")
            {
                return Json(StoredVehicle());
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };
    }

    private static VehicleDetailDto StoredVehicle() =>
        new()
        {
            Id = VehicleId,
            Make = "Chevrolet",
            Model = "Silverado",
            Year = 2018,
            Trim = "LT",
            Mileage = 54_321,
            Ownership = OwnershipType.Purchased,
            Color = "Silver",
            Tags = [new VehicleTagDto { Id = SunroofTagId, Name = "Sunroof" }, new VehicleTagDto { Id = TowingTagId, Name = "Towing Package" }],
            Images = [],
            CreatedAt = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
        };

    private static StickerImportResultDto Sticker() =>
        new()
        {
            Outcome = StickerImportOutcome.Filled,
            Year = 2024,
            Make = "Ford",
            Model = "F-150",
            Vin = "1FTFW1E50NFA00001",
            FuelTypeId = Gasoline.Id,
            TransmissionId = Automatic.Id,
            DrivetrainId = FourWd.Id,
            VehicleTypeText = "Crew Cab Pickup",
            Engine = "3.5L V6",
            Horsepower = 400,
            FieldsFilled = 9,
            Features =
            [
                new StickerFeatureDto { Name = "Panoramic Sunroof", ReplacesTagIds = [SunroofTagId], ReplacesNames = ["Sunroof"] },
                new StickerFeatureDto { Name = "Heated Seats" },
            ],
        };

    private (HttpClient Client, StubHandler Handler) CreateClient(bool signedIn = true)
    {
        var handler = new StubHandler();
        var client = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(new VehiclesApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
                services.AddSingleton(new LookupsApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
                services.AddSingleton(new StickerImportApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
            }))
            .CreateClient(new() { AllowAutoRedirect = false });
        if (signedIn)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "Alice Garage");
        }

        return (client, handler);
    }

    private static async Task<IHtmlDocument> ParseAsync(HttpResponseMessage response) =>
        await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());

    private static IHtmlFormElement FormNamed(IHtmlDocument document, string handler) =>
        document.QuerySelectorAll("form").OfType<IHtmlFormElement>().Single(form => form.QuerySelector($"input[name='_handler'][value='{handler}']") is not null);

    // What a browser would send for a form's named controls (the file is added separately).
    private static List<KeyValuePair<string, string>> Fields(IHtmlFormElement form)
    {
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
                case IHtmlInputElement { Type: "file" or "submit" or "button" }:
                    break;
                case IHtmlInputElement { Type: "checkbox" or "radio" } input:
                    if (input.IsChecked)
                    {
                        fields.Add(new(name, input.Value));
                    }

                    break;
                case IHtmlInputElement input:
                    fields.Add(new(name, input.GetAttribute("value") ?? string.Empty));
                    break;
                case IHtmlSelectElement select:
                    fields.Add(new(name, select.QuerySelector("option[selected]")?.GetAttribute("value") ?? select.QuerySelector("option")?.GetAttribute("value") ?? string.Empty));
                    break;
                case IHtmlTextAreaElement area:
                    fields.Add(new(name, area.Value));
                    break;
            }
        }

        return fields;
    }

    private static MultipartFormDataContent ImportForm(IHtmlDocument page, byte[]? file, string fileName = "sticker.pdf", string contentType = "application/pdf")
    {
        var content = new MultipartFormDataContent();
        foreach (var (name, value) in Fields(FormNamed(page, "import-sticker")))
        {
            content.Add(new StringContent(value), name);
        }

        if (file is not null)
        {
            var part = new ByteArrayContent(file);
            part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            content.Add(part, StickerImportHandler.FileField, fileName);
        }

        return content;
    }

    private static byte[] Pdf() => System.Text.Encoding.ASCII.GetBytes("%PDF-1.7 sticker");

    private static async Task<string> Describe(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return $"{(int)response.StatusCode}: {(body.Length > 2500 ? body[..2500] : body)}";
    }

    private static string ValueOf(IHtmlDocument page, string selector) =>
        page.QuerySelector(selector) switch
        {
            IHtmlInputElement input => input.Value,
            IHtmlSelectElement select => select.QuerySelector("option[selected]")?.GetAttribute("value") ?? string.Empty,
            _ => throw new InvalidOperationException($"No field matches {selector}."),
        };

    private static string[] ActivePanes(IHtmlDocument page) => [.. page.QuerySelectorAll(".tab-pane.active").Select(pane => pane.Id!)];

    [Fact]
    public async Task TheAddPage_OpensOnTheImportTab_WithItBeforeDetails()
    {
        var (client, _) = CreateClient();

        var response = await client.GetAsync("/vehicles/new");

        Assert.True(response.IsSuccessStatusCode, await Describe(response));
        var page = await ParseAsync(response);
        Assert.Equal(["Import from sticker", "Details"], page.QuerySelectorAll("ul.mg-tabs button[role='tab']").Select(tab => tab.TextContent.Trim()));
        Assert.Equal(["tab-import"], ActivePanes(page));
        Assert.NotNull(FormNamed(page, "import-sticker").QuerySelector("input[name='stickerFile']"));
        Assert.NotNull(FormNamed(page, "create-vehicle"));
    }

    [Fact]
    public async Task ImportingOnTheAddPage_FillsTheDetailsTab_WithoutARedirectOrQueryValues()
    {
        var (client, handler) = CreateClient();
        var page = await ParseAsync(await client.GetAsync("/vehicles/new"));

        var response = await client.PostAsync("/vehicles/new", ImportForm(page, Pdf()));

        Assert.True(response.IsSuccessStatusCode, await Describe(response));
        Assert.Null(response.Headers.Location);
        var result = await ParseAsync(response);
        Assert.Equal(["tab-details"], ActivePanes(result));
        Assert.Equal("2024", ValueOf(result, "#year"));
        Assert.Equal("Ford", ValueOf(result, "#make"));
        Assert.Equal("F-150", ValueOf(result, "#model"));
        Assert.Equal("1FTFW1E50NFA00001", ValueOf(result, "#vin"));
        Assert.Equal("3.5L V6", ValueOf(result, "#engine"));
        Assert.Equal("400", ValueOf(result, "#horsepower"));
        Assert.Equal(Gasoline.Id.ToString(), ValueOf(result, "#fuel-type"));
        Assert.Equal(Automatic.Id.ToString(), ValueOf(result, "#transmission"));
        Assert.Equal(FourWd.Id.ToString(), ValueOf(result, "#drivetrain"));
        Assert.Equal(string.Empty, ValueOf(result, "#vehicle-type"));
        Assert.Equal("The sticker says: Crew Cab Pickup", result.QuerySelector("[data-testid='sticker-hint-vehicle-type']")!.TextContent);
        Assert.Contains("Read 9 details and 2 features from the sticker.", result.QuerySelector("[data-testid='sticker-read-count']")!.TextContent);
        Assert.Equal(["Panoramic Sunroof", "Heated Seats"], result.QuerySelectorAll("[data-testid='sticker-feature'] label").Select(label => label.TextContent));
        var call = Assert.Single(handler.Calls, call => call.Path == "/api/vehicles/sticker-extractions");
        Assert.Contains("sticker", call.Body);
        Assert.DoesNotContain("vehicleId", call.Body);
    }

    [Fact]
    public async Task SavingAfterAnImportOnTheAddPage_SendsOnlyTheTickedFeatures()
    {
        var (client, handler) = CreateClient();
        var page = await ParseAsync(await client.GetAsync("/vehicles/new"));
        var imported = await ParseAsync(await client.PostAsync("/vehicles/new", ImportForm(page, Pdf())));

        var fields = Fields(FormNamed(imported, "create-vehicle"));
        fields.RemoveAll(field => field.Key == StickerFeatureChoice.IncludeField(1));
        var response = await client.PostAsync("/vehicles/new", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal($"/vehicles/{VehicleId}", response.Headers.Location!.AbsolutePath);
        var body = Assert.Single(handler.Calls, call => call.Method == HttpMethod.Post && call.Path == "/api/vehicles").Body;
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Ford", json.RootElement.GetProperty("make").GetString());
        var features = json.RootElement.GetProperty("stickerFeatures").EnumerateArray().ToList();
        var feature = Assert.Single(features);
        Assert.Equal("Panoramic Sunroof", feature.GetProperty("name").GetString());
        Assert.Equal(1, feature.GetProperty("replacesTagIds").GetArrayLength());
    }

    [Fact]
    public async Task ARejectedSaveAfterAnImport_KeepsTheProposedFeaturesAndTheirSelection()
    {
        var (client, handler) = CreateClient();
        var page = await ParseAsync(await client.GetAsync("/vehicles/new"));
        var imported = await ParseAsync(await client.PostAsync("/vehicles/new", ImportForm(page, Pdf())));
        var fields = Fields(FormNamed(imported, "create-vehicle"));
        fields.RemoveAll(field => field.Key == StickerFeatureChoice.IncludeField(1));
        handler.RejectSave = true;

        var response = await client.PostAsync("/vehicles/new", new FormUrlEncodedContent(fields));

        Assert.True(response.IsSuccessStatusCode, await Describe(response));
        var result = await ParseAsync(response);
        Assert.Equal(["tab-details"], ActivePanes(result));
        Assert.Equal(["Panoramic Sunroof", "Heated Seats"], result.QuerySelectorAll("[data-testid='sticker-feature'] label").Select(label => label.TextContent));
        Assert.Equal([true, false], result.QuerySelectorAll("[data-testid='sticker-feature'] input[type='checkbox']").Cast<IHtmlInputElement>().Select(box => box.IsChecked));
        Assert.Contains("A vehicle can have at most 100 features.", result.QuerySelector("[data-testid='sticker-features-error']")!.TextContent);
    }

    [Fact]
    public async Task ImportingAnotherFile_ReplacesTheValuesAndTheProposals()
    {
        var (client, handler) = CreateClient();
        var page = await ParseAsync(await client.GetAsync("/vehicles/new"));
        await client.PostAsync("/vehicles/new", ImportForm(page, Pdf()));
        handler.Extraction = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new StickerImportResultDto
            {
                Outcome = StickerImportOutcome.Filled,
                Make = "Toyota",
                FieldsFilled = 1,
                Features = [new StickerFeatureDto { Name = "Tow Hooks" }],
            }),
        };

        var response = await client.PostAsync("/vehicles/new", ImportForm(page, Pdf()));

        var result = await ParseAsync(response);
        Assert.Equal("Toyota", ValueOf(result, "#make"));
        Assert.Equal(["Tow Hooks"], result.QuerySelectorAll("[data-testid='sticker-feature'] label").Select(label => label.TextContent));
    }

    [Fact]
    public async Task TheEditPage_OpensOnDetails_WithImportListedFirst_AndTheImportTabOnRequest()
    {
        var (client, _) = CreateClient();

        var details = await ParseAsync(await client.GetAsync($"/vehicles/{VehicleId}/edit"));
        var import = await ParseAsync(await client.GetAsync($"/vehicles/{VehicleId}/edit?tab=import"));

        Assert.Equal(["Import from sticker", "Details", "Media", "Description"], details.QuerySelectorAll("ul.mg-tabs button[role='tab']").Select(tab => tab.TextContent.Trim()));
        Assert.Equal(["tab-details"], ActivePanes(details));
        Assert.Equal(["tab-import"], ActivePanes(import));
    }

    [Fact]
    public async Task ImportingOnTheEditPage_KeepsStoredValuesTheStickerLacks_ReplacesTheRest_AndShowsWhatItReplaces()
    {
        var (client, handler) = CreateClient();
        var page = await ParseAsync(await client.GetAsync($"/vehicles/{VehicleId}/edit"));

        var response = await client.PostAsync($"/vehicles/{VehicleId}/edit", ImportForm(page, Pdf()));

        Assert.True(response.IsSuccessStatusCode, await Describe(response));
        var result = await ParseAsync(response);
        Assert.Equal(["tab-details"], ActivePanes(result));
        Assert.Equal("Ford", ValueOf(result, "#make"));
        Assert.Equal("2024", ValueOf(result, "#year"));
        Assert.Equal("LT", ValueOf(result, "#trim"));
        Assert.Equal("54321", ValueOf(result, "#mileage"));
        Assert.Equal("Silver", ValueOf(result, "#color"));
        Assert.Equal("Replaces: Sunroof", result.QuerySelector("[data-testid='sticker-feature-replaces']")!.TextContent);
        var call = Assert.Single(handler.Calls, call => call.Path == "/api/vehicles/sticker-extractions");
        Assert.Contains(VehicleId.ToString(), call.Body);
    }

    [Fact]
    public async Task SavingAfterAnImportOnTheEditPage_SendsTheReplacedTagIds()
    {
        var (client, handler) = CreateClient();
        var page = await ParseAsync(await client.GetAsync($"/vehicles/{VehicleId}/edit"));
        var imported = await ParseAsync(await client.PostAsync($"/vehicles/{VehicleId}/edit", ImportForm(page, Pdf())));

        var fields = Fields(FormNamed(imported, "update-vehicle"));
        var response = await client.PostAsync($"/vehicles/{VehicleId}/edit", new FormUrlEncodedContent(fields));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var body = Assert.Single(handler.Calls, call => call.Method == HttpMethod.Put).Body;
        using var json = JsonDocument.Parse(body);
        var features = json.RootElement.GetProperty("stickerFeatures").EnumerateArray().ToList();
        Assert.Equal(["Panoramic Sunroof", "Heated Seats"], features.Select(feature => feature.GetProperty("name").GetString()));
        Assert.Equal([SunroofTagId.ToString()], features[0].GetProperty("replacesTagIds").EnumerateArray().Select(id => id.GetString()));
    }

    [Fact]
    public async Task UntickingAProposalOnTheEditPage_LeavesItsReplacedFeatureAlone()
    {
        var (client, handler) = CreateClient();
        var page = await ParseAsync(await client.GetAsync($"/vehicles/{VehicleId}/edit"));
        var imported = await ParseAsync(await client.PostAsync($"/vehicles/{VehicleId}/edit", ImportForm(page, Pdf())));

        var fields = Fields(FormNamed(imported, "update-vehicle"));
        fields.RemoveAll(field => field.Key == StickerFeatureChoice.IncludeField(0));
        await client.PostAsync($"/vehicles/{VehicleId}/edit", new FormUrlEncodedContent(fields));

        var body = Assert.Single(handler.Calls, call => call.Method == HttpMethod.Put).Body;
        using var json = JsonDocument.Parse(body);
        var feature = Assert.Single(json.RootElement.GetProperty("stickerFeatures").EnumerateArray());
        Assert.Equal("Heated Seats", feature.GetProperty("name").GetString());
        Assert.Equal(0, feature.GetProperty("replacesTagIds").GetArrayLength());
    }

    [Fact]
    public async Task NoDetailsFound_LeavesTheFormUnchangedAndStaysOnTheImportTab()
    {
        var (client, handler) = CreateClient();
        handler.Extraction = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new StickerImportResultDto { Outcome = StickerImportOutcome.NoDetailsFound }),
        };
        var page = await ParseAsync(await client.GetAsync("/vehicles/new"));

        var response = await client.PostAsync("/vehicles/new", ImportForm(page, Pdf()));

        var result = await ParseAsync(response);
        Assert.Equal(["tab-import"], ActivePanes(result));
        Assert.Equal(string.Empty, ValueOf(result, "#make"));
        Assert.Contains("No vehicle details were found in that file.", result.QuerySelector("[data-testid='sticker-no-details']")!.TextContent);
        Assert.Empty(result.QuerySelectorAll("[data-testid='sticker-feature']"));
    }

    [Fact]
    public async Task APostWithNoFile_SaysSoAndNeverCallsTheApi()
    {
        var (client, handler) = CreateClient();
        var page = await ParseAsync(await client.GetAsync("/vehicles/new"));

        var response = await client.PostAsync("/vehicles/new", ImportForm(page, null));

        var result = await ParseAsync(response);
        Assert.Equal("Choose a file first.", result.QuerySelector("[data-testid='sticker-message']")!.TextContent);
        Assert.Equal(["tab-import"], ActivePanes(result));
        Assert.DoesNotContain(handler.Calls, call => call.Path == "/api/vehicles/sticker-extractions");
    }

    [Fact]
    public async Task AnEditOfAVehicleTheApiDoesNotKnow_IsTheNotFoundPage()
    {
        var (client, handler) = CreateClient();
        var page = await ParseAsync(await client.GetAsync($"/vehicles/{VehicleId}/edit"));
        handler.Extraction = () => new HttpResponseMessage(HttpStatusCode.NotFound);

        var response = await client.PostAsync($"/vehicles/{VehicleId}/edit", ImportForm(page, Pdf()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("Chevrolet", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheAddPageNeverPutsTheVinInTheAddressBar()
    {
        var (client, _) = CreateClient();
        var page = await ParseAsync(await client.GetAsync("/vehicles/new"));

        var response = await client.PostAsync("/vehicles/new", ImportForm(page, Pdf()));

        Assert.Null(response.Headers.Location);
        Assert.DoesNotContain("1FTFW1E50NFA00001", response.RequestMessage!.RequestUri!.ToString());
    }

    [Theory]
    [InlineData(400, null, "That file is not a valid PDF, JPG, PNG, or WebP.")]
    [InlineData(413, null, "That file is larger than 10 MB.")]
    [InlineData(429, null, "You have started several imports in a row. Wait a minute and try again.")]
    [InlineData(503, "not_configured", "Reading stickers is not set up here. Enter the details by hand.")]
    [InlineData(503, "timeout", "The sticker could not be read right now. Nothing was changed; try again or enter the details by hand.")]
    [InlineData(503, "service_error", "The sticker could not be read right now. Nothing was changed; try again or enter the details by hand.")]
    [InlineData(503, null, "The sticker could not be read right now. Nothing was changed; try again or enter the details by hand.")]
    public async Task EachApiFailure_ShowsItsMessageOnTheImportTab_AndLeavesTheFormUnchanged(int status, string? code, string message)
    {
        var (client, handler) = CreateClient();
        handler.Extraction = () => new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = code is null ? null : JsonContent.Create(new { title = "x", status, code }),
        };
        var page = await ParseAsync(await client.GetAsync("/vehicles/new"));

        var response = await client.PostAsync("/vehicles/new", ImportForm(page, Pdf()));

        Assert.True(response.IsSuccessStatusCode, await Describe(response));
        var result = await ParseAsync(response);
        Assert.Equal(message, result.QuerySelector("[data-testid='sticker-message']")!.TextContent);
        Assert.Equal(["tab-import"], ActivePanes(result));
        Assert.Equal(string.Empty, ValueOf(result, "#make"));
        Assert.Empty(result.QuerySelectorAll("[data-testid='sticker-feature']"));
    }

    [Fact]
    public async Task AFailedImportOnTheEditPage_LeavesTheStoredVehicleInTheForm()
    {
        var (client, handler) = CreateClient();
        handler.Extraction = () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var page = await ParseAsync(await client.GetAsync($"/vehicles/{VehicleId}/edit"));

        var response = await client.PostAsync($"/vehicles/{VehicleId}/edit", ImportForm(page, Pdf()));

        var result = await ParseAsync(response);
        Assert.Equal(["tab-import"], ActivePanes(result));
        Assert.Equal("Chevrolet", ValueOf(result, "#make"));
        Assert.Contains("could not be read right now", result.QuerySelector("[data-testid='sticker-message']")!.TextContent);
    }

    [Fact]
    public async Task AFileOver10MB_IsRefusedByThePageBeforeTheApiIsCalled()
    {
        var (client, handler) = CreateClient();
        var page = await ParseAsync(await client.GetAsync("/vehicles/new"));

        var response = await client.PostAsync("/vehicles/new", ImportForm(page, new byte[(int)StickerImportLimits.MaxFileBytes + 1]));

        var result = await ParseAsync(response);
        Assert.Equal("That file is larger than 10 MB.", result.QuerySelector("[data-testid='sticker-message']")!.TextContent);
        Assert.DoesNotContain(handler.Calls, call => call.Path == "/api/vehicles/sticker-extractions");
    }

    [Theory]
    [InlineData("/vehicles/new", "Vehicle")]
    [InlineData("edit", "Chevrolet")]
    public async Task ASignInRejection_ShowsTheSignInAlertAboveTheFormWhichStays(string target, string expectedMake)
    {
        var (client, handler) = CreateClient();
        handler.Extraction = () => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        var url = target == "edit" ? $"/vehicles/{VehicleId}/edit" : target;
        var page = await ParseAsync(await client.GetAsync(url));

        var response = await client.PostAsync(url, ImportForm(page, Pdf()));

        var result = await ParseAsync(response);
        Assert.Equal(VehicleMessages.NotAccepted, result.QuerySelector(".alert.alert-warning")!.TextContent.Trim());
        var signIn = result.QuerySelector("a.btn-primary")!;
        Assert.Equal($"/authentication/login?returnUrl={Uri.EscapeDataString(url)}", signIn.GetAttribute("href"));
        Assert.NotNull(result.QuerySelector("#make"));
        Assert.Equal(target == "edit" ? expectedMake : string.Empty, ValueOf(result, "#make"));
    }

    [Fact]
    public async Task ANetworkFailure_ShowsTheUnavailableAlert_AndTheFormStays()
    {
        var (client, handler) = CreateClient();
        handler.Extraction = () => throw new HttpRequestException("boom");
        var page = await ParseAsync(await client.GetAsync("/vehicles/new"));

        var response = await client.PostAsync("/vehicles/new", ImportForm(page, Pdf()));

        var result = await ParseAsync(response);
        Assert.Equal(VehicleMessages.Unavailable, result.QuerySelector(".alert.alert-warning")!.TextContent.Trim());
        Assert.NotNull(result.QuerySelector("#make"));
    }

    [Fact]
    public async Task TheNoticesOfAPartialResult_AreListedInTheImportPane()
    {
        var (client, handler) = CreateClient();
        handler.Extraction = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new StickerImportResultDto
            {
                Outcome = StickerImportOutcome.Partial,
                Make = "Ford",
                FieldsFilled = 1,
                Notices =
                [
                    new StickerNoticeDto { Code = StickerNoticeCode.UnreadableValues, Message = "These values could not be used and were left empty: VIN." },
                    new StickerNoticeDto { Code = StickerNoticeCode.MultipleVehicles, Message = "The file seems to show more than one vehicle; only one was read." },
                    new StickerNoticeDto { Code = StickerNoticeCode.DuplicateVin, Message = "A vehicle with this VIN is already in your garage." },
                    new StickerNoticeDto { Code = StickerNoticeCode.FeaturesLeftOut, Message = "2 features were left out to stay within 100 per vehicle." },
                ],
            }),
        };
        var page = await ParseAsync(await client.GetAsync("/vehicles/new"));

        var response = await client.PostAsync("/vehicles/new", ImportForm(page, Pdf()));

        var result = await ParseAsync(response);
        Assert.Equal(["tab-details"], ActivePanes(result));
        Assert.Equal(
            [
                "These values could not be used and were left empty: VIN.",
                "The file seems to show more than one vehicle; only one was read.",
                "A vehicle with this VIN is already in your garage.",
                "2 features were left out to stay within 100 per vehicle.",
            ],
            result.QuerySelectorAll("#tab-import [data-testid='sticker-notices'] li").Select(item => item.TextContent));
    }

    [Fact]
    public async Task ASecondImportOnTheEditPage_StartsFromTheStoredVehicleAgain()
    {
        var (client, handler) = CreateClient();
        var page = await ParseAsync(await client.GetAsync($"/vehicles/{VehicleId}/edit"));
        await client.PostAsync($"/vehicles/{VehicleId}/edit", ImportForm(page, Pdf()));
        handler.Extraction = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new StickerImportResultDto { Outcome = StickerImportOutcome.Filled, Engine = "2.0L", FieldsFilled = 1 }),
        };

        var response = await client.PostAsync($"/vehicles/{VehicleId}/edit", ImportForm(page, Pdf()));

        var result = await ParseAsync(response);
        Assert.Equal("Chevrolet", ValueOf(result, "#make"));
        Assert.Equal("2.0L", ValueOf(result, "#engine"));
        Assert.Empty(result.QuerySelectorAll("[data-testid='sticker-feature']"));
    }

    [Fact]
    public async Task AnImport_NeverLogsTheStickersContentsTheFileNameOrTheBytes()
    {
        var entries = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var provider = new CapturingLoggerProvider(entries);
        var handler = new StubHandler
        {
            Extraction = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new StickerImportResultDto
                {
                    Outcome = StickerImportOutcome.Filled,
                    Make = "QZMAKE",
                    Vin = "1FTFW1E50NFA00001",
                    Engine = "QZENGINE",
                    FieldsFilled = 3,
                    Features = [new StickerFeatureDto { Name = "QZFEATURE" }],
                }),
            },
        };
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
                logging.AddProvider(provider);
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(new VehiclesApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
                services.AddSingleton(new LookupsApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
                services.AddSingleton(new StickerImportApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
            });
        });
        var client = host.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "Alice Garage");
        var page = await ParseAsync(await client.GetAsync("/vehicles/new"));

        var response = await client.PostAsync("/vehicles/new", ImportForm(page, System.Text.Encoding.ASCII.GetBytes("%PDF-1.7 QZBYTES"), "QZFILE-sticker.pdf"));

        Assert.True(response.IsSuccessStatusCode, await Describe(response));
        foreach (var entry in entries)
        {
            foreach (var marker in new[] { "QZMAKE", "QZENGINE", "QZFEATURE", "QZFILE", "QZBYTES", "1FTFW1E50NFA00001" })
            {
                Assert.DoesNotContain(marker, entry, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private sealed class CapturingLoggerProvider(System.Collections.Concurrent.ConcurrentQueue<string> entries) : Microsoft.Extensions.Logging.ILoggerProvider, Microsoft.Extensions.Logging.ILogger
    {
        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(' ', pairs.Select(pair => $"{pair.Key}={pair.Value}")) : string.Empty;
            entries.Enqueue($"{logLevel} {formatter(state, exception)} {values} {exception}");
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task AnImport_NeverWritesToTheUsersLookups()
    {
        var (client, handler) = CreateClient();
        var page = await ParseAsync(await client.GetAsync("/vehicles/new"));

        await client.PostAsync("/vehicles/new", ImportForm(page, Pdf()));

        Assert.DoesNotContain(handler.Calls, call => call.Path.StartsWith("/api/lookups", StringComparison.Ordinal) && call.Method != HttpMethod.Get);
    }
}
