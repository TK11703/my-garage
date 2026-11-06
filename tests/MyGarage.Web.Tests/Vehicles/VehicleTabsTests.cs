using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleTabsTests
{
    [Fact]
    public void TheAddFormTabsAreImportThenDetails()
    {
        var tabs = VehicleTabs.ForForm(withImport: true, withAttachments: false);

        Assert.Equal(["import", "details"], tabs.Select(tab => tab.Id));
        Assert.Equal("Import from sticker", tabs[0].Label);
    }

    [Fact]
    public void TheEditFormTabsAreImportDetailsMediaDescription()
    {
        var tabs = VehicleTabs.ForForm(withImport: true, withAttachments: true);

        Assert.Equal(["import", "details", "media", "description"], tabs.Select(tab => tab.Id));
    }

    [Fact]
    public void WithoutTheImportPane_TheImportTabIsNotListed()
    {
        Assert.Equal(["details", "media", "description"], VehicleTabs.ForForm(withImport: false, withAttachments: true).Select(tab => tab.Id));
        Assert.Equal(["details"], VehicleTabs.ForForm(withImport: false, withAttachments: false).Select(tab => tab.Id));
    }

    [Theory]
    [InlineData("import", "import")]
    [InlineData("details", "details")]
    [InlineData("media", "media")]
    [InlineData("description", "description")]
    [InlineData("service-records", "details")]
    [InlineData("reminders", "details")]
    [InlineData("zzz", "details")]
    [InlineData(null, "details")]
    public void NormalizeForEdit_KnowsTheImportTab_AndDefaultsToDetails(string? tab, string expected)
    {
        Assert.Equal(expected, VehicleTabs.NormalizeForEdit(tab));
    }

    [Fact]
    public void TheDetailPageTabsEndWithMaintenanceAndReminders()
    {
        Assert.Equal(["details", "media", "description", "service-records", "reminders"], VehicleTabs.All.Select(tab => tab.Id));
        Assert.Equal("details", VehicleTabs.Normalize("import"));
    }
}
