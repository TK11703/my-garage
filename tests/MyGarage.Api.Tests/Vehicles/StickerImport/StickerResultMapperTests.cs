using MyGarage.Api.Vehicles;
using MyGarage.Api.Vehicles.StickerImport;
using MyGarage.Database.Lookups;
using MyGarage.Database.Tags;
using MyGarage.Shared.Vehicles.StickerImport;

namespace MyGarage.Api.Tests.Vehicles.StickerImport;

public class StickerResultMapperTests
{
    private static readonly DateTimeOffset Today = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private readonly StickerResultMapper mapper = new(new VehicleRequestValidator(new FixedTimeProvider(Today)));

    private static readonly Guid SuvId = Guid.NewGuid();
    private static readonly Guid GasId = Guid.NewGuid();
    private static readonly Guid AutoId = Guid.NewGuid();
    private static readonly Guid FourWdId = Guid.NewGuid();

    private static readonly LookupItemRecord[] Lookups =
    [
        new() { Id = SuvId, Kind = LookupKind.VehicleType, Name = "SUV" },
        new() { Id = Guid.NewGuid(), Kind = LookupKind.VehicleType, Name = "Pickup Truck" },
        new() { Id = GasId, Kind = LookupKind.FuelType, Name = "Gasoline" },
        new() { Id = AutoId, Kind = LookupKind.Transmission, Name = "Automatic" },
        new() { Id = FourWdId, Kind = LookupKind.Drivetrain, Name = "4WD" },
    ];

    private static StickerExtraction Extraction(Func<StickerExtraction, StickerExtraction>? change = null)
    {
        var extraction = new StickerExtraction
        {
            IsWindowSticker = true,
            VehiclesDetected = 1,
            Year = 2024,
            Make = "Ford",
            Model = "F-150",
            Trim = "Lariat",
            Vin = "1FTFW1E50NFA00001",
            BodyStyle = "SUV",
            Engine = "3.5L V6",
            Horsepower = 400,
            FuelType = "Gasoline",
            Transmission = "Automatic",
            Drivetrain = "4WD",
            ExteriorColor = "Agate Black",
            Seats = 5,
            Doors = 4,
            WheelSizeInches = 20m,
            TireSize = "275/55R20",
        };
        return change is null ? extraction : change(extraction);
    }

    private static VehicleTagRecord Tag(string name) => new() { Id = Guid.NewGuid(), Name = name, CreatedAt = Today };

    private static ExtractedFeature Feature(string name, params int[] sameAs) => new(name, sameAs);

    private StickerImportResultDto Map(StickerExtraction extraction, IReadOnlyList<VehicleTagRecord>? tags = null, bool duplicateVin = false) =>
        mapper.Map(extraction, Lookups, tags ?? [], duplicateVin);

    [Fact]
    public void AFullSticker_FillsEveryFieldAndIsFilled()
    {
        var result = Map(Extraction());

        Assert.Equal(StickerImportOutcome.Filled, result.Outcome);
        Assert.Equal(2024, result.Year);
        Assert.Equal("Ford", result.Make);
        Assert.Equal("F-150", result.Model);
        Assert.Equal("Lariat", result.Trim);
        Assert.Equal("1FTFW1E50NFA00001", result.Vin);
        Assert.Equal("3.5L V6", result.Engine);
        Assert.Equal(400, result.Horsepower);
        Assert.Equal("Agate Black", result.Color);
        Assert.Equal(5, result.Seats);
        Assert.Equal(4, result.Doors);
        Assert.Equal(20m, result.WheelSize);
        Assert.Equal("275/55R20", result.TireSize);
        Assert.Equal(16, result.FieldsFilled);
        Assert.Empty(result.Notices);
    }

    [Theory]
    [InlineData("1HGCM82633A00435")]
    [InlineData("1HGCM82633A0043I5")]
    public void AnInvalidVin_IsDropped(string vin)
    {
        var result = Map(Extraction(e => e with { Vin = vin }));

        Assert.Null(result.Vin);
        Assert.Equal(StickerImportOutcome.Partial, result.Outcome);
        var notice = Assert.Single(result.Notices, notice => notice.Code == StickerNoticeCode.UnreadableValues);
        Assert.Contains("VIN", notice.Message);
        Assert.DoesNotContain(vin, notice.Message);
    }

    [Fact]
    public void ValuesThatFailTheVehicleRules_AreDroppedNeverReturned()
    {
        var result = Map(Extraction(e => e with { Year = 3000, Horsepower = 0, WheelSizeInches = 80m, Seats = 99, Doors = 0 }));

        Assert.Null(result.Year);
        Assert.Null(result.Horsepower);
        Assert.Null(result.WheelSize);
        Assert.Null(result.Seats);
        Assert.Null(result.Doors);
        Assert.Equal("Ford", result.Make);
        Assert.Equal(StickerImportOutcome.Partial, result.Outcome);
    }

