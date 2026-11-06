using System.Security.Claims;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyGarage.Api.Common;
using MyGarage.Api.Drafts;
using MyGarage.Api.Vehicles;
using MyGarage.Database.EmailIntake;
using MyGarage.Shared.EmailIntake;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.EmailIntake;

// What happened to the emails attributed to the caller. Every read is scoped to the caller's owner id in SQL.
public sealed class EmailHistoryService(EmailSubmissionRepository submissions)
{
    private const int DefaultPageSize = 10;

    // The outcome words the history can be filtered by (the stored JSON text of EmailHistoryOutcome).
    public static readonly IReadOnlyList<string> Outcomes = ["awaiting_review", "included", "discarded", "expired", "not_applicable", "failed"];

    public async Task<PagedResultDto<EmailHistoryItemDto>> ListAsync(string ownerId, int page, int? pageSize, CancellationToken cancellationToken, string? outcome = null)
    {
        var size = pageSize ?? DefaultPageSize;
        var result = await submissions.ListHistoryPageAsync(ownerId, page, size, cancellationToken, outcome);

        // A page beyond the last is answered with the last page; the returned Page is the one served.
        var effectivePage = Math.Max(1, Math.Min(page, result.TotalPages));
        if (effectivePage != page)
        {
            result = await submissions.ListHistoryPageAsync(ownerId, effectivePage, size, cancellationToken, outcome);
        }

        return DraftMapper.ToHistoryPageDto(result);
    }
}

public static class EmailHistoryEndpoints
{
    public static IServiceCollection AddEmailHistory(this IServiceCollection services)
    {
        services.TryAddSingleton<IValidator<PagingQuery>, PagingQueryValidator>();
        services.AddSingleton<EmailHistoryService>();
        return services;
    }

    public static IEndpointRouteBuilder MapEmailHistoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Signed-in users only (the default policy); the workflow's app-only token is refused.
        var group = endpoints.MapGroup("/api/emails").WithTags("Drafts");

        group.MapGet("", async (
                [AsParameters] PagingQuery query,
                string? outcome,
                ClaimsPrincipal user,
                EmailHistoryService history,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                if (outcome is not null && !EmailHistoryService.Outcomes.Contains(outcome))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["outcome"] = [$"Outcome must be one of: {string.Join(", ", EmailHistoryService.Outcomes)}."],
                    });
                }

                return Results.Ok(await history.ListAsync(ownerId, query.Page, query.PageSize, cancellationToken, outcome));
            })
            .AddEndpointFilter<ValidationFilter<PagingQuery>>()
            .WithName("ListEmailHistory")
            .WithSummary("Page of what happened to the emails attributed to the caller, newest first; optionally only one outcome.")
            .Produces<PagedResultDto<EmailHistoryItemDto>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }
}
