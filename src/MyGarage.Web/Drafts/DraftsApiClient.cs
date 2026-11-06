using MyGarage.Shared.Drafts;
using MyGarage.Shared.EmailIntake;
using MyGarage.Shared.Vehicles;
using MyGarage.Web.Vehicles;

namespace MyGarage.Web.Drafts;

public sealed class DraftsApiClient(HttpClient http)
{
    public Task<ApiResult<PagedResultDto<MaintenanceDraftSummaryDto>>> ListAsync(int page, CancellationToken cancellationToken = default) =>
        SendAsync<PagedResultDto<MaintenanceDraftSummaryDto>>(HttpMethod.Get, $"api/drafts?page={page}", null, cancellationToken);

    // What happened to the caller's emails (history), newest first; outcome (such as "included") keeps only that outcome.
    public Task<ApiResult<PagedResultDto<EmailHistoryItemDto>>> ListEmailHistoryAsync(int page, string? outcome = null, CancellationToken cancellationToken = default) =>
        SendAsync<PagedResultDto<EmailHistoryItemDto>>(HttpMethod.Get, outcome is null ? $"api/emails?page={page}" : $"api/emails?page={page}&outcome={Uri.EscapeDataString(outcome)}", null, cancellationToken);

    public Task<ApiResult<MaintenanceDraftDto>> GetAsync(Guid draftId, CancellationToken cancellationToken = default) =>
        SendAsync<MaintenanceDraftDto>(HttpMethod.Get, $"api/drafts/{draftId}", null, cancellationToken);

    // Checks an edited proposal without saving anything.
    public Task<ApiResult<CompatibilityDto>> CheckCompatibilityAsync(Guid draftId, DraftProposal proposal, CancellationToken cancellationToken = default) =>
        SendAsync<CompatibilityDto>(HttpMethod.Post, $"api/drafts/{draftId}/compatibility", proposal, cancellationToken);

    public Task<ApiResult<MaintenanceDraftDto>> ChangeKindAsync(Guid draftId, DraftKind? kind, CancellationToken cancellationToken = default) =>
        SendAsync<MaintenanceDraftDto>(HttpMethod.Put, $"api/drafts/{draftId}/kind", new ChangeDraftKindRequest { Kind = kind }, cancellationToken);

    public Task<ApiResult<AcceptedDraftDto>> AcceptAsync(Guid draftId, AcceptDraftRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<AcceptedDraftDto>(HttpMethod.Post, $"api/drafts/{draftId}/accept", request, cancellationToken);

    public Task<ApiResult<bool>> DiscardAsync(Guid draftId, CancellationToken cancellationToken = default) =>
        SendAsync<bool>(HttpMethod.Post, $"api/drafts/{draftId}/discard", null, cancellationToken);

    // Streams the original file from the API; null when the draft has none for this user.
    public Task<ImageDownload?> OpenAttachmentAsync(Guid draftId, CancellationToken cancellationToken = default) =>
        ApiCalls.OpenAsync(http, $"api/drafts/{draftId}/attachment", cancellationToken);

    private Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken) =>
        ApiCalls.SendAsync<T>(http, method, path, body, cancellationToken);
}
