using MyGarage.Shared.Taxes;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Taxes;

public sealed class TaxesApiClient(HttpClient http)
{
    public Task<ApiResult<PagedResultDto<TaxDto>>> ListAsync(Guid vehicleId, int page, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<PagedResultDto<TaxDto>>(http, HttpMethod.Get, $"api/vehicles/{vehicleId}/taxes?page={page}", null, cancellationToken);

    public Task<ApiResult<TaxDto>> CreateAsync(Guid vehicleId, TaxRequest request, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<TaxDto>(http, HttpMethod.Post, $"api/vehicles/{vehicleId}/taxes", request, cancellationToken);

    public Task<ApiResult<TaxDto>> GetAsync(Guid vehicleId, Guid entryId, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<TaxDto>(http, HttpMethod.Get, $"api/vehicles/{vehicleId}/taxes/{entryId}", null, cancellationToken);

    public Task<ApiResult<TaxDto>> UpdateAsync(Guid vehicleId, Guid entryId, TaxRequest request, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<TaxDto>(http, HttpMethod.Put, $"api/vehicles/{vehicleId}/taxes/{entryId}", request, cancellationToken);

    public Task<ApiResult<bool>> DeleteAsync(Guid vehicleId, Guid entryId, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<bool>(http, HttpMethod.Delete, $"api/vehicles/{vehicleId}/taxes/{entryId}", null, cancellationToken);
}
