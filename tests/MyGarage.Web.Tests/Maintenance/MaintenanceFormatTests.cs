using MyGarage.Web.Maintenance;

namespace MyGarage.Web.Tests.Maintenance;

public class MaintenanceFormatTests
{
    [Theory]
    [InlineData(0, "0 inspections")]
    [InlineData(1, "1 inspection")]
    [InlineData(2, "2 inspections")]
    public void Inspections(int count, string expected) => Assert.Equal(expected, MaintenanceFormat.Inspections(count));

    [Theory]
    [InlineData(0, "0 registrations")]
    [InlineData(1, "1 registration")]
    [InlineData(5, "5 registrations")]
    public void Registrations(int count, string expected) => Assert.Equal(expected, MaintenanceFormat.Registrations(count));

    [Theory]
    [InlineData(0, "0 insurance")]
    [InlineData(1, "1 insurance")]
    [InlineData(3, "3 insurance")]
    public void Insurance(int count, string expected) => Assert.Equal(expected, MaintenanceFormat.Insurance(count));

    [Theory]
    [InlineData(0, "0 taxes")]
    [InlineData(1, "1 tax")]
    [InlineData(4, "4 taxes")]
    public void Taxes(int count, string expected) => Assert.Equal(expected, MaintenanceFormat.Taxes(count));
}
