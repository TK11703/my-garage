using System.Security.Claims;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyGarage.Api.Common;
using MyGarage.Api.Vehicles;
using MyGarage.Database.Registrations;
using MyGarage.Shared.Registrations;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Registrations;

public static class RegistrationsEndpoints
{
    private const int DefaultPageSize = 10;

    public static IServiceCollection AddRegistrations(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<RegistrationRequest>, RegistrationRequestValidator>();
        services.TryAddSingleton<IValidator<PagingQuery>, PagingQueryValidator>();
        return services;
    }

    public static IEndpointRouteBuilder MapRegistrationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/vehicles/{vehicleId:guid}/registrations").WithTags("Registrations");

        group.MapGet("", async (
                Guid vehicleId,
                [AsParameters] PagingQuery query,
                ClaimsPrincipal user,
                RegistrationRepository registrations,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var pageSize = query.PageSize ?? DefaultPageSize;
                var result = await registrations.ListPageAsync(ownerId, vehicleId, query.Page, pageSize, cancellationToken);
                if (result is null)
                {
                    return VehiclesEndpoints.NotFound();
                }

                // A page beyond the last is answered with the last page; the returned Page is the one served.
                var effectivePage = Math.Max(1, Math.Min(query.Page, result.TotalPages));
                if (effectivePage != query.Page)
                {
                    result = await registrations.ListPageAsync(ownerId, vehicleId, effectivePage, pageSize, cancellationToken);
                }

                return Results.Ok(RegistrationMapper.ToPageDto(result!));
            })
            .AddEndpointFilter<ValidationFilter<PagingQuery>>()
            .WithName("ListRegistrations")
            .WithSummary("Page of the vehicle's registrations, latest expiration date first.")
            .Produces<PagedResultDto<RegistrationDto>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("", async (
                Guid vehicleId,
                RegistrationRequest request,
                ClaimsPrincipal user,
                RegistrationRepository registrations,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var registration = await registrations.AddAsync(
                    ownerId,
                    vehicleId,
                    Guid.CreateVersion7(),
                    request.State!,
                    request.Cost!.Value,
                    request.ExpirationDate!.Value,
                    timeProvider.GetUtcNow(),
                    cancellationToken);

                return registration is null
                    ? VehiclesEndpoints.NotFound()
                    : Results.Created($"/api/vehicles/{vehicleId}/registrations/{registration.Id}", RegistrationMapper.ToDto(registration));
            })
            .AddEndpointFilter<ValidationFilter<RegistrationRequest>>()
            .WithName("CreateRegistration")
            .WithSummary("Add a registration to the vehicle.")
            .Produces<RegistrationDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{entryId:guid}", async (
                Guid vehicleId,
                Guid entryId,
                ClaimsPrincipal user,
                RegistrationRepository registrations,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var registration = await registrations.GetAsync(ownerId, vehicleId, entryId, cancellationToken);
                return registration is null ? VehiclesEndpoints.NotFound() : Results.Ok(RegistrationMapper.ToDto(registration));
            })
            .WithName("GetRegistration")
            .WithSummary("One registration.")
            .Produces<RegistrationDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{entryId:guid}", async (
                Guid vehicleId,
                Guid entryId,
                RegistrationRequest request,
                ClaimsPrincipal user,
                RegistrationRepository registrations,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var registration = await registrations.UpdateAsync(
                    ownerId,
                    vehicleId,
                    entryId,
                    request.State!,
                    request.Cost!.Value,
                    request.ExpirationDate!.Value,
                    timeProvider.GetUtcNow(),
                    cancellationToken);

                return registration is null ? VehiclesEndpoints.NotFound() : Results.Ok(RegistrationMapper.ToDto(registration));
            })
            .AddEndpointFilter<ValidationFilter<RegistrationRequest>>()
            .WithName("UpdateRegistration")
            .WithSummary("Replace the registration's values.")
            .Produces<RegistrationDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{entryId:guid}", async (
                Guid vehicleId,
                Guid entryId,
                ClaimsPrincipal user,
                RegistrationRepository registrations,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                return await registrations.DeleteAsync(ownerId, vehicleId, entryId, cancellationToken) ? Results.NoContent() : VehiclesEndpoints.NotFound();
            })
            .WithName("DeleteRegistration")
            .WithSummary("Delete the registration.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
