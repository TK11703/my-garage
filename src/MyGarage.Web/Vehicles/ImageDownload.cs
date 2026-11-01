namespace MyGarage.Web.Vehicles;

public enum ImageUploadOutcome
{
    Uploaded,
    Invalid,
    TooLarge,
    LimitReached,
    NotFound,
    Unavailable,
}

// A picture streamed from the API; disposing it releases the underlying HTTP response.
public sealed class ImageDownload(HttpResponseMessage response, string contentType, Stream stream) : IAsyncDisposable
{
    public string ContentType { get; } = contentType;

    public Stream Stream { get; } = stream;

    public async ValueTask DisposeAsync()
    {
        await Stream.DisposeAsync();
        response.Dispose();
    }
}
