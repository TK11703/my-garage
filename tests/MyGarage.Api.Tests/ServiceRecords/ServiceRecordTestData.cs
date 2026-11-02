using System.Net;
using System.Net.Http.Json;
using MyGarage.Shared.ServiceRecords;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.ServiceRecords;

public static class ServiceRecordTestData
{
    public static async Task<VehicleDetailDto> CreateVehicleAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/vehicles", new VehicleRequest { Make = "Ford", Model = "F-150", Year = 2020 });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VehicleDetailDto>())!;
    }

    public static Task<HttpResponseMessage> PostRecordAsync(HttpClient client, Guid vehicleId, ServiceRecordRequest request) =>
        client.PostAsJsonAsync($"/api/vehicles/{vehicleId}/service-records", request);

    public static async Task<ServiceRecordDto> CreateRecordAsync(
        HttpClient client, Guid vehicleId, DateOnly? date = null, int odometer = 1000, string? shop = null)
    {
        var response = await PostRecordAsync(client, vehicleId, new ServiceRecordRequest
        {
            DateServiced = date ?? new DateOnly(2026, 5, 1),
            OdometerReading = odometer,
            ShopOrPerson = shop,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ServiceRecordDto>())!;
    }

    public static Task<HttpResponseMessage> PostLineItemAsync(HttpClient client, Guid vehicleId, Guid recordId, ServiceLineItemRequest request) =>
        client.PostAsJsonAsync($"/api/vehicles/{vehicleId}/service-records/{recordId}/line-items", request);

    public static async Task<ServiceRecordDto> AddFlatLineItemAsync(
        HttpClient client, Guid vehicleId, Guid recordId, string description = "Tire rotation", decimal cost = 40m)
    {
        var response = await PostLineItemAsync(client, vehicleId, recordId, new ServiceLineItemRequest { Description = description, FlatCost = cost });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ServiceRecordDto>())!;
    }
}
