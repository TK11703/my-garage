using MyGarage.Shared.Insurance;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Insurance;

public sealed class InsuranceApiClient(HttpClient http)
{
    public Task<ApiResult<PagedResultDto<InsurancePolicyDto>>> ListAsync(Guid vehicleId, int page, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<PagedResultDto<InsurancePolicyDto>>(http, HttpMethod.Get, $"api/vehicles/{vehicleId}/insurance?page={page}", null, cancellationToken);

    public Task<ApiResult<InsurancePolicyDto>> CreateAsync(Guid vehicleId, InsurancePolicyRequest request, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<InsurancePolicyDto>(http, HttpMethod.Post, $"api/vehicles/{vehicleId}/insurance", request, cancellationToken);

    public Task<ApiResult<InsurancePolicyDto>> GetAsync(Guid vehicleId, Guid entryId, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<InsurancePolicyDto>(http, HttpMethod.Get, $"api/vehicles/{vehicleId}/insurance/{entryId}", null, cancellationToken);

    public Task<ApiResult<InsurancePolicyDto>> UpdateAsync(Guid vehicleId, Guid entryId, InsurancePolicyRequest request, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<InsurancePolicyDto>(http, HttpMethod.Put, $"api/vehicles/{vehicleId}/insurance/{entryId}", request, cancellationToken);

    public Task<ApiResult<bool>> DeleteAsync(Guid vehicleId, Guid entryId, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<bool>(http, HttpMethod.Delete, $"api/vehicles/{vehicleId}/insurance/{entryId}", null, cancellationToken);
}
