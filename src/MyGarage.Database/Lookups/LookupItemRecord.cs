namespace MyGarage.Database.Lookups;

public sealed record LookupItemRecord
{
    public Guid Id { get; init; }

    public string Kind { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public bool IsDefault { get; init; }

    public bool IsHidden { get; init; }

    public int SortOrder { get; init; }
}
