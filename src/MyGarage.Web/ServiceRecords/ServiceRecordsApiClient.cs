using MyGarage.Shared.ServiceRecords;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.ServiceRecords;

public sealed class ServiceRecordsApiClient(HttpClient http)
{
    public Task<ApiResult<PagedResultDto<ServiceRecordSummaryDto>>> ListAsync(Guid vehicleId, int page, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<PagedResultDto<ServiceRecordSummaryDto>>(http, HttpMethod.Get, $"api/vehicles/{vehicleId}/service-records?page={page}", null, cancellationToken);

    public Task<ApiResult<ServiceRecordDto>> CreateAsync(Guid vehicleId, ServiceRecordRequest request, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<ServiceRecordDto>(http, HttpMethod.Post, $"api/vehicles/{vehicleId}/service-records", request, cancellationToken);

    public Task<ApiResult<ServiceRecordDto>> GetAsync(Guid vehicleId, Guid recordId, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<ServiceRecordDto>(http, HttpMethod.Get, $"api/vehicles/{vehicleId}/service-records/{recordId}", null, cancellationToken);

    public Task<ApiResult<ServiceRecordDto>> UpdateAsync(Guid vehicleId, Guid recordId, ServiceRecordRequest request, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<ServiceRecordDto>(http, HttpMethod.Put, $"api/vehicles/{vehicleId}/service-records/{recordId}", request, cancellationToken);

    public Task<ApiResult<bool>> DeleteAsync(Guid vehicleId, Guid recordId, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<bool>(http, HttpMethod.Delete, $"api/vehicles/{vehicleId}/service-records/{recordId}", null, cancellationToken);

    // A 409 means the record already has the maximum number of line items; its title is in ApiResult.Title.
    public Task<ApiResult<ServiceRecordDto>> AddLineItemAsync(Guid vehicleId, Guid recordId, ServiceLineItemRequest request, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<ServiceRecordDto>(http, HttpMethod.Post, $"api/vehicles/{vehicleId}/service-records/{recordId}/line-items", request, cancellationToken);

    public Task<ApiResult<ServiceRecordDto>> UpdateLineItemAsync(Guid vehicleId, Guid recordId, Guid lineItemId, ServiceLineItemRequest request, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<ServiceRecordDto>(http, HttpMethod.Put, $"api/vehicles/{vehicleId}/service-records/{recordId}/line-items/{lineItemId}", request, cancellationToken);

    public Task<ApiResult<bool>> DeleteLineItemAsync(Guid vehicleId, Guid recordId, Guid lineItemId, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<bool>(http, HttpMethod.Delete, $"api/vehicles/{vehicleId}/service-records/{recordId}/line-items/{lineItemId}", null, cancellationToken);
}
