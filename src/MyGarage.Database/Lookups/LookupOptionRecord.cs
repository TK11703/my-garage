namespace MyGarage.Database.Lookups;

// A lookup item offered as a filter choice.
public sealed record LookupOptionRecord
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;
}
