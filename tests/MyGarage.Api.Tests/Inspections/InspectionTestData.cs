using System.Net;
using System.Net.Http.Json;
using MyGarage.Shared.Inspections;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Inspections;

public static class InspectionTestData
{
    public static InspectionRequest Request(
        InspectionType type = InspectionType.Safety,
        DateOnly? date = null,
        decimal cost = 25.50m,
        DateOnly? nextDueDate = null) =>
        new() { Type = type, InspectionDate = date ?? new DateOnly(2026, 5, 1), Cost = cost, NextDueDate = nextDueDate };

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid vehicleId, InspectionRequest request) =>
        client.PostAsJsonAsync($"/api/vehicles/{vehicleId}/inspections", request);

    public static async Task<InspectionDto> CreateAsync(HttpClient client, Guid vehicleId, InspectionRequest? request = null)
    {
        var response = await PostAsync(client, vehicleId, request ?? Request());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InspectionDto>())!;
    }

    public static async Task<PagedResultDto<InspectionDto>> ListAsync(HttpClient client, Guid vehicleId, string query = "") =>
        (await client.GetFromJsonAsync<PagedResultDto<InspectionDto>>($"/api/vehicles/{vehicleId}/inspections{query}"))!;
}
