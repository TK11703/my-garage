using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MyGarage.Api.EmailIntake.Extraction;

namespace MyGarage.Api.Tests.EmailIntake;

public class ResponsesApiExtractorTests
{
    private const string Endpoint = "https://aoai.example.test/";

    private sealed record Captured(HttpMethod Method, Uri? Uri, string? Authorization, string Body);

    private sealed class FakeHandler(Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        private readonly List<Captured> requests = [];

        public IReadOnlyList<Captured> Requests
        {
            get
            {
                lock (requests)
                {
                    return [.. requests];
                }
            }
        }

        public static FakeHandler Always(HttpStatusCode status, string body) =>
            new((_, _, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            int count;
            lock (requests)
            {
                requests.Add(new Captured(request.Method, request.RequestUri, request.Headers.Authorization?.ToString(), body));
                count = requests.Count;
            }

            return await respond(count, request, cancellationToken);
        }
    }

    private sealed class FakeCredential(bool fail = false) : TokenCredential
    {
        public List<string> Scopes { get; } = [];

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            GetTokenAsync(requestContext, cancellationToken).AsTask().GetAwaiter().GetResult();

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Scopes.AddRange(requestContext.Scopes);
            return fail
                ? throw new InvalidOperationException("The fake credential was told to fail.")
                : new ValueTask<AccessToken>(new AccessToken("tok-1", DateTimeOffset.UtcNow.AddHours(1)));
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(' ', pairs.Select(pair => $"{pair.Key}={pair.Value}")) : string.Empty;
            Entries.Add($"{logLevel} {formatter(state, exception)} {values} {exception}");
        }
    }

    private static ResponsesApiExtractor Create(
        HttpMessageHandler handler,
        FakeCredential? credential = null,
        string? endpoint = Endpoint,
        string? deployment = "gpt-test",
        int timeoutSeconds = 90,
        ILogger<ResponsesApiExtractor>? logger = null) =>
        new(
            new HttpClient(handler),
            Options.Create(new ExtractionOptions { Endpoint = endpoint, Deployment = deployment, TimeoutSeconds = timeoutSeconds }),
            credential ?? new FakeCredential(),
            logger ?? NullLogger<ResponsesApiExtractor>.Instance);

