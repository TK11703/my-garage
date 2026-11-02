namespace MyGarage.Shared.Lookups;

public sealed class LookupItemDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    // True for items provided by the application.
    public required bool IsDefault { get; init; }

    // True when the caller has hidden the item from their dropdowns.
    public required bool IsHidden { get; init; }
}
