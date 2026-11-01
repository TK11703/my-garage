namespace MyGarage.Shared.Vehicles;

// Create and update both send the text and the optional free-text category.
public sealed class JournalNoteRequest
{
    public string? Body { get; init; }

    public string? Category { get; init; }
}