    [Fact]
    public void AWheelSizeWithTwoDecimals_IsDropped()
    {
        Assert.Null(Map(Extraction(e => e with { WheelSizeInches = 17.25m })).WheelSize);
        Assert.Equal(17.5m, Map(Extraction(e => e with { WheelSizeInches = 17.5m })).WheelSize);
    }

    [Fact]
    public void ATooLongMake_IsDropped()
    {
        Assert.Null(Map(Extraction(e => e with { Make = new string('a', 51) })).Make);
    }

    [Fact]
    public void ALowerCaseVinWithSpaces_IsTrimmedAndUpperCased()
    {
        var result = Map(Extraction(e => e with { Vin = " 1ftfw1e50nfa00001 " }));

        Assert.Equal("1FTFW1E50NFA00001", result.Vin);
    }

    [Fact]
    public void AMissingYearMakeOrModel_IsNotAnError()
    {
        var result = Map(Extraction(e => e with { Year = null, Make = null, Model = "  " }));

        Assert.Null(result.Year);
        Assert.Null(result.Make);
        Assert.Null(result.Model);
        Assert.Equal(StickerImportOutcome.Filled, result.Outcome);
        Assert.DoesNotContain(result.Notices, notice => notice.Code == StickerNoticeCode.UnreadableValues);
    }

    [Fact]
    public void LookupTexts_MatchTheShownItemsIgnoringCaseAndSpacing()
    {
        var result = Map(Extraction(e => e with { BodyStyle = "  suv ", FuelType = "GASOLINE", Transmission = "automatic", Drivetrain = "4wd" }));

        Assert.Equal(SuvId, result.VehicleTypeId);
        Assert.Equal(GasId, result.FuelTypeId);
        Assert.Equal(AutoId, result.TransmissionId);
        Assert.Equal(FourWdId, result.DrivetrainId);
        Assert.Null(result.VehicleTypeText);
        Assert.Null(result.FuelTypeText);
        Assert.Null(result.TransmissionText);
        Assert.Null(result.DrivetrainText);
    }

    [Fact]
    public void AnUnmatchedLookupText_LeavesTheIdUnsetAndKeepsTheStickersText()
    {
        var result = Map(Extraction(e => e with { BodyStyle = "Crew Cab Pickup", Transmission = "10-Speed Automatic" }));

        Assert.Null(result.VehicleTypeId);
        Assert.Equal("Crew Cab Pickup", result.VehicleTypeText);
        Assert.Null(result.TransmissionId);
        Assert.Equal("10-Speed Automatic", result.TransmissionText);
        Assert.Equal(14, result.FieldsFilled);
    }

    [Fact]
    public void AHiddenOrOtherKindItem_DoesNotMatch()
    {
        // Only shown items are passed in, so a hidden "Gasoline" is simply absent; a vehicle type name never matches a fuel type.
        var result = mapper.Map(
            Extraction(e => e with { FuelType = "SUV" }),
            [new LookupItemRecord { Id = SuvId, Kind = LookupKind.VehicleType, Name = "SUV" }],
            [],
            false);

        Assert.Null(result.FuelTypeId);
        Assert.Equal("SUV", result.FuelTypeText);
    }

    [Fact]
    public void Features_AreTrimmedShortenedAtAWordBoundaryAndDeDuplicated()
    {
        var result = Map(Extraction(e => e with
        {
            Features =
            [
                Feature("  Heated   Seats  "), Feature("heated seats"), Feature("Heated-Seats"), Feature(""), Feature(null!),
                Feature("Premium Audio System With Subwoofer And Amplifier"), Feature("Supercalifragilisticexpialidocious-Package"),
            ],
        }));

        Assert.Equal(
            ["Heated Seats", "Premium Audio System With", "Supercalifragilisticexpialidoc"],
            result.Features.Select(feature => feature.Name));
        Assert.All(result.Features, feature => Assert.InRange(feature.Name.Length, 1, 30));
    }

    [Fact]
    public void OnAnAdd_ReplacesIsAlwaysEmpty_EvenWhenTheModelNamesNumbers()
    {
        var result = Map(Extraction(e => e with { Features = [Feature("Sunroof", 1, 2)] }));

        var feature = Assert.Single(result.Features);
        Assert.Empty(feature.ReplacesTagIds);
        Assert.Empty(feature.ReplacesNames);
    }

