using MyGarage.Api.Vehicles;
using MyGarage.Api.Vehicles.StickerImport;
using MyGarage.Database.Tags;
using MyGarage.Shared.Vehicles.StickerImport;

namespace MyGarage.Api.Tests.Vehicles.StickerImport;

public class StickerNoticesTests
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private readonly StickerResultMapper mapper = new(new VehicleRequestValidator(new FixedTimeProvider(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero))));

    private static StickerExtraction Full() =>
        new()
        {
            IsWindowSticker = true,
            VehiclesDetected = 1,
            Year = 2024,
            Make = "Ford",
            Model = "F-150",
            Trim = "Lariat",
            Vin = "1FTFW1E50NFA00001",
            Engine = "3.5L V6",
            Horsepower = 400,
            ExteriorColor = "Black",
            Seats = 5,
        };

    private StickerImportResultDto Map(StickerExtraction extraction, bool duplicateVin = false, IReadOnlyList<VehicleTagRecord>? tags = null) =>
        mapper.Map(extraction, [], tags ?? [], duplicateVin);

    [Fact]
    public void AFullReadWithNothingWrong_HasNoNotices()
    {
        Assert.Empty(Map(Full()).Notices);
    }

    [Fact]
    public void UnreadableValues_NameOnlyTheFields_NeverTheValues()
    {
        var result = Map(Full() with { Vin = "TOO-SHORT-VIN", Horsepower = 0, WheelSizeInches = 99m });

        var notice = Assert.Single(result.Notices, notice => notice.Code == StickerNoticeCode.UnreadableValues);
        Assert.Equal("These values could not be used and were left empty: VIN, Horsepower, Wheel size.", notice.Message);
        Assert.DoesNotContain("TOO-SHORT-VIN", notice.Message);
        Assert.DoesNotContain("99", notice.Message);
    }

    [Fact]
    public void FewFieldsRead_AddsThePartiallyReadNotice()
    {
        var result = Map(new StickerExtraction { IsWindowSticker = true, VehiclesDetected = 1, Make = "Ford", Model = "F-150" });

        Assert.Contains(result.Notices, notice => notice.Code == StickerNoticeCode.PartiallyRead);
        Assert.Equal(StickerImportOutcome.Filled, result.Outcome);
    }

    [Fact]
    public void FeaturesLeftOut_ReportsOnlyTheCount()
    {
        var existing = Enumerable.Range(1, VehicleTagRepository.MaxTagsPerVehicle - 1).Select(i => new VehicleTagRecord { Id = Guid.NewGuid(), Name = $"existing {i}" }).ToList();
        ExtractedFeature[] features = [new("Secret Option One", []), new("Secret Option Two", []), new("Secret Option Three", [])];

        var result = Map(Full() with { Features = features }, tags: existing);

        var notice = Assert.Single(result.Notices, notice => notice.Code == StickerNoticeCode.FeaturesLeftOut);
        Assert.Equal($"2 features were left out to stay within {VehicleTagRepository.MaxTagsPerVehicle} per vehicle.", notice.Message);
        Assert.DoesNotContain("Secret", notice.Message);
    }

    [Fact]
    public void OneFeatureLeftOut_IsWordedInTheSingular()
    {
        var existing = Enumerable.Range(1, VehicleTagRepository.MaxTagsPerVehicle).Select(i => new VehicleTagRecord { Id = Guid.NewGuid(), Name = $"existing {i}" }).ToList();

        var result = Map(Full() with { Features = [new ExtractedFeature("One", [])] }, tags: existing);

        Assert.Equal($"1 feature was left out to stay within {VehicleTagRepository.MaxTagsPerVehicle} per vehicle.", Assert.Single(result.Notices, notice => notice.Code == StickerNoticeCode.FeaturesLeftOut).Message);
    }

    [Fact]
    public void MoreThanOneVehicle_AddsTheNoticeAndMakesTheResultPartial()
    {
        var result = Map(Full() with { VehiclesDetected = 2 });

        Assert.Contains(result.Notices, notice => notice.Code == StickerNoticeCode.MultipleVehicles);
        Assert.Equal(StickerImportOutcome.Partial, result.Outcome);
    }

    [Fact]
    public void NothingReadFromAValidSticker_IsNoDetails()
    {
        var result = Map(new StickerExtraction { IsWindowSticker = true, VehiclesDetected = 1 });

        Assert.Equal(StickerImportOutcome.NoDetailsFound, result.Outcome);
        Assert.Contains(result.Notices, notice => notice.Code == StickerNoticeCode.NoDetails);
        Assert.DoesNotContain(result.Notices, notice => notice.Code == StickerNoticeCode.PartiallyRead);
    }

    [Fact]
    public void NotAStickerFile_HasBothNotices()
    {
        var result = Map(Full() with { IsWindowSticker = false });

        Assert.Equal([StickerNoticeCode.NotAStickerLikely, StickerNoticeCode.NoDetails], result.Notices.Select(notice => notice.Code));
    }

    [Fact]
    public void ADuplicateVin_AddsTheNotice_OnlyWhenTheVinWasKept()
    {
        var kept = Map(Full(), duplicateVin: true);
        var dropped = Map(Full() with { Vin = "BAD" }, duplicateVin: true);

        Assert.Single(kept.Notices, notice => notice.Code == StickerNoticeCode.DuplicateVin);
        Assert.DoesNotContain("1FTFW1E50NFA00001", kept.Notices.Single(notice => notice.Code == StickerNoticeCode.DuplicateVin).Message);
        Assert.DoesNotContain(dropped.Notices, notice => notice.Code == StickerNoticeCode.DuplicateVin);
    }

    [Fact]
    public void NoNoticeMessageContainsExtractedText()
    {
        var extraction = Full() with
        {
            Make = new string('m', 60),
            Engine = "ENGINE-MARK",
            Features = [new ExtractedFeature("FEATURE-MARK", [])],
            VehiclesDetected = 3,
        };

        var result = Map(extraction, duplicateVin: true);

        foreach (var notice in result.Notices)
        {
            Assert.DoesNotContain("ENGINE-MARK", notice.Message);
            Assert.DoesNotContain("FEATURE-MARK", notice.Message);
            Assert.DoesNotContain("mmmm", notice.Message);
            Assert.DoesNotContain("1FTFW1E50NFA00001", notice.Message);
        }
    }
}
