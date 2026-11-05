namespace MyGarage.Shared.Vehicles;

// A feature to add when the vehicle is saved, and the vehicle's existing features it replaces (feature 010).
public sealed class StickerFeatureRequest
{
    // 1 to 30 characters, no control characters, trimmed by the API.
    public string? Name { get; init; }

    // Must be empty on create.
    public IReadOnlyList<Guid>? ReplacesTagIds { get; init; }
}
