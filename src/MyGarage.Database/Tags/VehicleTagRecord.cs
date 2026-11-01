namespace MyGarage.Database.Tags;

public sealed record VehicleTagRecord
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }
}

public enum AddTagStatus
{
    Added,
    VehicleNotFound,
    Duplicate,
    LimitReached,
}

public sealed record AddTagResult(AddTagStatus Status, VehicleTagRecord? Tag = null);
