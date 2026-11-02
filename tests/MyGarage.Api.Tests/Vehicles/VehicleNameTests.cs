using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles;

public class VehicleNameTests
{
    [Theory]
    [InlineData(2020, "Ford", "F-150", "2020 Ford F-150")]
    [InlineData(1999, "Mazda", "MX-5", "1999 Mazda MX-5")]
    [InlineData(null, "Ford", "F-150", "Ford F-150")]
    public void Compose_IsYearMakeModelWithTheYearLeftOutWhenNull(int? year, string make, string model, string expected) =>
        Assert.Equal(expected, VehicleName.Compose(year, make, model));
}
