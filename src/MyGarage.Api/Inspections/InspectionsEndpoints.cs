using System.Security.Claims;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyGarage.Api.Common;
using MyGarage.Api.Vehicles;
using MyGarage.Database.Inspections;
using MyGarage.Shared.Inspections;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Inspections;

public static class InspectionsEndpoints
{
    private const int DefaultPageSize = 10;

    public static IServiceCollection AddInspections(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<InspectionRequest>, InspectionRequestValidator>();
        services.TryAddSingleton<IValidator<PagingQuery>, PagingQueryValidator>();
        return services;
    }

    public static IEndpointRouteBuilder MapInspectionsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/vehicles/{vehicleId:guid}/inspections").WithTags("Inspections").RequireActiveVehicleForWrites();

        group.MapGet("", async (
                Guid vehicleId,
                [AsParameters] PagingQuery query,
                ClaimsPrincipal user,
                InspectionRepository inspections,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var pageSize = query.PageSize ?? DefaultPageSize;
                var result = await inspections.ListPageAsync(ownerId, vehicleId, query.Page, pageSize, cancellationToken);
                if (result is null)
                {
                    return VehiclesEndpoints.NotFound();
                }

                // A page beyond the last is answered with the last page; the returned Page is the one served.
                var effectivePage = Math.Max(1, Math.Min(query.Page, result.TotalPages));
                if (effectivePage != query.Page)
                {
                    result = await inspections.ListPageAsync(ownerId, vehicleId, effectivePage, pageSize, cancellationToken);
                }

                return Results.Ok(InspectionMapper.ToPageDto(result!));
            })
            .AddEndpointFilter<ValidationFilter<PagingQuery>>()
            .WithName("ListInspections")
            .WithSummary("Page of the vehicle's inspections, newest inspection date first.")
            .Produces<PagedResultDto<InspectionDto>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("", async (
                Guid vehicleId,
                InspectionRequest request,
                ClaimsPrincipal user,
                InspectionRepository inspections,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var inspection = await inspections.AddAsync(
                    ownerId,
                    vehicleId,
                    Guid.CreateVersion7(),
                    InspectionMapper.ToStoredType(request.Type!.Value),
                    request.InspectionDate!.Value,
                    request.Cost!.Value,
                    request.NextDueDate,
                    timeProvider.GetUtcNow(),
                    cancellationToken);

                return inspection is null
                    ? VehiclesEndpoints.NotFound()
                    : Results.Created($"/api/vehicles/{vehicleId}/inspections/{inspection.Id}", InspectionMapper.ToDto(inspection));
            })
            .AddEndpointFilter<ValidationFilter<InspectionRequest>>()
            .WithName("CreateInspection")
            .WithSummary("Add an inspection to the vehicle.")
            .Produces<InspectionDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{entryId:guid}", async (
                Guid vehicleId,
                Guid entryId,
                ClaimsPrincipal user,
                InspectionRepository inspections,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var inspection = await inspections.GetAsync(ownerId, vehicleId, entryId, cancellationToken);
                return inspection is null ? VehiclesEndpoints.NotFound() : Results.Ok(InspectionMapper.ToDto(inspection));
            })
            .WithName("GetInspection")
            .WithSummary("One inspection.")
            .Produces<InspectionDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{entryId:guid}", async (
                Guid vehicleId,
                Guid entryId,
                InspectionRequest request,
                ClaimsPrincipal user,
                InspectionRepository inspections,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var inspection = await inspections.UpdateAsync(
                    ownerId,
                    vehicleId,
                    entryId,
                    InspectionMapper.ToStoredType(request.Type!.Value),
                    request.InspectionDate!.Value,
                    request.Cost!.Value,
                    request.NextDueDate,
                    timeProvider.GetUtcNow(),
                    cancellationToken);

                return inspection is null ? VehiclesEndpoints.NotFound() : Results.Ok(InspectionMapper.ToDto(inspection));
            })
            .AddEndpointFilter<ValidationFilter<InspectionRequest>>()
            .WithName("UpdateInspection")
            .WithSummary("Replace the inspection's values.")
            .Produces<InspectionDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{entryId:guid}", async (
                Guid vehicleId,
                Guid entryId,
                ClaimsPrincipal user,
                InspectionRepository inspections,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                return await inspections.DeleteAsync(ownerId, vehicleId, entryId, cancellationToken) ? Results.NoContent() : VehiclesEndpoints.NotFound();
            })
            .WithName("DeleteInspection")
            .WithSummary("Delete the inspection.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
