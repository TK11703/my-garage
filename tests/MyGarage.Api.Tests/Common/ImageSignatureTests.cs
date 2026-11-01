using MyGarage.Api.Common;

namespace MyGarage.Api.Tests.Common;

public class ImageSignatureTests
{
    private static string? Detect(params byte[] bytes) => ImageSignature.Detect(new MemoryStream(bytes));

    private static readonly byte[] Padding = new byte[32];

    [Fact]
    public void Jpeg_IsRecognized() => Assert.Equal("image/jpeg", Detect([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, .. Padding]));

    [Fact]
    public void Png_IsRecognized() =>
        Assert.Equal("image/png", Detect([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, .. Padding]));

    [Fact]
    public void Webp_IsRecognized() =>
        Assert.Equal("image/webp", Detect([.. "RIFF"u8.ToArray(), 0x10, 0x00, 0x00, 0x00, .. "WEBP"u8.ToArray(), .. Padding]));

    [Fact]
    public void RiffThatIsNotWebp_IsRejected() =>
        Assert.Null(Detect([.. "RIFF"u8.ToArray(), 0x10, 0x00, 0x00, 0x00, .. "WAVE"u8.ToArray(), .. Padding]));

    [Fact]
    public void TextRenamedToAnImage_IsRejected() => Assert.Null(Detect([.. "this is not an image"u8.ToArray()]));

    [Fact]
    public void Gif_IsRejected() => Assert.Null(Detect([.. "GIF89a"u8.ToArray(), .. Padding]));

    [Fact]
    public void Empty_IsRejected() => Assert.Null(Detect());

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void TruncatedJpegHeader_IsRejected(int length) => Assert.Null(Detect([.. new byte[] { 0xFF, 0xD8 }.Take(length)]));

    [Fact]
    public void TruncatedPngAndWebpHeaders_AreRejected()
    {
        Assert.Null(Detect(0x89, 0x50, 0x4E, 0x47));
        Assert.Null(Detect([.. "RIFF"u8.ToArray(), 0x10, 0x00, 0x00, 0x00, .. "WEB"u8.ToArray()]));
    }

    [Fact]
    public void StreamIsLeftAtTheStart()
    {
        using var stream = new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3]);

        ImageSignature.Detect(stream);

        Assert.Equal(0, stream.Position);
    }
}
