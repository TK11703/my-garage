using System.Net;
using System.Net.Http.Json;
using System.Text;
using MyGarage.Shared.Vehicles.StickerImport;
using MyGarage.Web.Common;
using MyGarage.Web.Vehicles.StickerImport;

namespace MyGarage.Web.Tests.Vehicles.StickerImport;

public class StickerImportApiClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.Method, request.RequestUri!.AbsolutePath, body));
            return await respond(request);
        }
    }

    private static StickerImportApiClient Create(StubHandler handler) => new(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") });

    private static StubHandler Always(HttpStatusCode status, object? body = null) =>
        new(_ => Task.FromResult(new HttpResponseMessage(status) { Content = body is null ? null : JsonContent.Create(body) }));

    private static Stream Bytes() => new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7 content-mark"));

    [Fact]
    public async Task ASuccessfulCall_PostsTheFileAndReturnsTheResult()
    {
        var handler = Always(HttpStatusCode.OK, new StickerImportResultDto { Outcome = StickerImportOutcome.Filled, Make = "Ford", FieldsFilled = 1 });

        var response = await Create(handler).ExtractAsync(Bytes(), "application/pdf", null);

        Assert.Equal(StickerImportFailure.None, response.Failure);
        Assert.Equal("Ford", response.Result!.Make);
        var call = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("/api/vehicles/sticker-extractions", call.Path);
        Assert.Contains("name=file", call.Body);
        Assert.Contains("content-mark", call.Body);
        Assert.DoesNotContain("vehicleId", call.Body);
    }

    [Fact]
    public async Task TheUsersFileNameIsNeverSentOn_AndTheVehicleIdIsSentOnAnEdit()
    {
        var handler = Always(HttpStatusCode.OK, new StickerImportResultDto { Outcome = StickerImportOutcome.Filled });
        var vehicleId = Guid.NewGuid();

        await Create(handler).ExtractAsync(Bytes(), "application/pdf", vehicleId);

        var body = Assert.Single(handler.Calls).Body;
        Assert.Contains("filename=window-sticker", body);
        Assert.Contains("name=vehicleId", body);
        Assert.Contains(vehicleId.ToString(), body);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, StickerImportFailure.Invalid)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, StickerImportFailure.TooLarge)]
    [InlineData(HttpStatusCode.NotFound, StickerImportFailure.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, StickerImportFailure.RateLimited)]
    public async Task AnswersThatAreFailuresOfTheImport_BecomeAFailureCode(HttpStatusCode status, StickerImportFailure expected)
    {
        var response = await Create(Always(status)).ExtractAsync(Bytes(), "application/pdf", null);

        Assert.Equal(expected, response.Failure);
        Assert.Null(response.Result);
    }

    [Theory]
    [InlineData("not_configured", StickerImportFailure.NotConfigured)]
    [InlineData("timeout", StickerImportFailure.ReadFailed)]
    [InlineData("rate_limited", StickerImportFailure.ReadFailed)]
    [InlineData("content_filter", StickerImportFailure.ReadFailed)]
    [InlineData("service_error", StickerImportFailure.ReadFailed)]
    [InlineData("invalid_output", StickerImportFailure.ReadFailed)]
    public async Task A503_IsNotConfiguredOrAFailedReadByItsCode(string code, StickerImportFailure expected)
    {
        var response = await Create(Always(HttpStatusCode.ServiceUnavailable, new { title = "x", status = 503, code })).ExtractAsync(Bytes(), "application/pdf", null);

        Assert.Equal(expected, response.Failure);
    }

    [Fact]
    public async Task A503WithNoCode_IsAFailedRead()
    {
        var response = await Create(Always(HttpStatusCode.ServiceUnavailable)).ExtractAsync(Bytes(), "application/pdf", null);

        Assert.Equal(StickerImportFailure.ReadFailed, response.Failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ASignInRejection_IsThrownAsNotAccepted(HttpStatusCode status)
    {
        var ex = await Assert.ThrowsAsync<GarageApiException>(() => Create(Always(status)).ExtractAsync(Bytes(), "application/pdf", null));

        Assert.Equal(GarageApiFailure.NotAccepted, ex.Failure);
    }

    [Fact]
    public async Task AnUnexpectedStatusOrANetworkError_IsThrownAsUnavailable()
    {
        var unexpected = await Assert.ThrowsAsync<GarageApiException>(() => Create(Always(HttpStatusCode.InternalServerError)).ExtractAsync(Bytes(), "application/pdf", null));
        var network = await Assert.ThrowsAsync<GarageApiException>(() =>
            Create(new StubHandler(_ => throw new HttpRequestException("boom"))).ExtractAsync(Bytes(), "application/pdf", null));

        Assert.Equal(GarageApiFailure.Unavailable, unexpected.Failure);
        Assert.Equal(GarageApiFailure.Unavailable, network.Failure);
    }

    [Fact]
    public async Task TheClientsOwnTimeout_IsAFailedRead()
    {
        var handler = new StubHandler(async _ =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new StickerImportApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/"), Timeout = TimeSpan.FromMilliseconds(100) });

        var response = await client.ExtractAsync(Bytes(), "application/pdf", null);

        Assert.Equal(StickerImportFailure.ReadFailed, response.Failure);
    }

    [Fact]
    public async Task ACallerCancellation_StillCancels()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var handler = new StubHandler(async _ =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(handler).ExtractAsync(Bytes(), "application/pdf", null, cancelled.Token));
    }

    [Theory]
    [InlineData(StickerImportFailure.Invalid, "That file is not a valid PDF, JPG, PNG, or WebP.")]
    [InlineData(StickerImportFailure.TooLarge, "That file is larger than 10 MB.")]
    [InlineData(StickerImportFailure.RateLimited, "You have started several imports in a row. Wait a minute and try again.")]
    [InlineData(StickerImportFailure.NotConfigured, "Reading stickers is not set up here. Enter the details by hand.")]
    [InlineData(StickerImportFailure.ReadFailed, "The sticker could not be read right now. Nothing was changed; try again or enter the details by hand.")]
    public void EachFailureHasItsOwnMessage(StickerImportFailure failure, string message)
    {
        Assert.Equal(message, StickerImportMessages.For(failure));
    }

    [Fact]
    public void NoFailureAndANotFound_HaveNoMessageOfTheirOwn()
    {
        Assert.Null(StickerImportMessages.For(StickerImportFailure.None));
        Assert.Null(StickerImportMessages.For(StickerImportFailure.NotFound));
    }
}
