using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Microsoft.Extensions.Options;
using MyGarage.Api.EmailIntake.Extraction;

namespace MyGarage.Api.Common;

// The attached file, read once from Content (the caller owns and disposes the stream). ContentType is the verified type: a PDF or an image.
public sealed record ResponsesDocument(Stream Content, string ContentType, string FileName);

// One call: instructions, a strict JSON schema for the answer, the text to read, and at most one document.
public sealed record ResponsesCall(string Instructions, string SchemaName, string SchemaJson, string InputText, ResponsesDocument? Document, TimeSpan Timeout);

// The answer text, or a short reason code; the response body is never kept. StatusCode is set when the service answered.
public sealed record ResponsesOutcome(string? AnswerText, string? FailureReason, int? StatusCode)
{
    public bool IsFailure => FailureReason is not null;
}

// Calls the Azure OpenAI Responses API with a strict JSON schema, shared by email intake and window-sticker import (research R1).
// The model gets no tools and no data except what the caller puts in the call; every failure becomes a reason code, never content.
public sealed class ResponsesApiClient(HttpClient httpClient, IOptions<ExtractionOptions> options, TokenCredential credential)
{
    private const string TokenScope = "https://ai.azure.com/.default";

    public bool IsEnabled => !string.IsNullOrWhiteSpace(options.Value.Endpoint) && !string.IsNullOrWhiteSpace(options.Value.Deployment);

    public async Task<ResponsesOutcome> SendAsync(ResponsesCall call, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(call.Timeout);

        try
        {
            var token = await credential.GetTokenAsync(new TokenRequestContext([TokenScope]), timeout.Token);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{settings.Endpoint!.TrimEnd('/')}/openai/v1/responses"))
            {
                Content = BuildContent(call, settings.Deployment!),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            using var response = await httpClient.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                return new ResponsesOutcome(null, ReasonFor(response.StatusCode, body), status);
            }

            return ParseResponse(body) is { } answer
                ? new ResponsesOutcome(answer.Text, answer.FailureReason, status)
                : new ResponsesOutcome(null, "invalid_output", status);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ResponsesOutcome(null, "timeout", null);
        }
        catch (TimeoutException)
        {
            return new ResponsesOutcome(null, "timeout", null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Network, credential, and service failures all become a failed outcome; the caller's retry policy decides what happens next.
            return new ResponsesOutcome(null, "service_error", null);
        }
    }

    // Without a document the body is plain JSON. With one, the file's base64 is written into the request as it is read from its stream.
    private static HttpContent BuildContent(ResponsesCall call, string deployment)
    {
        var request = BuildRequest(call, deployment, out var placeholder);
        var json = request.ToJsonString();
        if (call.Document is not { } document)
        {
            return new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        var at = json.IndexOf(placeholder, StringComparison.Ordinal);
        return new DocumentRequestContent(json[..at], json[(at + placeholder.Length)..], document.Content);
    }

    private static JsonObject BuildRequest(ResponsesCall call, string deployment, out string placeholder)
    {
        // Letters and digits only, so the JSON writer leaves it untouched and it can be cut out of the serialized text.
        placeholder = $"DOCUMENT{Guid.NewGuid():N}";
        var content = new JsonArray(new JsonObject { ["type"] = "input_text", ["text"] = call.InputText });
        if (call.Document is { } document)
        {
            content.Add(document.ContentType == "application/pdf"
                ? new JsonObject { ["type"] = "input_file", ["filename"] = document.FileName, ["file_data"] = $"data:{document.ContentType};base64,{placeholder}" }
                : new JsonObject { ["type"] = "input_image", ["image_url"] = $"data:{document.ContentType};base64,{placeholder}" });
        }

        return new JsonObject
        {
            ["model"] = deployment,
            ["store"] = false,
            ["instructions"] = call.Instructions,
            ["input"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = content }),
            ["text"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = call.SchemaName,
                    ["strict"] = true,
                    ["schema"] = JsonNode.Parse(call.SchemaJson),
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

    private static (string? Text, string? FailureReason)? ParseResponse(string body)
    {
        try
        {
            using var envelope = JsonDocument.Parse(body);
            var root = envelope.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String && status.GetString() == "incomplete")
            {
                var reason = root.TryGetProperty("incomplete_details", out var details) && details.ValueKind == JsonValueKind.Object
                             && details.TryGetProperty("reason", out var why) && why.ValueKind == JsonValueKind.String
                    ? why.GetString()
                    : null;
                return (null, reason == "content_filter" ? "content_filter" : "invalid_output");
            }

            return AnswerText(root) is { } answer ? (answer, null) : null;
        }
        catch (JsonException)
        {
            return null;
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
}
