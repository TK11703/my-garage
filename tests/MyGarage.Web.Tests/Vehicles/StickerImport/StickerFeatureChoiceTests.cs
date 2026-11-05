using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using MyGarage.Shared.Vehicles;
using MyGarage.Shared.Vehicles.StickerImport;
using MyGarage.Web.Vehicles;
using MyGarage.Web.Vehicles.StickerImport;

namespace MyGarage.Web.Tests.Vehicles.StickerImport;

public class StickerFeatureChoiceTests
{
    private static readonly Guid FuelId = Guid.NewGuid();
    private static readonly Guid TagId = Guid.NewGuid();

    private static StickerImportResultDto Result() =>
        new()
        {
            Outcome = StickerImportOutcome.Filled,
            Year = 2024,
            Make = "Ford",
            Model = "F-150",
            Trim = "Lariat",
            Vin = "1FTFW1E50NFA00001",
            FuelTypeId = FuelId,
            Engine = "3.5L V6",
            Horsepower = 400,
            Color = "Agate Black",
            Seats = 5,
            Doors = 4,
            WheelSize = 20m,
            TireSize = "275/55R20",
            Features = [new StickerFeatureDto { Name = "Panoramic Sunroof", ReplacesTagIds = [TagId], ReplacesNames = ["Sunroof"] }, new StickerFeatureDto { Name = "Heated Seats" }],
        };

    [Fact]
    public void ApplyImport_PutsTheStickersValuesOverTheCurrentOnes()
    {
        var model = new VehicleFormModel { Make = "Old", Year = 1999, Mileage = 12_000, Ownership = OwnershipType.Leased, Color = "Red" };

        model.ApplyImport(Result());

        Assert.Equal("Ford", model.Make);
        Assert.Equal("F-150", model.Model);
        Assert.Equal(2024, model.Year);
        Assert.Equal("Lariat", model.Trim);
        Assert.Equal("1FTFW1E50NFA00001", model.Vin);
        Assert.Equal(FuelId, model.FuelTypeId);
        Assert.Equal("3.5L V6", model.Engine);
        Assert.Equal(400, model.Horsepower);
        Assert.Equal("Agate Black", model.Color);
        Assert.Equal(5, model.Seats);
        Assert.Equal(4, model.Doors);
        Assert.Equal(20m, model.WheelSize);
        Assert.Equal("275/55R20", model.TireSize);
    }

    [Fact]
    public void ApplyImport_KeepsEveryValueTheStickerDidNotProvide()
    {
        var typeId = Guid.NewGuid();
        var model = new VehicleFormModel { VehicleTypeId = typeId, Mileage = 12_000, Ownership = OwnershipType.Leased, Trim = "Base", Engine = "Old engine" };

        model.ApplyImport(new StickerImportResultDto { Outcome = StickerImportOutcome.Partial, Make = "Ford" });

        Assert.Equal("Ford", model.Make);
        Assert.Equal(typeId, model.VehicleTypeId);
        Assert.Equal(12_000, model.Mileage);
        Assert.Equal(OwnershipType.Leased, model.Ownership);
        Assert.Equal("Base", model.Trim);
        Assert.Equal("Old engine", model.Engine);
    }

    [Fact]
    public void ApplyImport_ProposesTheFeaturesWithWhatTheyReplace_AllSelected()
    {
        var model = new VehicleFormModel();

        model.ApplyImport(Result());

        Assert.Equal(["Panoramic Sunroof", "Heated Seats"], model.StickerFeatures.Select(row => row.Name));
        Assert.All(model.StickerFeatures, row => Assert.True(row.Include));
        Assert.Equal([TagId], model.StickerFeatures[0].ReplacesTagIds);
        Assert.Equal(["Sunroof"], model.StickerFeatures[0].ReplacesNames);
        Assert.Empty(model.StickerFeatures[1].ReplacesTagIds);
    }

