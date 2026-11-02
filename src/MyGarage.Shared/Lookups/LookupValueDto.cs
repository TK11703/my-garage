namespace MyGarage.Shared.Lookups;

// A vehicle's chosen lookup item, or a filter choice.
public sealed class LookupValueDto
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }
}
