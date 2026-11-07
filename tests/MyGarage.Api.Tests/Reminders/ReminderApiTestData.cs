using System.Net;
using System.Net.Http.Json;
using MyGarage.Shared.Reminders;

namespace MyGarage.Api.Tests.Reminders;

public static class ReminderApiTestData
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public static string Url(Guid vehicleId, Guid? reminderId = null, string? action = null) =>
        $"/api/vehicles/{vehicleId}/reminders" + (reminderId is null ? string.Empty : $"/{reminderId}") + (action is null ? string.Empty : $"/{action}");

    // Oil change every 5,000 miles, last done at 40,000.
    public static ReminderRequest Request(
        string name = "Oil change",
        int? miles = 5_000,
        ReminderTimeIntervalDto? time = null,
        DateOnly? lastDoneOn = null,
        int? lastDoneMileage = 40_000) =>
        new()
        {
            Name = name,
            IntervalMiles = miles,
            IntervalTime = time,
            LastDoneOn = lastDoneOn ?? Today.AddDays(-30),
            LastDoneMileage = lastDoneMileage,
        };

    public static ReminderTimeIntervalDto Time(int value, ReminderTimeUnit unit) => new() { Value = value, Unit = unit };

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid vehicleId, ReminderRequest request) =>
        client.PostAsJsonAsync(Url(vehicleId), request);

    public static async Task<ReminderDto> CreateAsync(HttpClient client, Guid vehicleId, ReminderRequest? request = null)
    {
        var response = await PostAsync(client, vehicleId, request ?? Request());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReminderDto>())!;
    }

    public static async Task<List<ReminderDto>> ListAsync(HttpClient client, Guid vehicleId) =>
        (await client.GetFromJsonAsync<List<ReminderDto>>(Url(vehicleId)))!;
}
