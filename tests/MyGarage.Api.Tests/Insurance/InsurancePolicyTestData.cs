using System.Net;
using System.Net.Http.Json;
using MyGarage.Shared.Insurance;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Insurance;

public static class InsurancePolicyTestData
{
    public static InsurancePolicyRequest Request(
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        InsuranceDuration duration = InsuranceDuration.Annual,
        decimal cost = 1240m) =>
        new()
        {
            StartDate = startDate ?? new DateOnly(2026, 5, 1),
            EndDate = endDate ?? new DateOnly(2027, 5, 1),
            Duration = duration,
            Cost = cost,
        };

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid vehicleId, InsurancePolicyRequest request) =>
        client.PostAsJsonAsync($"/api/vehicles/{vehicleId}/insurance", request);

    public static async Task<InsurancePolicyDto> CreateAsync(HttpClient client, Guid vehicleId, InsurancePolicyRequest? request = null)
    {
        var response = await PostAsync(client, vehicleId, request ?? Request());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InsurancePolicyDto>())!;
    }

    public static async Task<PagedResultDto<InsurancePolicyDto>> ListAsync(HttpClient client, Guid vehicleId, string query = "") =>
        (await client.GetFromJsonAsync<PagedResultDto<InsurancePolicyDto>>($"/api/vehicles/{vehicleId}/insurance{query}"))!;
}
