namespace MyGarage.Shared.Dashboard;

// One recent journal entry, shown in the dashboard's Recent activity list.
public sealed class RecentActivityDto
{
    public required Guid NoteId { get; init; }

    public required Guid VehicleId { get; init; }

    public required string VehicleName { get; init; }

    // The vehicle's main picture, for the avatar; null when it has none.
    public Guid? VehicleMainImageId { get; init; }

    public string? Category { get; init; }

    // The start of the note on one line, shortened with an ellipsis when it is long.
    public required string Excerpt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
