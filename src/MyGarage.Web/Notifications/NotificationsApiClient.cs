using MyGarage.Shared.Notifications;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Notifications;

public sealed class NotificationsApiClient(HttpClient http)
{
    public Task<ApiResult<NotificationDto[]>> ListAsync(int take, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<NotificationDto[]>(http, HttpMethod.Get, $"api/notifications?take={take}", null, cancellationToken);

    public Task<ApiResult<UnreadCountDto>> GetUnreadCountAsync(CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<UnreadCountDto>(http, HttpMethod.Get, "api/notifications/unread-count", null, cancellationToken);

    public Task<ApiResult<bool>> MarkReadAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<bool>(http, HttpMethod.Post, $"api/notifications/{notificationId}/read", null, cancellationToken);

    public Task<ApiResult<bool>> MarkAllReadAsync(CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<bool>(http, HttpMethod.Post, "api/notifications/read-all", null, cancellationToken);

    public Task<ApiResult<bool>> DeleteAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<bool>(http, HttpMethod.Delete, $"api/notifications/{notificationId}", null, cancellationToken);

    public Task<ApiResult<bool>> DeleteReadAsync(CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<bool>(http, HttpMethod.Delete, "api/notifications/read", null, cancellationToken);
}
