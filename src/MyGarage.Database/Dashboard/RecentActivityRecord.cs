namespace MyGarage.Database.Dashboard;

// One journal entry with the vehicle it belongs to.
public sealed record RecentActivityRecord
{
    public Guid NoteId { get; init; }

    public Guid VehicleId { get; init; }

    public int? VehicleYear { get; init; }

    public string VehicleMake { get; init; } = string.Empty;

    public string VehicleModel { get; init; } = string.Empty;

    public Guid? VehicleMainImageId { get; init; }

    public string? Category { get; init; }

    public string Body { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }
}
