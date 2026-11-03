using System.Net;
using System.Net.Http.Json;
using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.FuelEnergyEntries;

public static class FuelEnergyEntryTestData
{
    public static FuelEnergyEntryRequest Request(
        FuelEnergyKind kind = FuelEnergyKind.Fuel,
        DateOnly? date = null,
        decimal unitPrice = 3.459m,
        decimal quantity = 12.345m,
        decimal? totalCost = 42.70m) =>
        new() { Kind = kind, EntryDate = date ?? new DateOnly(2026, 5, 1), UnitPrice = unitPrice, Quantity = quantity, TotalCost = totalCost };

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid vehicleId, FuelEnergyEntryRequest request) =>
        client.PostAsJsonAsync($"/api/vehicles/{vehicleId}/fuel-energy-entries", request);

    public static async Task<FuelEnergyEntryDto> CreateAsync(HttpClient client, Guid vehicleId, FuelEnergyEntryRequest? request = null)
    {
        var response = await PostAsync(client, vehicleId, request ?? Request());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<FuelEnergyEntryDto>())!;
    }

    public static async Task<PagedResultDto<FuelEnergyEntryDto>> ListAsync(HttpClient client, Guid vehicleId, string query = "") =>
        (await client.GetFromJsonAsync<PagedResultDto<FuelEnergyEntryDto>>($"/api/vehicles/{vehicleId}/fuel-energy-entries{query}"))!;
}
