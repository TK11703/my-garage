using MyGarage.Web.Common;

namespace MyGarage.Web.Drafts;

// Browser-facing route for the original file of a draft; the browser never gets a storage address.
public static class DraftAttachmentEndpoints
{
    public static IEndpointRouteBuilder MapDraftAttachmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/drafts/{draftId:guid}/attachment", ServeAsync)
            .WithName("ServeDraftAttachment");

        return endpoints;
    }

    public static Task<IResult> ServeAsync(Guid draftId, DraftsApiClient api, HttpContext http, CancellationToken cancellationToken) =>
        PrivateFileProxy.ServeAsync(token => api.OpenAttachmentAsync(draftId, token), http, cancellationToken);
}
