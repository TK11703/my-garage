namespace MyGarage.Database.JournalNotes;

public sealed record JournalNoteRecord
{
    public Guid Id { get; init; }

    public string? Category { get; init; }

    public string Body { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public string CreatedBy { get; init; } = string.Empty;

    public string UpdatedBy { get; init; } = string.Empty;
}
