using System.Security.Claims;
using FluentValidation;
using MyGarage.Api.Common;
using MyGarage.Api.Vehicles;
using MyGarage.Database.Notifications;
using MyGarage.Shared.Notifications;

namespace MyGarage.Api.Notifications;

// The take of the notification list; null means the default of five.
public sealed record NotificationListQuery(bool UnreadOnly = false, int? Take = null);

public sealed class NotificationListQueryValidator : AbstractValidator<NotificationListQuery>
{
    public const int MaxTake = 50;

    public NotificationListQueryValidator() =>
        RuleFor(query => query.Take).InclusiveBetween(1, MaxTake).WithMessage($"Take must be between 1 and {MaxTake}.");
}

public static class NotificationsEndpoints
{
    private const int DefaultTake = 5;

    public static IServiceCollection AddNotifications(this IServiceCollection services)
    {
        services.AddSingleton<IValidator<NotificationListQuery>, NotificationListQueryValidator>();
        return services;
    }

    public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/notifications").WithTags("Notifications");

        group.MapGet("", async (
                [AsParameters] NotificationListQuery query,
                ClaimsPrincipal user,
                NotificationRepository notifications,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                var items = await notifications.ListAsync(ownerId, query.UnreadOnly, query.Take ?? DefaultTake, cancellationToken);
                return Results.Ok(items.Select(NotificationMapper.ToDto).ToList());
            })
            .AddEndpointFilter<ValidationFilter<NotificationListQuery>>()
            .WithName("ListNotifications")
            .WithSummary("The caller's latest notifications, newest first.")
            .Produces<List<NotificationDto>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/unread-count", async (
                ClaimsPrincipal user,
                NotificationRepository notifications,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                return Results.Ok(new UnreadCountDto
                {
                    Unread = await notifications.GetUnreadCountAsync(ownerId, cancellationToken),
                    DraftsAwaitingReview = await notifications.CountDraftsAwaitingReviewAsync(ownerId, cancellationToken),
                });
            })
            .WithName("GetUnreadCount")
            .WithSummary("Unread notifications and drafts awaiting review; cheap enough to poll.")
            .Produces<UnreadCountDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/{notificationId:guid}/read", async (
                Guid notificationId,
                ClaimsPrincipal user,
                NotificationRepository notifications,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                return await notifications.MarkReadAsync(ownerId, notificationId, timeProvider.GetUtcNow(), cancellationToken)
                    ? Results.NoContent()
                    : VehiclesEndpoints.NotFound();
            })
            .WithName("MarkNotificationRead")
            .WithSummary("Mark one notification read.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/read-all", async (
                ClaimsPrincipal user,
                NotificationRepository notifications,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                await notifications.MarkAllReadAsync(ownerId, timeProvider.GetUtcNow(), cancellationToken);
                return Results.NoContent();
            })
            .WithName("MarkAllNotificationsRead")
            .WithSummary("Mark all of the caller's notifications read.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete("/{notificationId:guid}", async (
                Guid notificationId,
                ClaimsPrincipal user,
                NotificationRepository notifications,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                return await notifications.DeleteAsync(ownerId, notificationId, cancellationToken)
                    ? Results.NoContent()
                    : VehiclesEndpoints.NotFound();
            })
            .WithName("DeleteNotification")
            .WithSummary("Remove one notification; the draft it points to is untouched.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/read", async (
                ClaimsPrincipal user,
                NotificationRepository notifications,
                CancellationToken cancellationToken) =>
            {
                if (CurrentOwner.Get(user) is not { } ownerId)
                {
                    return VehiclesEndpoints.NoOwner();
                }

                await notifications.DeleteReadAsync(ownerId, cancellationToken);
                return Results.NoContent();
            })
            .WithName("DeleteReadNotifications")
            .WithSummary("Remove all of the caller's read notifications.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return endpoints;
    }
}
