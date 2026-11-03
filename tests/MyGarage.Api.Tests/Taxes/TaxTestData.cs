using System.Net;
using System.Net.Http.Json;
using MyGarage.Shared.Taxes;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Taxes;

public static class TaxTestData
{
    public static TaxRequest Request(
        TaxType type = TaxType.PersonalProperty,
        decimal amount = 312.40m,
        TaxRecurrence recurrence = TaxRecurrence.Annual) =>
        new() { Type = type, Amount = amount, Recurrence = recurrence };

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid vehicleId, TaxRequest request) =>
        client.PostAsJsonAsync($"/api/vehicles/{vehicleId}/taxes", request);

    public static async Task<TaxDto> CreateAsync(HttpClient client, Guid vehicleId, TaxRequest? request = null)
    {
        var response = await PostAsync(client, vehicleId, request ?? Request());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TaxDto>())!;
    }

    public static async Task<PagedResultDto<TaxDto>> ListAsync(HttpClient client, Guid vehicleId, string query = "") =>
        (await client.GetFromJsonAsync<PagedResultDto<TaxDto>>($"/api/vehicles/{vehicleId}/taxes{query}"))!;
}
