using System.Security.Claims;
using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Api.Reminders;
using MyGarage.Database.Tags;
using MyGarage.Database.Vehicles;
using MyGarage.Shared.Vehicles;
using OpenTelemetry.Metrics;

namespace MyGarage.Api.Vehicles;

public static class VehiclesEndpoints
{
    private const int DefaultPageSize = 12;

    public static IServiceCollection AddVehicles(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IValidator<VehicleRequest>, VehicleRequestValidator>();
        services.AddSingleton<IValidator<VehicleListQuery>, VehicleListQueryValidator>();
        services.AddSingleton<VehicleLookupChecker>();
        services.AddSingleton<VehicleService>();
        services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(VehicleMetrics.MeterName));
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
            .WithSummary("Page of the caller's vehicles matching the optional search and filters, ordered by make, model, year, then id.")
            .Produces<PagedResultDto<VehicleSummaryDto>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/filter-options", async (string? status, ClaimsPrincipal user, VehicleService service, CancellationToken cancellationToken) =>
                CurrentOwner.Get(user) is not { } ownerId
                    ? NoOwner()
                    : VehicleStatusFilter.IsValid(status)
                        ? Results.Ok(await service.GetFilterOptionsAsync(ownerId, status, cancellationToken))
                        : Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = [VehicleListQueryValidator.StatusMessage] }))
            .WithName("GetVehicleFilterOptions")
            .WithSummary("Distinct makes, colors, and tags in the caller's garage for the chosen status (active by default), for the filter choices.")
            .Produces<VehicleFilterOptionsDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("", async (VehicleRequest request, ClaimsPrincipal user, VehicleService service, VehicleLookupChecker checker, CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return NoOwner();
                }

                var errors = await checker.CheckAsync(ownerId, request, null, cancellationToken);
                if (request.StickerFeatures?.Any(feature => feature.ReplacesTagIds is { Count: > 0 }) == true)
                {
                    errors[nameof(VehicleRequest.StickerFeatures)] = ["A new vehicle has no features to replace."];
                }

                if (errors.Count > 0)
                {
                    return Results.ValidationProblem(errors);
                }

                var created = await service.CreateAsync(ownerId, request, cancellationToken);
                return created.Vehicle is { } vehicle
                    ? Results.Created($"/api/vehicles/{vehicle.Id}", vehicle)
                    : FeatureLimit();
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

        group.MapPut("/{vehicleId:guid}", async (Guid vehicleId, VehicleRequest request, ClaimsPrincipal user, VehicleService service, VehicleLookupChecker checker, ReminderEvaluator reminderEvaluator, CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return NoOwner();
                }

                var errors = await checker.CheckAsync(ownerId, request, vehicleId, cancellationToken);
                if (errors.Count > 0)
                {
                    return Results.ValidationProblem(errors);
                }

                var updated = await service.UpdateAsync(ownerId, vehicleId, request, cancellationToken);
                if (updated.Vehicle is not null)
                {
                    await reminderEvaluator.TryEvaluateVehicleAsync(ownerId, vehicleId, cancellationToken);
                }

                return updated switch
                {
                    { Vehicle: { } vehicle } => Results.Ok(vehicle),
                    { Status: VehicleFeatureWriteStatus.FeatureLimit } => FeatureLimit(),
                    _ => NotFound(),
                };
            })
            .RequireActiveVehicleForWrites()
            .AddEndpointFilter<ValidationFilter<VehicleRequest>>()
            .WithName("UpdateVehicle")
            .WithSummary("Replace the editable details of a vehicle.")
            .Produces<VehicleDetailDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{vehicleId:guid}/archive", async (Guid vehicleId, ClaimsPrincipal user, VehicleService service, ILogger<VehicleService> logger, CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return NoOwner();
                }

                var result = await service.ArchiveAsync(ownerId, vehicleId, cancellationToken);
                if (result.Change == VehicleStateChange.Changed)
                {
                    logger.LogInformation("Vehicle {VehicleId} was archived", vehicleId);
                }

                return result switch
                {
                    { Vehicle: { } vehicle } => Results.Ok(vehicle),
                    { Change: VehicleStateChange.AlreadyInState } => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This vehicle is already archived."),
                    _ => NotFound(),
                };
            })
            .WithName("ArchiveVehicle")
            .WithSummary("Archive a vehicle: keep everything attached to it, disable its reminders, and make it read-only until restored.")
            .Produces<VehicleDetailDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{vehicleId:guid}/restore", async (Guid vehicleId, ClaimsPrincipal user, VehicleService service, ReminderEvaluator reminderEvaluator, ILogger<VehicleService> logger, CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return NoOwner();
                }

                var result = await service.RestoreAsync(ownerId, vehicleId, cancellationToken);
                if (result.Change == VehicleStateChange.Changed)
                {
                    logger.LogInformation("Vehicle {VehicleId} was restored", vehicleId);
                    await reminderEvaluator.TryEvaluateVehicleAsync(ownerId, vehicleId, cancellationToken);
                }

                return result switch
                {
                    { Vehicle: { } vehicle } => Results.Ok(vehicle),
                    { Change: VehicleStateChange.AlreadyInState } => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This vehicle is not archived."),
                    _ => NotFound(),
                };
            })
            .WithName("RestoreVehicle")
            .WithSummary("Restore an archived vehicle: it counts again and its reminders are enabled again.")
            .Produces<VehicleDetailDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

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

    private static IResult FeatureLimit() =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [nameof(VehicleRequest.StickerFeatures)] = [$"A vehicle can have at most {VehicleTagRepository.MaxTagsPerVehicle} features."],
        });
}
