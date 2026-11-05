namespace MyGarage.Shared.Vehicles.StickerImport;

// A feature read from the sticker, proposed for the vehicle.
public sealed class StickerFeatureDto
{
    // 1 to 30 characters.
    public required string Name { get; init; }

    // Existing feature links of the edited vehicle this feature would replace; always a subset of that vehicle's own tags.
    public IReadOnlyList<Guid> ReplacesTagIds { get; init; } = [];

    public IReadOnlyList<string> ReplacesNames { get; init; } = [];
}
