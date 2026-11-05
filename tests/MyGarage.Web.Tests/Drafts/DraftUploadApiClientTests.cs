using System.Net;
using System.Net.Http.Json;
using System.Text;
using MyGarage.Shared.Drafts;
using MyGarage.Web.Common;
using MyGarage.Web.Drafts;

namespace MyGarage.Web.Tests.Drafts;

public class DraftUploadApiClientTests
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

    private static DraftUploadApiClient Create(StubHandler handler) => new(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") });

    private static StubHandler Always(HttpStatusCode status, object? body = null) =>
        new(_ => Task.FromResult(new HttpResponseMessage(status) { Content = body is null ? null : JsonContent.Create(body) }));

    private static UploadContent File(string text = "%PDF-1.7 content-mark") => new(new MemoryStream(Encoding.ASCII.GetBytes(text)), "application/pdf");

    [Fact]
    public async Task ASuccessfulCall_PostsEveryFileAsTheFilesFieldWithoutTheUsersFileNames_AndReturnsTheResult()
    {
        var submissionId = Guid.NewGuid();
        var handler = Always(HttpStatusCode.Accepted, new DraftUploadResultDto
        {
            SubmissionId = submissionId,
            AcceptedCount = 1,
            Refused = [new DraftUploadRefusalDto { Index = 1, Reason = DraftUploadLimits.UnsupportedType }],
        });

        var response = await Create(handler).UploadAsync([File("%PDF-1.7 first-mark"), File("second-mark")], null);

        Assert.Equal(DraftUploadFailure.None, response.Failure);
        Assert.Equal(submissionId, response.Result!.SubmissionId);
        Assert.Equal(DraftUploadLimits.UnsupportedType, Assert.Single(response.Result.Refused).Reason);
        var call = Assert.Single(handler.Calls);
        Assert.Equal(HttpMethod.Post, call.Method);
        Assert.Equal("/api/drafts/uploads", call.Path);
        Assert.Equal(2, call.Body.Split("name=files").Length - 1);
        Assert.Contains("first-mark", call.Body);
        Assert.Contains("second-mark", call.Body);
        Assert.Contains("filename=record-1", call.Body);
        Assert.Contains("filename=record-2", call.Body);
        Assert.DoesNotContain("vehicleId", call.Body);
    }

    [Fact]
    public async Task AGivenFileName_IsSentAsTheFilesDisplayName()
    {
        var handler = Always(HttpStatusCode.Accepted, new DraftUploadResultDto { SubmissionId = Guid.NewGuid(), AcceptedCount = 1 });

        await Create(handler).UploadAsync([new UploadContent(new MemoryStream([1]), "application/pdf", "Joes invoice.pdf")], null);

        Assert.Contains("Joes invoice.pdf", Assert.Single(handler.Calls).Body);
    }

    [Fact]
    public async Task TheVehicleIdIsSentWhenGiven()
    {
        var handler = Always(HttpStatusCode.Accepted, new DraftUploadResultDto { SubmissionId = Guid.NewGuid(), AcceptedCount = 1 });
        var vehicleId = Guid.NewGuid();

        await Create(handler).UploadAsync([File()], vehicleId);

        var body = Assert.Single(handler.Calls).Body;
        Assert.Contains("name=vehicleId", body);
        Assert.Contains(vehicleId.ToString(), body);
    }

    [Fact]
    public async Task A400_ReturnsTheMessagesOfEachFileByItsIndex()
    {
        var handler = Always(HttpStatusCode.BadRequest, new
        {
            title = "One or more validation errors occurred.",
            status = 400,
            errors = new Dictionary<string, string[]> { ["files[0]"] = ["The file is empty."], ["files[2]"] = ["The file must be 10 MB or smaller."] },
        });

        var response = await Create(handler).UploadAsync([File(), File(), File()], null);

        Assert.Equal(DraftUploadFailure.Invalid, response.Failure);
        Assert.Equal("The file is empty.", response.Errors[0]);
        Assert.Equal("The file must be 10 MB or smaller.", response.Errors[2]);
        Assert.Equal(2, response.Errors.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, DraftUploadFailure.TooLarge)]
    [InlineData(HttpStatusCode.NotFound, DraftUploadFailure.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests, DraftUploadFailure.RateLimited)]
    public async Task OtherAnswersThatAreFailuresOfTheUpload_BecomeAFailureCode(HttpStatusCode status, DraftUploadFailure expected)
    {
        var response = await Create(Always(status)).UploadAsync([File()], null);

        Assert.Equal(expected, response.Failure);
        Assert.Null(response.Result);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ASignInRejection_IsThrownAsNotAccepted(HttpStatusCode status)
    {
        var ex = await Assert.ThrowsAsync<GarageApiException>(() => Create(Always(status)).UploadAsync([File()], null));

        Assert.Equal(GarageApiFailure.NotAccepted, ex.Failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AnOutageOrAnUnexpectedStatus_IsThrownAsUnavailable(HttpStatusCode status)
    {
        var ex = await Assert.ThrowsAsync<GarageApiException>(() => Create(Always(status)).UploadAsync([File()], null));

        Assert.Equal(GarageApiFailure.Unavailable, ex.Failure);
    }

    [Fact]
    public async Task ANetworkError_IsThrownAsUnavailable()
    {
        var ex = await Assert.ThrowsAsync<GarageApiException>(() =>
            Create(new StubHandler(_ => throw new HttpRequestException("boom"))).UploadAsync([File()], null));

        Assert.Equal(GarageApiFailure.Unavailable, ex.Failure);
    }
}
