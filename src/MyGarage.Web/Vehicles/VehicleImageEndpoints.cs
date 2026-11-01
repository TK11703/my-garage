using Microsoft.AspNetCore.Mvc;
using MyGarage.Web.Common;

namespace MyGarage.Web.Vehicles;

// Browser-facing image routes. Pictures stay private: the browser never gets a storage address, only these
// signed-in endpoints, which call the API with the user's token.
public static class VehicleImageEndpoints
{
    // A little above the 5 MB picture limit to allow for the multipart envelope.
    private const long RequestSizeLimit = 5_767_168;

    // Short fixed codes keep user-controlled text out of the redirect.
    public static class Status
    {
        public const string Uploaded = "uploaded";
        public const string Invalid = "invalid";
        public const string TooLarge = "toolarge";
        public const string Limit = "limit";
        public const string NotFound = "notfound";
        public const string Unavailable = "unavailable";
        public const string SignIn = "signin";
    }

    public static IEndpointRouteBuilder MapVehicleImageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/vehicles/{vehicleId:guid}/images/{imageId:guid}", ServeAsync)
            .WithName("ServeVehicleImage");

        endpoints.MapPost("/vehicles/{vehicleId:guid}/images", UploadAsync)
            .WithMetadata(new RequestSizeLimitAttribute(RequestSizeLimit))
            .WithName("UploadVehicleImage");

        return endpoints;
    }

    public static async Task<IResult> ServeAsync(Guid vehicleId, Guid imageId, VehiclesApiClient api, HttpContext http, CancellationToken cancellationToken)
    {
        ImageDownload? download;
        try
        {
            download = await api.OpenImageAsync(vehicleId, imageId, cancellationToken);
        }
        catch (GarageApiException ex)
        {
            return ex.Failure == GarageApiFailure.NotAccepted
                ? Results.StatusCode(StatusCodes.Status401Unauthorized)
                : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (download is null)
        {
            return Results.NotFound();
        }

        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        http.Response.Headers.CacheControl = "private, max-age=86400";

        return Results.Stream(
            async output =>
            {
                await using (download)
                {
                    await download.Stream.CopyToAsync(output, cancellationToken);
                }
            },
            download.ContentType);
    }

    public static async Task<IResult> UploadAsync(
        Guid vehicleId,
        IFormFile? file,
        [FromForm] string? caption,
        VehiclesApiClient api,
        CancellationToken cancellationToken,
        [FromForm] string? returnTo = null)
    {
        if (file is null || file.Length == 0)
        {
            return Redirect(vehicleId, Status.Invalid, returnTo);
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var outcome = await api.UploadImageAsync(vehicleId, stream, Path.GetFileName(file.FileName), file.ContentType, caption, cancellationToken);
            return Redirect(vehicleId, outcome switch
            {
                ImageUploadOutcome.Uploaded => Status.Uploaded,
                ImageUploadOutcome.Invalid => Status.Invalid,
                ImageUploadOutcome.TooLarge => Status.TooLarge,
                ImageUploadOutcome.LimitReached => Status.Limit,
                ImageUploadOutcome.NotFound => Status.NotFound,
                _ => Status.Unavailable,
            }, returnTo);
        }
        catch (GarageApiException ex)
        {
            return Redirect(vehicleId, ex.Failure == GarageApiFailure.NotAccepted ? Status.SignIn : Status.Unavailable, returnTo);
        }
    }

    // Only the fixed value "edit" is honored; anything else returns to the vehicle page.
    private static IResult Redirect(Guid vehicleId, string status, string? returnTo) =>
        returnTo == "edit"
            ? Results.LocalRedirect($"/vehicles/{vehicleId}/edit?tab=media&imageStatus={status}")
            : Results.LocalRedirect($"/vehicles/{vehicleId}?imageStatus={status}");
}
