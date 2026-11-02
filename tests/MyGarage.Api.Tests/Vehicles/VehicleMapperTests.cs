using MyGarage.Api.Vehicles;
using MyGarage.Database.Common;
using MyGarage.Database.Vehicles;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles;

public class VehicleMapperTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ToRecord_TrimsValuesAndStampsOwnerAndTimes()
    {
        var id = Guid.NewGuid();
        var request = new VehicleRequest
        {
            Nickname = "  Daily  ",
            Make = " Ford ",
            Model = " F-150 ",
            Trim = " XLT ",
            Color = "  ",
            Mileage = 1234,
            Doors = 4,
        };

        var record = VehicleMapper.ToRecord(request, "owner-1", id, Now);

        Assert.Equal(id, record.Id);
        Assert.Equal("owner-1", record.OwnerId);
        Assert.Equal("Daily", record.Nickname);
        Assert.Equal("Ford", record.Make);
        Assert.Equal("F-150", record.Model);
        Assert.Equal("XLT", record.Trim);
        Assert.Null(record.Color);
        Assert.Equal(1234, record.Mileage);
        Assert.Equal(4, record.Doors);
        Assert.Equal(Now, record.CreatedAt);
        Assert.Equal(Now, record.UpdatedAt);
        Assert.Equal("owner-1", record.CreatedBy);
        Assert.Equal("owner-1", record.UpdatedBy);
    }

    [Fact]
    public void ToDetailDto_MapsEveryFieldWithEmptyChildCollections()
    {
        var record = new VehicleRecord
        {
            Id = Guid.NewGuid(),
            OwnerId = "owner-1",
            Nickname = "Daily",
            Make = "Ford",
            Model = "F-150",
            Trim = "XLT",
            Color = "Blue",
            Mileage = 10,
            Doors = 2,
            CreatedAt = Now,
            UpdatedAt = Now.AddHours(1),
            CreatedBy = "owner-1",
            UpdatedBy = "owner-1",
        };

        var dto = VehicleMapper.ToDetailDto(record, [], []);

        Assert.Equal(record.Id, dto.Id);
        Assert.Equal("Daily", dto.Nickname);
        Assert.Equal("Ford", dto.Make);
        Assert.Equal("F-150", dto.Model);
        Assert.Equal("XLT", dto.Trim);
        Assert.Equal("Blue", dto.Color);
        Assert.Equal(10, dto.Mileage);
        Assert.Equal(2, dto.Doors);
        Assert.Equal(Now, dto.CreatedAt);
        Assert.Equal(Now.AddHours(1), dto.UpdatedAt);
        Assert.Empty(dto.Tags);
        Assert.Empty(dto.Images);
    }

    [Fact]
    public void ToRecord_MapsEveryNewFieldNormalizingText()
    {
        var typeId = Guid.NewGuid();
        var fuelId = Guid.NewGuid();
        var gearboxId = Guid.NewGuid();
        var driveId = Guid.NewGuid();
        var request = new VehicleRequest
        {
            Nickname = "n",
            Make = "m",
            Model = "x",
            Year = 2019,
            Vin = " 1hgcm82633a004352 ",
            VehicleTypeId = typeId,
            Ownership = OwnershipType.Leased,
            PurchasedOn = new DateOnly(2024, 3, 15),
            LeaseEndsOn = new DateOnly(2027, 3, 14),
            Engine = " 2.0L turbo ",
            Horsepower = 250,
            FuelTypeId = fuelId,
            TransmissionId = gearboxId,
            Seats = 5,
            DrivetrainId = driveId,
            WheelSize = 17.5m,
            TireSize = " 225/45R17 ",
        };

        var record = VehicleMapper.ToRecord(request, "owner-1", Guid.NewGuid(), Now);

        Assert.Equal(2019, record.Year);
        Assert.Equal("1HGCM82633A004352", record.Vin);
        Assert.Equal(typeId, record.VehicleTypeId);
        Assert.Equal("leased", record.Ownership);
        Assert.Equal(new DateOnly(2024, 3, 15), record.PurchasedOn);
        Assert.Equal(new DateOnly(2027, 3, 14), record.LeaseEndsOn);
        Assert.Equal("2.0L turbo", record.Engine);
        Assert.Equal(250, record.Horsepower);
        Assert.Equal(fuelId, record.FuelTypeId);
        Assert.Equal(gearboxId, record.TransmissionId);
        Assert.Equal(5, record.Seats);
        Assert.Equal(driveId, record.DrivetrainId);
        Assert.Equal(17.5m, record.WheelSize);
        Assert.Equal("225/45R17", record.TireSize);
    }

    [Fact]
    public void ToRecord_BlankOptionalTextBecomesNullAndPurchasedMapsToPurchased()
    {
        var record = VehicleMapper.ToRecord(
            new VehicleRequest { Nickname = "n", Make = "m", Model = "x", Year = 2019, Vin = "  ", Engine = "", TireSize = " ", Ownership = OwnershipType.Purchased },
            "owner-1",
            Guid.NewGuid(),
            Now);

        Assert.Null(record.Vin);
        Assert.Null(record.Engine);
        Assert.Null(record.TireSize);
        Assert.Equal("purchased", record.Ownership);
    }

    [Fact]
    public void ToDetailDto_MapsNewFieldsAndLookupValues()
    {
        var typeId = Guid.NewGuid();
        var record = new VehicleRecord
        {
            Id = Guid.NewGuid(),
            OwnerId = "owner-1",
            Nickname = "n",
            Make = "m",
            Model = "x",
            Year = 2019,
            Vin = "1HGCM82633A004352",
            VehicleTypeId = typeId,
            VehicleTypeName = "Wagon",
            Ownership = "leased",
            PurchasedOn = new DateOnly(2024, 3, 15),
            LeaseEndsOn = new DateOnly(2027, 3, 14),
            Engine = "2.0L",
            Horsepower = 250,
            FuelTypeId = Guid.NewGuid(),
            FuelTypeName = "Hybrid",
            Seats = 5,
            WheelSize = 17.5m,
            TireSize = "225/45R17",
            CreatedAt = Now,
            UpdatedAt = Now,
            CreatedBy = "owner-1",
            UpdatedBy = "owner-1",
        };

        var dto = VehicleMapper.ToDetailDto(record, [], []);

        Assert.Equal(2019, dto.Year);
        Assert.Equal("1HGCM82633A004352", dto.Vin);
        Assert.Equal(typeId, dto.VehicleType!.Id);
        Assert.Equal("Wagon", dto.VehicleType.Name);
        Assert.Equal(OwnershipType.Leased, dto.Ownership);
        Assert.Equal(new DateOnly(2024, 3, 15), dto.PurchasedOn);
        Assert.Equal(new DateOnly(2027, 3, 14), dto.LeaseEndsOn);
        Assert.Equal("2.0L", dto.Engine);
        Assert.Equal(250, dto.Horsepower);
        Assert.Equal("Hybrid", dto.FuelType!.Name);
        Assert.Null(dto.Transmission);
        Assert.Null(dto.Drivetrain);
        Assert.Equal(5, dto.Seats);
        Assert.Equal(17.5m, dto.WheelSize);
        Assert.Equal("225/45R17", dto.TireSize);
    }

    [Fact]
    public void ToPageDto_MapsSummariesAndPaging()
    {
        var imageId = Guid.NewGuid();
        var summary = new VehicleSummaryRecord { Id = Guid.NewGuid(), Nickname = "A", Make = "B", Model = "C", Mileage = 5, MainImageId = imageId };
        var page = new PagedResult<VehicleSummaryRecord>([summary], 2, 12, 13);

        var dto = VehicleMapper.ToPageDto(page);

        Assert.Equal(2, dto.Page);
        Assert.Equal(12, dto.PageSize);
        Assert.Equal(13, dto.TotalCount);
        Assert.Equal(2, dto.TotalPages);
        var item = Assert.Single(dto.Items);
        Assert.Equal(summary.Id, item.Id);
        Assert.Equal("A", item.Nickname);
        Assert.Equal("B", item.Make);
        Assert.Equal("C", item.Model);
        Assert.Equal(5, item.Mileage);
        Assert.Equal(imageId, item.MainImageId);
    }

    [Fact]
    public void Dtos_HaveNoOwnerOrUserIdentifier()
    {
        var propertyNames = new[] { typeof(VehicleSummaryDto), typeof(VehicleDetailDto), typeof(VehicleRequest) }
            .SelectMany(type => type.GetProperties())
            .Select(property => property.Name);

        Assert.DoesNotContain(propertyNames, name => (name.Contains("Owner", StringComparison.OrdinalIgnoreCase) && name != nameof(VehicleRequest.Ownership))
                                                    || name.Contains("UserId", StringComparison.OrdinalIgnoreCase));
    }
}
