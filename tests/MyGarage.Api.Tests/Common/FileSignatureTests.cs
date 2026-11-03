using MyGarage.Api.Common;

namespace MyGarage.Api.Tests.Common;

public class FileSignatureTests
{
    private static readonly byte[] Padding = new byte[32];

    [Fact]
    public void Pdf_IsRecognized() => Assert.Equal("application/pdf", FileSignature.Detect([.. "%PDF-1.7\n"u8.ToArray(), .. Padding]));

    [Fact]
    public void Jpeg_IsRecognized() => Assert.Equal("image/jpeg", FileSignature.Detect([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, .. Padding]));

    [Fact]
    public void Png_IsRecognized() =>
        Assert.Equal("image/png", FileSignature.Detect([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, .. Padding]));

    [Fact]
    public void Webp_IsRecognized() =>
        Assert.Equal("image/webp", FileSignature.Detect([.. "RIFF"u8.ToArray(), 0x10, 0x00, 0x00, 0x00, .. "WEBP"u8.ToArray(), .. Padding]));

    [Fact]
    public void RiffThatIsNotWebp_IsRejected() =>
        Assert.Null(FileSignature.Detect([.. "RIFF"u8.ToArray(), 0x10, 0x00, 0x00, 0x00, .. "WAVE"u8.ToArray(), .. Padding]));

    [Fact]
    public void PlainText_IsRejected() => Assert.Null(FileSignature.Detect("this is not a document"u8));

    [Fact]
    public void Empty_IsRejected() => Assert.Null(FileSignature.Detect([]));

    [Fact]
    public void ThreeBytesOfText_AreRejected() => Assert.Null(FileSignature.Detect("abc"u8));

    [Fact]
    public void Gif_IsRejected() => Assert.Null(FileSignature.Detect([.. "GIF89a"u8.ToArray(), .. Padding]));

    [Fact]
    public void Html_IsRejected() => Assert.Null(FileSignature.Detect("<!DOCTYPE html><html><body>hi</body></html>"u8));

    [Fact]
    public void TextNamedOrDeclaredAsPdf_IsRejected()
    {
        // Only the bytes count: the file name and the content type a client claims are never trusted.
        var bytes = "invoice.pdf application/pdf %PDF- is not at the start"u8;

        Assert.Null(FileSignature.Detect(bytes));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void TruncatedJpegHeader_IsRejected(int length) => Assert.Null(FileSignature.Detect(new byte[] { 0xFF, 0xD8 }.AsSpan(0, length)));

    [Fact]
    public void TruncatedPdfPngAndWebpHeaders_AreRejected()
    {
        Assert.Null(FileSignature.Detect("%PDF"u8));
        Assert.Null(FileSignature.Detect([0x89, 0x50, 0x4E, 0x47]));
        Assert.Null(FileSignature.Detect([.. "RIFF"u8.ToArray(), 0x10, 0x00, 0x00, 0x00, .. "WEB"u8.ToArray()]));
    }

    [Fact]
    public void ImageSignature_StillRecognizesImagesButNotPdfs()
    {
        using var jpeg = new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3]);
        using var pdf = new MemoryStream([.. "%PDF-1.7"u8.ToArray()]);

        Assert.Equal("image/jpeg", ImageSignature.Detect(jpeg));
        Assert.Null(ImageSignature.Detect(pdf));
    }
}