    [Fact]
    public void OnAnEdit_AnExistingFeatureWithTheSameKey_IsReplaced()
    {
        var existing = Tag("heated seats");

        var result = Map(Extraction(e => e with { Features = [Feature("Heated Seats")] }), [existing, Tag("Towing Package")]);

        var feature = Assert.Single(result.Features);
        Assert.Equal([existing.Id], feature.ReplacesTagIds);
        Assert.Equal(["heated seats"], feature.ReplacesNames);
    }

    [Fact]
    public void OnAnEdit_TheModelsSameAsNumbers_AreMappedToTheVehiclesTagIds()
    {
        var sunroof = Tag("Sunroof");
        var towing = Tag("Towing Package");

        var result = Map(Extraction(e => e with { Features = [Feature("Panoramic Sunroof", 1)] }), [sunroof, towing]);

        var feature = Assert.Single(result.Features);
        Assert.Equal([sunroof.Id], feature.ReplacesTagIds);
        Assert.Equal(["Sunroof"], feature.ReplacesNames);
    }

    [Fact]
    public void ASameAsNumberThatIsNotOnTheList_IsIgnored()
    {
        var sunroof = Tag("Sunroof");

        var result = Map(Extraction(e => e with { Features = [Feature("Panoramic Sunroof", 0, 2, -1, 99, 1)] }), [sunroof]);

        Assert.Equal([sunroof.Id], Assert.Single(result.Features).ReplacesTagIds);
    }

    [Fact]
    public void TheUnionOfKeyAndSameAsDuplicates_IsReplacedOnce()
    {
        var sunroof = Tag("Sunroof");
        var panoramic = Tag("panoramic sunroof");

        var result = Map(Extraction(e => e with { Features = [Feature("Panoramic Sunroof", 1, 2)] }), [sunroof, panoramic]);

        var feature = Assert.Single(result.Features);
        Assert.Equal(2, feature.ReplacesTagIds.Count);
        Assert.Contains(sunroof.Id, feature.ReplacesTagIds);
        Assert.Contains(panoramic.Id, feature.ReplacesTagIds);
    }

    [Fact]
    public void Proposals_AreCappedSoTheVehicleStaysWithinTheLimit()
    {
        var existing = Enumerable.Range(1, VehicleTagRepository.MaxTagsPerVehicle - 2).Select(i => Tag($"existing {i}")).ToList();

        var result = Map(Extraction(e => e with { Features = [Feature("One"), Feature("Two"), Feature("Three"), Feature("Four")] }), existing);

        Assert.Equal(["One", "Two"], result.Features.Select(feature => feature.Name));
        Assert.Equal(StickerImportOutcome.Partial, result.Outcome);
        Assert.Contains(result.Notices, notice => notice.Code == StickerNoticeCode.FeaturesLeftOut && notice.Message.Contains('2'));
    }

    [Fact]
    public void AReplacementFreesRoomUnderTheCap()
    {
        var existing = Enumerable.Range(1, VehicleTagRepository.MaxTagsPerVehicle).Select(i => Tag($"existing {i}")).ToList();

        var result = Map(Extraction(e => e with { Features = [Feature("Replacement", 1), Feature("No Room")] }), existing);

        Assert.Equal(["Replacement"], result.Features.Select(feature => feature.Name));
    }

    [Fact]
    public void OnAnAdd_UpToTheLimitOfFeaturesAreKept()
    {
        var limit = VehicleTagRepository.MaxTagsPerVehicle;
        var result = Map(Extraction(e => e with { Features = [.. Enumerable.Range(1, limit + 5).Select(i => Feature($"Feature {i}"))] }));

        Assert.Equal(limit, result.Features.Count);
    }

    [Fact]
    public void NotAWindowSticker_IsNoDetailsFoundAndFillsNothing()
    {
        var result = Map(Extraction(e => e with { IsWindowSticker = false }));

        Assert.Equal(StickerImportOutcome.NoDetailsFound, result.Outcome);
        Assert.Null(result.Make);
        Assert.Equal(0, result.FieldsFilled);
        Assert.Empty(result.Features);
    }

    [Fact]
    public void AStickerWithNothingUsable_IsNoDetailsFound()
    {
        var empty = new StickerExtraction { IsWindowSticker = true, VehiclesDetected = 1 };

        var result = Map(empty);

        Assert.Equal(StickerImportOutcome.NoDetailsFound, result.Outcome);
    }

    [Fact]
    public void FieldsFilled_CountsVehicleFieldsButNotLookupTextHints()
    {
        var result = Map(new StickerExtraction { IsWindowSticker = true, VehiclesDetected = 1, Make = "Ford", BodyStyle = "Hovercraft", Features = [Feature("Sunroof")] });

        Assert.Equal(1, result.FieldsFilled);
        Assert.Equal("Hovercraft", result.VehicleTypeText);
    }
}
