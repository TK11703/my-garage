using System.Security.Claims;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyGarage.Api.Common;
using MyGarage.Api.Vehicles;
using MyGarage.Database.EmailIntake;
using MyGarage.Database.VehicleDocuments;
using MyGarage.Shared.Drafts;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Drafts;

public static class DraftsEndpoints
{
    public static IServiceCollection AddDrafts(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<AcceptDraftRequest>, AcceptDraftRequestValidator>();
        services.AddSingleton<IValidator<ChangeDraftKindRequest>, ChangeDraftKindRequestValidator>();
        services.TryAddSingleton<IValidator<PagingQuery>, PagingQueryValidator>();
        services.AddSingleton<IDraftDuplicateLookup, DraftDuplicateLookup>();
        services.AddSingleton<CompatibilityChecker>();
        services.AddSingleton<DraftService>();

        services.AddOptions<DraftExpiryOptions>().BindConfiguration(DraftExpiryOptions.SectionName);
        services.AddSingleton<DraftExpiryService>();
        services.AddHostedService(sp => sp.GetRequiredService<DraftExpiryService>());
        return services;
    }

    public static IEndpointRouteBuilder MapDraftsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/drafts").WithTags("Drafts").AddEndpointFilter<DraftStorageFilter>();

        group.MapGet("", async (
                [AsParameters] PagingQuery query,
                ClaimsPrincipal user,
                DraftService drafts,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                return Results.Ok(await drafts.ListAsync(ownerId, query.Page, query.PageSize, cancellationToken));
            })
            .AddEndpointFilter<ValidationFilter<PagingQuery>>()
            .WithName("ListDrafts")
            .WithSummary("Page of the caller's drafts awaiting review, newest first.")
            .Produces<PagedResultDto<MaintenanceDraftSummaryDto>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{draftId:guid}", async (
                Guid draftId,
                ClaimsPrincipal user,
                DraftService drafts,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var draft = await drafts.GetAsync(ownerId, draftId, cancellationToken);
                return draft is null ? VehiclesEndpoints.NotFound() : Results.Ok(draft);
            })
            .WithName("GetDraft")
            .WithSummary("One draft with its proposal, vehicle suggestions, and compatibility result; opening it marks its notification read.")
            .Produces<MaintenanceDraftDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{draftId:guid}/compatibility", async (
                Guid draftId,
                DraftProposal proposal,
                ClaimsPrincipal user,
                DraftService drafts,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var result = await drafts.CheckCompatibilityAsync(ownerId, draftId, proposal, cancellationToken);
                return result is null ? VehiclesEndpoints.NotFound() : Results.Ok(result);
            })
            .WithName("CheckDraftCompatibility")
            .WithSummary("Check an edited proposal against the validators of its kind without saving anything.")
            .Produces<CompatibilityDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{draftId:guid}/kind", async (
                Guid draftId,
                ChangeDraftKindRequest request,
                ClaimsPrincipal user,
                DraftService drafts,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var result = await drafts.ChangeKindAsync(ownerId, draftId, request.Kind!.Value, cancellationToken);
                return result.Outcome switch
                {
                    DraftOutcome.Ok => Results.Ok(result.Value),
                    DraftOutcome.Conflict => AlreadyDecided(),
                    _ => VehiclesEndpoints.NotFound(),
                };
            })
            .AddEndpointFilter<ValidationFilter<ChangeDraftKindRequest>>()
            .WithName("ChangeDraftKind")
            .WithSummary("Re-present the draft as another record kind, carrying over the fields that fit.")
            .Produces<MaintenanceDraftDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{draftId:guid}/accept", async (
                Guid draftId,
                AcceptDraftRequest request,
                ClaimsPrincipal user,
                DraftService drafts,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var result = await drafts.AcceptAsync(ownerId, draftId, request, cancellationToken);
                return result.Outcome switch
                {
                    DraftOutcome.Ok => Results.Created($"/api/vehicles/{result.Value!.VehicleId}", result.Value),
                    DraftOutcome.Conflict => AlreadyDecided(),
                    _ => VehiclesEndpoints.NotFound(),
                };
            })
            .AddEndpointFilter<ValidationFilter<AcceptDraftRequest>>()
            .WithName("AcceptDraft")
            .WithSummary("Create the record from the (possibly edited) proposal, atomically.")
            .Produces<AcceptedDraftDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{draftId:guid}/discard", async (
                Guid draftId,
                ClaimsPrincipal user,
                DraftService drafts,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                return await drafts.DiscardAsync(ownerId, draftId, cancellationToken) switch
                {
                    DraftOutcome.Ok => Results.NoContent(),
                    DraftOutcome.Conflict => AlreadyDecided(),
                    _ => VehiclesEndpoints.NotFound(),
                };
            })
            .WithName("DiscardDraft")
            .WithSummary("Discard the draft; its details, notification, and stored file are removed.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{draftId:guid}/attachment", async (
                Guid draftId,
                ClaimsPrincipal user,
                HttpContext http,
                DraftService drafts,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var content = await drafts.OpenAttachmentAsync(ownerId, draftId, cancellationToken);
                if (content is null)
                {
                    return VehiclesEndpoints.NotFound();
                }

                http.Response.Headers["X-Content-Type-Options"] = "nosniff";
                http.Response.Headers.CacheControl = "private, no-store";
                return Results.Stream(content.Content, content.ContentType);
            })
            .WithName("GetDraftAttachment")
            .WithSummary("The original PDF or image, streamed from private storage with the detected content type.")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    private static IResult AlreadyDecided() =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "This draft was already decided.");
}

// Answers 503 problem details when file storage cannot be reached, so the rest of the draft stays untouched and the action can be retried.
public sealed class DraftStorageFilter(ILogger<DraftStorageFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (Exception ex) when (ex is IntakeStorageUnavailableException or DocumentStorageUnavailableException)
        {
            logger.LogWarning(ex, "File storage is unavailable");
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "File storage is temporarily unavailable.");
        }
    }
}
