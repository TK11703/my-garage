using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Options;
using MyGarage.Api.Common;

namespace MyGarage.Api.EmailIntake.Extraction;

// Reads an email or one attached document with a strict JSON schema (research R6, R7); the HTTP and token plumbing is the shared ResponsesApiClient.
// The model gets no tools and no data of ours; every failure becomes a failed result with a reason code, never email content.
public sealed class ResponsesApiExtractor : IDraftExtractor
{
    private readonly ResponsesApiClient client;
    private readonly IOptions<ExtractionOptions> options;
    private readonly ILogger<ResponsesApiExtractor> logger;

    private static readonly JsonSerializerOptions AnswerOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
    };

    public ResponsesApiExtractor(HttpClient httpClient, IOptions<ExtractionOptions> options, TokenCredential credential, ILogger<ResponsesApiExtractor> logger)
    {
        client = new ResponsesApiClient(httpClient, options, credential);
        this.options = options;
        this.logger = logger;
    }

    public bool IsEnabled => client.IsEnabled;

    public async Task<ExtractionResult> ExtractAsync(ExtractionInput input, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var call = new ResponsesCall(
            ExtractionSchema.Instructions,
            ExtractionSchema.Name,
            ExtractionSchema.Json,
            BuildInputText(input),
            input.Document is { } document ? new ResponsesDocument(document.Content, document.ContentType, document.FileName) : null,
            TimeSpan.FromSeconds(Math.Max(1, options.Value.TimeoutSeconds)));

        var outcome = await client.SendAsync(call, cancellationToken);
        var result = outcome.IsFailure ? ExtractionResult.Failed(outcome.FailureReason!) : ParseAnswer(outcome.AnswerText!);

        if (result.IsFailure)
        {
            if (outcome.StatusCode is { } status)
            {
                logger.LogWarning("Extraction failed with {Reason} (HTTP {StatusCode})", result.FailureReason, status);
            }
            else
            {
                logger.LogWarning("Extraction failed with {Reason}", result.FailureReason);
            }
        }
        else
        {
            logger.LogInformation("Extraction answered kind {Kind} in {ElapsedMs} ms", result.Kind, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        return result;
    }

    private static string BuildInputText(ExtractionInput input)
    {
        var email = new StringBuilder();
        email.Append(input.Document is null ? "Email to read." : "Email text, context only; the attached document is what to read.");
        email.Append("\nSubject: ").Append(input.Subject);
        if (input.ReceivedAt is { } receivedAt)
        {
            email.Append("\nReceived: ").Append(receivedAt.ToString("O"));
        }

        email.Append("\n\n").Append(input.Text);
        return email.ToString();
    }

    private static ExtractionResult ParseAnswer(string answerJson)
    {
        ModelAnswer? answer;
        try
        {
            answer = JsonSerializer.Deserialize<ModelAnswer>(answerJson, AnswerOptions);
        }
        catch (JsonException)
        {
            return ExtractionResult.Failed("invalid_output");
        }

        if (answer is null)
        {
            return ExtractionResult.Failed("invalid_output");
        }

        return new ExtractionResult
        {
            Kind = ParseKind(answer.Kind),
            Confidence = answer.Confidence,
            Notes = answer.Notes,
            VehicleHint = new VehicleHint(answer.VehicleHint.Vin, answer.VehicleHint.Year, answer.VehicleHint.Make, answer.VehicleHint.Model, answer.VehicleHint.Odometer),
            Date = answer.Date,
            OdometerReading = answer.OdometerReading,
            ShopOrPerson = answer.ShopOrPerson,
            TotalCost = answer.TotalCost,
            Tax = answer.Tax,
            UnitPrice = answer.UnitPrice,
            Quantity = answer.Quantity,
            Type = answer.Type,
            NextDueDate = answer.NextDueDate,
            State = answer.State,
            ExpirationDate = answer.ExpirationDate,
            StartDate = answer.StartDate,
            EndDate = answer.EndDate,
            Duration = answer.Duration,
            Recurrence = answer.Recurrence,
            LineItems = answer.LineItems?.Select(line => new ExtractedLineItem(
                line.Description,
                line.FlatCost,
                line.LaborHours,
                line.LaborRate,
                line.Parts?.Select(part => new ExtractedPart(part.Name, part.Quantity, part.UnitPrice)).ToList(),
                line.LaborCost)).ToList(),
        };
    }

    // An unknown kind is treated as not a maintenance item.
    private static ExtractionKind ParseKind(string kind) => kind switch
    {
        "service_record" => ExtractionKind.ServiceRecord,
        "fuel" => ExtractionKind.Fuel,
        "energy" => ExtractionKind.Energy,
        "inspection" => ExtractionKind.Inspection,
        "registration" => ExtractionKind.Registration,
        "insurance" => ExtractionKind.Insurance,
        "tax" => ExtractionKind.Tax,
        _ => ExtractionKind.None,
    };

    // Mirrors ExtractionSchema: every property must be present (null is allowed), and none may be added.
    private sealed class ModelAnswer
    {
        public required string Kind { get; init; }

        public required decimal? Confidence { get; init; }

        public required string? Notes { get; init; }

        public required HintAnswer VehicleHint { get; init; }

        public required string? Date { get; init; }

        public required int? OdometerReading { get; init; }

        public required string? ShopOrPerson { get; init; }

        public required decimal? TotalCost { get; init; }

        // Optional here so an older stored answer without it still parses; the request schema requires it.
        public decimal? Tax { get; init; }

        public required decimal? UnitPrice { get; init; }

        public required decimal? Quantity { get; init; }

        public required string? Type { get; init; }

        public required string? NextDueDate { get; init; }

        public required string? State { get; init; }

        public required string? ExpirationDate { get; init; }

        public required string? StartDate { get; init; }

        public required string? EndDate { get; init; }

        public required string? Duration { get; init; }

        public required string? Recurrence { get; init; }

        public required List<LineAnswer>? LineItems { get; init; }
    }

    private sealed class HintAnswer
    {
        public required string? Vin { get; init; }

        public required int? Year { get; init; }

        public required string? Make { get; init; }

        public required string? Model { get; init; }

        public required int? Odometer { get; init; }
    }

    private sealed class LineAnswer
    {
        public required string? Description { get; init; }

        public required decimal? FlatCost { get; init; }

        public required decimal? LaborHours { get; init; }

        public required decimal? LaborRate { get; init; }

        public decimal? LaborCost { get; init; }

        public required List<PartAnswer>? Parts { get; init; }
    }

    private sealed class PartAnswer
    {
        public required string? Name { get; init; }

        public required decimal? Quantity { get; init; }

        public required decimal? UnitPrice { get; init; }
    }
}
