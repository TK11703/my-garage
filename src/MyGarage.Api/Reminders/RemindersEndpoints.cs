using System.Security.Claims;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyGarage.Api.Common;
using MyGarage.Api.Vehicles;
using MyGarage.Database.Reminders;
using MyGarage.Shared.Reminders;

namespace MyGarage.Api.Reminders;

public static class RemindersEndpoints
{
    public static IServiceCollection AddReminders(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IValidator<ReminderRequest>>(sp => new ReminderRequestValidator(sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IValidator<GarageReminderListQuery>, GarageReminderListQueryValidator>();
        return services;
    }

    public static IEndpointRouteBuilder MapRemindersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/vehicles/{vehicleId:guid}/reminders").WithTags("Reminders");

        group.MapGet("", async (
                Guid vehicleId,
                ClaimsPrincipal user,
                ReminderRepository reminders,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var items = await reminders.ListForVehicleAsync(ownerId, vehicleId, cancellationToken);
                if (items is null)
                {
                    return VehiclesEndpoints.NotFound();
                }

                var today = ReminderRules.Today(timeProvider);
                return Results.Ok(ReminderOrdering.MostUrgentFirst(items.Select(item => ReminderMapper.ToDto(item, today))));
            })
            .WithName("ListReminders")
            .WithSummary("The vehicle's reminders.")
            .Produces<List<ReminderDto>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("", async (
                Guid vehicleId,
                ReminderRequest request,
                ClaimsPrincipal user,
                ReminderRepository reminders,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var reminder = await reminders.AddAsync(
                    ownerId, vehicleId, Guid.CreateVersion7(), ReminderMapper.ToValues(request), timeProvider.GetUtcNow(), cancellationToken);

                return reminder is null
                    ? VehiclesEndpoints.NotFound()
                    : Results.Created(
                        $"/api/vehicles/{vehicleId}/reminders/{reminder.Id}", ReminderMapper.ToDto(reminder, ReminderRules.Today(timeProvider)));
            })
            .AddEndpointFilter<ValidationFilter<ReminderRequest>>()
            .WithName("CreateReminder")
            .WithSummary("Add a reminder to the vehicle.")
            .Produces<ReminderDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{reminderId:guid}", async (
                Guid vehicleId,
                Guid reminderId,
                ClaimsPrincipal user,
                ReminderRepository reminders,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var reminder = await reminders.GetAsync(ownerId, vehicleId, reminderId, cancellationToken);
                if (reminder is null)
                {
                    return VehiclesEndpoints.NotFound();
                }

                var completions = await reminders.ListCompletionsAsync(ownerId, vehicleId, reminderId, cancellationToken);
                return Results.Ok(ReminderMapper.ToDetailDto(reminder, completions, ReminderRules.Today(timeProvider)));
            })
            .WithName("GetReminder")
            .WithSummary("One reminder with its completion history, most recent first.")
            .Produces<ReminderDetailDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
