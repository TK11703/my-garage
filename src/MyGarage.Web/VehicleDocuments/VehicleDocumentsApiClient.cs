using MyGarage.Shared.VehicleDocuments;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.VehicleDocuments;

public sealed class VehicleDocumentsApiClient(HttpClient http)
{
    public Task<ApiResult<PagedResultDto<VehicleDocumentDto>>> ListAsync(Guid vehicleId, int page, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<PagedResultDto<VehicleDocumentDto>>(http, HttpMethod.Get, $"api/vehicles/{vehicleId}/documents?page={page}", null, cancellationToken);

    public Task<ApiResult<bool>> DeleteAsync(Guid vehicleId, Guid documentId, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<bool>(http, HttpMethod.Delete, $"api/vehicles/{vehicleId}/documents/{documentId}", null, cancellationToken);

    // Streams a kept original from the API; null when it does not exist for this user.
    public Task<ImageDownload?> OpenContentAsync(Guid vehicleId, Guid documentId, CancellationToken cancellationToken = default) =>
        ApiCalls.OpenAsync(http, $"api/vehicles/{vehicleId}/documents/{documentId}/content", cancellationToken);
}
