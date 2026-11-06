using MyGarage.Shared.Reminders;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Reminders;

public sealed class RemindersApiClient(HttpClient http)
{
    public Task<ApiResult<List<ReminderDto>>> ListAsync(Guid vehicleId, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<List<ReminderDto>>(http, HttpMethod.Get, $"api/vehicles/{vehicleId}/reminders", null, cancellationToken);

    // Every reminder across the caller's vehicles, most urgent first; status filters on the server (snake_case text of the status).
    public Task<ApiResult<List<ReminderDto>>> ListAllAsync(CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<List<ReminderDto>>(http, HttpMethod.Get, "api/reminders", null, cancellationToken);

    public Task<ApiResult<ReminderDetailDto>> GetAsync(Guid vehicleId, Guid reminderId, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<ReminderDetailDto>(http, HttpMethod.Get, $"api/vehicles/{vehicleId}/reminders/{reminderId}", null, cancellationToken);

    public Task<ApiResult<ReminderDto>> CreateAsync(Guid vehicleId, ReminderRequest request, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<ReminderDto>(http, HttpMethod.Post, $"api/vehicles/{vehicleId}/reminders", request, cancellationToken);

    public Task<ApiResult<ReminderDetailDto>> CompleteAsync(Guid vehicleId, Guid reminderId, CompleteReminderRequest request, CancellationToken cancellationToken = default) =>
        ApiCalls.SendAsync<ReminderDetailDto>(http, HttpMethod.Post, $"api/vehicles/{vehicleId}/reminders/{reminderId}/complete", request, cancellationToken);
}
