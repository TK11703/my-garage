using Microsoft.AspNetCore.Http;
using MyGarage.Api.Vehicles.Images;

namespace MyGarage.Api.Tests.Vehicles.Images;

public class UploadImageRequestValidatorTests
{
    private readonly UploadImageRequestValidator validator = new();

    private static FormFile File(long length) => new(new MemoryStream([1]), 0, length, "file", "x.png");

    private bool IsValid(IFormFile? file, string? caption = null) =>
        validator.Validate(new UploadImageRequest { File = file, Caption = caption }).IsValid;

    [Fact]
    public void FileIsRequired() => Assert.False(IsValid(null));

    [Fact]
    public void EmptyFile_IsRejected() => Assert.False(IsValid(File(0)));

    [Fact]
    public void MaxSize_IsFiveMegabytes()
    {
        Assert.True(IsValid(File(1)));
        Assert.True(IsValid(File(5_242_880)));
        Assert.False(IsValid(File(5_242_881)));
    }

    [Fact]
    public void Caption_IsOptionalMax200Trimmed()
    {
        Assert.True(IsValid(File(1), null));
        Assert.True(IsValid(File(1), "   "));
        Assert.True(IsValid(File(1), new string('a', 200)));
        Assert.True(IsValid(File(1), " " + new string('a', 200) + " "));
        Assert.False(IsValid(File(1), new string('a', 201)));
    }

    [Theory]
    [InlineData("two\nlines")]
    [InlineData("bell\u0007")]
    public void Caption_RejectsControlCharacters(string caption) => Assert.False(IsValid(File(1), caption));
}
