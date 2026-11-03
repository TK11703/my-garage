using MyGarage.Database.Drafts;

namespace MyGarage.Api.Drafts;

// The advisory duplicate checks, answered by owner-scoped SQL.
public sealed class DraftDuplicateLookup(MaintenanceDraftRepository drafts) : IDraftDuplicateLookup
{
    public Task<bool> HasDraftWithSameAttachmentAsync(string ownerId, Guid draftId, string sha256, CancellationToken cancellationToken) =>
        drafts.HasDraftWithSameAttachmentAsync(ownerId, draftId, sha256, cancellationToken);

    public Task<bool> HasRecordLikeAsync(string ownerId, Guid vehicleId, string kind, DateOnly date, decimal total, CancellationToken cancellationToken) =>
        drafts.HasRecordLikeAsync(ownerId, vehicleId, kind, date, total, cancellationToken);
}
