using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Identity.Web;
using MyGarage.Shared.Drafts;
using MyGarage.Web.Common;

namespace MyGarage.Web.Drafts;

// Why an upload produced nothing; each has its own message (DraftUploadMessages).
public enum DraftUploadFailure
{
    None,
    Invalid,
    TooLarge,
    NotFound,
    RateLimited,
}

// One file to send; the stream is read once. FileName is a display name only, which the Api sanitizes again.
public sealed record UploadContent(Stream Content, string? ContentType, string? FileName = null);

// Errors holds the Api's message for each refused file, keyed by the file's position in the request.
public sealed record DraftUploadResponse(DraftUploadResultDto? Result, DraftUploadFailure Failure, IReadOnlyDictionary<int, string> Errors);

// Sends uploaded files to the Api. Sign-in rejections and outages are thrown as GarageApiException; everything else the Api
// answers becomes a DraftUploadFailure. It has its own client: a slow connection may need longer than the default limit, and an
// automatic retry would queue the files twice.
public sealed class DraftUploadApiClient(HttpClient http)
{
    private static readonly IReadOnlyDictionary<int, string> NoErrors = new Dictionary<int, string>();

    public async Task<DraftUploadResponse> UploadAsync(IReadOnlyList<UploadContent> files, Guid? vehicleId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            for (var index = 0; index < files.Count; index++)
            {
                var content = new StreamContent(files[index].Content);
                content.Headers.ContentType = MediaTypeHeaderValue.TryParse(files[index].ContentType, out var parsed) ? parsed : null;
                // The Api stores the name for display only and never uses it as a path.
                form.Add(content, "files", string.IsNullOrWhiteSpace(files[index].FileName) ? $"record-{index + 1}" : files[index].FileName!);
            }

            if (vehicleId is { } id)
            {
                form.Add(new StringContent(id.ToString()), "vehicleId");
            }

            using var response = await http.PostAsync("api/drafts/uploads", form, cancellationToken);
            switch (response.StatusCode)
            {
                case HttpStatusCode.Accepted:
                    return new DraftUploadResponse(await response.Content.ReadFromJsonAsync<DraftUploadResultDto>(cancellationToken), DraftUploadFailure.None, NoErrors);
                case HttpStatusCode.BadRequest:
                    return new DraftUploadResponse(null, DraftUploadFailure.Invalid, await ReadErrorsAsync(response, cancellationToken));
                case HttpStatusCode.RequestEntityTooLarge:
                    return new DraftUploadResponse(null, DraftUploadFailure.TooLarge, NoErrors);
                case HttpStatusCode.NotFound:
                    return new DraftUploadResponse(null, DraftUploadFailure.NotFound, NoErrors);
                case HttpStatusCode.Conflict:
                    throw new GarageApiException(GarageApiFailure.VehicleArchived);
                case HttpStatusCode.TooManyRequests:
                    return new DraftUploadResponse(null, DraftUploadFailure.RateLimited, NoErrors);
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    throw new GarageApiException(GarageApiFailure.NotAccepted);
                default:
                    throw new GarageApiException(GarageApiFailure.Unavailable);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException)
        {
            throw new GarageApiException(GarageApiFailure.Unavailable);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The client's own timeout.
            throw new GarageApiException(GarageApiFailure.Unavailable);
        }
        catch (MicrosoftIdentityWebChallengeUserException)
        {
            throw new GarageApiException(GarageApiFailure.NotAccepted);
        }
    }

    // The messages of the files[i] keys, by index; a message that is not about one file is kept under -1.
    private static async Task<IReadOnlyDictionary<int, string>> ReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<int, string>();
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (document.RootElement.TryGetProperty("errors", out var list) && list.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in list.EnumerateObject())
                {
                    var message = property.Value.ValueKind == JsonValueKind.Array && property.Value.GetArrayLength() > 0 ? property.Value[0].GetString() : null;
                    if (message is not null)
                    {
                        errors[IndexOf(property.Name)] = message;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // An unreadable body is shown as the general message.
        }

        return errors;
    }

    private static int IndexOf(string key)
    {
        var open = key.IndexOf('[', StringComparison.Ordinal);
        var close = key.IndexOf(']', StringComparison.Ordinal);
        return open >= 0 && close > open && int.TryParse(key.AsSpan(open + 1, close - open - 1), out var index) ? index : -1;
    }
}
