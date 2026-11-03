namespace MyGarage.Api.Common;

// Identifies a PDF or an image by its leading bytes; the file name and the content type a client claims are never trusted.
public static class FileSignature
{
    private static ReadOnlySpan<byte> Pdf => "%PDF-"u8;
    private static ReadOnlySpan<byte> Jpeg => [0xFF, 0xD8, 0xFF];
    private static ReadOnlySpan<byte> Png => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> Riff => "RIFF"u8;
    private static ReadOnlySpan<byte> Webp => "WEBP"u8;

    // Returns application/pdf, image/jpeg, image/png, or image/webp, or null.
    public static string? Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(Pdf))
        {
            return "application/pdf";
        }

        if (bytes.StartsWith(Jpeg))
        {
            return "image/jpeg";
        }

        if (bytes.StartsWith(Png))
        {
            return "image/png";
        }

        if (bytes.Length >= 12 && bytes.StartsWith(Riff) && bytes.Slice(8, 4).SequenceEqual(Webp))
        {
            return "image/webp";
        }

        return null;
    }
}
