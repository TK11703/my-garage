namespace MyGarage.Api.Common;

// Identifies an image by its leading bytes; the file name and the content type a client claims are never trusted.
public static class ImageSignature
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Riff = "RIFF"u8.ToArray();
    private static readonly byte[] Webp = "WEBP"u8.ToArray();

    // Returns image/jpeg, image/png, or image/webp, or null. The stream is left at its start.
    public static string? Detect(Stream stream)
    {
        Span<byte> header = stackalloc byte[12];
        var read = ReadHeader(stream, header);
        stream.Position = 0;

        var bytes = header[..read];
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

    private static int ReadHeader(Stream stream, Span<byte> buffer)
    {
        stream.Position = 0;
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
