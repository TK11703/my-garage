namespace MyGarage.Shared.Lookups;

public sealed class SetLookupVisibilityRequest
{
    // Nullable so that a missing value is rejected by validation instead of silently meaning "show".
    public bool? IsHidden { get; init; }
}
