using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Vehicles;
using MyGarage.Shared.Vehicles.StickerImport;

namespace MyGarage.Api.Tests.Vehicles.StickerImport;

public class StickerImportEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";
    private const string Url = "/api/vehicles/sticker-extractions";

    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public async Task InitializeAsync()
    {
        await factory.ResetDataAsync();
        factory.StickerExtractor.Reset();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static byte[] Pdf(int length = 200) => [.. Encoding.ASCII.GetBytes("%PDF-1.7\n"), .. new byte[Math.Max(0, length - 9)]];

    private static byte[] Png(int length = 200) => [.. PngHeader, .. new byte[Math.Max(0, length - PngHeader.Length)]];

    private static MultipartFormDataContent Form(byte[]? bytes, string fileName = "sticker.pdf", string claimedType = "application/pdf", string? vehicleId = null)
    {
        var content = new MultipartFormDataContent();
        if (bytes is not null)
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue(claimedType);
            content.Add(file, "file", fileName);
        }

        if (vehicleId is not null)
        {
            content.Add(new StringContent(vehicleId), "vehicleId");
        }

        return content;
    }

    private static async Task<VehicleDetailDto> CreateVehicleAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/vehicles", new VehicleRequest { Make = "Ford", Model = "F-150", Year = 2020 });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VehicleDetailDto>())!;
    }

    private static async Task AddTagAsync(HttpClient client, Guid vehicleId, string name) =>
        (await client.PostAsJsonAsync($"/api/vehicles/{vehicleId}/tags", new AddVehicleTagRequest { Name = name })).EnsureSuccessStatusCode();

    [Fact]
    public async Task APdf_ReturnsTheMappedValuesAndTheOwnersLookupIds()
    {
        var client = factory.CreateClientForUser(Alice);

        var response = await client.PostAsync(Url, Form(Pdf()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<StickerImportResultDto>())!;
        Assert.Equal(StickerImportOutcome.Filled, result.Outcome);
        Assert.Equal(2024, result.Year);
        Assert.Equal("Ford", result.Make);
        Assert.Equal("1FTFW1E50NFA00001", result.Vin);
        Assert.NotNull(result.FuelTypeId);
        Assert.NotNull(result.TransmissionId);
        Assert.NotNull(result.DrivetrainId);
        var call = Assert.Single(factory.StickerExtractor.Calls);
        Assert.Equal("application/pdf", call.ContentType);
        Assert.Equal(200, call.Bytes);
        Assert.Null(call.ExistingFeatures);
        Assert.Equal(["Heated Seats", "Panoramic Sunroof"], result.Features.Select(feature => feature.Name));
        Assert.All(result.Features, feature => Assert.Empty(feature.ReplacesTagIds));
    }

    [Fact]
    public async Task APng_IsAccepted_AndTheTypeComesFromTheContentNotTheDeclaredType()
    {
        var client = factory.CreateClientForUser(Alice);

        var response = await client.PostAsync(Url, Form(Png(), "photo.pdf", "application/pdf"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", Assert.Single(factory.StickerExtractor.Calls).ContentType);
    }

    [Fact]
    public async Task OnAnEdit_TheVehiclesFeatureNamesReachTheExtractor_AndDuplicatesComeBackAsReplaced()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        await AddTagAsync(client, vehicle.Id, "Sunroof");
        await AddTagAsync(client, vehicle.Id, "Towing Package");
        var tags = (await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{vehicle.Id}"))!.Tags;

        var response = await client.PostAsync(Url, Form(Pdf(), vehicleId: vehicle.Id.ToString()));

        var result = (await response.Content.ReadFromJsonAsync<StickerImportResultDto>())!;
        var call = Assert.Single(factory.StickerExtractor.Calls);
        Assert.Equal(tags.Select(tag => tag.Name), call.ExistingFeatures);
        var panoramic = Assert.Single(result.Features, feature => feature.Name == "Panoramic Sunroof");
        var sunroofTag = tags.Single(tag => tag.Name == "Sunroof");
        Assert.Equal([sunroofTag.Id], panoramic.ReplacesTagIds);
        Assert.Equal(["Sunroof"], panoramic.ReplacesNames);
    }

    [Fact]
    public async Task AVehicleThatDoesNotExistOrBelongsToAnotherUser_Returns404AndTheExtractorIsNotCalled()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);

        var theirs = await bob.PostAsync(Url, Form(Pdf(), vehicleId: vehicle.Id.ToString()));
        var missing = await bob.PostAsync(Url, Form(Pdf(), vehicleId: Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.NotFound, theirs.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Empty(factory.StickerExtractor.Calls);
    }

    [Fact]
    public async Task NoFile_Returns400()
    {
        var client = factory.CreateClientForUser(Alice);

        var response = await client.PostAsync(Url, Form(null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.StickerExtractor.Calls);
    }

    [Fact]
    public async Task Nothing_IsStored_ByAnExtraction()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var features = await factory.CountFeaturesAsync();

        await client.PostAsync(Url, Form(Pdf(), vehicleId: vehicle.Id.ToString()));

        Assert.Equal(1L, await factory.ExecuteScalarAsync("SELECT count(*) FROM vehicles"));
        Assert.Equal(features, await factory.CountFeaturesAsync());
        Assert.Equal(0L, await factory.ExecuteScalarAsync("SELECT count(*) FROM vehicle_tags"));
        Assert.Empty(await BlobNamesAsync());
    }

    private async Task<List<string>> BlobNamesAsync()
    {
        var names = new List<string>();
        foreach (var container in new[] { factory.ImageContainer, factory.IntakeContainer, factory.DocumentContainer })
        {
            if (!await container.ExistsAsync())
            {
                continue;
            }

            await foreach (var blob in container.GetBlobsAsync())
            {
                names.Add(blob.Name);
            }
        }

        return names;
    }

    [Fact]
    public async Task TheRouteDoesNotCollideWithTheVehicleIdRoutes()
    {
        var client = factory.CreateClientForUser(Alice);

        var get = await client.GetAsync(Url);

        Assert.True(get.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound);
        _ = await client.GetFromJsonAsync<object>("/api/vehicles/filter-options");
    }

    [Fact]
    public async Task TheResponseIsExactlyTheDocumentedShape()
    {
        var client = factory.CreateClientForUser(Alice);

        var response = await client.PostAsync(Url, Form(Pdf()));

        var json = await response.Content.ReadAsStringAsync();
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet();
        Assert.Contains("outcome", names);
        Assert.Contains("features", names);
        Assert.Contains("notices", names);
        Assert.Contains("fieldsFilled", names);
        Assert.Equal("Filled", document.RootElement.GetProperty("outcome").GetString());
        Assert.DoesNotContain("exteriorColor", names);
        Assert.DoesNotContain("bodyStyle", names);
    }
}
