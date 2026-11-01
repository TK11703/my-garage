using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleImageEndpointsTests
{
    private static readonly Guid VehicleId = Guid.NewGuid();
    private static readonly Guid ImageId = Guid.NewGuid();

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request, body));
            return await respond(request);
        }
    }

    private static (VehiclesApiClient Client, StubHandler Handler) CreateClient(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(request => Task.FromResult(respond(request)));
        return (new VehiclesApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }), handler);
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        return new DefaultHttpContext { RequestServices = services, Response = { Body = new MemoryStream() } };
    }

    private static async Task<(int Status, string Body, DefaultHttpContext Context)> ExecuteAsync(IResult result)
    {
        var context = CreateHttpContext();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync(), context);
    }

    private static HttpResponseMessage Image(byte[] bytes, string contentType = "image/png")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return response;
    }

    private static FormFile File(string content = "PNGDATA", string name = "photo.png", string contentType = "image/png")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    [Fact]
    public async Task Serve_StreamsTheBytesWithTheApisContentTypeAndSafeHeaders()
    {
        var (client, handler) = CreateClient(_ => Image([1, 2, 3, 4], "image/webp"));
        var context = CreateHttpContext();

        var result = await VehicleImageEndpoints.ServeAsync(VehicleId, ImageId, client, context, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("image/webp", context.Response.ContentType);
        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"]);
        Assert.Equal("private, max-age=86400", context.Response.Headers.CacheControl);
        Assert.Equal($"/api/vehicles/{VehicleId}/images/{ImageId}/content", handler.Calls.Single().Request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Serve_WritesTheExactImageBytes()
    {
        var bytes = new byte[] { 9, 8, 7, 6, 5 };
        var (client, _) = CreateClient(_ => Image(bytes));
        var http = CreateHttpContext();

        var result = await VehicleImageEndpoints.ServeAsync(VehicleId, ImageId, client, http, CancellationToken.None);
        await result.ExecuteAsync(http);

        Assert.Equal(bytes, ((MemoryStream)http.Response.Body).ToArray());
    }

    [Fact]
    public async Task Serve_UnknownImage_Returns404()
    {
        var (client, _) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await VehicleImageEndpoints.ServeAsync(VehicleId, ImageId, client, CreateHttpContext(), CancellationToken.None);

        Assert.Equal(404, (await ExecuteAsync(result)).Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, 401)]
    [InlineData(HttpStatusCode.Forbidden, 401)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 503)]
    [InlineData(HttpStatusCode.InternalServerError, 503)]
    public async Task Serve_ApiFailures_MapToAStatusWithoutDetails(HttpStatusCode apiStatus, int expected)
    {
        var (client, _) = CreateClient(_ => new HttpResponseMessage(apiStatus));

        var result = await VehicleImageEndpoints.ServeAsync(VehicleId, ImageId, client, CreateHttpContext(), CancellationToken.None);

        Assert.Equal(expected, (await ExecuteAsync(result)).Status);
    }

    [Fact]
    public async Task Upload_ForwardsTheFileAndCaptionAndRedirectsWithTheUploadedCode()
    {
        var (client, handler) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.Created));

        var result = await VehicleImageEndpoints.UploadAsync(VehicleId, File("HELLO-BYTES"), " Front ", client, CancellationToken.None);
        var (status, _, context) = await ExecuteAsync(result);

        Assert.Equal(302, status);
        Assert.Equal($"/vehicles/{VehicleId}?imageStatus=uploaded", context.Response.Headers.Location);
        var call = handler.Calls.Single();
        Assert.Equal(HttpMethod.Post, call.Request.Method);
        Assert.Equal($"/api/vehicles/{VehicleId}/images", call.Request.RequestUri!.AbsolutePath);
        Assert.Contains("HELLO-BYTES", call.Body);
        Assert.Contains("name=file", call.Body.Replace("\"", string.Empty));
        Assert.Contains("Front", call.Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "invalid")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "toolarge")]
    [InlineData(HttpStatusCode.Conflict, "limit")]
    [InlineData(HttpStatusCode.NotFound, "notfound")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "unavailable")]
    [InlineData(HttpStatusCode.Unauthorized, "signin")]
    [InlineData(HttpStatusCode.Forbidden, "signin")]
    [InlineData(HttpStatusCode.InternalServerError, "unavailable")]
    public async Task Upload_ApiOutcomes_BecomeShortFixedCodes(HttpStatusCode apiStatus, string code)
    {
        var (client, _) = CreateClient(_ => new HttpResponseMessage(apiStatus));

        var result = await VehicleImageEndpoints.UploadAsync(VehicleId, File(), null, client, CancellationToken.None);
        var (_, _, context) = await ExecuteAsync(result);

        Assert.Equal($"/vehicles/{VehicleId}?imageStatus={code}", context.Response.Headers.Location);
    }

    [Fact]
    public async Task Upload_ApiUnreachable_RedirectsWithUnavailable()
    {
        var (client, _) = CreateClient(_ => throw new HttpRequestException("boom"));

        var result = await VehicleImageEndpoints.UploadAsync(VehicleId, File(), null, client, CancellationToken.None);
        var (_, _, context) = await ExecuteAsync(result);

        Assert.Equal($"/vehicles/{VehicleId}?imageStatus=unavailable", context.Response.Headers.Location);
    }

    [Fact]
    public async Task Upload_NoFileOrEmptyFile_RedirectsWithInvalidAndNeverCallsTheApi()
    {
        var (client, handler) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.Created));

        var none = await ExecuteAsync(await VehicleImageEndpoints.UploadAsync(VehicleId, null, null, client, CancellationToken.None));
        var empty = await ExecuteAsync(await VehicleImageEndpoints.UploadAsync(VehicleId, File(string.Empty), null, client, CancellationToken.None));

        Assert.Equal($"/vehicles/{VehicleId}?imageStatus=invalid", none.Context.Response.Headers.Location);
        Assert.Equal($"/vehicles/{VehicleId}?imageStatus=invalid", empty.Context.Response.Headers.Location);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task Upload_TheClientSuppliedFileNameIsReducedToItsLastSegment()
    {
        var (client, handler) = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.Created));

        await VehicleImageEndpoints.UploadAsync(VehicleId, File(name: "..\\..\\evil\\photo.png"), null, client, CancellationToken.None);

        Assert.DoesNotContain("evil", handler.Calls.Single().Body.Replace("photo.png", string.Empty, StringComparison.Ordinal));
    }
}
