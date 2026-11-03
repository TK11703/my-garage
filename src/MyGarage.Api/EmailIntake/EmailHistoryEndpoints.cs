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

    public async Task<PagedResultDto<EmailHistoryItemDto>> ListAsync(string ownerId, int page, int? pageSize, CancellationToken cancellationToken)
    {
        var size = pageSize ?? DefaultPageSize;
        var result = await submissions.ListHistoryPageAsync(ownerId, page, size, cancellationToken);

        // A page beyond the last is answered with the last page; the returned Page is the one served.
        var effectivePage = Math.Max(1, Math.Min(page, result.TotalPages));
        if (effectivePage != page)
        {
            result = await submissions.ListHistoryPageAsync(ownerId, effectivePage, size, cancellationToken);
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
                ClaimsPrincipal user,
                EmailHistoryService history,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                return Results.Ok(await history.ListAsync(ownerId, query.Page, query.PageSize, cancellationToken));
            })
            .AddEndpointFilter<ValidationFilter<PagingQuery>>()
            .WithName("ListEmailHistory")
            .WithSummary("Page of what happened to the emails attributed to the caller, newest first.")
            .Produces<PagedResultDto<EmailHistoryItemDto>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }
}
