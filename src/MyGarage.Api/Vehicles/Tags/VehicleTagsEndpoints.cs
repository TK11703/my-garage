using System.Security.Claims;
using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Database.Tags;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Vehicles.Tags;

public static class VehicleTagsEndpoints
{
    public static IServiceCollection AddVehicleTags(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<AddVehicleTagRequest>, AddVehicleTagRequestValidator>();
        return services;
    }

    public static IEndpointRouteBuilder MapVehicleTagsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/vehicles/{vehicleId:guid}/tags").WithTags("Tags");

        group.MapPost("", async (
                Guid vehicleId,
                AddVehicleTagRequest request,
                ClaimsPrincipal user,
                VehicleTagRepository tags,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var result = await tags.AddAsync(
                    ownerId, vehicleId, Guid.CreateVersion7(), request.Name!.Trim(), timeProvider.GetUtcNow(), cancellationToken);

                return result.Status switch
                {
                    AddTagStatus.Added => Results.Created($"/api/vehicles/{vehicleId}", VehicleTagMapper.ToDto(result.Tag!)),
                    AddTagStatus.Duplicate => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This vehicle already has that tag."),
                    AddTagStatus.LimitReached => Results.Problem(
                        statusCode: StatusCodes.Status409Conflict,
                        title: $"A vehicle can have at most {VehicleTagRepository.MaxTagsPerVehicle} tags."),
                    _ => VehiclesEndpoints.NotFound(),
                };
            })
            .AddEndpointFilter<ValidationFilter<AddVehicleTagRequest>>()
            .WithName("AddVehicleTag")
            .WithSummary("Add a free-text tag (max 20 per vehicle, unique ignoring case).")
            .Produces<VehicleTagDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{tagId:guid}", async (
                Guid vehicleId,
                Guid tagId,
                ClaimsPrincipal user,
                VehicleTagRepository tags,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                return await tags.RemoveAsync(ownerId, vehicleId, tagId, cancellationToken) ? Results.NoContent() : VehiclesEndpoints.NotFound();
            })
            .WithName("RemoveVehicleTag")
            .WithSummary("Remove a tag.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
