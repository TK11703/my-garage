namespace MyGarage.Shared.EmailIntake;

public sealed class IntakeAttachmentRequest
{
    public string? FileName { get; init; }

    // Informational only; the stored type is decided from the file bytes.
    public string? ContentType { get; init; }

    public bool IsInline { get; init; }

    public string? ContentBase64 { get; init; }
}
