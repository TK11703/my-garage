namespace MyGarage.Api.Drafts.Uploads;

// Bound from DraftUploads.
public sealed class DraftUploadOptions
{
    public const string SectionName = "DraftUploads";

    // Every file is read by a paid model call, so each user may start only this many uploads in one minute.
    public int RequestsPerMinute { get; set; } = 10;
}
