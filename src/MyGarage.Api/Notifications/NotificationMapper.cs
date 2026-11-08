using MyGarage.Database.Notifications;
using MyGarage.Shared.Notifications;

namespace MyGarage.Api.Notifications;

public static class NotificationMapper
{
    public static NotificationDto ToDto(NotificationRecord record) =>
        new()
        {
            Id = record.Id,
            Kind = record.Kind,
            Title = record.Title,
            DraftId = record.DraftId,
            ReminderId = record.ReminderId,
            VehicleId = record.VehicleId,
            CreatedAt = record.CreatedAt,
            IsRead = record.ReadAt is not null,
        };
}
