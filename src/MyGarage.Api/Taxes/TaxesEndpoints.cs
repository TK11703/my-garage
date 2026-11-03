using System.Security.Claims;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyGarage.Api.Common;
using MyGarage.Api.Vehicles;
using MyGarage.Database.Taxes;
using MyGarage.Shared.Taxes;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Taxes;

public static class TaxesEndpoints
{
    private const int DefaultPageSize = 10;

    public static IServiceCollection AddTaxes(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<TaxRequest>, TaxRequestValidator>();
        services.TryAddSingleton<IValidator<PagingQuery>, PagingQueryValidator>();
        return services;
    }

    public static IEndpointRouteBuilder MapTaxesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/vehicles/{vehicleId:guid}/taxes").WithTags("Taxes");

        group.MapGet("", async (
                Guid vehicleId,
                [AsParameters] PagingQuery query,
                ClaimsPrincipal user,
                TaxRepository taxes,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var pageSize = query.PageSize ?? DefaultPageSize;
                var result = await taxes.ListPageAsync(ownerId, vehicleId, query.Page, pageSize, cancellationToken);
                if (result is null)
                {
                    return VehiclesEndpoints.NotFound();
                }

                // A page beyond the last is answered with the last page; the returned Page is the one served.
                var effectivePage = Math.Max(1, Math.Min(query.Page, result.TotalPages));
                if (effectivePage != query.Page)
                {
                    result = await taxes.ListPageAsync(ownerId, vehicleId, effectivePage, pageSize, cancellationToken);
                }

                return Results.Ok(TaxMapper.ToPageDto(result!));
            })
            .AddEndpointFilter<ValidationFilter<PagingQuery>>()
            .WithName("ListTaxes")
            .WithSummary("Page of the vehicle's taxes, most recently added first.")
            .Produces<PagedResultDto<TaxDto>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("", async (
                Guid vehicleId,
                TaxRequest request,
                ClaimsPrincipal user,
                TaxRepository taxes,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var tax = await taxes.AddAsync(
                    ownerId,
                    vehicleId,
                    Guid.CreateVersion7(),
                    TaxMapper.ToStoredType(request.Type!.Value),
                    request.Amount!.Value,
                    TaxMapper.ToStoredRecurrence(request.Recurrence!.Value),
                    timeProvider.GetUtcNow(),
                    cancellationToken);

                return tax is null
                    ? VehiclesEndpoints.NotFound()
                    : Results.Created($"/api/vehicles/{vehicleId}/taxes/{tax.Id}", TaxMapper.ToDto(tax));
            })
            .AddEndpointFilter<ValidationFilter<TaxRequest>>()
            .WithName("CreateTax")
            .WithSummary("Add a tax to the vehicle.")
            .Produces<TaxDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{entryId:guid}", async (
                Guid vehicleId,
                Guid entryId,
                ClaimsPrincipal user,
                TaxRepository taxes,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var tax = await taxes.GetAsync(ownerId, vehicleId, entryId, cancellationToken);
                return tax is null ? VehiclesEndpoints.NotFound() : Results.Ok(TaxMapper.ToDto(tax));
            })
            .WithName("GetTax")
            .WithSummary("One tax.")
            .Produces<TaxDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{entryId:guid}", async (
                Guid vehicleId,
                Guid entryId,
                TaxRequest request,
                ClaimsPrincipal user,
                TaxRepository taxes,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var tax = await taxes.UpdateAsync(
                    ownerId,
                    vehicleId,
                    entryId,
                    TaxMapper.ToStoredType(request.Type!.Value),
                    request.Amount!.Value,
                    TaxMapper.ToStoredRecurrence(request.Recurrence!.Value),
                    timeProvider.GetUtcNow(),
                    cancellationToken);

                return tax is null ? VehiclesEndpoints.NotFound() : Results.Ok(TaxMapper.ToDto(tax));
            })
            .AddEndpointFilter<ValidationFilter<TaxRequest>>()
            .WithName("UpdateTax")
            .WithSummary("Replace the tax's values.")
            .Produces<TaxDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{entryId:guid}", async (
                Guid vehicleId,
                Guid entryId,
                ClaimsPrincipal user,
                TaxRepository taxes,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                return await taxes.DeleteAsync(ownerId, vehicleId, entryId, cancellationToken) ? Results.NoContent() : VehiclesEndpoints.NotFound();
            })
            .WithName("DeleteTax")
            .WithSummary("Delete the tax.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
