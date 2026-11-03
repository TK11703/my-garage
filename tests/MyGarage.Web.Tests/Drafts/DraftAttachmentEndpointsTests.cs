using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Web.Drafts;
using MyGarage.Web.Tests.Common;

namespace MyGarage.Web.Tests.Drafts;

// The same-origin attachment proxy as wired into the real pipeline, with the API replaced by a stub.
public class DraftAttachmentEndpointsTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly Guid DraftId = Guid.NewGuid();
    private static readonly byte[] Bytes = "%PDF-1.7 fake"u8.ToArray();

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private HttpClient CreateClient(StubHandler handler, bool signedIn)
    {
        var client = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton(new DraftsApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }))))
            .CreateClient(new() { AllowAutoRedirect = false });
        if (signedIn)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "Alice Garage");
        }

        return client;
    }

    private static HttpResponseMessage Pdf(string contentType = "application/pdf")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Bytes) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return response;
    }

    [Fact]
    public async Task Anonymous_IsChallenged_AndTheApiIsNeverCalled()
    {
        var handler = new StubHandler(_ => Pdf());

        var response = await CreateClient(handler, signedIn: false).GetAsync($"/drafts/{DraftId}/attachment");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SignedIn_StreamsTheFile_WithTheApisContentType_AndSafeHeaders()
    {
        var handler = new StubHandler(_ => Pdf());

        var response = await CreateClient(handler, signedIn: true).GetAsync($"/drafts/{DraftId}/attachment");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Bytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.True(response.Headers.CacheControl.Private);
        Assert.Equal("inline", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
        Assert.Equal($"/api/drafts/{DraftId}/attachment", handler.Requests.Single().RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task AnImage_IsForwardedWithItsOwnContentType()
    {
        var handler = new StubHandler(_ => Pdf("image/png"));

        var response = await CreateClient(handler, signedIn: true).GetAsync($"/drafts/{DraftId}/attachment");

        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal("sandbox", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task ANotFoundFromTheApi_IsANotFound_RevealingNothing()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var response = await CreateClient(handler, signedIn: true).GetAsync($"/drafts/{DraftId}/attachment");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(Bytes, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AnOutage_IsA503()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var response = await CreateClient(handler, signedIn: true).GetAsync($"/drafts/{DraftId}/attachment");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task ARejectedToken_Is401()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var response = await CreateClient(handler, signedIn: true).GetAsync($"/drafts/{DraftId}/attachment");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ANonGuidId_IsNotRouted()
    {
        var handler = new StubHandler(_ => Pdf());

        var response = await CreateClient(handler, signedIn: true).GetAsync("/drafts/abc/attachment");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Serve_CalledDirectly_WritesTheExactBytes()
    {
        var handler = new StubHandler(_ => Pdf());
        var client = new DraftsApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") });
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services, Response = { Body = new MemoryStream() } };

        var result = await DraftAttachmentEndpoints.ServeAsync(DraftId, client, context, CancellationToken.None);
        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        var written = new MemoryStream();
        await context.Response.Body.CopyToAsync(written);
        Assert.Equal(Bytes, written.ToArray());
        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"]);
    }
}
