using MyGarage.Api.Vehicles;
using MyGarage.Api.Vehicles.StickerImport;
using MyGarage.Shared.Vehicles;

namespace MyGarage.Api.Tests.Vehicles.StickerImport;

public class StickerFeatureRequestValidatorTests
{
    private readonly StickerFeatureRequestValidator validator = new();
    private readonly VehicleRequestValidator vehicleValidator = new(TimeProvider.System);

    private static StickerFeatureRequest Feature(string? name, params Guid[] replaces) => new() { Name = name, ReplacesTagIds = replaces };

    private static VehicleRequest Vehicle(IReadOnlyList<StickerFeatureRequest>? features) =>
        new() { Make = "Ford", Model = "F-150", Year = 2020, StickerFeatures = features };

    [Theory]
    [InlineData("A")]
    [InlineData("Heated Seats")]
    public void ANameOf1To30Characters_IsValid(string name)
    {
        Assert.True(validator.Validate(Feature(name)).IsValid);
        Assert.True(validator.Validate(Feature(new string('a', 30))).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("tab\there")]
    [InlineData("line\nbreak")]
    public void AnEmptyOrControlCharacterName_IsRejected(string? name)
    {
        Assert.False(validator.Validate(Feature(name)).IsValid);
    }

    [Fact]
    public void A31CharacterName_IsRejected()
    {
        Assert.False(validator.Validate(Feature(new string('a', 31))).IsValid);
    }

    [Fact]
    public void MoreThanTheLimitOfReplacedTags_AreRejected()
    {
        var limit = MyGarage.Database.Tags.VehicleTagRepository.MaxTagsPerVehicle;
        Assert.True(validator.Validate(Feature("A", [.. Enumerable.Range(0, limit).Select(_ => Guid.NewGuid())])).IsValid);
        Assert.False(validator.Validate(Feature("A", [.. Enumerable.Range(0, limit + 1).Select(_ => Guid.NewGuid())])).IsValid);
    }

    [Fact]
    public void NullAndEmptyStickerFeatures_AreValid()
    {
        Assert.True(vehicleValidator.Validate(Vehicle(null)).IsValid);
        Assert.True(vehicleValidator.Validate(Vehicle([])).IsValid);
    }

    [Fact]
    public void ExactlyTheLimitOfFeatures_AreValid_AndOneMoreIsRejected()
    {
        var limit = MyGarage.Database.Tags.VehicleTagRepository.MaxTagsPerVehicle;
        Assert.True(vehicleValidator.Validate(Vehicle([.. Enumerable.Range(1, limit).Select(i => Feature($"feature {i}"))])).IsValid);

        var result = vehicleValidator.Validate(Vehicle([.. Enumerable.Range(1, limit + 1).Select(i => Feature($"feature {i}"))]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "StickerFeatures");
    }

    [Fact]
    public void TheSharedAndTheDatabaseLimitsAreTheSame()
    {
        Assert.Equal(MyGarage.Database.Tags.VehicleTagRepository.MaxTagsPerVehicle, MyGarage.Shared.Vehicles.StickerImport.StickerImportLimits.MaxFeaturesPerVehicle);
        Assert.Equal(100, MyGarage.Database.Tags.VehicleTagRepository.MaxTagsPerVehicle);
    }

    [Fact]
    public void DuplicateNamesIgnoringCase_AreRejected()
    {
        var result = vehicleValidator.Validate(Vehicle([Feature("Heated Seats"), Feature("heated seats")]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "StickerFeatures");
    }

    [Fact]
    public void ErrorsCarryTheIndexedPropertyPath()
    {
        var result = vehicleValidator.Validate(Vehicle([Feature("ok"), Feature(new string('a', 31))]));

        Assert.Contains(result.Errors, error => error.PropertyName == "StickerFeatures[1].Name");
    }
}
