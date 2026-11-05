using System.Net;
using System.Text.Json;
using Microsoft.Identity.Web;
using MyGarage.Shared.Vehicles.StickerImport;
using MyGarage.Web.Common;

namespace MyGarage.Web.Vehicles.StickerImport;

// Why an import produced nothing; each has its own message (StickerImportMessages).
public enum StickerImportFailure
{
    None,
    Invalid,
    TooLarge,
    NotFound,
    RateLimited,
    NotConfigured,
    ReadFailed,
}

public sealed record StickerImportResponse(StickerImportResultDto? Result, StickerImportFailure Failure);

// Sends one uploaded sticker to the API and returns what was read. Sign-in rejections and outages are thrown as
// GarageApiException; everything else the API answers becomes a StickerImportFailure.
public sealed class StickerImportApiClient(HttpClient http)
{
    public async Task<StickerImportResponse> ExtractAsync(Stream file, string? contentType, Guid? vehicleId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            var fileContent = new StreamContent(file);
            fileContent.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(contentType, out var parsed) ? parsed : null;
            // The user's own file name is never sent on.
            form.Add(fileContent, "file", "window-sticker");
            if (vehicleId is { } id)
            {
                form.Add(new StringContent(id.ToString()), "vehicleId");
            }

            using var response = await http.PostAsync("api/vehicles/sticker-extractions", form, cancellationToken);
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    return new StickerImportResponse(await response.Content.ReadFromJsonAsync<StickerImportResultDto>(cancellationToken), StickerImportFailure.None);
                case HttpStatusCode.BadRequest:
                    return new StickerImportResponse(null, StickerImportFailure.Invalid);
                case HttpStatusCode.RequestEntityTooLarge:
                    return new StickerImportResponse(null, StickerImportFailure.TooLarge);
                case HttpStatusCode.NotFound:
                    return new StickerImportResponse(null, StickerImportFailure.NotFound);
                case HttpStatusCode.TooManyRequests:
                    return new StickerImportResponse(null, StickerImportFailure.RateLimited);
                case HttpStatusCode.ServiceUnavailable:
                    return new StickerImportResponse(null, await ReadCodeAsync(response, cancellationToken) == "not_configured" ? StickerImportFailure.NotConfigured : StickerImportFailure.ReadFailed);
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    throw new GarageApiException(GarageApiFailure.NotAccepted);
                default:
                    throw new GarageApiException(GarageApiFailure.Unavailable);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The client's own timeout: the read took too long, which the user is told like any other failed read.
            return new StickerImportResponse(null, StickerImportFailure.ReadFailed);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException)
        {
            throw new GarageApiException(GarageApiFailure.Unavailable);
        }
        catch (MicrosoftIdentityWebChallengeUserException)
        {
            throw new GarageApiException(GarageApiFailure.NotAccepted);
        }
    }

    private static async Task<string?> ReadCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String ? code.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
