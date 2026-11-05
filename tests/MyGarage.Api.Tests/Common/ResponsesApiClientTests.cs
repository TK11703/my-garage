using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Options;
using MyGarage.Api.Common;
using MyGarage.Api.EmailIntake.Extraction;

namespace MyGarage.Api.Tests.Common;

public class ResponsesApiClientTests
{
    private const string Endpoint = "https://aoai.example.test/";
    private const string Schema = """{"type":"object","additionalProperties":false,"required":["a"],"properties":{"a":{"type":["string","null"]}}}""";

    private sealed record Captured(HttpMethod Method, Uri? Uri, string? Authorization, string Body);

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<Captured> Requests { get; } = [];

        public static FakeHandler Always(HttpStatusCode status, string body) =>
            new((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Captured(request.Method, request.RequestUri, request.Headers.Authorization?.ToString(), body));
            return await respond(request, cancellationToken);
        }
    }

    private sealed class FakeCredential : TokenCredential
    {
        public List<string> Scopes { get; } = [];

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            GetTokenAsync(requestContext, cancellationToken).AsTask().GetAwaiter().GetResult();

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Scopes.AddRange(requestContext.Scopes);
            return new ValueTask<AccessToken>(new AccessToken("tok-1", DateTimeOffset.UtcNow.AddHours(1)));
        }
    }

    private static ResponsesApiClient Create(HttpMessageHandler handler, FakeCredential? credential = null, string? endpoint = Endpoint, string? deployment = "gpt-test") =>
        new(new HttpClient(handler), Options.Create(new ExtractionOptions { Endpoint = endpoint, Deployment = deployment }), credential ?? new FakeCredential());

    private static ResponsesCall Call(ResponsesDocument? document = null, TimeSpan? timeout = null) =>
        new("INSTRUCTIONS-MARK", "test_schema", Schema, "INPUT-MARK", document, timeout ?? TimeSpan.FromSeconds(30));

    private static string Envelope(string answerJson) =>
        JsonSerializer.Serialize(new { id = "resp_1", output = new object[] { new { type = "reasoning", summary = Array.Empty<object>() }, new { type = "message", role = "assistant", content = new[] { new { type = "output_text", text = answerJson } } } } });

    [Fact]
    public async Task Request_IsAStrictSchemaCallWithNoToolsAndNoStorage_AuthenticatedWithABearerToken()
    {
        var handler = FakeHandler.Always(HttpStatusCode.OK, Envelope("""{"a":"x"}"""));
        var credential = new FakeCredential();

        var outcome = await Create(handler, credential).SendAsync(Call(), CancellationToken.None);

        Assert.False(outcome.IsFailure);
        Assert.Equal("""{"a":"x"}""", outcome.AnswerText);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://aoai.example.test/openai/v1/responses", request.Uri?.ToString());
        Assert.Equal("Bearer tok-1", request.Authorization);
        Assert.Equal(["https://ai.azure.com/.default"], credential.Scopes);

        using var body = JsonDocument.Parse(request.Body);
        var root = body.RootElement;
        Assert.Equal("gpt-test", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.False(root.TryGetProperty("tools", out _));
        Assert.Equal("INSTRUCTIONS-MARK", root.GetProperty("instructions").GetString());
        var format = root.GetProperty("text").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.Equal("test_schema", format.GetProperty("name").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());
        var content = root.GetProperty("input")[0].GetProperty("content");
        var part = Assert.Single(content.EnumerateArray());
        Assert.Equal("input_text", part.GetProperty("type").GetString());
        Assert.Equal("INPUT-MARK", part.GetProperty("text").GetString());
    }

    [Fact]
    public async Task APdf_IsSentAsAnInputFileWithItsBase64()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7 pdf-mark");
        var handler = FakeHandler.Always(HttpStatusCode.OK, Envelope("{}"));

        await Create(handler).SendAsync(Call(new ResponsesDocument(new MemoryStream(bytes), "application/pdf", "sticker.pdf")), CancellationToken.None);

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        var file = body.RootElement.GetProperty("input")[0].GetProperty("content")[1];
        Assert.Equal("input_file", file.GetProperty("type").GetString());
        Assert.Equal("sticker.pdf", file.GetProperty("filename").GetString());
        Assert.Equal($"data:application/pdf;base64,{Convert.ToBase64String(bytes)}", file.GetProperty("file_data").GetString());
    }

    [Fact]
    public async Task AnImage_IsSentAsAnInputImageDataUri()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        var handler = FakeHandler.Always(HttpStatusCode.OK, Envelope("{}"));

        await Create(handler).SendAsync(Call(new ResponsesDocument(new MemoryStream(bytes), "image/png", "sticker.png")), CancellationToken.None);

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        var image = body.RootElement.GetProperty("input")[0].GetProperty("content")[1];
        Assert.Equal("input_image", image.GetProperty("type").GetString());
        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(bytes)}", image.GetProperty("image_url").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "{}", "rate_limited")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"code":"content_filter"}}""", "content_filter")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"code":"other"}}""", "service_error")]
    [InlineData(HttpStatusCode.InternalServerError, "oops", "service_error")]
    public async Task AFailureStatus_BecomesAReasonCodeAndNeverTheBody(HttpStatusCode status, string body, string reason)
    {
        var outcome = await Create(FakeHandler.Always(status, body)).SendAsync(Call(), CancellationToken.None);

        Assert.True(outcome.IsFailure);
        Assert.Equal(reason, outcome.FailureReason);
        Assert.Equal((int)status, outcome.StatusCode);
        Assert.Null(outcome.AnswerText);
    }

    [Fact]
    public async Task ASlowService_BecomesATimeout()
    {
        var handler = new FakeHandler(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var outcome = await Create(handler).SendAsync(Call(timeout: TimeSpan.FromMilliseconds(50)), CancellationToken.None);

        Assert.Equal("timeout", outcome.FailureReason);
        Assert.Null(outcome.StatusCode);
    }

    [Fact]
    public async Task ANetworkError_BecomesAServiceError()
    {
        var handler = new FakeHandler((_, _) => throw new HttpRequestException("boom"));

        var outcome = await Create(handler).SendAsync(Call(), CancellationToken.None);

        Assert.Equal("service_error", outcome.FailureReason);
    }

    [Fact]
    public async Task ACancelledCaller_StillCancels()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var handler = new FakeHandler(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(handler).SendAsync(Call(), cancelled.Token));
    }

    [Theory]
    [InlineData("""{"status":"incomplete","incomplete_details":{"reason":"max_output_tokens"}}""", "invalid_output")]
    [InlineData("""{"status":"incomplete","incomplete_details":{"reason":"content_filter"}}""", "content_filter")]
    [InlineData("""{"output":[]}""", "invalid_output")]
    [InlineData("not json", "invalid_output")]
    public async Task AnUnusableEnvelope_IsReportedWithTheStatus(string envelope, string reason)
    {
        var outcome = await Create(FakeHandler.Always(HttpStatusCode.OK, envelope)).SendAsync(Call(), CancellationToken.None);

        Assert.Equal(reason, outcome.FailureReason);
        Assert.Equal(200, outcome.StatusCode);
    }

    [Fact]
    public async Task TheAnswerText_CanComeFromOutputText()
    {
        var outcome = await Create(FakeHandler.Always(HttpStatusCode.OK, """{"output_text":"{\"a\":\"direct\"}"}""")).SendAsync(Call(), CancellationToken.None);

        Assert.Equal("""{"a":"direct"}""", outcome.AnswerText);
    }

    [Theory]
    [InlineData(null, "gpt")]
    [InlineData("", "gpt")]
    [InlineData(Endpoint, null)]
    [InlineData(Endpoint, " ")]
    public void WithoutAnEndpointOrDeployment_TheClientIsDisabled(string? endpoint, string? deployment)
    {
        Assert.False(Create(FakeHandler.Always(HttpStatusCode.OK, "{}"), endpoint: endpoint, deployment: deployment).IsEnabled);
    }
}
