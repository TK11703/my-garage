using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Identity.Web;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Common;

namespace MyGarage.Web.Vehicles;

public sealed class VehiclesApiClient(HttpClient http)
{
    public Task<ApiResult<PagedResultDto<VehicleSummaryDto>>> ListAsync(VehicleListCriteria criteria, int page, CancellationToken cancellationToken = default) =>
        SendAsync<PagedResultDto<VehicleSummaryDto>>(HttpMethod.Get, $"api/vehicles?{criteria.ToQueryString(page)}", null, cancellationToken);

    public Task<ApiResult<VehicleFilterOptionsDto>> GetFilterOptionsAsync(CancellationToken cancellationToken = default) =>
        SendAsync<VehicleFilterOptionsDto>(HttpMethod.Get, "api/vehicles/filter-options", null, cancellationToken);

    public Task<ApiResult<VehicleDetailDto>> CreateAsync(VehicleRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<VehicleDetailDto>(HttpMethod.Post, "api/vehicles", request, cancellationToken);

    public Task<ApiResult<VehicleDetailDto>> GetAsync(Guid vehicleId, CancellationToken cancellationToken = default) =>
        SendAsync<VehicleDetailDto>(HttpMethod.Get, $"api/vehicles/{vehicleId}", null, cancellationToken);

    public Task<ApiResult<VehicleDetailDto>> UpdateAsync(Guid vehicleId, VehicleRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<VehicleDetailDto>(HttpMethod.Put, $"api/vehicles/{vehicleId}", request, cancellationToken);

    public Task<ApiResult<bool>> DeleteAsync(Guid vehicleId, CancellationToken cancellationToken = default) =>
        SendAsync<bool>(HttpMethod.Delete, $"api/vehicles/{vehicleId}", null, cancellationToken);

    public Task<ApiResult<VehicleTagDto>> AddTagAsync(Guid vehicleId, string? name, CancellationToken cancellationToken = default) =>
        SendAsync<VehicleTagDto>(HttpMethod.Post, $"api/vehicles/{vehicleId}/tags", new AddVehicleTagRequest { Name = name }, cancellationToken);

    public Task<ApiResult<bool>> RemoveTagAsync(Guid vehicleId, Guid tagId, CancellationToken cancellationToken = default) =>
        SendAsync<bool>(HttpMethod.Delete, $"api/vehicles/{vehicleId}/tags/{tagId}", null, cancellationToken);

    public Task<ApiResult<PagedResultDto<JournalNoteDto>>> ListNotesAsync(Guid vehicleId, int page, CancellationToken cancellationToken = default) =>
        SendAsync<PagedResultDto<JournalNoteDto>>(HttpMethod.Get, $"api/vehicles/{vehicleId}/notes?page={page}", null, cancellationToken);

    public Task<ApiResult<JournalNoteDto>> CreateNoteAsync(Guid vehicleId, JournalNoteRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<JournalNoteDto>(HttpMethod.Post, $"api/vehicles/{vehicleId}/notes", request, cancellationToken);

    public Task<ApiResult<JournalNoteDto>> UpdateNoteAsync(Guid vehicleId, Guid noteId, JournalNoteRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<JournalNoteDto>(HttpMethod.Put, $"api/vehicles/{vehicleId}/notes/{noteId}", request, cancellationToken);

    public Task<ApiResult<bool>> DeleteNoteAsync(Guid vehicleId, Guid noteId, CancellationToken cancellationToken = default) =>
        SendAsync<bool>(HttpMethod.Delete, $"api/vehicles/{vehicleId}/notes/{noteId}", null, cancellationToken);

    public Task<ApiResult<bool>> SetMainImageAsync(Guid vehicleId, Guid imageId, CancellationToken cancellationToken = default) =>
        SendAsync<bool>(HttpMethod.Put, $"api/vehicles/{vehicleId}/images/{imageId}/main", null, cancellationToken);

    public Task<ApiResult<bool>> DeleteImageAsync(Guid vehicleId, Guid imageId, CancellationToken cancellationToken = default) =>
        SendAsync<bool>(HttpMethod.Delete, $"api/vehicles/{vehicleId}/images/{imageId}", null, cancellationToken);

    // Streams the picture from the API; null when it does not exist for this user.
    public async Task<ImageDownload?> OpenImageAsync(Guid vehicleId, Guid imageId, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage? response = null;
        try
        {
            response = await http.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, $"api/vehicles/{vehicleId}/images/{imageId}/content"),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new GarageApiException(GarageApiFailure.NotAccepted);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                response.Dispose();
                return null;
            }

            response.EnsureSuccessStatusCode();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            return new ImageDownload(response, contentType, await response.Content.ReadAsStreamAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            response?.Dispose();
            throw new GarageApiException(GarageApiFailure.Unavailable);
        }
        catch (GarageApiException)
        {
            response?.Dispose();
            throw;
        }
        catch (MicrosoftIdentityWebChallengeUserException)
        {
            response?.Dispose();
            throw new GarageApiException(GarageApiFailure.NotAccepted);
        }
    }

    // Forwards a browser upload to the API; the user's token is attached by the handler pipeline.
    public async Task<ImageUploadOutcome> UploadImageAsync(Guid vehicleId, Stream file, string fileName, string? contentType, string? caption, CancellationToken cancellationToken = default)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            var fileContent = new StreamContent(file);
            fileContent.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(contentType, out var parsed) ? parsed : null;
            form.Add(fileContent, "file", fileName);
            if (!string.IsNullOrWhiteSpace(caption))
            {
                form.Add(new StringContent(caption), "caption");
            }

            using var response = await http.PostAsync($"api/vehicles/{vehicleId}/images", form, cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.Created => ImageUploadOutcome.Uploaded,
                HttpStatusCode.BadRequest => ImageUploadOutcome.Invalid,
                HttpStatusCode.RequestEntityTooLarge => ImageUploadOutcome.TooLarge,
                HttpStatusCode.Conflict => ImageUploadOutcome.LimitReached,
                HttpStatusCode.NotFound => ImageUploadOutcome.NotFound,
                HttpStatusCode.ServiceUnavailable => ImageUploadOutcome.Unavailable,
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => throw new GarageApiException(GarageApiFailure.NotAccepted),
                _ => throw new GarageApiException(GarageApiFailure.Unavailable),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw new GarageApiException(GarageApiFailure.Unavailable);
        }
        catch (MicrosoftIdentityWebChallengeUserException)
        {
            throw new GarageApiException(GarageApiFailure.NotAccepted);
        }
    }

    private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, body.GetType());
            }

