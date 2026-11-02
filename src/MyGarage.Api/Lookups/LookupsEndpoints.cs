using System.Security.Claims;
using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Database.Lookups;
using MyGarage.Shared.Lookups;

namespace MyGarage.Api.Lookups;

public static class LookupsEndpoints
{
    public static IServiceCollection AddLookups(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IValidator<AddLookupItemRequest>, AddLookupItemRequestValidator>();
        services.AddSingleton<IValidator<SetLookupVisibilityRequest>, SetLookupVisibilityRequestValidator>();
        services.AddSingleton<LookupService>();
        return services;
    }

    public static IEndpointRouteBuilder MapLookupsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/lookups").WithTags("Lookups");

        group.MapGet("", async (bool? includeHidden, ClaimsPrincipal user, LookupService service, CancellationToken cancellationToken) =>
                CurrentOwner.Get(user) is { } ownerId
                    ? Results.Ok(await service.GetAsync(ownerId, includeHidden ?? false, cancellationToken))
                    : NoOwner())
            .WithName("GetLookups")
            .WithSummary("The caller's vehicle type, fuel type, transmission, and drivetrain lists: defaults first, then the caller's own items.")
            .Produces<LookupsDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/{kind}", async (string kind, AddLookupItemRequest request, ClaimsPrincipal user, LookupService service, CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return NoOwner();
                }

                if (LookupMapper.ToStoredKind(kind) is not { } storedKind)
                {
                    return NotFound();
                }

                var result = await service.AddAsync(ownerId, storedKind, request.Name!, cancellationToken);
                return result.Outcome switch
                {
                    AddLookupOutcome.Created => Results.Created("/api/lookups", result.Item),
                    AddLookupOutcome.Duplicate => Conflict(result.ExistingIsHidden
                        ? "That item already exists but is hidden. Show it instead."
                        : "That item already exists."),
                    _ => Conflict($"You can add up to {LookupRepository.MaxCustomItemsPerKind} items to this list."),
                };
            })
            .AddEndpointFilter<ValidationFilter<AddLookupItemRequest>>()
            .WithName("AddLookupItem")
            .WithSummary("Add a custom item to one of the caller's lookups (unique ignoring case, at most 50 per lookup).")
            .Produces<LookupItemDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{kind}/{itemId:guid}/visibility", async (string kind, Guid itemId, SetLookupVisibilityRequest request, ClaimsPrincipal user, LookupService service, CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return NoOwner();
                }

                if (LookupMapper.ToStoredKind(kind) is not { } storedKind)
                {
                    return NotFound();
                }

                var item = await service.SetVisibilityAsync(ownerId, storedKind, itemId, request.IsHidden!.Value, cancellationToken);
                return item is null ? NotFound() : Results.Ok(item);
            })
            .AddEndpointFilter<ValidationFilter<SetLookupVisibilityRequest>>()
            .WithName("SetLookupItemVisibility")
            .WithSummary("Hide or show an item for the caller only; idempotent.")
            .Produces<LookupItemDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    internal static IResult NoOwner() =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "The token does not identify a user.");

    internal static IResult NotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found.");

    private static IResult Conflict(string title) =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: title);
}
