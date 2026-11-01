using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using MyGarage.Api.Common;
using MyGarage.Database.VehicleImages;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Vehicles.Images;

public static class VehicleImagesEndpoints
{
    // A little above the 5 MB file limit to allow for the multipart envelope.
    private const long RequestSizeLimit = 5_767_168;

    public static IServiceCollection AddVehicleImages(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<UploadImageRequest>, UploadImageRequestValidator>();
        services.AddHostedService<ImageContainerHostedService>();
        return services;
    }

    public static IEndpointRouteBuilder MapVehicleImagesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/vehicles/{vehicleId:guid}/images")
            .WithTags("VehicleImages")
            .AddEndpointFilter<StorageUnavailableFilter>();

        group.MapPost("", async (
                Guid vehicleId,
                [FromForm] UploadImageRequest request,
                ClaimsPrincipal user,
                VehicleImageRepository images,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var file = request.File!;
                await using var stream = file.OpenReadStream();
                var contentType = ImageSignature.Detect(stream);
                if (contentType is null)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["file"] = ["The file must be a JPEG, PNG, or WebP image."],
                    });
                }

                var caption = string.IsNullOrWhiteSpace(request.Caption) ? null : request.Caption.Trim();
                var result = await images.AddAsync(
                    ownerId, vehicleId, Guid.CreateVersion7(), contentType, (int)file.Length, caption, stream, timeProvider.GetUtcNow(), cancellationToken);

                return result.Status switch
                {
                    AddImageStatus.Added => Results.Created($"/api/vehicles/{vehicleId}", VehicleImageMapper.ToDto(result.Image!)),
                    AddImageStatus.LimitReached => Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: $"A vehicle can have at most {VehicleImageRepository.MaxImagesPerVehicle} images."),
                    _ => VehiclesEndpoints.NotFound(),
                };
            })
            .DisableAntiforgery()
            .Accepts<UploadImageRequest>("multipart/form-data")
            .WithMetadata(new RequestSizeLimitAttribute(RequestSizeLimit))
            .AddEndpointFilter<UploadSizeFilter>()
            .AddEndpointFilter<ValidationFilter<UploadImageRequest>>()
            .WithName("UploadVehicleImage")
            .WithSummary("Upload a JPEG, PNG, or WebP picture (max 5 MB, max 10 per vehicle).")
            .Produces<VehicleImageDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/{imageId:guid}/content", async (
                Guid vehicleId,
                Guid imageId,
                ClaimsPrincipal user,
                HttpContext http,
                VehicleImageRepository images,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var content = await images.OpenContentAsync(ownerId, vehicleId, imageId, cancellationToken);
                if (content is null)
                {
                    return VehiclesEndpoints.NotFound();
                }

                http.Response.Headers["X-Content-Type-Options"] = "nosniff";
                http.Response.Headers.CacheControl = "private, max-age=86400";
                return Results.Stream(content.Content, content.ContentType);
            })
            .WithName("GetVehicleImageContent")
            .WithSummary("Image bytes streamed from private storage with the detected content type.")
            .Produces(StatusCodes.Status200OK, contentType: "image/jpeg")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPut("/{imageId:guid}/main", async (
                Guid vehicleId,
                Guid imageId,
                ClaimsPrincipal user,
                VehicleImageRepository images,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                return await images.SetMainAsync(ownerId, vehicleId, imageId, cancellationToken) ? Results.NoContent() : VehiclesEndpoints.NotFound();
            })
            .WithName("SetMainVehicleImage")
            .WithSummary("Make this image the vehicle's main (card) image.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{imageId:guid}", async (
                Guid vehicleId,
                Guid imageId,
                ClaimsPrincipal user,
                VehicleImageRepository images,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                return await images.DeleteAsync(ownerId, vehicleId, imageId, cancellationToken) ? Results.NoContent() : VehiclesEndpoints.NotFound();
            })
            .WithName("DeleteVehicleImage")
            .WithSummary("Delete an image; if it was main, the oldest remaining image becomes main.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
