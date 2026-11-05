namespace MyGarage.Database.Tags;

public sealed record VehicleTagRecord
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    // Null until the feature has been categorized.
    public string? Category { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

// A shared feature still waiting for a category.
public sealed record UncategorizedFeature(Guid Id, string Name);

public enum AddTagStatus
{
    Added,
    VehicleNotFound,
    Duplicate,
    LimitReached,
}

public sealed record AddTagResult(AddTagStatus Status, VehicleTagRecord? Tag = null);
