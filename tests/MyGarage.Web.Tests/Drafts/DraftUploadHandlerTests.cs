using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Http;
using MyGarage.Shared.Drafts;
using MyGarage.Web.Common;
using MyGarage.Web.Drafts;

namespace MyGarage.Web.Tests.Drafts;

public class DraftUploadHandlerTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return respond(request);
        }
    }

    private static DraftUploadApiClient Client(StubHandler handler) => new(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") });

    private static StubHandler Accepting(DraftUploadResultDto? result = null) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = JsonContent.Create(result ?? new DraftUploadResultDto { SubmissionId = Guid.NewGuid(), AcceptedCount = 1 }),
        });

    private static FormFile Pdf(string name, long? length = null, string text = "%PDF-1.7 data")
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        return new FormFile(new MemoryStream(bytes), 0, length ?? bytes.Length, DraftUploadHandler.FileField, name)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf",
        };
    }

    private static HttpContext Post(params FormFile[] files)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.ContentType = "multipart/form-data; boundary=x";
        var collection = new FormFileCollection();
        collection.AddRange(files);
        context.Request.Form = new FormCollection([], collection);
        return context;
    }

    [Fact]
    public async Task NoFile_IsRefusedBeforeAnyApiCall()
    {
        var stub = Accepting();

        var attempt = await DraftUploadHandler.RunAsync(Post(), Client(stub), null, CancellationToken.None);

        Assert.Null(attempt.Result);
        Assert.Equal(DraftUploadMessages.ChooseFile, attempt.Message);
        Assert.Equal("danger", attempt.Level);
        Assert.Equal(0, stub.Calls);
    }

    [Fact]
    public async Task ARequestWithoutAFormBody_IsRefusedLikeNoFile()
    {
        var stub = Accepting();

        var attempt = await DraftUploadHandler.RunAsync(null, Client(stub), null, CancellationToken.None);

        Assert.Equal(DraftUploadMessages.ChooseFile, attempt.Message);
        Assert.Equal(0, stub.Calls);
    }

    [Fact]
    public async Task MoreThanFiveFiles_AreRefusedBeforeAnyApiCall()
    {
        var stub = Accepting();
        var files = Enumerable.Range(1, DraftUploadLimits.MaxFiles + 1).Select(index => Pdf($"{index}.pdf")).ToArray();

        var attempt = await DraftUploadHandler.RunAsync(Post(files), Client(stub), null, CancellationToken.None);

        Assert.Equal(DraftUploadMessages.TooManyFiles, attempt.Message);
        Assert.Equal(0, stub.Calls);
    }

    [Fact]
    public async Task AFileOverTenMegabytes_IsRefusedBeforeAnyApiCall()
    {
        var stub = Accepting();

        var attempt = await DraftUploadHandler.RunAsync(Post(Pdf("big.pdf", DraftUploadLimits.MaxFileBytes + 1)), Client(stub), null, CancellationToken.None);

        Assert.Equal(DraftUploadMessages.FileTooLarge, attempt.Message);
        Assert.Equal(0, stub.Calls);
    }

    [Fact]
    public async Task FilesThatAddUpToMoreThanFifteenMegabytes_AreRefusedBeforeAnyApiCall()
    {
        var stub = Accepting();
        var files = new[] { Pdf("a.pdf", DraftUploadLimits.MaxFileBytes), Pdf("b.pdf", DraftUploadLimits.MaxTotalBytes - DraftUploadLimits.MaxFileBytes + 1) };

        var attempt = await DraftUploadHandler.RunAsync(Post(files), Client(stub), null, CancellationToken.None);

        Assert.Equal(DraftUploadMessages.TotalTooLarge, attempt.Message);
        Assert.Equal(0, stub.Calls);
    }

    [Fact]
    public async Task ValidFiles_AreStreamedToTheApi_WithTheirDisplayNames_AndTheVehicle()
    {
        var stub = Accepting(new DraftUploadResultDto { SubmissionId = Guid.NewGuid(), AcceptedCount = 2 });
        var vehicleId = Guid.NewGuid();

        var attempt = await DraftUploadHandler.RunAsync(Post(Pdf("secret-name.pdf", text: "%PDF-one"), Pdf("other.pdf", text: "%PDF-two")), Client(stub), vehicleId, CancellationToken.None);

        Assert.Equal(2, attempt.Result!.AcceptedCount);
        Assert.Empty(attempt.Refused);
        Assert.Null(attempt.Message);
        var body = Assert.Single(stub.Bodies);
        Assert.Contains("%PDF-one", body);
        Assert.Contains("%PDF-two", body);
        Assert.Contains("filename=secret-name.pdf", body);
        Assert.Contains("filename=other.pdf", body);
        Assert.Contains(vehicleId.ToString(), body);
    }

    [Fact]
    public async Task RefusedFilesOfAnAcceptedUpload_AreNamedByTheirOwnNamesWithTheReasonText()
    {
        var stub = Accepting(new DraftUploadResultDto
        {
            SubmissionId = Guid.NewGuid(),
            AcceptedCount = 1,
            Refused = [new DraftUploadRefusalDto { Index = 1, Reason = DraftUploadLimits.UnsupportedType }, new DraftUploadRefusalDto { Index = 2, Reason = DraftUploadLimits.Empty }],
        });

        var attempt = await DraftUploadHandler.RunAsync(Post(Pdf("ok.pdf"), Pdf("notes.txt"), Pdf("empty.pdf")), Client(stub), null, CancellationToken.None);

        Assert.Equal(1, attempt.Result!.AcceptedCount);
        Assert.Equal(["notes.txt", "empty.pdf"], attempt.Refused.Select(refusal => refusal.FileName));
        Assert.Equal(DraftUploadMessages.ForReason(DraftUploadLimits.UnsupportedType), attempt.Refused[0].Message);
        Assert.Equal(DraftUploadMessages.ForReason(DraftUploadLimits.Empty), attempt.Refused[1].Message);
    }

    [Fact]
    public async Task A400_BecomesAPerFileListWithTheApisMessages()
    {
        var stub = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new { title = "x", status = 400, errors = new Dictionary<string, string[]> { ["files[0]"] = ["The file is empty."] } }),
        });

        var attempt = await DraftUploadHandler.RunAsync(Post(Pdf("a.pdf")), Client(stub), null, CancellationToken.None);

        Assert.Null(attempt.Result);
        var refusal = Assert.Single(attempt.Refused);
        Assert.Equal("a.pdf", refusal.FileName);
        Assert.Equal("The file is empty.", refusal.Message);
        Assert.Equal("danger", attempt.Level);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "TotalTooLarge", "danger")]
    [InlineData(HttpStatusCode.TooManyRequests, "RateLimited", "warning")]
    public async Task OtherFailures_ShowTheirOwnMessage(HttpStatusCode status, string messageName, string level)
    {
        var expected = (string)typeof(DraftUploadMessages).GetField(messageName)!.GetValue(null)!;

        var attempt = await DraftUploadHandler.RunAsync(Post(Pdf("a.pdf")), Client(new StubHandler(_ => new HttpResponseMessage(status))), null, CancellationToken.None);

        Assert.Equal(expected, attempt.Message);
        Assert.Equal(level, attempt.Level);
        Assert.False(attempt.VehicleNotFound);
    }

    [Fact]
    public async Task AVehicleTheApiDoesNotKnow_IsReportedAsNotFound()
    {
        var attempt = await DraftUploadHandler.RunAsync(
            Post(Pdf("a.pdf")), Client(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))), Guid.NewGuid(), CancellationToken.None);

        Assert.True(attempt.VehicleNotFound);
        Assert.Null(attempt.Result);
    }

    [Fact]
    public async Task ASignInRejection_AndAnOutage_AreThrown()
    {
        var rejected = await Assert.ThrowsAsync<GarageApiException>(() =>
            DraftUploadHandler.RunAsync(Post(Pdf("a.pdf")), Client(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized))), null, CancellationToken.None));
        var down = await Assert.ThrowsAsync<GarageApiException>(() =>
            DraftUploadHandler.RunAsync(Post(Pdf("a.pdf")), Client(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))), null, CancellationToken.None));

        Assert.Equal(GarageApiFailure.NotAccepted, rejected.Failure);
        Assert.Equal(GarageApiFailure.Unavailable, down.Failure);
    }
}
