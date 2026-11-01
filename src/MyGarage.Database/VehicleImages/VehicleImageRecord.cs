namespace MyGarage.Database.VehicleImages;

public sealed record VehicleImageRecord
{
    public Guid Id { get; init; }

    public string ContentType { get; init; } = string.Empty;

    public int SizeBytes { get; init; }

    public string? Caption { get; init; }

    public bool IsMain { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

public enum AddImageStatus
{
    Added,
    VehicleNotFound,
    LimitReached,
}

public sealed record AddImageResult(AddImageStatus Status, VehicleImageRecord? Image = null);

// An image file opened for reading; dispose it to release the storage connection.
public sealed class VehicleImageContent(string contentType, Stream content, long? length) : IAsyncDisposable
{
    public string ContentType { get; } = contentType;

    public Stream Content { get; } = content;

    public long? Length { get; } = length;

    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

// Raised when the image store cannot be reached or rejects a request; image data is never partially stored.
public sealed class ImageStorageUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
