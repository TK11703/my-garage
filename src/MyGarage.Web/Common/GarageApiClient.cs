using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Identity.Web;
using MyGarage.Shared.SampleData;
using MyGarage.Shared.Users;

namespace MyGarage.Web.Common;

public enum GarageApiFailure
{
    Unavailable,
    NotAccepted,
}

public sealed class GarageApiException(GarageApiFailure failure) : Exception($"My Garage API call failed: {failure}")
{
    public GarageApiFailure Failure { get; } = failure;
}

public sealed class GarageApiClient(HttpClient http)
{
    public Task<CurrentUserDto> GetCurrentUserAsync(CancellationToken cancellationToken = default) =>
        GetAsync<CurrentUserDto>("api/users/me", cancellationToken);

    public Task<List<SampleItemDto>> GetSampleDataAsync(CancellationToken cancellationToken = default) =>
        GetAsync<List<SampleItemDto>>("api/sample-data", cancellationToken);

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(path, cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new GarageApiException(GarageApiFailure.NotAccepted);
            }

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                   ?? throw new GarageApiException(GarageApiFailure.Unavailable);
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
}
