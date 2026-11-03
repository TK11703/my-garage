using System.Security.Claims;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyGarage.Api.Common;
using MyGarage.Api.Vehicles;
using MyGarage.Database.Insurance;
using MyGarage.Shared.Insurance;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Insurance;

public static class InsuranceEndpoints
{
    private const int DefaultPageSize = 10;

    public static IServiceCollection AddInsurance(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<InsurancePolicyRequest>, InsurancePolicyRequestValidator>();
        services.TryAddSingleton<IValidator<PagingQuery>, PagingQueryValidator>();
        return services;
    }

    public static IEndpointRouteBuilder MapInsuranceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/vehicles/{vehicleId:guid}/insurance").WithTags("Insurance");

        group.MapGet("", async (
                Guid vehicleId,
                [AsParameters] PagingQuery query,
                ClaimsPrincipal user,
                InsurancePolicyRepository policies,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var pageSize = query.PageSize ?? DefaultPageSize;
                var result = await policies.ListPageAsync(ownerId, vehicleId, query.Page, pageSize, cancellationToken);
                if (result is null)
                {
                    return VehiclesEndpoints.NotFound();
                }

                // A page beyond the last is answered with the last page; the returned Page is the one served.
                var effectivePage = Math.Max(1, Math.Min(query.Page, result.TotalPages));
                if (effectivePage != query.Page)
                {
                    result = await policies.ListPageAsync(ownerId, vehicleId, effectivePage, pageSize, cancellationToken);
                }

                return Results.Ok(InsurancePolicyMapper.ToPageDto(result!));
            })
            .AddEndpointFilter<ValidationFilter<PagingQuery>>()
            .WithName("ListInsurancePolicies")
            .WithSummary("Page of the vehicle's insurance policies, latest end date first.")
            .Produces<PagedResultDto<InsurancePolicyDto>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("", async (
                Guid vehicleId,
                InsurancePolicyRequest request,
                ClaimsPrincipal user,
                InsurancePolicyRepository policies,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var policy = await policies.AddAsync(
                    ownerId,
                    vehicleId,
                    Guid.CreateVersion7(),
                    request.StartDate!.Value,
                    request.EndDate!.Value,
                    InsurancePolicyMapper.ToStoredDuration(request.Duration!.Value),
                    request.Cost!.Value,
                    timeProvider.GetUtcNow(),
                    cancellationToken);

                return policy is null
                    ? VehiclesEndpoints.NotFound()
                    : Results.Created($"/api/vehicles/{vehicleId}/insurance/{policy.Id}", InsurancePolicyMapper.ToDto(policy));
            })
            .AddEndpointFilter<ValidationFilter<InsurancePolicyRequest>>()
            .WithName("CreateInsurancePolicy")
            .WithSummary("Add an insurance policy to the vehicle.")
            .Produces<InsurancePolicyDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{entryId:guid}", async (
                Guid vehicleId,
                Guid entryId,
                ClaimsPrincipal user,
                InsurancePolicyRepository policies,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var policy = await policies.GetAsync(ownerId, vehicleId, entryId, cancellationToken);
                return policy is null ? VehiclesEndpoints.NotFound() : Results.Ok(InsurancePolicyMapper.ToDto(policy));
            })
            .WithName("GetInsurancePolicy")
            .WithSummary("One insurance policy.")
            .Produces<InsurancePolicyDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{entryId:guid}", async (
                Guid vehicleId,
                Guid entryId,
                InsurancePolicyRequest request,
                ClaimsPrincipal user,
                InsurancePolicyRepository policies,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var policy = await policies.UpdateAsync(
                    ownerId,
                    vehicleId,
                    entryId,
                    request.StartDate!.Value,
                    request.EndDate!.Value,
                    InsurancePolicyMapper.ToStoredDuration(request.Duration!.Value),
                    request.Cost!.Value,
                    timeProvider.GetUtcNow(),
                    cancellationToken);

                return policy is null ? VehiclesEndpoints.NotFound() : Results.Ok(InsurancePolicyMapper.ToDto(policy));
            })
            .AddEndpointFilter<ValidationFilter<InsurancePolicyRequest>>()
            .WithName("UpdateInsurancePolicy")
            .WithSummary("Replace the insurance policy's values.")
            .Produces<InsurancePolicyDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{entryId:guid}", async (
                Guid vehicleId,
                Guid entryId,
                ClaimsPrincipal user,
                InsurancePolicyRepository policies,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                return await policies.DeleteAsync(ownerId, vehicleId, entryId, cancellationToken) ? Results.NoContent() : VehiclesEndpoints.NotFound();
            })
            .WithName("DeleteInsurancePolicy")
            .WithSummary("Delete the insurance policy.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
