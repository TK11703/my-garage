using MyGarage.Shared.Drafts;

namespace MyGarage.Web.Drafts;

// The messages shown on the upload page (contracts/web-routes.md).
public static class DraftUploadMessages
{
    public const string ChooseFile = "Choose at least one file.";

    public const string TooManyFiles = "Upload at most 5 files at a time.";

    public const string FileTooLarge = "One of the files is larger than 10 MB.";

    public const string TotalTooLarge = "The files add up to more than 15 MB. Upload fewer files at a time.";

    public const string RateLimited = "Too many uploads. Try again in a minute.";

    public const string NotFound = "That vehicle was not found.";

    public const string Invalid = "The files could not be uploaded. Choose PDF, JPG, PNG, or WebP files and try again.";

    public static string ForReason(string reason) => reason switch
    {
        DraftUploadLimits.Empty => "The file is empty.",
        DraftUploadLimits.TooLarge => "The file is larger than 10 MB.",
        _ => "The file must be a PDF, JPG, PNG, or WebP.",
    };

    public static string Uploaded(int count) =>
        count == 1
            ? "1 file uploaded. It is being read; the draft appears below and in your notifications when ready."
            : $"{count} files uploaded. They are being read; each draft appears below and in your notifications when ready.";
}
