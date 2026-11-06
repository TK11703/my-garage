using System.Security.Claims;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyGarage.Api.Common;
using MyGarage.Api.Vehicles;
using MyGarage.Database.Reminders;
using MyGarage.Database.Notifications;
using MyGarage.Shared.Reminders;
using OpenTelemetry.Metrics;

namespace MyGarage.Api.Reminders;

public static class RemindersEndpoints
{
    public static IServiceCollection AddReminders(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IValidator<ReminderRequest>>(sp => new ReminderRequestValidator(sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<ReminderEvaluator>();
        services.AddOptions<ReminderSweepOptions>().BindConfiguration(ReminderSweepOptions.SectionName);
        services.AddSingleton<ReminderSweepService>();
        services.AddHostedService(sp => sp.GetRequiredService<ReminderSweepService>());
        services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(ReminderMetrics.MeterName));
        services.AddSingleton<IValidator<CompleteReminderRequest>>(sp => new CompleteReminderRequestValidator(sp.GetRequiredService<TimeProvider>()));
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
                ReminderEvaluator reminderEvaluator,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var reminder = await reminders.AddAsync(
                    ownerId, vehicleId, Guid.CreateVersion7(), ReminderMapper.ToValues(request), timeProvider.GetUtcNow(), cancellationToken);

                if (reminder is null)
                {
                    return VehiclesEndpoints.NotFound();
                }

                await reminderEvaluator.TryEvaluateVehicleAsync(ownerId, vehicleId, cancellationToken);
                return Results.Created(
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

        group.MapPost("/{reminderId:guid}/complete", async (
                Guid vehicleId,
                Guid reminderId,
                CompleteReminderRequest request,
                ClaimsPrincipal user,
                ReminderRepository reminders,
                NotificationRepository notifications,
                ReminderEvaluator reminderEvaluator,
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

                if (MileageProblem(reminder, request) is { } problem)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["Mileage"] = [problem] });
                }

                var updated = await reminders.CompleteAsync(
                    ownerId, vehicleId, reminderId, Guid.CreateVersion7(), request.DoneOn!.Value, request.Mileage,
                    request.UpdateVehicleMileage, timeProvider.GetUtcNow(), cancellationToken);
                if (updated is null)
                {
                    return VehiclesEndpoints.NotFound();
                }

                await notifications.MarkReadForReminderAsync(ownerId, reminderId, timeProvider.GetUtcNow(), cancellationToken);
                await reminderEvaluator.TryEvaluateVehicleAsync(ownerId, vehicleId, cancellationToken);

                var completions = await reminders.ListCompletionsAsync(ownerId, vehicleId, reminderId, cancellationToken);
                return Results.Ok(ReminderMapper.ToDetailDto(updated, completions, ReminderRules.Today(timeProvider)));
            })
            .AddEndpointFilter<ValidationFilter<CompleteReminderRequest>>()
            .WithName("CompleteReminder")
            .WithSummary("Mark the reminder done and roll it forward.")
            .Produces<ReminderDetailDto>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    // A distance reminder needs the mileage it was done at, and it cannot be below the previous one.
    private static string? MileageProblem(ReminderRecord reminder, CompleteReminderRequest request)
    {
        if (reminder.IntervalMiles is not null && request.Mileage is null)
        {
            return "Mileage is required for a reminder that repeats by miles.";
        }

        return request.Mileage is { } mileage && reminder.LastDoneMileage is { } last && mileage < last
            ? $"Mileage cannot be below the last done mileage of {last:N0}."
            : null;
    }
}