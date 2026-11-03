using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Web.Tests.Common;
using MyGarage.Web.VehicleDocuments;

namespace MyGarage.Web.Tests.VehicleDocuments;

// The kept-original proxy as wired into the real pipeline, with the API replaced by a stub.
public class DocumentContentEndpointsTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly Guid VehicleId = Guid.NewGuid();
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly byte[] Bytes = "%PDF-1.7 kept"u8.ToArray();

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
                services.AddSingleton(new VehicleDocumentsApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }))))
            .CreateClient(new() { AllowAutoRedirect = false });
        if (signedIn)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "Alice Garage");
        }

        return client;
    }

    private static HttpResponseMessage Pdf()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Bytes) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        return response;
    }

    [Fact]
    public async Task Anonymous_IsChallenged_AndTheApiIsNeverCalled()
    {
        var handler = new StubHandler(_ => Pdf());

        var response = await CreateClient(handler, signedIn: false).GetAsync($"/vehicles/{VehicleId}/documents/{DocumentId}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SignedIn_StreamsTheFile_WithSafeHeaders()
    {
        var handler = new StubHandler(_ => Pdf());

        var response = await CreateClient(handler, signedIn: true).GetAsync($"/vehicles/{VehicleId}/documents/{DocumentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Bytes, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.True(response.Headers.CacheControl.Private);
        Assert.Equal("inline", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
        Assert.Equal($"/api/vehicles/{VehicleId}/documents/{DocumentId}/content", handler.Requests.Single().RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ANotFoundFromTheApi_IsANotFound()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var response = await CreateClient(handler, signedIn: true).GetAsync($"/vehicles/{VehicleId}/documents/{DocumentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnOutage_IsA503()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var response = await CreateClient(handler, signedIn: true).GetAsync($"/vehicles/{VehicleId}/documents/{DocumentId}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