            using var response = await http.SendAsync(request, cancellationToken);

            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    throw new GarageApiException(GarageApiFailure.NotAccepted);
                case HttpStatusCode.NotFound:
                    return new ApiResult<T> { Outcome = ApiOutcome.NotFound };
                case HttpStatusCode.BadRequest:
                    return new ApiResult<T> { Outcome = ApiOutcome.Invalid, Errors = await ReadErrorsAsync(response, cancellationToken) };
                case HttpStatusCode.Conflict:
                    return new ApiResult<T> { Outcome = ApiOutcome.Conflict, Title = await ReadTitleAsync(response, cancellationToken) };
            }

            response.EnsureSuccessStatusCode();

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return new ApiResult<T> { Outcome = ApiOutcome.Success };
            }

            return new ApiResult<T>
            {
                Outcome = ApiOutcome.Success,
                Value = await response.Content.ReadFromJsonAsync<T>(cancellationToken),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            throw new GarageApiException(GarageApiFailure.Unavailable);
        }
        catch (MicrosoftIdentityWebChallengeUserException)
        {
            throw new GarageApiException(GarageApiFailure.NotAccepted);
        }
    }

    private static async Task<IReadOnlyDictionary<string, string[]>> ReadErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(cancellationToken);
        return problem?.Errors ?? new Dictionary<string, string[]>();
    }

    private static async Task<string?> ReadTitleAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        (await response.Content.ReadFromJsonAsync<ProblemBody>(cancellationToken))?.Title;

    private sealed class ProblemBody
    {
        public string? Title { get; init; }

        public Dictionary<string, string[]>? Errors { get; init; }
    }
}
