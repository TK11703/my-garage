using System.Security.Claims;
using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Vehicles;

public static class VehiclesEndpoints
{
    private const int DefaultPageSize = 12;

    public static IServiceCollection AddVehicles(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IValidator<VehicleRequest>, VehicleRequestValidator>();
        services.AddSingleton<IValidator<VehicleListQuery>, VehicleListQueryValidator>();
        services.AddSingleton<VehicleService>();
        return services;
    }

    public static IEndpointRouteBuilder MapVehiclesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/vehicles").WithTags("Vehicles");

        group.MapGet("", async ([AsParameters] VehicleListQuery query, ClaimsPrincipal user, VehicleService service, CancellationToken cancellationToken) =>
                CurrentOwner.Get(user) is { } ownerId
                    ? Results.Ok(await service.ListAsync(
                        ownerId, VehicleMapper.ToCriteria(query), query.Page, query.PageSize ?? DefaultPageSize, cancellationToken))
                    : NoOwner())
            .AddEndpointFilter<ValidationFilter<VehicleListQuery>>()
            .WithName("ListVehicles")
            .WithSummary("Page of the caller's vehicles matching the optional search and filters, ordered by nickname then id.")
            .Produces<PagedResultDto<VehicleSummaryDto>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/filter-options", async (ClaimsPrincipal user, VehicleService service, CancellationToken cancellationToken) =>
                CurrentOwner.Get(user) is { } ownerId
                    ? Results.Ok(await service.GetFilterOptionsAsync(ownerId, cancellationToken))
                    : NoOwner())
            .WithName("GetVehicleFilterOptions")
            .WithSummary("Distinct makes, colors, and tags in the caller's garage, for the filter choices.")
            .Produces<VehicleFilterOptionsDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("", async (VehicleRequest request, ClaimsPrincipal user, VehicleService service, CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return NoOwner();
                }

                var created = await service.CreateAsync(ownerId, request, cancellationToken);
                return Results.Created($"/api/vehicles/{created.Id}", created);
            })
            .AddEndpointFilter<ValidationFilter<VehicleRequest>>()
            .WithName("CreateVehicle")
            .WithSummary("Add a vehicle to the caller's garage.")
            .Produces<VehicleDetailDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{vehicleId:guid}", async (Guid vehicleId, ClaimsPrincipal user, VehicleService service, CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return NoOwner();
                }

                var vehicle = await service.GetAsync(ownerId, vehicleId, cancellationToken);
                return vehicle is null ? NotFound() : Results.Ok(vehicle);
            })
            .WithName("GetVehicle")
            .WithSummary("Vehicle details including tags and image metadata.")
            .Produces<VehicleDetailDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{vehicleId:guid}", async (Guid vehicleId, VehicleRequest request, ClaimsPrincipal user, VehicleService service, CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return NoOwner();
                }

                var updated = await service.UpdateAsync(ownerId, vehicleId, request, cancellationToken);
                return updated is null ? NotFound() : Results.Ok(updated);
            })
            .AddEndpointFilter<ValidationFilter<VehicleRequest>>()
            .WithName("UpdateVehicle")
            .WithSummary("Replace the editable details of a vehicle.")
            .Produces<VehicleDetailDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{vehicleId:guid}", async (Guid vehicleId, ClaimsPrincipal user, VehicleService service, CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return NoOwner();
                }

                return await service.DeleteAsync(ownerId, vehicleId, cancellationToken) ? Results.NoContent() : NotFound();
            })
            .WithName("DeleteVehicle")
            .WithSummary("Delete a vehicle and all of its tags, notes, and images.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    internal static IResult NoOwner() =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "The token does not identify a user.");

    internal static IResult NotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found.");
}
