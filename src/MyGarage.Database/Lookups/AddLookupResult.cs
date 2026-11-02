namespace MyGarage.Database.Lookups;

public enum AddLookupOutcome
{
    Created,
    Duplicate,
    LimitReached,
}

// Item is set when Created; ExistingIsHidden tells a Duplicate that the matching item is hidden for that owner.
public sealed record AddLookupResult(AddLookupOutcome Outcome, LookupItemRecord? Item = null, bool ExistingIsHidden = false);
