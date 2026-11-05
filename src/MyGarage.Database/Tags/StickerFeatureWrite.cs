namespace MyGarage.Database.Tags;

// A feature to link to a vehicle, and the vehicle's own tag rows it replaces.
public sealed record StickerFeatureWrite(string Name, IReadOnlyList<Guid> ReplacesTagIds);

public enum VehicleFeatureWriteStatus
{
    Written,
    NotFound,
    FeatureLimit,
}
