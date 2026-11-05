using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.Lookups;
using MyGarage.Shared.Vehicles.StickerImport;
using MyGarage.Web.Lookups;
using MyGarage.Web.Tests.Common;
using MyGarage.Web.Vehicles;
using MyGarage.Web.Vehicles.StickerImport;

namespace MyGarage.Web.Tests.Vehicles.StickerImport;

public class StickerImportPrivacyTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(request.RequestUri.AbsolutePath == "/api/vehicles/sticker-extractions"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new StickerImportResultDto { Outcome = StickerImportOutcome.Filled, Make = "Ford", FieldsFilled = 1 }) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new LookupsDto { VehicleTypes = [], FuelTypes = [], Transmissions = [], Drivetrains = [] }) });
        }
    }

    private (HttpClient Client, StubHandler Handler) CreateAnonymousClient()
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
        return (client, handler);
    }

    private static MultipartFormDataContent Upload()
    {
        var content = new MultipartFormDataContent { { new StringContent("import-sticker"), "_handler" } };
        content.Add(new ByteArrayContent("%PDF-1.7 sticker"u8.ToArray()), StickerImportHandler.FileField, "sticker.pdf");
        return content;
    }

    [Theory]
    [InlineData("/vehicles/new")]
    [InlineData("/vehicles/6c4b0e4a-7f3a-4b3a-9a7b-0d0f6e7a1c11/edit")]
    public async Task ASignedOutPost_IsSentToSignIn_AndTheFileIsNeverRead(string path)
    {
        var (client, handler) = CreateAnonymousClient();

        var response = await client.PostAsync(path, Upload());

        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.Unauthorized, response.StatusCode.ToString());
        Assert.DoesNotContain("/api/vehicles/sticker-extractions", handler.Paths);
        Assert.Empty(handler.Paths);
    }

    [Fact]
    public async Task ASignedOutPageRequest_AlsoNeedsSignIn()
    {
        var (client, handler) = CreateAnonymousClient();

        var response = await client.GetAsync("/vehicles/new");

        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.Unauthorized, response.StatusCode.ToString());
        Assert.Empty(handler.Paths);
    }
}
