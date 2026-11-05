namespace MyGarage.Shared.Drafts;

// Limits for one upload; they equal the limits for email attachments.
public static class DraftUploadLimits
{
    public const int MaxFiles = 5;

    public const int MaxFileBytes = 10_485_760;

    public const int MaxTotalBytes = 15_728_640;

    public const string Accept = "application/pdf,image/jpeg,image/png,image/webp";

    public const string UnsupportedType = "unsupported_type";

    public const string Empty = "empty";

    public const string TooLarge = "too_large";
}
