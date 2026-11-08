using System.Security.Claims;
using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Api.Vehicles;
using MyGarage.Database.Reminders;
using MyGarage.Shared.Reminders;

namespace MyGarage.Api.Reminders;

// The status is the snake_case text of ReminderStatus (overdue, due_soon, upcoming, paused, mileage_needed); null means all.
public sealed record GarageReminderListQuery(string? Status = null)
{
    internal static readonly IReadOnlyDictionary<string, ReminderStatus> Statuses = new Dictionary<string, ReminderStatus>(StringComparer.Ordinal)
    {
        ["upcoming"] = ReminderStatus.Upcoming,
        ["due_soon"] = ReminderStatus.DueSoon,
        ["overdue"] = ReminderStatus.Overdue,
        ["paused"] = ReminderStatus.Paused,
        ["mileage_needed"] = ReminderStatus.MileageNeeded,
    };

    internal ReminderStatus? ParsedStatus => Status is not null && Statuses.TryGetValue(Status, out var status) ? status : null;
}

public sealed class GarageReminderListQueryValidator : AbstractValidator<GarageReminderListQuery>
{
    public GarageReminderListQueryValidator() =>
        RuleFor(query => query.Status)
            .Must(status => status is null || GarageReminderListQuery.Statuses.ContainsKey(status))
            .WithMessage("Status must be one of: " + string.Join(", ", GarageReminderListQuery.Statuses.Keys) + ".");
}

public static class GarageRemindersEndpoints
{
    public static IEndpointRouteBuilder MapGarageRemindersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/reminders").WithTags("Reminders");

        group.MapGet("", async (
                [AsParameters] GarageReminderListQuery query,
                ClaimsPrincipal user,
                ReminderRepository reminders,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var today = ReminderRules.Today(timeProvider);
                var items = (await reminders.ListForOwnerAsync(ownerId, cancellationToken)).Select(item => ReminderMapper.ToDto(item, today));
                if (query.ParsedStatus is { } status)
                {
                    items = items.Where(item => item.Status == status);
                }

                return Results.Ok(ReminderOrdering.MostUrgentFirst(items));
            })
            .AddEndpointFilter<ValidationFilter<GarageReminderListQuery>>()
            .WithName("ListGarageReminders")
            .WithSummary("Reminders across all of the caller's vehicles, most urgent first, optionally in one status.")
            .Produces<List<ReminderDto>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }
}
