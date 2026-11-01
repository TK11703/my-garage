using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyGarage.Api.Tests.Common;
using MyGarage.Database.VehicleImages;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles.Images;

public class VehicleImagesEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";

    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF, 0xE0];

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static byte[] Png(int totalLength = 64) => [.. PngHeader, .. new byte[Math.Max(0, totalLength - PngHeader.Length)]];

    private static MultipartFormDataContent Form(byte[] bytes, string fileName = "photo.png", string claimedType = "image/png", string? caption = null)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(claimedType);
        content.Add(file, "file", fileName);
        if (caption is not null)
        {
            content.Add(new StringContent(caption), "caption");
        }

        return content;
    }

    private static async Task<VehicleDetailDto> CreateVehicleAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/vehicles", new VehicleRequest { Nickname = "Daily", Make = "Ford", Model = "F-150" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VehicleDetailDto>())!;
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid vehicleId, MultipartFormDataContent form) =>
        client.PostAsync($"/api/vehicles/{vehicleId}/images", form);

    private static async Task<VehicleImageDto> UploadOkAsync(HttpClient client, Guid vehicleId, byte[]? bytes = null, string? caption = null)
    {
        var response = await UploadAsync(client, vehicleId, Form(bytes ?? Png(), caption: caption));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<VehicleImageDto>())!;
    }

    private async Task<List<string>> BlobNamesAsync()
    {
        var names = new List<string>();
        await foreach (var blob in factory.ImageContainer.GetBlobsAsync())
        {
            names.Add(blob.Name);
        }

        return names;
    }

    [Fact]
    public async Task Upload_ValidPng_Returns201StoresTheBlobAndBecomesMain()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await UploadAsync(client, vehicle.Id, Form(Png(100), caption: "  Front view "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var image = await response.Content.ReadFromJsonAsync<VehicleImageDto>();
        Assert.NotNull(image);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal(100, image.SizeBytes);
        Assert.Equal("Front view", image.Caption);
        Assert.True(image.IsMain);
        Assert.Equal([$"{vehicle.Id}/{image.Id}"], await BlobNamesAsync());
        var detail = await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{vehicle.Id}");
        Assert.Equal([image.Id], detail!.Images.Select(i => i.Id));
    }

    [Fact]
    public async Task Upload_JpegAndWebp_AreAccepted()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var jpeg = await UploadOkAsync(client, vehicle.Id, [.. JpegHeader, .. new byte[40]]);
        var webp = await UploadOkAsync(client, vehicle.Id, [.. "RIFF"u8.ToArray(), 0x20, 0, 0, 0, .. "WEBP"u8.ToArray(), .. new byte[40]]);

        Assert.Equal("image/jpeg", jpeg.ContentType);
        Assert.Equal("image/webp", webp.ContentType);
        Assert.False(webp.IsMain);
    }

    [Fact]
    public async Task Upload_TextRenamedToJpegWithASpoofedContentType_Returns400AndStoresNothing()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await UploadAsync(client, vehicle.Id, Form("not an image at all"u8.ToArray(), "evil.jpg", "image/jpeg"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains(problem!.Errors.Keys, key => key.Equals("file", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(await BlobNamesAsync());
        Assert.Empty((await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{vehicle.Id}"))!.Images);
    }

    [Fact]
    public async Task Upload_MissingFileOrEmptyFile_Returns400()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var noFile = new MultipartFormDataContent { { new StringContent("just a caption"), "caption" } };

        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(client, vehicle.Id, noFile)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(client, vehicle.Id, Form([]))).StatusCode);
    }

    [Fact]
    public async Task Upload_CaptionOver200Characters_Returns400()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await UploadAsync(client, vehicle.Id, Form(Png(), caption: new string('a', 201)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_Over5Megabytes_Returns413AndStoresNothing()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await UploadAsync(client, vehicle.Id, Form(Png(5_242_881)));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(await BlobNamesAsync());
    }

    [Fact]
    public async Task Upload_Exactly5Megabytes_IsAccepted()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);

        var response = await UploadAsync(client, vehicle.Id, Form(Png(5_242_880)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Upload_The11thImage_Returns409()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        for (var i = 0; i < 10; i++)
        {
            await UploadOkAsync(client, vehicle.Id);
        }

        var response = await UploadAsync(client, vehicle.Id, Form(Png()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(10, (await BlobNamesAsync()).Count);
    }

    [Fact]
    public async Task Upload_AnotherUsersOrUnknownVehicle_Returns404AndStoresNothing()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);

        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(factory.CreateClientForUser(Bob), vehicle.Id, Form(Png()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(alice, Guid.NewGuid(), Form(Png()))).StatusCode);
        Assert.Empty(await BlobNamesAsync());
    }

    [Fact]
    public async Task Content_ReturnsTheBytesWithTheStoredTypeAndSafeHeaders()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var bytes = Png(200);
        var image = await UploadOkAsync(client, vehicle.Id, bytes);

        var response = await client.GetAsync($"/api/vehicles/{vehicle.Id}/images/{image.Id}/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.Equal(TimeSpan.FromDays(1), response.Headers.CacheControl?.MaxAge);
        Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Content_AnotherUsersOrUnknownImage_Returns404()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);
        var image = await UploadOkAsync(alice, vehicle.Id);

        var foreign = await factory.CreateClientForUser(Bob).GetAsync($"/api/vehicles/{vehicle.Id}/images/{image.Id}/content");
        var unknown = await alice.GetAsync($"/api/vehicles/{vehicle.Id}/images/{Guid.NewGuid()}/content");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("application/problem+json", foreign.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task SetMain_MakesThatImageTheCardImage()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var first = await UploadOkAsync(client, vehicle.Id);
        var second = await UploadOkAsync(client, vehicle.Id);

        var response = await client.PutAsync($"/api/vehicles/{vehicle.Id}/images/{second.Id}/main", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var detail = await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{vehicle.Id}");
        Assert.Equal([second.Id], detail!.Images.Where(i => i.IsMain).Select(i => i.Id));
        var page = await client.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>("/api/vehicles");
        Assert.Equal(second.Id, page!.Items.Single().MainImageId);
        Assert.NotEqual(first.Id, page.Items.Single().MainImageId);
    }

    [Fact]
    public async Task SetMain_AnotherUsersOrUnknownImage_Returns404()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);
        var image = await UploadOkAsync(alice, vehicle.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClientForUser(Bob).PutAsync($"/api/vehicles/{vehicle.Id}/images/{image.Id}/main", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PutAsync($"/api/vehicles/{vehicle.Id}/images/{Guid.NewGuid()}/main", null)).StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesTheImageAndItsBlobAndPromotesAnotherWhenMain()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var main = await UploadOkAsync(client, vehicle.Id);
        var other = await UploadOkAsync(client, vehicle.Id);

        var response = await client.DeleteAsync($"/api/vehicles/{vehicle.Id}/images/{main.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var detail = await client.GetFromJsonAsync<VehicleDetailDto>($"/api/vehicles/{vehicle.Id}");
        Assert.Equal([other.Id], detail!.Images.Select(i => i.Id));
        Assert.True(detail.Images.Single().IsMain);
        Assert.Equal([$"{vehicle.Id}/{other.Id}"], await BlobNamesAsync());
    }

    [Fact]
    public async Task Delete_AnotherUsersOrUnknownImage_Returns404AndKeepsTheBlob()
    {
        var alice = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(alice);
        var image = await UploadOkAsync(alice, vehicle.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await factory.CreateClientForUser(Bob).DeleteAsync($"/api/vehicles/{vehicle.Id}/images/{image.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.DeleteAsync($"/api/vehicles/{vehicle.Id}/images/{Guid.NewGuid()}")).StatusCode);
        Assert.Single(await BlobNamesAsync());
    }

    [Fact]
    public async Task DeletingTheVehicle_RemovesItsBlobs()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        await UploadOkAsync(client, vehicle.Id);
        await UploadOkAsync(client, vehicle.Id);
        var survivor = await CreateVehicleAsync(client);
        var kept = await UploadOkAsync(client, survivor.Id);

        var response = await client.DeleteAsync($"/api/vehicles/{vehicle.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal([$"{survivor.Id}/{kept.Id}"], await BlobNamesAsync());
    }

    [Fact]
    public async Task Summaries_ShowTheMainImageIdOnTheGarageCards()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var image = await UploadOkAsync(client, vehicle.Id);

        var page = await client.GetFromJsonAsync<PagedResultDto<VehicleSummaryDto>>("/api/vehicles");

        Assert.Equal(image.Id, page!.Items.Single().MainImageId);
    }

    [Fact]
    public async Task EveryEndpoint_WithoutToken_Returns401()
    {
        var anonymous = factory.CreateClientWithToken(null);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await UploadAsync(anonymous, id, Form(Png()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/vehicles/{id}/images/{id}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsync($"/api/vehicles/{id}/images/{id}/main", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/vehicles/{id}/images/{id}")).StatusCode);
    }

    private sealed class UnreachableStore : IVehicleImageBlobStore
    {
        public Task EnsureContainerAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UploadAsync(Guid vehicleId, Guid imageId, Stream content, string contentType, CancellationToken cancellationToken) =>
            throw new ImageStorageUnavailableException("down");

        public Task<Stream?> OpenReadAsync(Guid vehicleId, Guid imageId, CancellationToken cancellationToken) =>
            throw new ImageStorageUnavailableException("down");

        public Task DeleteAsync(Guid vehicleId, Guid imageId, CancellationToken cancellationToken) =>
            throw new ImageStorageUnavailableException("down");

        public Task DeleteVehicleAsync(Guid vehicleId, CancellationToken cancellationToken) =>
            throw new ImageStorageUnavailableException("down");
    }

    [Fact]
    public async Task StorageUnavailable_Returns503ForImagesWhileTheVehicleStillWorks()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client);
        var image = await UploadOkAsync(client, vehicle.Id);

        using var broken = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IVehicleImageBlobStore>();
            services.AddSingleton<IVehicleImageBlobStore>(new UnreachableStore());
        }));
        var brokenClient = broken.CreateClient();
        brokenClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.Create(claims: new Dictionary<string, object> { ["oid"] = Alice }));

        var upload = await UploadAsync(brokenClient, vehicle.Id, Form(Png()));
        var content = await brokenClient.GetAsync($"/api/vehicles/{vehicle.Id}/images/{image.Id}/content");
        var details = await brokenClient.GetAsync($"/api/vehicles/{vehicle.Id}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, upload.StatusCode);
        Assert.Equal("application/problem+json", upload.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, content.StatusCode);
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        Assert.Single((await details.Content.ReadFromJsonAsync<VehicleDetailDto>())!.Images);
    }
}
