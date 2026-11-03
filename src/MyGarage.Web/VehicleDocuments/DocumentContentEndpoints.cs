using MyGarage.Web.Common;

namespace MyGarage.Web.VehicleDocuments;

// Browser-facing route for a kept original; the browser never gets a storage address.
public static class DocumentContentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentContentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/vehicles/{vehicleId:guid}/documents/{documentId:guid}", ServeAsync)
            .WithName("ServeVehicleDocument");

        return endpoints;
    }

    public static Task<IResult> ServeAsync(Guid vehicleId, Guid documentId, VehicleDocumentsApiClient api, HttpContext http, CancellationToken cancellationToken) =>
        PrivateFileProxy.ServeAsync(token => api.OpenContentAsync(vehicleId, documentId, token), http, cancellationToken);
}
