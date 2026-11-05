using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MyGarage.Api.Tests.Common;
using MyGarage.Api.Vehicles.StickerImport;
using MyGarage.Shared.Vehicles;
using MyGarage.Shared.Vehicles.StickerImport;

namespace MyGarage.Api.Tests.Vehicles.StickerImport;

public class StickerImportFailureTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private const string Alice = "alice-oid";
    private const string Bob = "bob-oid";
    private const string Url = "/api/vehicles/sticker-extractions";

    public async Task InitializeAsync()
    {
        await factory.ResetDataAsync();
        factory.StickerExtractor.Reset();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static byte[] Pdf(int length = 200) => [.. Encoding.ASCII.GetBytes("%PDF-1.7\n"), .. new byte[Math.Max(0, length - 9)]];

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

    private static async Task<VehicleDetailDto> CreateVehicleAsync(HttpClient client, string? vin = null)
    {
        var response = await client.PostAsJsonAsync("/api/vehicles", new VehicleRequest { Make = "Ford", Model = "F-150", Year = 2020, Vin = vin });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VehicleDetailDto>())!;
    }

    [Theory]
    [InlineData("notes.txt", "text/plain", "just some text")]
    [InlineData("evil.pdf", "application/pdf", "this is text renamed to pdf")]
    [InlineData("photo.jpg", "image/jpeg", "not a jpeg at all")]
    [InlineData("archive.zip", "application/zip", "PK\u0003\u0004 zip data")]
    public async Task AnUnsupportedOrDamagedFile_Returns400_AndTheExtractorIsNotCalled(string fileName, string claimedType, string content)
    {
        var client = factory.CreateClientForUser(Alice);

        var response = await client.PostAsync(Url, Form(Encoding.UTF8.GetBytes(content), fileName, claimedType));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("PDF, JPEG, PNG, or WebP", body);
        Assert.Empty(factory.StickerExtractor.Calls);
    }

    [Fact]
    public async Task AnEmptyFile_Returns400()
    {
        var client = factory.CreateClientForUser(Alice);

        var response = await client.PostAsync(Url, Form([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.StickerExtractor.Calls);
    }

    [Fact]
    public async Task AFileOver10MB_Returns413_AndTheExtractorIsNotCalled()
    {
        var client = factory.CreateClientForUser(Alice);

        var response = await client.PostAsync(Url, Form(Pdf((int)StickerImportLimits.MaxFileBytes + 1)));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(factory.StickerExtractor.Calls);
    }

    [Fact]
    public async Task AFileExactlyAtTheLimit_IsAccepted()
    {
        var client = factory.CreateClientForUser(Alice);

        var response = await client.PostAsync(Url, Form(Pdf((int)StickerImportLimits.MaxFileBytes)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ARequestFarOverTheLimit_IsRefusedBeforeItIsRead()
    {
        var client = factory.CreateClientForUser(Alice);

        var response = await client.PostAsync(Url, Form(Pdf(14 * 1024 * 1024)));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(factory.StickerExtractor.Calls);
    }

    [Fact]
    public async Task AMalformedVehicleId_Returns400()
    {
        var client = factory.CreateClientForUser(Alice);

        var response = await client.PostAsync(Url, Form(Pdf(), vehicleId: "not-a-guid"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.StickerExtractor.Calls);
    }

    [Theory]
    [InlineData("timeout", "Reading the sticker took too long.")]
    [InlineData("rate_limited", "The reading service is busy. Try again shortly.")]
    [InlineData("content_filter", "The sticker could not be read right now.")]
    [InlineData("service_error", "The sticker could not be read right now.")]
    [InlineData("invalid_output", "The sticker could not be read right now.")]
    public async Task AnExtractorFailure_Returns503WithItsCode_AndATitleWithNoFileText(string code, string title)
    {
        factory.StickerExtractor.Result = StickerExtraction.Failed(code);
        var client = factory.CreateClientForUser(Alice);

        var response = await client.PostAsync(Url, Form(Pdf(), "SECRET-NAME.pdf"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(title, problem.RootElement.GetProperty("title").GetString());
        Assert.DoesNotContain("SECRET-NAME", problem.RootElement.ToString());
    }

    [Fact]
    public async Task WhenReadingIsNotConfigured_Returns503NotConfigured_WithoutCallingTheExtractor()
    {
        factory.StickerExtractor.IsEnabled = false;
        var client = factory.CreateClientForUser(Alice);

        var response = await client.PostAsync(Url, Form(Pdf()));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("not_configured", problem.RootElement.GetProperty("code").GetString());
        Assert.Empty(factory.StickerExtractor.Calls);
    }

    [Fact]
    public async Task NotAWindowSticker_Returns200NoDetailsFound()
    {
        factory.StickerExtractor.Result = FakeStickerExtractor.Sticker() with { IsWindowSticker = false };
        var client = factory.CreateClientForUser(Alice);

        var response = await client.PostAsync(Url, Form(Pdf()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<StickerImportResultDto>())!;
        Assert.Equal(StickerImportOutcome.NoDetailsFound, result.Outcome);
        Assert.Null(result.Make);
        Assert.Contains(result.Notices, notice => notice.Code == StickerNoticeCode.NotAStickerLikely);
    }

    [Fact]
    public async Task TheFileNameTheUserChose_IsNotPassedToTheReader()
    {
        var client = factory.CreateClientForUser(Alice);

        await client.PostAsync(Url, Form(Pdf(), "my-private-name.pdf"));

        Assert.Equal("window-sticker.pdf", Assert.Single(factory.StickerExtractor.Calls).FileName);
    }

    [Fact]
    public async Task ADuplicateVinInTheOwnersGarage_AddsANoticeThatNeverContainsTheVin()
    {
        var client = factory.CreateClientForUser(Alice);
        await CreateVehicleAsync(client, "1FTFW1E50NFA00001");

        var response = await client.PostAsync(Url, Form(Pdf()));

        var result = (await response.Content.ReadFromJsonAsync<StickerImportResultDto>())!;
        var notice = Assert.Single(result.Notices, notice => notice.Code == StickerNoticeCode.DuplicateVin);
        Assert.DoesNotContain("1FTFW1E50NFA00001", notice.Message);
    }

    [Fact]
    public async Task AnotherOwnersVehicleWithTheSameVin_ProducesNoNotice()
    {
        await CreateVehicleAsync(factory.CreateClientForUser(Bob), "1FTFW1E50NFA00001");
        var client = factory.CreateClientForUser(Alice);

        var response = await client.PostAsync(Url, Form(Pdf()));

        var result = (await response.Content.ReadFromJsonAsync<StickerImportResultDto>())!;
        Assert.DoesNotContain(result.Notices, notice => notice.Code == StickerNoticeCode.DuplicateVin);
    }

    [Fact]
    public async Task TheEditedVehiclesOwnVin_IsNotADuplicate()
    {
        var client = factory.CreateClientForUser(Alice);
        var vehicle = await CreateVehicleAsync(client, "1FTFW1E50NFA00001");

        var response = await client.PostAsync(Url, Form(Pdf(), vehicleId: vehicle.Id.ToString()));

        var result = (await response.Content.ReadFromJsonAsync<StickerImportResultDto>())!;
        Assert.DoesNotContain(result.Notices, notice => notice.Code == StickerNoticeCode.DuplicateVin);
    }

    [Fact]
    public async Task ALookupTextThatMatchesNothing_AddsNoItemToTheUsersLists()
    {
        factory.StickerExtractor.Result = FakeStickerExtractor.Sticker() with { BodyStyle = "Hovercraft" };
        var client = factory.CreateClientForUser(Alice);
        var before = await client.GetStringAsync("/api/lookups?includeHidden=true");

        var response = await client.PostAsync(Url, Form(Pdf()));

        var result = (await response.Content.ReadFromJsonAsync<StickerImportResultDto>())!;
        Assert.Equal("Hovercraft", result.VehicleTypeText);
        Assert.Null(result.VehicleTypeId);
        Assert.Equal(before, await client.GetStringAsync("/api/lookups?includeHidden=true"));
    }
}
