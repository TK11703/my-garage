using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Shared.Inspections;
using MyGarage.Shared.Insurance;
using MyGarage.Shared.Registrations;
using MyGarage.Shared.Taxes;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Maintenance;

// FR-017: logs never carry the values entered on fuel and charging entries, inspections, registrations, insurance, or taxes.
// Shares a collection with the vehicle logging test because hosts replace the static Serilog logger and must not overlap.
[Collection("Logging")]
public class MaintenanceLoggingTests(ApiFactory factory) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    // Distinctive amounts that cannot appear in a log by accident.
    private static readonly string[] Markers = ["8765.4321", "4321.987", "7654.32", "6543.21", "5432.19", "4321.09", "3210.98", "2109.87"];

    public Task InitializeAsync() => factory.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class CapturingLoggerProvider : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(' ', pairs.Select(pair => $"{pair.Key}={pair.Value}")) : string.Empty;
            Entries.Enqueue($"{logLevel} {formatter(state, exception)} {values} {exception}");
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    public async Task EveryFlow_KeepsEnteredValuesOutOfTheLogs()
    {
        var provider = new CapturingLoggerProvider();
        using var logged = factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(provider);
        }));
        var client = logged.CreateClient();
        // Proves the host writes to the capturing provider, so an empty result below could not hide a leak.
        logged.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MaintenanceLoggingTests").LogInformation("capture check");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwt.Create(claims: new Dictionary<string, object> { ["oid"] = "log-user" }));

        var vehicle = (await (await client.PostAsJsonAsync("/api/vehicles", new VehicleRequest { Make = "Ford", Model = "F-150", Year = 2020 }))
            .Content.ReadFromJsonAsync<VehicleDetailDto>())!;
        var baseUrl = $"/api/vehicles/{vehicle.Id}";

        // Fuel and charging: valid create, update, invalid create, delete.
        var fuel = (await (await client.PostAsJsonAsync($"{baseUrl}/fuel-energy-entries", new FuelEnergyEntryRequest
        {
            Kind = FuelEnergyKind.Fuel, EntryDate = new DateOnly(2026, 5, 1), UnitPrice = 8765.4321m, Quantity = 4321.987m, TotalCost = 7654.32m,
        })).Content.ReadFromJsonAsync<FuelEnergyEntryDto>())!;
        await client.PutAsJsonAsync($"{baseUrl}/fuel-energy-entries/{fuel.Id}", new FuelEnergyEntryRequest
        {
            Kind = FuelEnergyKind.Energy, EntryDate = new DateOnly(2026, 5, 2), UnitPrice = 8765.4321m, Quantity = 4321.987m, TotalCost = null,
        });
        await client.PostAsJsonAsync($"{baseUrl}/fuel-energy-entries", new FuelEnergyEntryRequest
        {
            Kind = FuelEnergyKind.Fuel, EntryDate = new DateOnly(2999, 1, 1), UnitPrice = 8765.4321111m, Quantity = 4321.987m, TotalCost = 7654.32m,
        });
        await client.GetAsync($"{baseUrl}/fuel-energy-entries?page=1");
        await client.DeleteAsync($"{baseUrl}/fuel-energy-entries/{fuel.Id}");

        // Inspections.
        var inspection = (await (await client.PostAsJsonAsync($"{baseUrl}/inspections", new InspectionRequest
        {
            Type = InspectionType.Safety, InspectionDate = new DateOnly(2026, 5, 1), Cost = 6543.21m, NextDueDate = new DateOnly(2027, 5, 1),
        })).Content.ReadFromJsonAsync<InspectionDto>())!;
        await client.PutAsJsonAsync($"{baseUrl}/inspections/{inspection.Id}", new InspectionRequest
        {
            Type = InspectionType.Emissions, InspectionDate = new DateOnly(2026, 5, 2), Cost = 6543.21m,
        });
        await client.PostAsJsonAsync($"{baseUrl}/inspections", new InspectionRequest
        {
            Type = InspectionType.Safety, InspectionDate = new DateOnly(2026, 5, 1), Cost = 6543.215m, NextDueDate = new DateOnly(2026, 4, 1),
        });
        await client.GetAsync($"{baseUrl}/inspections/{inspection.Id}");
        await client.DeleteAsync($"{baseUrl}/inspections/{inspection.Id}");

        // Registrations.
        var registration = (await (await client.PostAsJsonAsync($"{baseUrl}/registrations", new RegistrationRequest
        {
            State = "WA", Cost = 5432.19m, ExpirationDate = new DateOnly(2027, 5, 1),
        })).Content.ReadFromJsonAsync<RegistrationDto>())!;
        await client.PutAsJsonAsync($"{baseUrl}/registrations/{registration.Id}", new RegistrationRequest
        {
            State = "OR", Cost = 5432.19m, ExpirationDate = new DateOnly(2028, 5, 1),
        });
        await client.PostAsJsonAsync($"{baseUrl}/registrations", new RegistrationRequest { State = "ZZ", Cost = 5432.195m, ExpirationDate = new DateOnly(2101, 1, 1) });
        await client.DeleteAsync($"{baseUrl}/registrations/{registration.Id}");

        // Insurance.
        var policy = (await (await client.PostAsJsonAsync($"{baseUrl}/insurance", new InsurancePolicyRequest
        {
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2027, 1, 1), Duration = InsuranceDuration.Annual, Cost = 4321.09m,
        })).Content.ReadFromJsonAsync<InsurancePolicyDto>())!;
        await client.PutAsJsonAsync($"{baseUrl}/insurance/{policy.Id}", new InsurancePolicyRequest
        {
            StartDate = new DateOnly(2026, 2, 1), EndDate = new DateOnly(2027, 2, 1), Duration = InsuranceDuration.Monthly, Cost = 4321.09m,
        });
        await client.PostAsJsonAsync($"{baseUrl}/insurance", new InsurancePolicyRequest
        {
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2025, 1, 1), Duration = InsuranceDuration.Annual, Cost = 4321.095m,
        });
        await client.DeleteAsync($"{baseUrl}/insurance/{policy.Id}");

        // Taxes.
        var tax = (await (await client.PostAsJsonAsync($"{baseUrl}/taxes", new TaxRequest
        {
            Type = TaxType.PersonalProperty, Amount = 3210.98m, Recurrence = TaxRecurrence.Annual,
        })).Content.ReadFromJsonAsync<TaxDto>())!;
        await client.PutAsJsonAsync($"{baseUrl}/taxes/{tax.Id}", new TaxRequest { Type = TaxType.Lease, Amount = 2109.87m, Recurrence = TaxRecurrence.Monthly });
        await client.PostAsJsonAsync($"{baseUrl}/taxes", new TaxRequest { Type = TaxType.Purchase, Amount = 3210.985m, Recurrence = TaxRecurrence.Once });
        await client.DeleteAsync($"{baseUrl}/taxes/{tax.Id}");

        // Unknown and foreign addresses.
        await client.GetAsync($"/api/vehicles/{Guid.NewGuid()}/taxes");

        var entries = provider.Entries.ToArray();
        Assert.Contains(entries, entry => entry.Contains("capture check", StringComparison.Ordinal));
        foreach (var marker in Markers)
        {
            Assert.DoesNotContain(entries, entry => entry.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }
    }
}
