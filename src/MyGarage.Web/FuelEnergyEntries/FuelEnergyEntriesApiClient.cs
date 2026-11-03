using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.FuelEnergyEntries;

public sealed class FuelEnergyEntriesApiClient(HttpClient http)
{
    public Task<ApiResult<PagedResultDto<FuelEnergyEntryDto>>> ListAsync(Guid vehicleId, int page, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<PagedResultDto<FuelEnergyEntryDto>>(http, HttpMethod.Get, $"api/vehicles/{vehicleId}/fuel-energy-entries?page={page}", null, cancellationToken);

    public Task<ApiResult<FuelEnergyEntryDto>> CreateAsync(Guid vehicleId, FuelEnergyEntryRequest request, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<FuelEnergyEntryDto>(http, HttpMethod.Post, $"api/vehicles/{vehicleId}/fuel-energy-entries", request, cancellationToken);

    public Task<ApiResult<FuelEnergyEntryDto>> GetAsync(Guid vehicleId, Guid entryId, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<FuelEnergyEntryDto>(http, HttpMethod.Get, $"api/vehicles/{vehicleId}/fuel-energy-entries/{entryId}", null, cancellationToken);

    public Task<ApiResult<FuelEnergyEntryDto>> UpdateAsync(Guid vehicleId, Guid entryId, FuelEnergyEntryRequest request, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<FuelEnergyEntryDto>(http, HttpMethod.Put, $"api/vehicles/{vehicleId}/fuel-energy-entries/{entryId}", request, cancellationToken);

    public Task<ApiResult<bool>> DeleteAsync(Guid vehicleId, Guid entryId, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<bool>(http, HttpMethod.Delete, $"api/vehicles/{vehicleId}/fuel-energy-entries/{entryId}", null, cancellationToken);
}
