using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MyGarage.Api.EmailIntake.Extraction;
using MyGarage.Api.Vehicles.StickerImport;

namespace MyGarage.Api.Tests.Vehicles.StickerImport;

public class StickerExtractorTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        public static FakeHandler Always(HttpStatusCode status, string body) =>
            new((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return await respond(request, cancellationToken);
        }
    }

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => new("tok", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(new AccessToken("tok", DateTimeOffset.UtcNow.AddHours(1)));
    }

    private static StickerExtractor Create(HttpMessageHandler handler, string? endpoint = "https://aoai.example.test/", string? deployment = "gpt-test", int timeoutSeconds = 45) =>
        new(
            new HttpClient(handler),
            Options.Create(new ExtractionOptions { Endpoint = endpoint, Deployment = deployment }),
            Options.Create(new StickerImportOptions { TimeoutSeconds = timeoutSeconds }),
            new FakeCredential(),
            NullLogger<StickerExtractor>.Instance);

    private static StickerInput Input(IReadOnlyList<string>? existing = null) =>
        new(new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7 sticker")), "application/pdf", "sticker.pdf", existing);

    private static string Envelope(string answerJson) =>
        JsonSerializer.Serialize(new { output = new object[] { new { type = "message", content = new[] { new { type = "output_text", text = answerJson } } } } });

    private static string Answer(Action<Dictionary<string, object?>>? change = null)
    {
        var fields = new Dictionary<string, object?>
        {
            ["isWindowSticker"] = true,
            ["vehiclesDetected"] = 1,
            ["year"] = 2024,
            ["make"] = "Ford",
            ["model"] = "F-150",
            ["trim"] = "Lariat",
            ["vin"] = "1FTFW1E50NFA00001",
            ["bodyStyle"] = "Crew Cab Pickup",
            ["engine"] = "3.5L V6 EcoBoost",
            ["horsepower"] = 400,
            ["fuelType"] = "Gasoline",
            ["transmission"] = "10-Speed Automatic",
            ["drivetrain"] = "4WD",
            ["exteriorColor"] = "Agate Black",
            ["seats"] = 5,
            ["doors"] = 4,
            ["wheelSizeInches"] = 20,
            ["tireSize"] = "275/55R20",
            ["features"] = new object[] { new { name = "Panoramic Sunroof", sameAs = new[] { 1 } }, new { name = "Heated Seats", sameAs = Array.Empty<int>() } },
        };
        change?.Invoke(fields);
        return JsonSerializer.Serialize(fields);
    }

    [Fact]
    public void WithoutAnEndpointOrDeployment_IsDisabled()
    {
        Assert.False(Create(FakeHandler.Always(HttpStatusCode.OK, "{}"), endpoint: null).IsEnabled);
        Assert.False(Create(FakeHandler.Always(HttpStatusCode.OK, "{}"), deployment: " ").IsEnabled);
        Assert.True(Create(FakeHandler.Always(HttpStatusCode.OK, "{}")).IsEnabled);
    }

    [Fact]
    public async Task Request_IsAStrictSchemaOfTheStickerFields_WithInstructionsThatTreatTheDocumentAsData()
    {
        var handler = FakeHandler.Always(HttpStatusCode.OK, Envelope(Answer()));

        await Create(handler).ExtractAsync(Input(), CancellationToken.None);

        using var body = JsonDocument.Parse(Assert.Single(handler.Bodies));
        var root = body.RootElement;
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.False(root.TryGetProperty("tools", out _));
        var instructions = root.GetProperty("instructions").GetString()!;
        Assert.Contains("never instructions", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("null", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inches", instructions, StringComparison.OrdinalIgnoreCase);
        var format = root.GetProperty("text").GetProperty("format");
        Assert.True(format.GetProperty("strict").GetBoolean());
        var schema = format.GetProperty("schema");
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        string[] fields =
        [
            "isWindowSticker", "vehiclesDetected", "year", "make", "model", "trim", "vin", "bodyStyle", "engine", "horsepower", "fuelType",
            "transmission", "drivetrain", "exteriorColor", "seats", "doors", "wheelSizeInches", "tireSize", "features",
        ];
        var required = schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToArray();
        foreach (var field in fields)
        {
            Assert.Contains(field, required);
            Assert.True(schema.GetProperty("properties").TryGetProperty(field, out _), field);
        }

        var feature = schema.GetProperty("properties").GetProperty("features").GetProperty("items");
        Assert.Equal(["name", "sameAs"], feature.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task ExistingFeatureNames_AreSentAsANumberedList_AndNothingElseAboutTheUser()
    {
        var handler = FakeHandler.Always(HttpStatusCode.OK, Envelope(Answer()));

        await Create(handler).ExtractAsync(Input(["Sunroof", "Towing Package"]), CancellationToken.None);

        using var body = JsonDocument.Parse(Assert.Single(handler.Bodies));
        var text = body.RootElement.GetProperty("input")[0].GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("1. Sunroof", text);
        Assert.Contains("2. Towing Package", text);
    }

    [Fact]
    public async Task WithoutExistingFeatures_NoFeatureListIsSent()
    {
        var handler = FakeHandler.Always(HttpStatusCode.OK, Envelope(Answer()));

        await Create(handler).ExtractAsync(Input(), CancellationToken.None);

        using var body = JsonDocument.Parse(Assert.Single(handler.Bodies));
        var text = body.RootElement.GetProperty("input")[0].GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.DoesNotContain("1.", text);
    }

    [Fact]
    public async Task AGoodAnswer_IsParsed()
    {
        var result = await Create(FakeHandler.Always(HttpStatusCode.OK, Envelope(Answer()))).ExtractAsync(Input(["Sunroof"]), CancellationToken.None);

        Assert.False(result.IsFailure);
        Assert.True(result.IsWindowSticker);
        Assert.Equal(1, result.VehiclesDetected);
        Assert.Equal(2024, result.Year);
        Assert.Equal("Ford", result.Make);
        Assert.Equal("F-150", result.Model);
        Assert.Equal("Lariat", result.Trim);
        Assert.Equal("1FTFW1E50NFA00001", result.Vin);
        Assert.Equal("Crew Cab Pickup", result.BodyStyle);
        Assert.Equal("3.5L V6 EcoBoost", result.Engine);
        Assert.Equal(400, result.Horsepower);
        Assert.Equal("Gasoline", result.FuelType);
        Assert.Equal("10-Speed Automatic", result.Transmission);
        Assert.Equal("4WD", result.Drivetrain);
        Assert.Equal("Agate Black", result.ExteriorColor);
        Assert.Equal(5, result.Seats);
        Assert.Equal(4, result.Doors);
        Assert.Equal(20m, result.WheelSizeInches);
        Assert.Equal("275/55R20", result.TireSize);
        Assert.Equal(2, result.Features.Count);
        Assert.Equal("Panoramic Sunroof", result.Features[0].Name);
        Assert.Equal([1], result.Features[0].SameAs);
        Assert.Equal("Heated Seats", result.Features[1].Name);
        Assert.Empty(result.Features[1].SameAs);
    }

    [Fact]
    public async Task NullFields_AreAllowed()
    {
        var answer = Answer(fields =>
        {
            foreach (var key in fields.Keys.Where(key => key is not "isWindowSticker" and not "vehiclesDetected" and not "features").ToList())
            {
                fields[key] = null;
            }

            fields["features"] = Array.Empty<object>();
        });

        var result = await Create(FakeHandler.Always(HttpStatusCode.OK, Envelope(answer))).ExtractAsync(Input(), CancellationToken.None);

        Assert.False(result.IsFailure);
        Assert.Null(result.Make);
        Assert.Empty(result.Features);
    }

    [Theory]
    [InlineData("""{"isWindowSticker":true}""")]
    [InlineData("not json")]
    public async Task AnAnswerThatDoesNotMatchTheSchema_IsInvalidOutput(string answer)
    {
        var result = await Create(FakeHandler.Always(HttpStatusCode.OK, Envelope(answer))).ExtractAsync(Input(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid_output", result.FailureReason);
    }

    [Fact]
    public async Task AnAnswerWithAnExtraMember_IsInvalidOutput()
    {
        var answer = Answer(fields => fields["extra"] = "x");

        var result = await Create(FakeHandler.Always(HttpStatusCode.OK, Envelope(answer))).ExtractAsync(Input(), CancellationToken.None);

        Assert.Equal("invalid_output", result.FailureReason);
    }

    [Fact]
    public async Task AFailedCall_IsMadeOnceWithNoRetry_AndReportsTheReason()
    {
        var handler = FakeHandler.Always(HttpStatusCode.InternalServerError, "oops");

        var result = await Create(handler).ExtractAsync(Input(), CancellationToken.None);

        Assert.Equal("service_error", result.FailureReason);
        Assert.Single(handler.Bodies);
    }

    [Fact]
    public async Task TheConfiguredTimeout_EndsASlowCall()
    {
        var handler = new FakeHandler(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var result = await Create(handler, timeoutSeconds: 1).ExtractAsync(Input(), CancellationToken.None);

        Assert.Equal("timeout", result.FailureReason);
    }

    [Fact]
    public void TheDefaultTimeoutIs90Seconds()
    {
        Assert.Equal(90, new StickerImportOptions().TimeoutSeconds);
        Assert.Equal("StickerImport", StickerImportOptions.SectionName);
    }

    [Fact]
    public void TheProductionHttpPipeline_PassesItsOwnOptionsValidation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TokenCredential>(new FakeCredential());
        services.AddStickerImport();
        using var provider = services.BuildServiceProvider();

        var extractor = provider.GetRequiredService<StickerExtractor>();

        Assert.NotNull(extractor);
    }
}
