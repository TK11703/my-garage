using MyGarage.Database.Lookups;
using MyGarage.Shared.Lookups;

namespace MyGarage.Api.Lookups;

public sealed record LookupAddResult(AddLookupOutcome Outcome, LookupItemDto? Item = null, bool ExistingIsHidden = false);

public sealed class LookupService(LookupRepository lookups, TimeProvider timeProvider)
{
    public async Task<LookupsDto> GetAsync(string ownerId, bool includeHidden, CancellationToken cancellationToken) =>
        LookupMapper.ToLookupsDto(await lookups.ListAsync(ownerId, includeHidden, cancellationToken));

    // kind is the stored kind (see LookupMapper.ToStoredKind).
    public async Task<LookupAddResult> AddAsync(string ownerId, string kind, string name, CancellationToken cancellationToken)
    {
        var result = await lookups.AddCustomAsync(ownerId, kind, name, timeProvider.GetUtcNow(), cancellationToken);
        return new LookupAddResult(result.Outcome, result.Item is null ? null : LookupMapper.ToDto(result.Item), result.ExistingIsHidden);
    }

    // Null when the item does not exist for the caller (unknown id, wrong kind, another user's item).
    public async Task<LookupItemDto?> SetVisibilityAsync(string ownerId, string kind, Guid itemId, bool isHidden, CancellationToken cancellationToken)
    {
        var item = isHidden
            ? await lookups.HideAsync(ownerId, kind, itemId, timeProvider.GetUtcNow(), cancellationToken)
            : await lookups.ShowAsync(ownerId, kind, itemId, cancellationToken);
        return item is null ? null : LookupMapper.ToDto(item);
    }
}