    [Fact]
    public void ASecondImport_ReplacesTheProposedFeaturesOfTheFirst()
    {
        var model = new VehicleFormModel();
        model.ApplyImport(Result());

        model.ApplyImport(new StickerImportResultDto { Outcome = StickerImportOutcome.Filled, Features = [new StickerFeatureDto { Name = "Tow Hooks" }] });

        Assert.Equal(["Tow Hooks"], model.StickerFeatures.Select(row => row.Name));
    }

    [Fact]
    public void StickerFeatures_IsNeverNull()
    {
        Assert.Empty(new VehicleFormModel().StickerFeatures);
    }

    [Fact]
    public void ToRequest_SendsOnlyTheSelectedRows()
    {
        var model = new VehicleFormModel { Make = "Ford", Model = "F-150", Year = 2024 };
        model.ApplyImport(Result());
        model.StickerFeatures[1].Include = false;

        var request = model.ToRequest();

        var feature = Assert.Single(request.StickerFeatures!);
        Assert.Equal("Panoramic Sunroof", feature.Name);
        Assert.Equal([TagId], feature.ReplacesTagIds);
    }

    [Fact]
    public void ToRequest_WithNoSelectedRows_SendsNull()
    {
        var model = new VehicleFormModel { Make = "Ford", Model = "F-150", Year = 2024 };
        Assert.Null(model.ToRequest().StickerFeatures);

        model.ApplyImport(Result());
        model.StickerFeatures.ForEach(row => row.Include = false);

        Assert.Null(model.ToRequest().StickerFeatures);
    }

    [Fact]
    public void ReadFrom_RebuildsTheRowsFromAPostedForm_AnUntickedRowHasNoIncludeField()
    {
        var other = Guid.NewGuid();
        var form = new FormCollection(new Dictionary<string, StringValues>
        {
            [StickerFeatureChoice.NameField(0)] = "Panoramic Sunroof",
            [StickerFeatureChoice.IncludeField(0)] = "true",
            [StickerFeatureChoice.ReplacesIdField(0)] = new StringValues([TagId.ToString(), other.ToString(), "not-a-guid"]),
            [StickerFeatureChoice.ReplacesNameField(0)] = new StringValues(["Sunroof", "Moonroof"]),
            [StickerFeatureChoice.NameField(1)] = "Heated Seats",
        });

        var rows = StickerFeatureChoice.ReadFrom(form);

        Assert.Equal(["Panoramic Sunroof", "Heated Seats"], rows.Select(row => row.Name));
        Assert.True(rows[0].Include);
        Assert.False(rows[1].Include);
        Assert.Equal([TagId, other], rows[0].ReplacesTagIds);
        Assert.Equal(["Sunroof", "Moonroof"], rows[0].ReplacesNames);
        Assert.Empty(rows[1].ReplacesTagIds);
    }

    [Fact]
    public void ReadFrom_StopsAtTheFirstMissingRow_AndCapsAtTheFeatureLimit()
    {
        var fields = new Dictionary<string, StringValues> { [StickerFeatureChoice.NameField(0)] = "A", [StickerFeatureChoice.NameField(2)] = "C" };
        Assert.Single(StickerFeatureChoice.ReadFrom(new FormCollection(fields)));

        var limit = StickerImportLimits.MaxFeaturesPerVehicle;
        var many = Enumerable.Range(0, limit + 10).ToDictionary(i => StickerFeatureChoice.NameField(i), i => new StringValues($"Feature {i}"));
        Assert.Equal(limit, StickerFeatureChoice.ReadFrom(new FormCollection(many)).Count);
    }

    [Fact]
    public void ReadFrom_AFormWithNoRows_GivesNone()
    {
        Assert.Empty(StickerFeatureChoice.ReadFrom(new FormCollection(new Dictionary<string, StringValues>())));
    }

    [Fact]
    public void FromTheDto_CopiesTheNameAndWhatItReplaces()
    {
        var row = StickerFeatureChoice.From(new StickerFeatureDto { Name = "X", ReplacesTagIds = [TagId], ReplacesNames = ["Y"] });

        Assert.Equal("X", row.Name);
        Assert.True(row.Include);
        Assert.Equal([TagId], row.ReplacesTagIds);
        Assert.Equal(["Y"], row.ReplacesNames);
    }
}
