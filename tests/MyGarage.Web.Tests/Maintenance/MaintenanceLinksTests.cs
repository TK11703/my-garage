using MyGarage.Web.Maintenance;

namespace MyGarage.Web.Tests.Maintenance;

public class MaintenanceLinksTests
{
    private static readonly Guid Vehicle = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void TheDefaultSectionAndPageOneAreNotWritten() =>
        Assert.Equal($"/vehicles/{Vehicle}?tab=service-records", MaintenanceLinks.TabHref(Vehicle, MaintenanceSection.Records));

    [Theory]
    [InlineData(MaintenanceSection.Fuel, "&section=fuel")]
    [InlineData(MaintenanceSection.Compliance, "&section=compliance")]
    public void OtherSectionsAreAdded(MaintenanceSection section, string suffix) =>
        Assert.Equal($"/vehicles/{Vehicle}?tab=service-records{suffix}", MaintenanceLinks.TabHref(Vehicle, section));

    [Theory]
    [InlineData(MaintenanceSection.Records, MaintenanceList.Records, "&recordsPage=3")]
    [InlineData(MaintenanceSection.Fuel, MaintenanceList.Fuel, "&section=fuel&fuelPage=3")]
    [InlineData(MaintenanceSection.Compliance, MaintenanceList.Inspections, "&section=compliance&inspectionsPage=3")]
    [InlineData(MaintenanceSection.Compliance, MaintenanceList.Registrations, "&section=compliance&registrationsPage=3")]
    [InlineData(MaintenanceSection.Compliance, MaintenanceList.Insurance, "&section=compliance&insurancePage=3")]
    [InlineData(MaintenanceSection.Compliance, MaintenanceList.Taxes, "&section=compliance&taxesPage=3")]
    public void APageAboveOneAddsItsOwnParameter(MaintenanceSection section, MaintenanceList list, string suffix) =>
        Assert.Equal($"/vehicles/{Vehicle}?tab=service-records{suffix}", MaintenanceLinks.TabHref(Vehicle, section, list, 3));

    [Fact]
    public void PageOneAddsNoParameter() =>
        Assert.Equal($"/vehicles/{Vehicle}?tab=service-records&section=fuel", MaintenanceLinks.TabHref(Vehicle, MaintenanceSection.Fuel, MaintenanceList.Fuel, 1));

    [Theory]
    [InlineData("fuel", MaintenanceSection.Fuel)]
    [InlineData("compliance", MaintenanceSection.Compliance)]
    [InlineData("records", MaintenanceSection.Records)]
    [InlineData(null, MaintenanceSection.Records)]
    [InlineData("", MaintenanceSection.Records)]
    [InlineData("bogus", MaintenanceSection.Records)]
    public void ParseSection_FallsBackToTheDefault(string? value, MaintenanceSection expected) =>
        Assert.Equal(expected, MaintenanceLinks.ParseSection(value));

    [Theory]
    [InlineData("3", 3)]
    [InlineData(null, 1)]
    [InlineData("abc", 1)]
    [InlineData("0", 1)]
    [InlineData("-2", 1)]
    public void ParsePage_FallsBackToPageOne(string? value, int expected) =>
        Assert.Equal(expected, MaintenanceLinks.ParsePage(value));
}
