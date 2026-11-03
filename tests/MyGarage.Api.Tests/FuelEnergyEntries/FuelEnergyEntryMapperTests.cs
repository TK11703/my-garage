using MyGarage.Api.FuelEnergyEntries;
using MyGarage.Database.Common;
using MyGarage.Database.FuelEnergyEntries;
using MyGarage.Shared.FuelEnergyEntries;

namespace MyGarage.Api.Tests.FuelEnergyEntries;

public class FuelEnergyEntryMapperTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static FuelEnergyEntryRecord Record(string kind = "fuel") =>
        new()
        {
            Id = Guid.NewGuid(),
            VehicleId = Guid.NewGuid(),
            Kind = kind,
            EntryDate = new DateOnly(2026, 5, 1),
            UnitPrice = 3.459m,
            Quantity = 12.345m,
            TotalCost = 42.70m,
            CreatedAt = At,
            UpdatedAt = At,
            CreatedBy = "owner-1",
            UpdatedBy = "owner-1",
        };

    [Theory]
    [InlineData(FuelEnergyKind.Fuel, "fuel")]
    [InlineData(FuelEnergyKind.Energy, "energy")]
    public void Kind_ConvertsBothWays(FuelEnergyKind kind, string stored)
    {
        Assert.Equal(stored, FuelEnergyEntryMapper.ToStoredKind(kind));
        Assert.Equal(kind, FuelEnergyEntryMapper.FromStoredKind(stored));
    }

    [Fact]
    public void UnknownKinds_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FuelEnergyEntryMapper.ToStoredKind((FuelEnergyKind)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => FuelEnergyEntryMapper.FromStoredKind("diesel"));
    }

    [Fact]
    public void Record_MapsToTheDtoWithoutOwnerInformation()
    {
        var record = Record("energy");

        var dto = FuelEnergyEntryMapper.ToDto(record);

        Assert.Equal(record.Id, dto.Id);
        Assert.Equal(record.VehicleId, dto.VehicleId);
        Assert.Equal(FuelEnergyKind.Energy, dto.Kind);
        Assert.Equal(new DateOnly(2026, 5, 1), dto.EntryDate);
        Assert.Equal(3.459m, dto.UnitPrice);
        Assert.Equal(12.345m, dto.Quantity);
        Assert.Equal(42.70m, dto.TotalCost);
        Assert.Equal(At, dto.CreatedAt);
        Assert.Equal(At, dto.UpdatedAt);
        Assert.DoesNotContain(typeof(FuelEnergyEntryDto).GetProperties(), property => property.Name.Contains("By", StringComparison.Ordinal) || property.Name.Contains("Owner", StringComparison.Ordinal));
    }

    [Fact]
    public void Page_MapsItemsAndCounts()
    {
        var page = new PagedResult<FuelEnergyEntryRecord>([Record(), Record("energy")], 2, 10, 12);

        var dto = FuelEnergyEntryMapper.ToPageDto(page);

        Assert.Equal(2, dto.Items.Count);
        Assert.Equal(2, dto.Page);
        Assert.Equal(10, dto.PageSize);
        Assert.Equal(12, dto.TotalCount);
        Assert.Equal(2, dto.TotalPages);
    }
}
