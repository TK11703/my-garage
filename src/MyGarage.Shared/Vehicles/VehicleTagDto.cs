namespace MyGarage.Shared.Vehicles;

public sealed class VehicleTagDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    // One of FeatureCategories.All; Other while the feature has not been categorized yet.
    public string Category { get; init; } = FeatureCategories.Other;
}
