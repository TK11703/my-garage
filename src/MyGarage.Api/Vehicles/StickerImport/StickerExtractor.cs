using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Options;
using MyGarage.Api.Common;
using MyGarage.Api.EmailIntake.Extraction;

namespace MyGarage.Api.Vehicles.StickerImport;

// Reads a window sticker through the shared Responses client with a strict schema (research R1, R7).
// The model gets no tools and no data except the sticker and, on an edit, the vehicle's own feature names.
public sealed class StickerExtractor : IStickerExtractor
{
    private static readonly JsonSerializerOptions AnswerOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
    };

    private readonly ResponsesApiClient client;
    private readonly IOptions<StickerImportOptions> options;
    private readonly ILogger<StickerExtractor> logger;

    public StickerExtractor(
        HttpClient httpClient,
        IOptions<ExtractionOptions> extractionOptions,
        IOptions<StickerImportOptions> options,
        TokenCredential credential,
        ILogger<StickerExtractor> logger)
    {
        client = new ResponsesApiClient(httpClient, extractionOptions, credential);
        this.options = options;
        this.logger = logger;
    }

    public bool IsEnabled => client.IsEnabled;

    public async Task<StickerExtraction> ExtractAsync(StickerInput input, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var call = new ResponsesCall(
            StickerSchema.Instructions,
            StickerSchema.Name,
            StickerSchema.Json,
            BuildInputText(input.ExistingFeatures),
            new ResponsesDocument(input.Content, input.ContentType, input.FileName),
            TimeSpan.FromSeconds(Math.Max(1, options.Value.TimeoutSeconds)));

        var outcome = await client.SendAsync(call, cancellationToken);
        var result = outcome.IsFailure ? StickerExtraction.Failed(outcome.FailureReason!) : ParseAnswer(outcome.AnswerText!);

        if (result.IsFailure)
        {
            logger.LogWarning("Sticker reading failed with {Reason} (HTTP {StatusCode})", result.FailureReason, outcome.StatusCode);
        }
        else
        {
            logger.LogInformation("Sticker read in {ElapsedMs} ms", (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        return result;
    }

    private static string BuildInputText(IReadOnlyList<string>? existingFeatures)
    {
        var text = new StringBuilder("Read the attached window sticker.");
        if (existingFeatures is { Count: > 0 })
        {
            text.Append("\n\nExisting features of this vehicle (numbered):");
            for (var i = 0; i < existingFeatures.Count; i++)
            {
                text.Append('\n').Append(i + 1).Append(". ").Append(existingFeatures[i]);
            }
        }

        return text.ToString();
    }

    private static StickerExtraction ParseAnswer(string answerJson)
    {
        ModelAnswer? answer;
        try
        {
            answer = JsonSerializer.Deserialize<ModelAnswer>(answerJson, AnswerOptions);
        }
        catch (JsonException)
        {
            return StickerExtraction.Failed("invalid_output");
        }

        if (answer is null)
        {
            return StickerExtraction.Failed("invalid_output");
        }

        return new StickerExtraction
        {
            IsWindowSticker = answer.IsWindowSticker,
            VehiclesDetected = answer.VehiclesDetected,
            Year = answer.Year,
            Make = answer.Make,
            Model = answer.Model,
            Trim = answer.Trim,
            Vin = answer.Vin,
            BodyStyle = answer.BodyStyle,
            Engine = answer.Engine,
            Horsepower = answer.Horsepower,
            FuelType = answer.FuelType,
            Transmission = answer.Transmission,
            Drivetrain = answer.Drivetrain,
            ExteriorColor = answer.ExteriorColor,
            Seats = answer.Seats,
            Doors = answer.Doors,
            WheelSizeInches = answer.WheelSizeInches,
            TireSize = answer.TireSize,
            Features = [.. answer.Features.Select(feature => new ExtractedFeature(feature.Name, feature.SameAs))],
        };
    }

    // Mirrors StickerSchema: every property must be present (null is allowed), and none may be added.
    private sealed class ModelAnswer
    {
        public required bool IsWindowSticker { get; init; }

        public required int VehiclesDetected { get; init; }

        public required int? Year { get; init; }

        public required string? Make { get; init; }

        public required string? Model { get; init; }

        public required string? Trim { get; init; }

        public required string? Vin { get; init; }

        public required string? BodyStyle { get; init; }

        public required string? Engine { get; init; }

        public required int? Horsepower { get; init; }

        public required string? FuelType { get; init; }

        public required string? Transmission { get; init; }

        public required string? Drivetrain { get; init; }

        public required string? ExteriorColor { get; init; }

        public required int? Seats { get; init; }

        public required int? Doors { get; init; }

        public required decimal? WheelSizeInches { get; init; }

        public required string? TireSize { get; init; }

        public required List<FeatureAnswer> Features { get; init; }
    }

    private sealed class FeatureAnswer
    {
        public required string? Name { get; init; }

        public required List<int> SameAs { get; init; }
    }
}
