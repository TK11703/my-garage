using MyGarage.Api.Lookups;
using MyGarage.Database.Lookups;
using MyGarage.Shared.Lookups;

namespace MyGarage.Api.Tests.Lookups;

public class LookupMapperTests
{
    private static LookupItemRecord Item(string kind, string name, bool isDefault = true, bool isHidden = false) =>
        new() { Id = Guid.NewGuid(), Kind = kind, Name = name, IsDefault = isDefault, IsHidden = isHidden };

    [Fact]
    public void ToDto_MapsEveryField()
    {
        var record = Item(LookupKind.FuelType, "Hybrid", isDefault: false, isHidden: true);

        var dto = LookupMapper.ToDto(record);

        Assert.Equal(record.Id, dto.Id);
        Assert.Equal("Hybrid", dto.Name);
        Assert.False(dto.IsDefault);
        Assert.True(dto.IsHidden);
    }

    [Fact]
    public void ToLookupsDto_GroupsByKindKeepingTheGivenOrder()
    {
        var records = new[]
        {
            Item(LookupKind.VehicleType, "Sedan"),
            Item(LookupKind.FuelType, "Gasoline"),
            Item(LookupKind.VehicleType, "SUV"),
            Item(LookupKind.Drivetrain, "FWD"),
            Item(LookupKind.VehicleType, "Mine", isDefault: false),
        };

        var dto = LookupMapper.ToLookupsDto(records);

        Assert.Equal(["Sedan", "SUV", "Mine"], dto.VehicleTypes.Select(item => item.Name));
        Assert.Equal(["Gasoline"], dto.FuelTypes.Select(item => item.Name));
        Assert.Empty(dto.Transmissions);
        Assert.Equal(["FWD"], dto.Drivetrains.Select(item => item.Name));
    }

    [Theory]
    [InlineData(LookupKinds.VehicleTypes, LookupKind.VehicleType)]
    [InlineData(LookupKinds.FuelTypes, LookupKind.FuelType)]
    [InlineData(LookupKinds.Transmissions, LookupKind.Transmission)]
    [InlineData(LookupKinds.Drivetrains, LookupKind.Drivetrain)]
    public void ToStoredKind_MapsEverySlug(string slug, string expected) =>
        Assert.Equal(expected, LookupMapper.ToStoredKind(slug));

    [Theory]
    [InlineData("body-styles")]
    [InlineData("vehicle_type")]
    [InlineData("")]
    [InlineData(null)]
    public void ToStoredKind_UnknownSlug_ReturnsNull(string? slug) => Assert.Null(LookupMapper.ToStoredKind(slug));

    [Fact]
    public void Dtos_CarryNoOwnerOrUserId()
    {
        var names = typeof(LookupItemDto).GetProperties().Select(property => property.Name)
            .Concat(typeof(LookupsDto).GetProperties().Select(property => property.Name));

        Assert.DoesNotContain(names, name => name.Contains("Owner", StringComparison.OrdinalIgnoreCase) || name.Contains("User", StringComparison.OrdinalIgnoreCase));
    }
}
