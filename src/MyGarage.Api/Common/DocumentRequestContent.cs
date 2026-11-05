using System.Buffers;
using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace MyGarage.Api.Common;

// A JSON request body whose one string value is a document: the JSON before and after it is written as is, and the document is
// read from its stream in small blocks and base64-encoded straight into the request, so a 10 MB file is never held as bytes
// and again as a base64 string. A seekable stream can be sent again (retries); a one-pass stream can be sent only once.
internal sealed class DocumentRequestContent : HttpContent
{
    // A multiple of 3 so every block but the last encodes without padding.
    private const int BlockSize = 3 * 16 * 1024;

    private readonly byte[] prefix;
    private readonly byte[] suffix;
    private readonly Stream document;
    private readonly long start;
    private bool sent;

    public DocumentRequestContent(string prefix, string suffix, Stream document)
    {
        this.prefix = Encoding.UTF8.GetBytes(prefix);
        this.suffix = Encoding.UTF8.GetBytes(suffix);
        this.document = document;
        start = document.CanSeek ? document.Position : 0;
        Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
    }

    protected override bool TryComputeLength(out long length)
    {
        if (!document.CanSeek)
        {
            length = 0;
            return false;
        }

        var remaining = document.Length - start;
        length = prefix.Length + suffix.Length + (remaining + 2) / 3 * 4;
        return true;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        Begin();
        await stream.WriteAsync(prefix, cancellationToken);

        var source = ArrayPool<byte>.Shared.Rent(BlockSize);
        var encoded = ArrayPool<byte>.Shared.Rent(Base64.GetMaxEncodedToUtf8Length(BlockSize));
        try
        {
            while (true)
            {
                var filled = await FillAsync(source, cancellationToken);
                if (filled == 0)
                {
                    break;
                }

                var written = Encode(source.AsSpan(0, filled), encoded, filled < BlockSize);
                await stream.WriteAsync(encoded.AsMemory(0, written), cancellationToken);
                if (filled < BlockSize)
                {
                    break;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(source);
            ArrayPool<byte>.Shared.Return(encoded);
        }

        await stream.WriteAsync(suffix, cancellationToken);
    }

    protected override void SerializeToStream(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        Begin();
        stream.Write(prefix);

        var source = ArrayPool<byte>.Shared.Rent(BlockSize);
        var encoded = ArrayPool<byte>.Shared.Rent(Base64.GetMaxEncodedToUtf8Length(BlockSize));
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var filled = 0;
                while (filled < BlockSize)
                {
                    var read = document.Read(source, filled, BlockSize - filled);
                    if (read == 0)
                    {
                        break;
                    }

                    filled += read;
                }

                if (filled == 0)
                {
                    break;
                }

                var written = Encode(source.AsSpan(0, filled), encoded, filled < BlockSize);
                stream.Write(encoded, 0, written);
                if (filled < BlockSize)
                {
                    break;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(source);
            ArrayPool<byte>.Shared.Return(encoded);
        }

        stream.Write(suffix);
    }

    // The same document cannot be read twice from a one-pass stream; a seekable one starts over.
    private void Begin()
    {
        if (document.CanSeek)
        {
            document.Position = start;
        }
        else if (sent)
        {
            throw new InvalidOperationException("The document stream cannot be read a second time.");
        }

        sent = true;
    }

    // Reads until the block is full or the stream ends, so blocks stay a multiple of 3 bytes.
    private async Task<int> FillAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        var filled = 0;
        while (filled < BlockSize)
        {
            var read = await document.ReadAsync(buffer.AsMemory(filled, BlockSize - filled), cancellationToken);
            if (read == 0)
            {
                break;
            }

            filled += read;
        }

        return filled;
    }

    private static int Encode(ReadOnlySpan<byte> block, byte[] destination, bool isFinalBlock)
    {
        var status = Base64.EncodeToUtf8(block, destination, out _, out var written, isFinalBlock);
        return status == OperationStatus.Done
            ? written
            : throw new InvalidOperationException("The document could not be encoded.");
    }
}
