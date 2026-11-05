using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles.StickerImport;

public class StickerImportPrivacyTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "priv-alice-oid";
    private const string Bob = "priv-bob-oid";
    private const string Url = "/api/vehicles/sticker-extractions";

    public async Task InitializeAsync()
    {
        await factory.ResetDataAsync();
        factory.StickerExtractor.Reset();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static MultipartFormDataContent Form(string? vehicleId = null)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7 sticker"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", "sticker.pdf");
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

    [Fact]
    public async Task NoToken_Returns401_AndNothingIsRead()
    {
        var response = await factory.CreateClientWithToken(null).PostAsync(Url, Form());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.StickerExtractor.Calls);
    }

    [Fact]
    public async Task ABadToken_Returns401()
    {
        var response = await factory.CreateClientWithToken("not-a-jwt").PostAsync(Url, Form());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(factory.StickerExtractor.Calls);
    }

    [Fact]
    public async Task TheWorkflowsAppOnlyToken_IsRefused()
    {
        var response = await factory.CreateClientForWorkflow().PostAsync(Url, Form());

        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, response.StatusCode.ToString());
        Assert.Empty(factory.StickerExtractor.Calls);
    }

    [Fact]
    public async Task ATokenWithNeitherTheScopeNorARole_IsRefused()
    {
        var response = await factory.CreateClientWithNeitherRoleNorScope().PostAsync(Url, Form());

        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, response.StatusCode.ToString());
        Assert.Empty(factory.StickerExtractor.Calls);
    }

    [Fact]
    public async Task AnotherUsersVehicle_IsIndistinguishableFromAMissingOne_AndItsFeaturesAreNeverSentToTheReader()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var vehicle = await CreateVehicleAsync(alice);
        (await alice.PostAsJsonAsync($"/api/vehicles/{vehicle.Id}/tags", new AddVehicleTagRequest { Name = "Private Feature" })).EnsureSuccessStatusCode();

        var theirs = await bob.PostAsync(Url, Form(vehicle.Id.ToString()));
        var missing = await bob.PostAsync(Url, Form(Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.NotFound, theirs.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(StripIds(await missing.Content.ReadAsStringAsync()), StripIds(await theirs.Content.ReadAsStringAsync()));
        Assert.Empty(factory.StickerExtractor.Calls);
    }

    // Problem bodies carry a trace id; everything else must be identical.
    private static string StripIds(string body) => System.Text.RegularExpressions.Regex.Replace(body, "\"traceId\":\"[^\"]*\"", string.Empty);

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("no-details")]
    public async Task AfterAnyExtraction_NothingNewIsStored(string outcome)
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        factory.StickerExtractor.Result = outcome switch
        {
            "failure" => MyGarage.Api.Vehicles.StickerImport.StickerExtraction.Failed("service_error"),
            "no-details" => FakeStickerExtractor.Sticker() with { IsWindowSticker = false },
            _ => FakeStickerExtractor.Sticker(),
        };
        var featuresBefore = await factory.CountFeaturesAsync();

        await client.PostAsync(Url, Form(vehicle.Id.ToString()));

        Assert.Equal(1L, await factory.ExecuteScalarAsync("SELECT count(*) FROM vehicles"));
        Assert.Equal(0L, await factory.ExecuteScalarAsync("SELECT count(*) FROM vehicle_tags"));
        Assert.Equal(featuresBefore, await factory.CountFeaturesAsync());
        Assert.Equal(0L, await factory.ExecuteScalarAsync("SELECT count(*) FROM email_submissions"));
        foreach (var container in new[] { factory.ImageContainer, factory.IntakeContainer, factory.DocumentContainer })
        {
            if (await container.ExistsAsync())
            {
                await foreach (var blob in container.GetBlobsAsync())
                {
                    Assert.Fail($"A blob was stored: {blob.Name}");
                }
            }
        }
    }

    [Fact]
    public async Task ReplacingAnotherUsersTagIds_OnMyOwnVehicle_NeverTouchesTheirTags()
    {
        var alice = factory.CreateClientForUser(Alice);
        var bob = factory.CreateClientForUser(Bob);
        var theirs = await CreateVehicleAsync(alice);
        (await alice.PostAsJsonAsync($"/api/vehicles/{theirs.Id}/tags", new AddVehicleTagRequest { Name = "Sunroof" })).EnsureSuccessStatusCode();
        var theirTag = (await alice.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{theirs.Id}"))!.Tags.Single();
        var mine = await CreateVehicleAsync(bob);

        var response = await bob.PutAsJsonAsync($"/api/vehicles/{mine.Id}", new VehicleRequest
        {
            Make = "Ford",
            Model = "F-150",
            Year = 2020,
            StickerFeatures = [new StickerFeatureRequest { Name = "Panoramic Sunroof", ReplacesTagIds = [theirTag.Id] }],
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Sunroof"], (await alice.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{theirs.Id}"))!.Tags.Select(tag => tag.Name));
    }
}
