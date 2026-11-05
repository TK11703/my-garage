using Microsoft.AspNetCore.Http;
using MyGarage.Api.Drafts.Uploads;
using MyGarage.Shared.Drafts;

namespace MyGarage.Api.Tests.Drafts;

public class DraftUploadRequestValidatorTests
{
    private static readonly DraftUploadRequestValidator Validator = new();

    private static FormFile File(long length) => new(Stream.Null, 0, length, "files", "invoice.pdf");

    private static DraftUploadRequest Request(Guid? vehicleId = null, params long[] lengths)
    {
        var files = new FormFileCollection();
        foreach (var length in lengths)
        {
            files.Add(File(length));
        }

        return new DraftUploadRequest { Files = files, VehicleId = vehicleId };
    }

    [Fact]
    public void NoFiles_FailsOnFiles()
    {
        Assert.Contains(Validator.Validate(new DraftUploadRequest()).Errors, error => error.PropertyName == "Files");
        Assert.Contains(Validator.Validate(new DraftUploadRequest { Files = new FormFileCollection() }).Errors, error => error.PropertyName == "Files");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void OneToFiveFiles_Pass(int count) =>
        Assert.True(Validator.Validate(Request(null, [.. Enumerable.Repeat(1000L, count)])).IsValid);

    [Fact]
    public void SixFiles_Fail() =>
        Assert.Contains(Validator.Validate(Request(null, [.. Enumerable.Repeat(1000L, DraftUploadLimits.MaxFiles + 1)])).Errors, error => error.PropertyName == "Files");

    [Fact]
    public void TheTotalLimit_PassesAtTheLimitAndFailsOneByteOver()
    {
        // Two files, one at the 10 MB file limit, adding up to exactly 15 MB.
        long[] atLimit = [DraftUploadLimits.MaxFileBytes, DraftUploadLimits.MaxTotalBytes - DraftUploadLimits.MaxFileBytes];
        long[] over = [DraftUploadLimits.MaxFileBytes, DraftUploadLimits.MaxTotalBytes - DraftUploadLimits.MaxFileBytes + 1];

        Assert.True(Validator.Validate(Request(null, atLimit)).IsValid);
        Assert.Contains(Validator.Validate(Request(null, over)).Errors, error => error.PropertyName == "Files");
    }

    [Fact]
    public void AnEmptyGuidVehicle_Fails_AndANullVehiclePasses()
    {
        Assert.Contains(Validator.Validate(Request(Guid.Empty, 1000)).Errors, error => error.PropertyName == "VehicleId");
        Assert.True(Validator.Validate(Request(null, 1000)).IsValid);
        Assert.True(Validator.Validate(Request(Guid.NewGuid(), 1000)).IsValid);
    }
}