    private static ExtractionInput Input(string subject = "Fwd: invoice", string text = "Oil change today") => new(subject, text, new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero));

    private static string Answer(
        string kind = "service_record",
        double? confidence = 0.9,
        object? lineItems = null,
        Action<Dictionary<string, object?>>? change = null)
    {
        var fields = new Dictionary<string, object?>
        {
            ["kind"] = kind,
            ["confidence"] = confidence,
            ["notes"] = "Looks like an invoice",
            ["vehicleHint"] = new { vin = "1FTEW1EP0JKD00001", year = 2018, make = "Ford", model = "F-150", odometer = 48210 },
            ["date"] = "2026-10-12",
            ["odometerReading"] = 48210,
            ["shopOrPerson"] = "Main St Garage",
            ["totalCost"] = 84.5,
            ["unitPrice"] = null,
            ["quantity"] = null,
            ["type"] = null,
            ["nextDueDate"] = null,
            ["state"] = null,
            ["expirationDate"] = null,
            ["startDate"] = null,
            ["endDate"] = null,
            ["duration"] = null,
            ["recurrence"] = null,
            ["lineItems"] = lineItems ?? new[]
            {
                new { description = "Oil change", flatCost = (double?)null, laborHours = (double?)0.5, laborRate = (double?)90.0, parts = new[] { new { name = "Filter", quantity = (double?)1, unitPrice = (double?)9.5 } } },
            },
        };
        change?.Invoke(fields);
        return JsonSerializer.Serialize(fields);
    }

    private static string Envelope(string answerJson) =>
        JsonSerializer.Serialize(new { id = "resp_1", output = new object[] { new { type = "reasoning", summary = Array.Empty<object>() }, new { type = "message", role = "assistant", content = new[] { new { type = "output_text", text = answerJson } } } } });

    [Fact]
    public async Task Request_IsAStrictSchemaCallWithNoToolsAndNoStorage_AuthenticatedWithABearerToken()
    {
        var handler = FakeHandler.Always(HttpStatusCode.OK, Envelope(Answer()));
        var credential = new FakeCredential();
        var extractor = Create(handler, credential);

        await extractor.ExtractAsync(Input("Fwd: SUBJECT-MARK", "BODY-MARK"), CancellationToken.None);

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
        Assert.Contains("untrusted", root.GetProperty("instructions").GetString(), StringComparison.OrdinalIgnoreCase);
        var format = root.GetProperty("text").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());
        var schema = format.GetProperty("schema");
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Contains("lineItems", schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        var content = root.GetProperty("input")[0].GetProperty("content");
        Assert.All(content.EnumerateArray(), part => Assert.Equal("input_text", part.GetProperty("type").GetString()));
        var sent = string.Join('\n', content.EnumerateArray().Select(part => part.GetProperty("text").GetString()));
        Assert.Contains("SUBJECT-MARK", sent);
        Assert.Contains("BODY-MARK", sent);
    }

    [Fact]
    public async Task AValidAnswer_IsParsedIntoTheResult()
    {
        var extractor = Create(FakeHandler.Always(HttpStatusCode.OK, Envelope(Answer())));

        var result = await extractor.ExtractAsync(Input(), CancellationToken.None);

        Assert.False(result.IsFailure);
        Assert.Equal(ExtractionKind.ServiceRecord, result.Kind);
        Assert.Equal(0.9m, result.Confidence);
        Assert.Equal("Looks like an invoice", result.Notes);
        Assert.Equal(new VehicleHint("1FTEW1EP0JKD00001", 2018, "Ford", "F-150", 48210), result.VehicleHint);
        Assert.Equal("2026-10-12", result.Date);
        Assert.Equal(48210, result.OdometerReading);
        Assert.Equal("Main St Garage", result.ShopOrPerson);
        Assert.Equal(84.5m, result.TotalCost);
        var line = Assert.Single(result.LineItems!);
        Assert.Equal("Oil change", line.Description);
        Assert.Null(line.FlatCost);
        Assert.Equal(0.5m, line.LaborHours);
        Assert.Equal(90m, line.LaborRate);
        var part = Assert.Single(line.Parts!);
        Assert.Equal(new ExtractedPart("Filter", 1m, 9.5m), part);
    }

    [Fact]
    public async Task TheSchemaAsksForTheTaxAndAPerLineLaborTotal_AndTheInstructionsExplainDiscountsTaxAndLabor()
    {
        var handler = FakeHandler.Always(HttpStatusCode.OK, Envelope(Answer()));

        await Create(handler).ExtractAsync(Input(), CancellationToken.None);

        using var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body);
        var root = body.RootElement;
        var schema = root.GetProperty("text").GetProperty("format").GetProperty("schema");
        Assert.Contains("tax", schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(["number", "null"], schema.GetProperty("properties").GetProperty("tax").GetProperty("type").EnumerateArray().Select(item => item.GetString()));
        var line = schema.GetProperty("properties").GetProperty("lineItems").GetProperty("items");
        Assert.Contains("laborCost", line.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(["number", "null"], line.GetProperty("properties").GetProperty("laborCost").GetProperty("type").EnumerateArray().Select(item => item.GetString()));
        var instructions = root.GetProperty("instructions").GetString()!;
        Assert.Contains("discount", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("negative", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tax", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("laborCost", instructions, StringComparison.Ordinal);
        Assert.Contains("MISC", instructions, StringComparison.Ordinal);
        Assert.Contains("document total", instructions, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnAnswerWithATaxADiscountAndALaborTotalLine_IsParsed()
    {
        var lines = new object[]
        {
            new { description = "Coupon", flatCost = (double?)-4.04, laborHours = (double?)null, laborRate = (double?)null, laborCost = (double?)null, parts = Array.Empty<object>() },
            new { description = "Labor", flatCost = (double?)null, laborHours = (double?)null, laborRate = (double?)null, laborCost = (double?)180.0, parts = Array.Empty<object>() },
        };
        var extractor = Create(FakeHandler.Always(HttpStatusCode.OK, Envelope(Answer(lineItems: lines, change: fields => fields["tax"] = 8.25))));

        var result = await extractor.ExtractAsync(Input(), CancellationToken.None);

        Assert.False(result.IsFailure);
        Assert.Equal(8.25m, result.Tax);
        Assert.Equal(-4.04m, result.LineItems![0].FlatCost);
        Assert.Equal(180m, result.LineItems[1].LaborCost);
    }

    [Fact]
    public async Task AnAnswerThatOmitsTheNewFields_StillParses_WithThemNull()
    {
        var extractor = Create(FakeHandler.Always(HttpStatusCode.OK, Envelope(Answer())));

        var result = await extractor.ExtractAsync(Input(), CancellationToken.None);

        Assert.False(result.IsFailure);
        Assert.Null(result.Tax);
        Assert.Null(Assert.Single(result.LineItems!).LaborCost);
    }

    [Fact]
    public async Task ATopLevelOutputTextAnswer_IsAlsoAccepted()
    {
        var body = JsonSerializer.Serialize(new { output_text = Answer("fuel") });
        var extractor = Create(FakeHandler.Always(HttpStatusCode.OK, body));

        var result = await extractor.ExtractAsync(Input(), CancellationToken.None);

        Assert.Equal(ExtractionKind.Fuel, result.Kind);
    }

    [Fact]
    public async Task NoneAndAnUnknownKind_AreBothKindNone()
    {
        var none = await Create(FakeHandler.Always(HttpStatusCode.OK, Envelope(Answer("none", lineItems: Array.Empty<object>())))).ExtractAsync(Input(), CancellationToken.None);
        var unknown = await Create(FakeHandler.Always(HttpStatusCode.OK, Envelope(Answer("oil_change")))).ExtractAsync(Input(), CancellationToken.None);

        Assert.False(none.IsFailure);
        Assert.Equal(ExtractionKind.None, none.Kind);
        Assert.False(unknown.IsFailure);
        Assert.Equal(ExtractionKind.None, unknown.Kind);
    }

    [Fact]
    public async Task AnAnswerThatBreaksTheSchema_IsAFailedResult_NotAnException()
    {
        var answers = new[]
        {
            "this is not json",
            "{\"kind\": \"fuel\"",
            Answer(change: fields => fields.Remove("totalCost")),
            Answer(change: fields => fields["extra"] = "not in the schema"),
            Answer(change: fields => fields["odometerReading"] = "forty thousand"),
            Answer(change: fields => fields["lineItems"] = "none"),
            "[]",
            "null",
        };

        foreach (var answer in answers)
        {
            var result = await Create(FakeHandler.Always(HttpStatusCode.OK, Envelope(answer))).ExtractAsync(Input(), CancellationToken.None);

            Assert.True(result.IsFailure, answer);
            Assert.Equal("invalid_output", result.FailureReason);
        }
    }

    [Fact]
    public async Task AnEnvelopeWithoutAnswerText_IsAFailedResult()
    {
        foreach (var body in new[] { "{}", "not json", "{\"output\":[]}", "{\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"refusal\",\"refusal\":\"no\"}]}]}" })
        {
            var result = await Create(FakeHandler.Always(HttpStatusCode.OK, body)).ExtractAsync(Input(), CancellationToken.None);

            Assert.True(result.IsFailure, body);
            Assert.Equal("invalid_output", result.FailureReason);
        }
    }

    [Theory]
    [InlineData(400, "{\"error\":{\"code\":\"content_filter\",\"message\":\"blocked\"}}", "content_filter")]
    [InlineData(400, "{\"error\":{\"code\":\"invalid_prompt\"}}", "service_error")]
    [InlineData(401, "{}", "service_error")]
    [InlineData(404, "no body json", "service_error")]
    public async Task ClientErrors_BecomeFailedResultsWithAReasonCode(int status, string body, string reason)
    {
        var extractor = Create(FakeHandler.Always((HttpStatusCode)status, body));

        var result = await extractor.ExtractAsync(Input(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(reason, result.FailureReason);
    }

    [Theory]
    [InlineData(429, "rate_limited")]
    [InlineData(500, "service_error")]
    [InlineData(503, "service_error")]
    public async Task TransientErrors_RetryThroughTheResiliencePipeline_AndThenBecomeFailedResults(int status, string reason)
    {
        var handler = FakeHandler.Always((HttpStatusCode)status, "{}");
        using var provider = BuildClientWithResilience(handler);

        var result = await provider.GetRequiredService<ResponsesApiExtractor>().ExtractAsync(Input(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(reason, result.FailureReason);
        Assert.True(handler.Requests.Count > 1, "The resilience handler should have retried.");
    }

    [Fact]
    public async Task ATransientError_ThatClears_ReturnsTheAnswer()
    {
        var handler = new FakeHandler((count, _, _) => Task.FromResult(count == 1
            ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}") }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Envelope(Answer()), Encoding.UTF8, "application/json") }));
        using var provider = BuildClientWithResilience(handler);

        var result = await provider.GetRequiredService<ResponsesApiExtractor>().ExtractAsync(Input(), CancellationToken.None);

        Assert.False(result.IsFailure);
        Assert.Equal(2, handler.Requests.Count);
    }

    // The same typed-client shape as production, with the retry delay removed so the test does not wait.
    private static ServiceProvider BuildClientWithResilience(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TokenCredential>(new FakeCredential());
        services.AddLogging();
        services.AddOptions<ExtractionOptions>().Configure(options =>
        {
            options.Endpoint = Endpoint;
            options.Deployment = "gpt-test";
        });
        services.AddHttpClient<ResponsesApiExtractor>()
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.Delay = TimeSpan.Zero;
                options.Retry.BackoffType = Polly.DelayBackoffType.Constant;
                options.Retry.UseJitter = false;
            });
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ACallThatRunsPastTheTimeout_IsCancelledAndFailsAsTimeout()
    {
        var handler = new FakeHandler(async (_, _, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var extractor = Create(handler, timeoutSeconds: 1);

        var result = await extractor.ExtractAsync(Input(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("timeout", result.FailureReason);
    }

    [Fact]
    public async Task ACallerCancellation_IsNotSwallowed()
    {
        var handler = new FakeHandler(async (_, _, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(handler).ExtractAsync(Input(), cancellation.Token));
    }

    [Fact]
    public async Task AFailingCredential_IsAFailedResult()
    {
        var handler = FakeHandler.Always(HttpStatusCode.OK, Envelope(Answer()));
        var extractor = Create(handler, new FakeCredential(fail: true));

        var result = await extractor.ExtractAsync(Input(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("service_error", result.FailureReason);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(null, "gpt-test", false)]
    [InlineData("", "gpt-test", false)]
    [InlineData(Endpoint, null, false)]
    [InlineData(Endpoint, " ", false)]
    [InlineData(Endpoint, "gpt-test", true)]
    public void IsEnabled_RequiresBothTheEndpointAndTheDeployment(string? endpoint, string? deployment, bool enabled)
    {
        var extractor = Create(FakeHandler.Always(HttpStatusCode.OK, "{}"), endpoint: endpoint, deployment: deployment);

        Assert.Equal(enabled, extractor.IsEnabled);
    }

    [Fact]
    public async Task Logs_NeverContainTheEmailOrTheModelOutput()
    {
        var logger = new CapturingLogger<ResponsesApiExtractor>();
        var secretBody = "{\"error\":{\"code\":\"content_filter\",\"message\":\"QZLEAK QZSUBJECT QZTEXT\"}}";
        var failing = Create(FakeHandler.Always(HttpStatusCode.BadRequest, secretBody), logger: logger);
        var garbled = Create(FakeHandler.Always(HttpStatusCode.OK, Envelope("QZLEAK not json")), logger: logger);
        var ok = Create(FakeHandler.Always(HttpStatusCode.OK, Envelope(Answer(change: fields => fields["shopOrPerson"] = "QZLEAK Garage"))), logger: logger);

        foreach (var extractor in new[] { failing, garbled, ok })
        {
            await extractor.ExtractAsync(Input("QZSUBJECT", "QZTEXT"), CancellationToken.None);
        }

        Assert.NotEmpty(logger.Entries);
        Assert.DoesNotContain(logger.Entries, entry => entry.Contains("QZ", StringComparison.Ordinal));
    }

    // Documents ---------------------------------------------------------------------------------------------------

    // A stream that cannot seek and hands out data in the sizes it is asked for; it records how much was read and the largest single read.
    private sealed class OnePassStream(byte[] data) : Stream
    {
        private int position;

        public long TotalRead { get; private set; }

        public int LargestRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var count = Math.Min(buffer.Length, data.Length - position);
            data.AsSpan(position, count).CopyTo(buffer);
            position += count;
            TotalRead += count;
            LargestRead = Math.Max(LargestRead, buffer.Length);
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new(Read(buffer.Span));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static ExtractionInput WithDocument(Stream content, string contentType, string fileName = "invoice.pdf", string subject = "Fwd: invoice", string text = "Oil change today") =>
        new(subject, text, new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero), new ExtractionDocument(content, contentType, fileName));

    private static JsonElement ContentParts(string requestBody) => JsonDocument.Parse(requestBody).RootElement.GetProperty("input")[0].GetProperty("content").Clone();

    [Fact]
    public async Task APdf_IsSentAsAnInputFile_WithItsFileNameAndABase64DataUri()
    {
        var handler = FakeHandler.Always(HttpStatusCode.OK, Envelope(Answer()));
        var bytes = "%PDF-1.4 sample document bytes"u8.ToArray();

        var result = await Create(handler).ExtractAsync(WithDocument(new MemoryStream(bytes), "application/pdf", "march-invoice.pdf"), CancellationToken.None);

        Assert.False(result.IsFailure);
        var parts = ContentParts(Assert.Single(handler.Requests).Body);
        var file = Assert.Single(parts.EnumerateArray(), part => part.GetProperty("type").GetString() == "input_file");
        Assert.Equal("march-invoice.pdf", file.GetProperty("filename").GetString());
        Assert.Equal($"data:application/pdf;base64,{Convert.ToBase64String(bytes)}", file.GetProperty("file_data").GetString());
        Assert.DoesNotContain(parts.EnumerateArray(), part => part.GetProperty("type").GetString() == "input_image");
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/webp")]
    public async Task AnImage_IsSentAsAnInputImage_WithTheRightMediaTypeInTheDataUri(string contentType)
    {
        var handler = FakeHandler.Always(HttpStatusCode.OK, Envelope(Answer()));
        var bytes = IntakeBytes();

        await Create(handler).ExtractAsync(WithDocument(new MemoryStream(bytes), contentType, "receipt.img"), CancellationToken.None);

        var parts = ContentParts(Assert.Single(handler.Requests).Body);
        var image = Assert.Single(parts.EnumerateArray(), part => part.GetProperty("type").GetString() == "input_image");
        Assert.Equal($"data:{contentType};base64,{Convert.ToBase64String(bytes)}", image.GetProperty("image_url").GetString());
        Assert.DoesNotContain(parts.EnumerateArray(), part => part.GetProperty("type").GetString() == "input_file");

        static byte[] IntakeBytes() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 1, 2, 3, 250, 251, 252, 253, 254, 255];
    }

    [Fact]
    public async Task TheEmailSubjectAndText_AccompanyTheDocument_AsContextOnly()
    {
        var handler = FakeHandler.Always(HttpStatusCode.OK, Envelope(Answer()));

        await Create(handler).ExtractAsync(WithDocument(new MemoryStream("%PDF-1.4"u8.ToArray()), "application/pdf", subject: "SUBJECT-MARK", text: "BODY-MARK"), CancellationToken.None);

        var parts = ContentParts(Assert.Single(handler.Requests).Body);
        var text = Assert.Single(parts.EnumerateArray(), part => part.GetProperty("type").GetString() == "input_text").GetProperty("text").GetString()!;
        Assert.Contains("SUBJECT-MARK", text);
        Assert.Contains("BODY-MARK", text);
        Assert.Contains("context only", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["input_text", "input_file"], parts.EnumerateArray().Select(part => part.GetProperty("type").GetString()));
    }

    [Fact]
    public async Task ATenMegabyteDocument_IsStreamedIntoTheRequest_InOnePassAndSmallReads()
    {
        const int size = 10 * 1024 * 1024;
        var bytes = new byte[size];
        "%PDF-1.7\n"u8.CopyTo(bytes);
        new Random(7).NextBytes(bytes.AsSpan(16));
        var source = new OnePassStream(bytes);
        var handler = FakeHandler.Always(HttpStatusCode.OK, Envelope(Answer()));

        var result = await Create(handler).ExtractAsync(WithDocument(source, "application/pdf"), CancellationToken.None);

        Assert.False(result.IsFailure);
        // The file was read once, front to back, in chunks: never loaded whole, and never read again to build a second copy.
        Assert.Equal(size, source.TotalRead);
        Assert.True(source.LargestRead <= 1024 * 1024, $"Largest single read was {source.LargestRead} bytes.");

        var parts = ContentParts(Assert.Single(handler.Requests).Body);
        var data = parts.EnumerateArray().Single(part => part.GetProperty("type").GetString() == "input_file").GetProperty("file_data").GetString()!;
        const string prefix = "data:application/pdf;base64,";
        Assert.StartsWith(prefix, data);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Convert.FromBase64String(data[prefix.Length..]))));
    }

    [Fact]
    public async Task ASeekableDocument_GivesTheRequestAnExactContentLength_AndIsSentInFullOnARetry()
    {
        var bytes = new byte[300_001];
        "%PDF-1.7\n"u8.CopyTo(bytes);
        new Random(11).NextBytes(bytes.AsSpan(16));
        var lengths = new List<(long? Declared, int Actual)>();
        var handler = new FakeHandler(async (count, request, cancellationToken) =>
        {
            var actual = (await request.Content!.ReadAsByteArrayAsync(cancellationToken)).Length;
            lengths.Add((request.Content.Headers.ContentLength, actual));
            return count == 1
                ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Envelope(Answer()), Encoding.UTF8, "application/json") };
        });
        using var provider = BuildClientWithResilience(handler);

        var result = await provider.GetRequiredService<ResponsesApiExtractor>().ExtractAsync(WithDocument(new MemoryStream(bytes), "application/pdf"), CancellationToken.None);

        Assert.False(result.IsFailure);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(lengths, entry => Assert.Equal(entry.Actual, entry.Declared));
        Assert.Equal(handler.Requests[0].Body, handler.Requests[1].Body);
        var file = ContentParts(handler.Requests[1].Body).EnumerateArray().Single(part => part.GetProperty("type").GetString() == "input_file");
        Assert.Equal(Convert.ToBase64String(bytes), file.GetProperty("file_data").GetString()!["data:application/pdf;base64,".Length..]);
    }

    [Fact]
    public async Task ADocumentThatCannotBeRead_IsAFailedResult_NotAnException()
    {
        var handler = FakeHandler.Always(HttpStatusCode.OK, Envelope(Answer()));
        var broken = new ThrowingStream();

        var result = await Create(handler).ExtractAsync(WithDocument(broken, "application/pdf"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("service_error", result.FailureReason);
    }

    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("The blob could not be read.");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw new IOException("The blob could not be read.");

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
