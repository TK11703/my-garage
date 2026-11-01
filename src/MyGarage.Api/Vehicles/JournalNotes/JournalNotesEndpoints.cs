using System.Security.Claims;
using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Database.JournalNotes;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Vehicles.JournalNotes;

public static class JournalNotesEndpoints
{
    private const int DefaultPageSize = 10;

    public static IServiceCollection AddJournalNotes(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<JournalNoteRequest>, JournalNoteRequestValidator>();
        services.AddSingleton<IValidator<PagingQuery>, PagingQueryValidator>();
        return services;
    }

    public static IEndpointRouteBuilder MapJournalNotesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/vehicles/{vehicleId:guid}/notes").WithTags("JournalNotes");

        group.MapGet("", async (
                Guid vehicleId,
                [AsParameters] PagingQuery query,
                ClaimsPrincipal user,
                JournalNoteRepository notes,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var pageSize = query.PageSize ?? DefaultPageSize;
                var result = await notes.ListPageAsync(ownerId, vehicleId, query.Page, pageSize, cancellationToken);
                if (result is null)
                {
                    return VehiclesEndpoints.NotFound();
                }

                // A page beyond the last is answered with the last page; the returned Page is the one served.
                var effectivePage = Math.Max(1, Math.Min(query.Page, result.TotalPages));
                if (effectivePage != query.Page)
                {
                    result = await notes.ListPageAsync(ownerId, vehicleId, effectivePage, pageSize, cancellationToken);
                }

                return Results.Ok(JournalNoteMapper.ToPageDto(result!));
            })
            .AddEndpointFilter<ValidationFilter<PagingQuery>>()
            .WithName("ListJournalNotes")
            .WithSummary("Page of the vehicle's journal notes, newest first.")
            .Produces<PagedResultDto<JournalNoteDto>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("", async (
                Guid vehicleId,
                JournalNoteRequest request,
                ClaimsPrincipal user,
                JournalNoteRepository notes,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var note = await notes.AddAsync(
                    ownerId,
                    vehicleId,
                    Guid.CreateVersion7(),
                    JournalNoteMapper.CleanCategory(request.Category),
                    request.Body!.Trim(),
                    timeProvider.GetUtcNow(),
                    cancellationToken);

                return note is null
                    ? VehiclesEndpoints.NotFound()
                    : Results.Created($"/api/vehicles/{vehicleId}/notes", JournalNoteMapper.ToDto(note));
            })
            .AddEndpointFilter<ValidationFilter<JournalNoteRequest>>()
            .WithName("CreateJournalNote")
            .WithSummary("Add a note with an optional free-text category.")
            .Produces<JournalNoteDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{noteId:guid}", async (
                Guid vehicleId,
                Guid noteId,
                JournalNoteRequest request,
                ClaimsPrincipal user,
                JournalNoteRepository notes,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var note = await notes.UpdateAsync(
                    ownerId,
                    vehicleId,
                    noteId,
                    JournalNoteMapper.CleanCategory(request.Category),
                    request.Body!.Trim(),
                    timeProvider.GetUtcNow(),
                    cancellationToken);

                return note is null ? VehiclesEndpoints.NotFound() : Results.Ok(JournalNoteMapper.ToDto(note));
            })
            .AddEndpointFilter<ValidationFilter<JournalNoteRequest>>()
            .WithName("UpdateJournalNote")
            .WithSummary("Edit a note's text and category.")
            .Produces<JournalNoteDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{noteId:guid}", async (
                Guid vehicleId,
                Guid noteId,
                ClaimsPrincipal user,
                JournalNoteRepository notes,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                return await notes.DeleteAsync(ownerId, vehicleId, noteId, cancellationToken) ? Results.NoContent() : VehiclesEndpoints.NotFound();
            })
            .WithName("DeleteJournalNote")
            .WithSummary("Delete a note.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
