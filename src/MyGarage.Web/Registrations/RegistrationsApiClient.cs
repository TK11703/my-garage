using MyGarage.Shared.Registrations;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Registrations;

public sealed class RegistrationsApiClient(HttpClient http)
{
    public Task<ApiResult<PagedResultDto<RegistrationDto>>> ListAsync(Guid vehicleId, int page, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<PagedResultDto<RegistrationDto>>(http, HttpMethod.Get, $"api/vehicles/{vehicleId}/registrations?page={page}", null, cancellationToken);

    public Task<ApiResult<RegistrationDto>> CreateAsync(Guid vehicleId, RegistrationRequest request, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<RegistrationDto>(http, HttpMethod.Post, $"api/vehicles/{vehicleId}/registrations", request, cancellationToken);

    public Task<ApiResult<RegistrationDto>> GetAsync(Guid vehicleId, Guid entryId, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<RegistrationDto>(http, HttpMethod.Get, $"api/vehicles/{vehicleId}/registrations/{entryId}", null, cancellationToken);

    public Task<ApiResult<RegistrationDto>> UpdateAsync(Guid vehicleId, Guid entryId, RegistrationRequest request, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<RegistrationDto>(http, HttpMethod.Put, $"api/vehicles/{vehicleId}/registrations/{entryId}", request, cancellationToken);

    public Task<ApiResult<bool>> DeleteAsync(Guid vehicleId, Guid entryId, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<bool>(http, HttpMethod.Delete, $"api/vehicles/{vehicleId}/registrations/{entryId}", null, cancellationToken);
}
