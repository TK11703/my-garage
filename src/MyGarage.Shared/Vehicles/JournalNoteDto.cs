namespace MyGarage.Shared.Vehicles;

public sealed class JournalNoteDto
{
    public required Guid Id { get; init; }

    public string? Category { get; init; }

    public required string Body { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
