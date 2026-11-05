using System.Net;
using System.Net.Http.Json;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Drafts;
using MyGarage.Web.Drafts;
using MyGarage.Web.Tests.Common;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Drafts;

// The upload page through the real static-SSR pipeline: the form is posted as a browser would (multipart, with the antiforgery field).
public class DraftUploadSsrTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Body)> Calls { get; } = [];

        public Func<HttpResponseMessage> Upload { get; set; } = () => new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = JsonContent.Create(new DraftUploadResultDto { SubmissionId = Guid.NewGuid(), AcceptedCount = 1 }),
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            return request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == "/api/drafts/uploads"
                ? Upload()
                : new HttpResponseMessage(HttpStatusCode.NotFound);
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

    // The named form's own hidden fields (the handler name and the antiforgery token), as a browser would send them.
    private static MultipartFormDataContent UploadForm(IHtmlDocument page, bool withToken, params (string Name, byte[] Bytes)[] files)
    {
        var form = page.QuerySelectorAll("form").OfType<IHtmlFormElement>()
            .Single(candidate => candidate.QuerySelector("input[name='_handler'][value='upload-record']") is not null);
        var content = new MultipartFormDataContent();
        foreach (var input in form.QuerySelectorAll("input[type='hidden'][name]").OfType<IHtmlInputElement>())
        {
            if (withToken || !input.Name!.Contains("antiforgery", StringComparison.OrdinalIgnoreCase) && input.Name != "__RequestVerificationToken")
            {
                content.Add(new StringContent(input.Value), input.Name!);
            }
        }

        foreach (var (name, bytes) in files)
        {
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            content.Add(part, DraftUploadHandler.FileField, name);
        }

        return content;
    }

    private static byte[] Pdf() => System.Text.Encoding.ASCII.GetBytes("%PDF-1.7 invoice");

    [Fact]
    public async Task ThePage_RendersTheNamedForm()
    {
        var (client, _) = CreateClient();

        var response = await client.GetAsync("/drafts/upload");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await ParseAsync(response);
        Assert.NotNull(page.QuerySelector("form input[name='_handler'][value='upload-record']"));
        Assert.NotNull(page.QuerySelector("form input[name='recordFiles'][type='file'][multiple]"));
    }

    [Fact]
    public async Task APostedFile_IsSentToTheApi_AndTheUserIsRedirectedToTheDraftsListWithTheCountOnly()
    {
        var (client, handler) = CreateClient();
        var page = await ParseAsync(await client.GetAsync("/drafts/upload"));

        var response = await client.PostAsync("/drafts/upload", UploadForm(page, withToken: true, ("<script>alert(1)</script>.pdf", Pdf())));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/drafts?uploaded=1", response.Headers.Location!.IsAbsoluteUri ? response.Headers.Location.PathAndQuery : response.Headers.Location.OriginalString);
        var call = Assert.Single(handler.Calls, call => call.Path == "/api/drafts/uploads");
        Assert.Contains("%PDF-1.7 invoice", call.Body);
        Assert.DoesNotContain("alert(1)", call.Body);
    }

    [Fact]
    public async Task APostWithoutTheAntiforgeryToken_IsRefused_AndNothingIsSent()
    {
        var (client, handler) = CreateClient();
        var page = await ParseAsync(await client.GetAsync("/drafts/upload"));

        var response = await client.PostAsync("/drafts/upload", UploadForm(page, withToken: false, ("invoice.pdf", Pdf())));

        Assert.NotEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(response.IsSuccessStatusCode);
        Assert.DoesNotContain(handler.Calls, call => call.Path == "/api/drafts/uploads");
    }
}
