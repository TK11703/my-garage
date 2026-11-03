using MyGarage.Shared.Inspections;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Inspections;

public sealed class InspectionsApiClient(HttpClient http)
{
    public Task<ApiResult<PagedResultDto<InspectionDto>>> ListAsync(Guid vehicleId, int page, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<PagedResultDto<InspectionDto>>(http, HttpMethod.Get, $"api/vehicles/{vehicleId}/inspections?page={page}", null, cancellationToken);

    public Task<ApiResult<InspectionDto>> CreateAsync(Guid vehicleId, InspectionRequest request, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<InspectionDto>(http, HttpMethod.Post, $"api/vehicles/{vehicleId}/inspections", request, cancellationToken);

    public Task<ApiResult<InspectionDto>> GetAsync(Guid vehicleId, Guid entryId, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<InspectionDto>(http, HttpMethod.Get, $"api/vehicles/{vehicleId}/inspections/{entryId}", null, cancellationToken);

    public Task<ApiResult<InspectionDto>> UpdateAsync(Guid vehicleId, Guid entryId, InspectionRequest request, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<InspectionDto>(http, HttpMethod.Put, $"api/vehicles/{vehicleId}/inspections/{entryId}", request, cancellationToken);

    public Task<ApiResult<bool>> DeleteAsync(Guid vehicleId, Guid entryId, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<bool>(http, HttpMethod.Delete, $"api/vehicles/{vehicleId}/inspections/{entryId}", null, cancellationToken);
}
