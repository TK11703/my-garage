using MyGarage.Database.VehicleImages;

namespace MyGarage.Api.Vehicles.Images;

// Answers 503 problem details when the image store cannot be reached, so nothing else about the vehicle is affected.
public sealed class StorageUnavailableFilter(ILogger<StorageUnavailableFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (ImageStorageUnavailableException ex)
        {
            logger.LogWarning(ex, "Image storage is unavailable");
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Image storage is temporarily unavailable.");
        }
    }
}

// Rejects an oversized upload with 413 before the request body is validated or stored.
public sealed class UploadSizeFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<UploadImageRequest>().FirstOrDefault();
        return request?.File is { Length: > UploadImageRequest.MaxFileBytes }
            ? ValueTask.FromResult<object?>(Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "The file must be 5 MB or smaller."))
            : next(context);
    }
}
