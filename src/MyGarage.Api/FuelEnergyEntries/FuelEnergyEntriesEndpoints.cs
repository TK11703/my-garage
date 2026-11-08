using System.Security.Claims;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyGarage.Api.Common;
using MyGarage.Api.Vehicles;
using MyGarage.Database.FuelEnergyEntries;
using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.FuelEnergyEntries;

public static class FuelEnergyEntriesEndpoints
{
    private const int DefaultPageSize = 10;

    public static IServiceCollection AddFuelEnergyEntries(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<FuelEnergyEntryRequest>, FuelEnergyEntryRequestValidator>();
        services.TryAddSingleton<IValidator<PagingQuery>, PagingQueryValidator>();
        return services;
    }

    public static IEndpointRouteBuilder MapFuelEnergyEntriesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/vehicles/{vehicleId:guid}/fuel-energy-entries").WithTags("FuelEnergyEntries").RequireActiveVehicleForWrites();

        group.MapGet("", async (
                Guid vehicleId,
                [AsParameters] PagingQuery query,
                ClaimsPrincipal user,
                FuelEnergyEntryRepository entries,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var pageSize = query.PageSize ?? DefaultPageSize;
                var result = await entries.ListPageAsync(ownerId, vehicleId, query.Page, pageSize, cancellationToken);
                if (result is null)
                {
                    return VehiclesEndpoints.NotFound();
                }

                // A page beyond the last is answered with the last page; the returned Page is the one served.
                var effectivePage = Math.Max(1, Math.Min(query.Page, result.TotalPages));
                if (effectivePage != query.Page)
                {
                    result = await entries.ListPageAsync(ownerId, vehicleId, effectivePage, pageSize, cancellationToken);
                }

                return Results.Ok(FuelEnergyEntryMapper.ToPageDto(result!));
            })
            .AddEndpointFilter<ValidationFilter<PagingQuery>>()
            .WithName("ListFuelEnergyEntries")
            .WithSummary("Page of the vehicle's fuel and charging entries, newest entry date first.")
            .Produces<PagedResultDto<FuelEnergyEntryDto>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("", async (
                Guid vehicleId,
                FuelEnergyEntryRequest request,
                ClaimsPrincipal user,
                FuelEnergyEntryRepository entries,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var entry = await entries.AddAsync(
                    ownerId,
                    vehicleId,
                    Guid.CreateVersion7(),
                    FuelEnergyEntryMapper.ToStoredKind(request.Kind!.Value),
                    request.EntryDate!.Value,
                    request.UnitPrice!.Value,
                    request.Quantity!.Value,
                    FuelEnergyTotal.Resolve(request.UnitPrice!.Value, request.Quantity!.Value, request.TotalCost),
                    timeProvider.GetUtcNow(),
                    cancellationToken);

                return entry is null
                    ? VehiclesEndpoints.NotFound()
                    : Results.Created($"/api/vehicles/{vehicleId}/fuel-energy-entries/{entry.Id}", FuelEnergyEntryMapper.ToDto(entry));
            })
            .AddEndpointFilter<ValidationFilter<FuelEnergyEntryRequest>>()
            .WithName("CreateFuelEnergyEntry")
            .WithSummary("Add a fuel entry or a charging entry to the vehicle.")
            .Produces<FuelEnergyEntryDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{entryId:guid}", async (
                Guid vehicleId,
                Guid entryId,
                ClaimsPrincipal user,
                FuelEnergyEntryRepository entries,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var entry = await entries.GetAsync(ownerId, vehicleId, entryId, cancellationToken);
                return entry is null ? VehiclesEndpoints.NotFound() : Results.Ok(FuelEnergyEntryMapper.ToDto(entry));
            })
            .WithName("GetFuelEnergyEntry")
            .WithSummary("One fuel or charging entry.")
            .Produces<FuelEnergyEntryDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{entryId:guid}", async (
                Guid vehicleId,
                Guid entryId,
                FuelEnergyEntryRequest request,
                ClaimsPrincipal user,
                FuelEnergyEntryRepository entries,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var entry = await entries.UpdateAsync(
                    ownerId,
                    vehicleId,
                    entryId,
                    FuelEnergyEntryMapper.ToStoredKind(request.Kind!.Value),
                    request.EntryDate!.Value,
                    request.UnitPrice!.Value,
                    request.Quantity!.Value,
                    FuelEnergyTotal.Resolve(request.UnitPrice!.Value, request.Quantity!.Value, request.TotalCost),
                    timeProvider.GetUtcNow(),
                    cancellationToken);

                return entry is null ? VehiclesEndpoints.NotFound() : Results.Ok(FuelEnergyEntryMapper.ToDto(entry));
            })
            .AddEndpointFilter<ValidationFilter<FuelEnergyEntryRequest>>()
            .WithName("UpdateFuelEnergyEntry")
            .WithSummary("Replace the entry's values.")
            .Produces<FuelEnergyEntryDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{entryId:guid}", async (
                Guid vehicleId,
                Guid entryId,
                ClaimsPrincipal user,
                FuelEnergyEntryRepository entries,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                return await entries.DeleteAsync(ownerId, vehicleId, entryId, cancellationToken) ? Results.NoContent() : VehiclesEndpoints.NotFound();
            })
            .WithName("DeleteFuelEnergyEntry")
            .WithSummary("Delete the entry.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
