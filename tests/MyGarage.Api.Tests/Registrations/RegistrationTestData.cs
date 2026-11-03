using System.Net;
using System.Net.Http.Json;
using MyGarage.Shared.Registrations;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Registrations;

public static class RegistrationTestData
{
    public static RegistrationRequest Request(string state = "WA", decimal cost = 78.75m, DateOnly? expirationDate = null) =>
        new() { State = state, Cost = cost, ExpirationDate = expirationDate ?? new DateOnly(2027, 5, 1) };

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid vehicleId, RegistrationRequest request) =>
        client.PostAsJsonAsync($"/api/vehicles/{vehicleId}/registrations", request);

    public static async Task<RegistrationDto> CreateAsync(HttpClient client, Guid vehicleId, RegistrationRequest? request = null)
    {
        var response = await PostAsync(client, vehicleId, request ?? Request());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RegistrationDto>())!;
    }

    public static async Task<PagedResultDto<RegistrationDto>> ListAsync(HttpClient client, Guid vehicleId, string query = "") =>
        (await client.GetFromJsonAsync<PagedResultDto<RegistrationDto>>($"/api/vehicles/{vehicleId}/registrations{query}"))!;
}
