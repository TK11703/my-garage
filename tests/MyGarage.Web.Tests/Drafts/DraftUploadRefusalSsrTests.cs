using System.Net;
using System.Net.Http.Json;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Drafts;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Drafts;
using MyGarage.Web.Tests.Common;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Drafts;

// What the user sees after a post that is refused or only partly accepted, through the real static-SSR pipeline.
public class DraftUploadRefusalSsrTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly Guid VehicleId = Guid.NewGuid();

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpResponseMessage> Upload { get; set; } = () => new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = JsonContent.Create(new DraftUploadResultDto { SubmissionId = Guid.NewGuid(), AcceptedCount = 1 }),
        };

        public int UploadCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path == "/api/drafts/uploads")
            {
                UploadCalls++;
                return Task.FromResult(Upload());
            }

            if (request.Method == HttpMethod.Get && path == $"/api/vehicles/{VehicleId}")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new VehicleDetailDto
                    {
                        Id = VehicleId,
                        Make = "Ford",
                        Model = "F-150",
                        Year = 2020,
                        Ownership = OwnershipType.Purchased,
                        Tags = [],
                        Images = [],
                        CreatedAt = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
                        UpdatedAt = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
                    }),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private (HttpClient Client, StubHandler Handler) CreateClient()
    {
        var handler = new StubHandler();
        var client = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(new DraftUploadApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
                services.AddSingleton(new VehiclesApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }));
            }))
            .CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "Alice Garage");
        return (client, handler);
    }

    private static async Task<IHtmlDocument> ParseAsync(HttpResponseMessage response) =>
        await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());

    private static MultipartFormDataContent Form(IHtmlDocument page, params string[] fileNames)
    {
        var form = page.QuerySelectorAll("form").OfType<IHtmlFormElement>()
            .Single(candidate => candidate.QuerySelector("input[name='_handler'][value='upload-record']") is not null);
        var content = new MultipartFormDataContent();
        foreach (var input in form.QuerySelectorAll("input[type='hidden'][name]").OfType<IHtmlInputElement>())
        {
            content.Add(new StringContent(input.Value), input.Name!);
        }

        foreach (var name in fileNames)
        {
            var part = new ByteArrayContent(System.Text.Encoding.ASCII.GetBytes("%PDF-1.7 data"));
            part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            content.Add(part, DraftUploadHandler.FileField, name);
        }

        return content;
    }

    private static async Task<IHtmlDocument> PostAsync(HttpClient client, string url, params string[] fileNames)
    {
        var page = await ParseAsync(await client.GetAsync(url));
        var response = await client.PostAsync(url, Form(page, fileNames));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ParseAsync(response);
    }

    [Fact]
    public async Task WhenNothingIsAccepted_EachRefusedFileIsNamedWithItsReason_AndTheNamesAreEscaped()
    {
        var (client, handler) = CreateClient();
        handler.Upload = () => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new
            {
                title = "x",
                status = 400,
                errors = new Dictionary<string, string[]>
                {
                    ["files[0]"] = ["The file must be a PDF, JPEG, PNG, or WebP."],
                    ["files[1]"] = ["The file is empty."],
                },
            }),
        };

        var page = await PostAsync(client, "/drafts/upload", "<b onclick=x>notes.txt", "empty.pdf");

        var refusals = page.QuerySelectorAll("[data-testid='upload-refusal']");
        Assert.Equal(2, refusals.Length);
        Assert.Contains("<b onclick=x>notes.txt", refusals[0].TextContent);
        Assert.Empty(refusals[0].QuerySelectorAll("b"));
        Assert.Contains("The file must be a PDF, JPEG, PNG, or WebP.", refusals[0].TextContent);
        Assert.Contains("empty.pdf", refusals[1].TextContent);
        Assert.Contains("The file is empty.", refusals[1].TextContent);
        Assert.Equal("alert", page.QuerySelector("[data-testid='upload-refused']")!.GetAttribute("role"));
        Assert.Null(page.QuerySelector("[data-testid='upload-notice']"));
        Assert.NotNull(page.QuerySelector("form input[name='recordFiles']"));
    }

    [Fact]
    public async Task ChoosingNoFile_ShowsChooseAtLeastOneFile_AndNeverCallsTheApi()
    {
        var (client, handler) = CreateClient();

        var page = await PostAsync(client, "/drafts/upload");

        Assert.Equal(DraftUploadMessages.ChooseFile, page.QuerySelector("[data-testid='upload-message']")!.TextContent.Trim());
        Assert.Equal(0, handler.UploadCalls);
    }

    [Fact]
    public async Task TooManyFiles_ShowTheLimit_AndNeverCallTheApi()
    {
        var (client, handler) = CreateClient();

        var page = await PostAsync(client, "/drafts/upload", "1.pdf", "2.pdf", "3.pdf", "4.pdf", "5.pdf", "6.pdf");

        Assert.Equal(DraftUploadMessages.TooManyFiles, page.QuerySelector("[data-testid='upload-message']")!.TextContent.Trim());
        Assert.Equal(0, handler.UploadCalls);
    }

    [Fact]
    public async Task ARateLimitedUpload_SaysToTryAgainInAMinute()
    {
        var (client, handler) = CreateClient();
        handler.Upload = () => new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        var page = await PostAsync(client, "/drafts/upload", "a.pdf");

        var message = page.QuerySelector("[data-testid='upload-message']")!;
        Assert.Equal(DraftUploadMessages.RateLimited, message.TextContent.Trim());
        Assert.Contains("alert-warning", message.ClassName);
    }

    [Fact]
    public async Task AVehicleThatBecameUnknown_ShowsTheNotFoundMessageAndNoForm()
    {
        var (client, handler) = CreateClient();
        handler.Upload = () => new HttpResponseMessage(HttpStatusCode.NotFound);

        var page = await PostAsync(client, $"/drafts/upload?vehicleId={VehicleId}", "a.pdf");

        Assert.Contains(DraftUploadMessages.NotFound, page.Body!.TextContent);
        Assert.Null(page.QuerySelector("form input[name='recordFiles']"));
    }

    [Fact]
    public async Task AnOutage_ShowsTheStandardUnavailableAlert_WithoutAnyApiDetail()
    {
        var (client, handler) = CreateClient();
        handler.Upload = () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("secret internal detail") };

        var page = await PostAsync(client, "/drafts/upload", "a.pdf");

        Assert.NotNull(page.QuerySelector("[role='alert']"));
        Assert.DoesNotContain("secret internal detail", page.Body!.TextContent);
    }

    [Fact]
    public async Task APartialSuccess_StaysOnThePage_WithTheCountALinkToTheListAndTheRefusedFilesNamed()
    {
        var (client, handler) = CreateClient();
        handler.Upload = () => new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = JsonContent.Create(new DraftUploadResultDto
            {
                SubmissionId = Guid.NewGuid(),
                AcceptedCount = 2,
                Refused = [new DraftUploadRefusalDto { Index = 1, Reason = DraftUploadLimits.UnsupportedType }],
            }),
        };

        var page = await PostAsync(client, "/drafts/upload", "a.pdf", "notes.txt", "c.pdf");

        var notice = page.QuerySelector("[data-testid='upload-notice']")!;
        Assert.Equal("status", notice.GetAttribute("role"));
        Assert.Contains("2 files uploaded", notice.TextContent);
        Assert.Equal("/drafts", notice.QuerySelector("a")!.GetAttribute("href"));
        var refusal = Assert.Single(page.QuerySelectorAll("[data-testid='upload-refusal']"));
        Assert.Contains("notes.txt", refusal.TextContent);
        Assert.Contains(DraftUploadMessages.ForReason(DraftUploadLimits.UnsupportedType), refusal.TextContent);
    }
}
