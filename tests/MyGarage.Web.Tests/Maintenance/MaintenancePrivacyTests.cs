using System.Net;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyGarage.Shared.FuelEnergyEntries;
using MyGarage.Shared.Inspections;
using MyGarage.Shared.Insurance;
using MyGarage.Shared.Registrations;
using MyGarage.Shared.Taxes;
using MyGarage.Web.Maintenance;
using MyGarage.Web.Tests.Common;
using MyGarage.Web.Tests.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Maintenance;

// FR-012: every page of this feature asks a signed-out visitor to sign in and returns them to the page.
public class MaintenanceSignedOutTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly Guid VehicleId = Guid.NewGuid();
    private static readonly Guid EntryId = Guid.NewGuid();

    public static TheoryData<string> Routes()
    {
        var data = new TheoryData<string>();
        foreach (var segment in new[] { "fuel-energy-entries", "inspections", "registrations", "insurance", "taxes" })
        {
            data.Add($"/vehicles/{VehicleId}/{segment}/new");
            data.Add($"/vehicles/{VehicleId}/{segment}/{EntryId}/edit");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task ASignedOutVisitor_IsChallengedAndReturnedToThePage(string route)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        while (factory.RedirectUris.TryDequeue(out _))
        {
        }

        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(WebAppFactory.FakeAuthority, response.Headers.Location!.ToString());
        string? last = null;
        while (factory.RedirectUris.TryDequeue(out var next))
        {
            last = next;
        }

        Assert.Equal(route, last);
    }
}

// FR-017: entry values are posted in form bodies and never travel in a page address.
public class MaintenancePrivacyTests : VehicleTestContext
{
    private readonly Guid vehicleId = Guid.NewGuid();

    private static readonly string[] Secrets = ["8765.4321", "4321.987", "7654.32", "6543.21", "5432.19", "4321.09", "3210.98"];

    private static readonly DateTimeOffset At = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    private static IEnumerable<string?> Addresses(IEnumerable<IElement> elements) =>
        elements.SelectMany(element => new[] { element.GetAttribute("href"), element.GetAttribute("action"), element.GetAttribute("src") });

    private void AssertNoSecretInAddresses(IRenderedComponent<IComponent> cut)
    {
        foreach (var secret in Secrets)
        {
            Assert.DoesNotContain(Addresses(cut.FindAll("[href], [action], [src]")), address => address?.Contains(secret, StringComparison.OrdinalIgnoreCase) == true);
            Assert.DoesNotContain(secret, CurrentUri, StringComparison.OrdinalIgnoreCase);
            Assert.All(Requests, request => Assert.DoesNotContain(secret, request.RequestUri!.ToString(), StringComparison.OrdinalIgnoreCase));
        }
    }

    private static PagedResultDtoOf<T> Pages<T>(T item) => new([item]);

    private sealed record PagedResultDtoOf<T>(IReadOnlyList<T> Items)
    {
        public int Page => 1;

        public int PageSize => 10;

        public int TotalCount => Items.Count;

        public int TotalPages => 1;
    }

    [Fact]
    public void TheListsOnTheMaintenanceTab_NeverPutEntryValuesInALinkOrFormAddress()
    {
        MaintenanceLists["fuel-energy-entries"] = Pages(new FuelEnergyEntryDto
        {
            Id = Guid.NewGuid(), VehicleId = vehicleId, Kind = FuelEnergyKind.Fuel, EntryDate = new DateOnly(2026, 5, 1),
            UnitPrice = 8765.4321m, Quantity = 4321.987m, TotalCost = 7654.32m, CreatedAt = At, UpdatedAt = At,
        });
        MaintenanceLists["inspections"] = Pages(new InspectionDto
        {
            Id = Guid.NewGuid(), VehicleId = vehicleId, Type = InspectionType.Safety, InspectionDate = new DateOnly(2026, 5, 1),
            Cost = 6543.21m, NextDueDate = new DateOnly(2027, 5, 1), CreatedAt = At, UpdatedAt = At,
        });
        MaintenanceLists["registrations"] = Pages(new RegistrationDto
        {
            Id = Guid.NewGuid(), VehicleId = vehicleId, State = "WA", Cost = 5432.19m, ExpirationDate = new DateOnly(2027, 5, 1), CreatedAt = At, UpdatedAt = At,
        });
        MaintenanceLists["insurance"] = Pages(new InsurancePolicyDto
        {
            Id = Guid.NewGuid(), VehicleId = vehicleId, StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2027, 1, 1),
            Duration = InsuranceDuration.Annual, Cost = 4321.09m, CreatedAt = At, UpdatedAt = At,
        });
        MaintenanceLists["taxes"] = Pages(new TaxDto
        {
            Id = Guid.NewGuid(), VehicleId = vehicleId, Type = TaxType.PersonalProperty, Amount = 3210.98m, Recurrence = TaxRecurrence.Annual, CreatedAt = At, UpdatedAt = At,
        });
        RegisterApi(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var cut = Render<MaintenanceView>(parameters => parameters.Add(view => view.VehicleId, vehicleId).Add(view => view.Section, MaintenanceSection.Compliance));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("$3,210.98", cut.Markup);
            Assert.Contains("$7,654.32", cut.Markup);
        });
        AssertNoSecretInAddresses(cut);
    }

    [Fact]
    public void CreatingAnEntry_RedirectsToAnAddressWithoutTheEnteredValues()
    {
        RegisterApi(request => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == $"/api/vehicles/{vehicleId}"
            ? Json(Detail(vehicleId))
            : Json(
                new FuelEnergyEntryDto
                {
                    Id = Guid.NewGuid(), VehicleId = vehicleId, Kind = FuelEnergyKind.Fuel, EntryDate = new DateOnly(2026, 5, 1),
                    UnitPrice = 8765.4321m, Quantity = 4321.987m, TotalCost = 7654.32m, CreatedAt = At, UpdatedAt = At,
                },
                HttpStatusCode.Created));
        var cut = Render<MyGarage.Web.FuelEnergyEntries.FuelEnergyEntryCreate>(parameters => parameters.Add(page => page.VehicleId, vehicleId));
        cut.WaitForElement("input#unit-price").Change("8765.4321");
        cut.Find("input#quantity").Change("4321.987");
        cut.Find("input#total-cost").Change("7654.32");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.EndsWith($"/vehicles/{vehicleId}?tab=service-records&section=fuel", CurrentUri));
        Assert.NotEqual("get", cut.Find("form").GetAttribute("method")?.ToLowerInvariant());
        AssertNoSecretInAddresses(cut);
    }
}
