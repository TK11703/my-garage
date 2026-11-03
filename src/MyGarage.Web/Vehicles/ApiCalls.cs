using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Identity.Web;
using MyGarage.Web.Common;

namespace MyGarage.Web.Vehicles;

// Shared JSON call handling for the typed API clients: sign-in rejections and outages become GarageApiException, answers become ApiResult.
internal static class ApiCalls
{
    public static async Task<ApiResult<T>> SendAsync<T>(HttpClient http, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
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

    // Streams a private file from the API; null when it does not exist for this user.
    public static async Task<ImageDownload?> OpenAsync(HttpClient http, string path, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        try
        {
            response = await http.SendAsync(new HttpRequestMessage(HttpMethod.Get, path), HttpCompletionOption.ResponseHeadersRead, cancellationToken);

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
