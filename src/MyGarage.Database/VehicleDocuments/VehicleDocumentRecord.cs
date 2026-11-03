namespace MyGarage.Database.VehicleDocuments;

public sealed record VehicleDocumentRecord
{
    public Guid Id { get; init; }

    public Guid VehicleId { get; init; }

    public string FileName { get; init; } = string.Empty;

    public string ContentType { get; init; } = string.Empty;

    public int SizeBytes { get; init; }

    // The draft kind text the original was kept with, when known.
    public string? SourceKind { get; init; }

    public Guid? SourceRecordId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public string CreatedBy { get; init; } = string.Empty;
}

// A document file opened for reading; dispose it to release the storage connection.
public sealed class VehicleDocumentContent(string contentType, string fileName, Stream content, long? length) : IAsyncDisposable
{
    public string ContentType { get; } = contentType;

    public string FileName { get; } = fileName;

    public Stream Content { get; } = content;

    public long? Length { get; } = length;

    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
