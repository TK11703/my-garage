using System.Text.Json;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Tests.Vehicles;

public class VehicleFormModelTests
{
    private static readonly DateOnly LeaseEnd = new(2027, 3, 14);

    [Theory]
    [InlineData(null)]
    [InlineData(OwnershipType.Purchased)]
    public void ToRequest_DropsTheLeaseEndUnlessTheVehicleIsLeased(OwnershipType? ownership)
    {
        var model = new VehicleFormModel { Make = "Ford", Model = "F-150", Year = 2020, Ownership = ownership, LeaseEndsOn = LeaseEnd };

        Assert.Null(model.ToRequest().LeaseEndsOn);
    }

    [Fact]
    public void ToRequest_KeepsTheLeaseEndForALeasedVehicle()
    {
        var model = new VehicleFormModel { Make = "Ford", Model = "F-150", Year = 2020, Ownership = OwnershipType.Leased, LeaseEndsOn = LeaseEnd };

        var request = model.ToRequest();

        Assert.Equal(LeaseEnd, request.LeaseEndsOn);
        Assert.Equal(OwnershipType.Leased, request.Ownership);
    }

    [Fact]
    public void ToRequest_KeepsThePurchaseDateWhateverTheOwnership()
    {
        var model = new VehicleFormModel { Ownership = OwnershipType.Purchased, PurchasedOn = new DateOnly(2024, 3, 15) };

        Assert.Equal(new DateOnly(2024, 3, 15), model.ToRequest().PurchasedOn);
    }

    [Fact]
    public void FromDetail_KeepsAStoredLeaseEndSoALeasedVehicleRoundTrips()
    {
        var detail = new VehicleDetailDto
        {
            Id = Guid.NewGuid(), Make = "Subaru", Model = "Outback", Year = 2019, Ownership = OwnershipType.Leased, LeaseEndsOn = LeaseEnd,
            Tags = [], Images = [], CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };

        var request = VehicleFormModel.From(detail).ToRequest();

        Assert.Equal(LeaseEnd, request.LeaseEndsOn);
    }

    [Fact]
    public void TheRequestCarriesNoNickname()
    {
        var json = JsonSerializer.Serialize(new VehicleFormModel { Make = "Ford", Model = "F-150" }.ToRequest());

        Assert.DoesNotContain("nickname", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BasicFieldsAreInFormOrder() =>
        Assert.Equal(
            ["Year", "Make", "Model", "Trim", "Vin", "VehicleTypeId", "Ownership", "PurchasedOn", "LeaseEndsOn", "Mileage", "LatestOdometerReading"],
            VehicleFormFields.BasicFields);

    [Fact]
    public void SpecificationFieldsAreInFormOrder() =>
        Assert.Equal(
            ["Engine", "Horsepower", "DrivetrainId", "Color", "FuelTypeId", "TransmissionId", "Seats", "Doors", "WheelSize", "TireSize"],
            VehicleFormFields.SpecificationFields);
}

public class VehicleNameTests
{
    [Fact]
    public void SummaryAndDetail_ComposeYearMakeAndModel()
    {
        var summary = new VehicleSummaryDto { Id = Guid.NewGuid(), Year = 2020, Make = "Ford", Model = "F-150" };
        var detail = new VehicleDetailDto
        {
            Id = Guid.NewGuid(), Year = 2020, Make = "Ford", Model = "F-150", Tags = [], Images = [],
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };

        Assert.Equal("2020 Ford F-150", summary.Name);
        Assert.Equal("2020 Ford F-150", detail.Name);
    }

    [Fact]
    public void MissingYear_IsLeftOut()
    {
        var summary = new VehicleSummaryDto { Id = Guid.NewGuid(), Make = "Ford", Model = "F-150" };

        Assert.Equal("Ford F-150", summary.Name);
    }

    [Fact]
    public void Name_IsNotSerialized()
    {
        var summary = new VehicleSummaryDto { Id = Guid.NewGuid(), Year = 2020, Make = "Ford", Model = "F-150" };

        var json = JsonSerializer.Serialize(summary);

        Assert.DoesNotContain("Name", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"name\"", json, StringComparison.OrdinalIgnoreCase);
    }
}
