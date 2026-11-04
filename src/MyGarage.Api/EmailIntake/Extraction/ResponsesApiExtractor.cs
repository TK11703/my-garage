using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Microsoft.Extensions.Options;

namespace MyGarage.Api.EmailIntake.Extraction;

// Reads an email or one attached document through the Azure OpenAI Responses API with a strict JSON schema (research R6, R7).
// The model gets no tools and no data of ours; every failure becomes a failed result with a reason code, never email content.
public sealed class ResponsesApiExtractor(
    HttpClient httpClient, IOptions<ExtractionOptions> options, TokenCredential credential, ILogger<ResponsesApiExtractor> logger) : IDraftExtractor
{
    private const string TokenScope = "https://ai.azure.com/.default";

    private static readonly JsonSerializerOptions AnswerOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
    };

    public bool IsEnabled => !string.IsNullOrWhiteSpace(options.Value.Endpoint) && !string.IsNullOrWhiteSpace(options.Value.Deployment);

    public async Task<ExtractionResult> ExtractAsync(ExtractionInput input, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var started = Stopwatch.GetTimestamp();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, settings.TimeoutSeconds)));

        try
        {
            var token = await credential.GetTokenAsync(new TokenRequestContext([TokenScope]), timeout.Token);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{settings.Endpoint!.TrimEnd('/')}/openai/v1/responses"))
            {
                Content = BuildContent(input, settings.Deployment!),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            using var response = await httpClient.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            var result = response.IsSuccessStatusCode ? ParseResponse(body) : ExtractionResult.Failed(ReasonFor(response.StatusCode, body));

            if (result.IsFailure)
            {
                logger.LogWarning("Extraction failed with {Reason} (HTTP {StatusCode})", result.FailureReason, (int)response.StatusCode);
            }
            else
            {
                logger.LogInformation("Extraction answered kind {Kind} in {ElapsedMs} ms", result.Kind, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }

            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Fail("timeout");
        }
        catch (TimeoutException)
        {
            return Fail("timeout");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Network, credential, and service failures all become a failed result; the caller's retry policy decides what happens next.
            return Fail("service_error");
        }

        ExtractionResult Fail(string reason)
        {
            logger.LogWarning("Extraction failed with {Reason}", reason);
            return ExtractionResult.Failed(reason);
        }
    }

    // Without a document the body is plain JSON. With one, the file's base64 is written into the request as it is read from its stream.
    private static HttpContent BuildContent(ExtractionInput input, string deployment)
    {
        var request = BuildRequest(input, deployment, out var placeholder);
        var json = request.ToJsonString();
        if (input.Document is not { } document)
        {
            return new StringContent(json, Encoding.UTF8, "application/json");
        }

        var at = json.IndexOf(placeholder, StringComparison.Ordinal);
        return new DocumentRequestContent(json[..at], json[(at + placeholder.Length)..], document.Content);
    }

    private static JsonObject BuildRequest(ExtractionInput input, string deployment, out string placeholder)
    {
        var email = new StringBuilder();
        email.Append(input.Document is null ? "Email to read." : "Email text, context only; the attached document is what to read.");
        email.Append("\nSubject: ").Append(input.Subject);
        if (input.ReceivedAt is { } receivedAt)
        {
            email.Append("\nReceived: ").Append(receivedAt.ToString("O"));
        }

        email.Append("\n\n").Append(input.Text);

        // Letters and digits only, so the JSON writer leaves it untouched and it can be cut out of the serialized text.
        placeholder = $"DOCUMENT{Guid.NewGuid():N}";
        var content = new JsonArray(new JsonObject { ["type"] = "input_text", ["text"] = email.ToString() });
        if (input.Document is { } document)
        {
            content.Add(document.ContentType == "application/pdf"
                ? new JsonObject { ["type"] = "input_file", ["filename"] = document.FileName, ["file_data"] = $"data:{document.ContentType};base64,{placeholder}" }
                : new JsonObject { ["type"] = "input_image", ["image_url"] = $"data:{document.ContentType};base64,{placeholder}" });
        }

        return new JsonObject
        {
            ["model"] = deployment,
            ["store"] = false,
            ["instructions"] = ExtractionSchema.Instructions,
            ["input"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = content }),
            ["text"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = ExtractionSchema.Name,
                    ["strict"] = true,
                    ["schema"] = JsonNode.Parse(ExtractionSchema.Json),
                },
            },
        };
    }

    // Only the status and the error code are read; the body text itself is never logged or kept.
    private static string ReasonFor(HttpStatusCode status, string body)
    {
        if (status == HttpStatusCode.TooManyRequests)
        {
            return "rate_limited";
        }

        if (status == HttpStatusCode.BadRequest && ErrorCode(body) == "content_filter")
        {
            return "content_filter";
        }

        return "service_error";
    }

    private static string? ErrorCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                   && error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String
                ? code.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ExtractionResult ParseResponse(string body)
    {
        try
        {
            using var envelope = JsonDocument.Parse(body);
            var root = envelope.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ExtractionResult.Failed("invalid_output");
            }

            if (root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String && status.GetString() == "incomplete")
            {
                var reason = root.TryGetProperty("incomplete_details", out var details) && details.ValueKind == JsonValueKind.Object
                             && details.TryGetProperty("reason", out var why) && why.ValueKind == JsonValueKind.String
                    ? why.GetString()
                    : null;
                return ExtractionResult.Failed(reason == "content_filter" ? "content_filter" : "invalid_output");
            }

            return AnswerText(root) is { } answer ? ParseAnswer(answer) : ExtractionResult.Failed("invalid_output");
        }
        catch (JsonException)
        {
            return ExtractionResult.Failed("invalid_output");
        }
    }

    private static string? AnswerText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var direct) && direct.ValueKind == JsonValueKind.String)
        {
            return direct.GetString();
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (part.ValueKind == JsonValueKind.Object
                    && part.TryGetProperty("type", out var type) && type.GetString() == "output_text"
                    && part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString();
                }
            }
        }

        return null;
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
