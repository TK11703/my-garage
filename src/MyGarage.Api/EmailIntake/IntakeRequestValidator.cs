using System.Buffers;
using System.Net.Mail;
using FluentValidation;
using MyGarage.Shared.EmailIntake;

namespace MyGarage.Api.EmailIntake;

// Validates the whole intake body before anything is stored. Attachment bytes are decoded into a pooled buffer only to prove
// they are valid base64 within the size limits; nothing is kept, and an unsupported file type is a skip, not a validation error.
public sealed class IntakeRequestValidator : AbstractValidator<IntakeEmailRequest>
{
    public const int MaxMessageIdLength = 512;
    public const int MaxAddressLength = 320;
    public const int MaxSubjectLength = 500;
    public const int MaxBodyTextLength = 100_000;
    public const int MaxFileNameLength = 255;
    public const int MaxNonInlineAttachments = 5;
    public const int MaxAttachmentBytes = 10_485_760;
    public const int MaxTotalAttachmentBytes = 15_728_640;

    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    // The longest base64 text that can still decode to MaxAttachmentBytes; longer text is refused without decoding it.
    private const int MaxBase64Length = (MaxAttachmentBytes + 2) / 3 * 4;

    public IntakeRequestValidator(TimeProvider timeProvider)
    {
        RuleFor(request => request.MessageId)
            .NotEmpty().WithMessage("messageId is required.")
            .MaximumLength(MaxMessageIdLength).WithMessage($"messageId must be {MaxMessageIdLength} characters or fewer.");

        RuleFor(request => request.From)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("from is required.")
            .MaximumLength(MaxAddressLength).WithMessage($"from must be {MaxAddressLength} characters or fewer.")
            .Must(IsAddress).WithMessage("from must be an email address.");

        RuleFor(request => request.ReceivedAt)
            .Must(receivedAt => receivedAt is null || receivedAt <= timeProvider.GetUtcNow() + MaxClockSkew)
            .WithMessage("receivedAt cannot be more than 5 minutes in the future.");

        RuleFor(request => request.Subject)
            .MaximumLength(MaxSubjectLength).WithMessage($"subject must be {MaxSubjectLength} characters or fewer.");

        RuleFor(request => request.BodyText)
            .MaximumLength(MaxBodyTextLength).WithMessage($"bodyText must be {MaxBodyTextLength:N0} characters or fewer.");

        RuleFor(request => request).Custom(ValidateAttachments);
    }

    private static bool IsAddress(string? from)
    {
        var address = from?.Trim();
        return !string.IsNullOrEmpty(address)
               && !address.Contains(' ')
               && MailAddress.TryCreate(address, out var parsed)
               && string.Equals(parsed.Address, address, StringComparison.Ordinal);
    }

    // Failures use the path form Attachments[1].ContentBase64 and name the attachment index in the message.
    private static void ValidateAttachments(IntakeEmailRequest request, ValidationContext<IntakeEmailRequest> context)
    {
        var attachments = request.Attachments ?? [];
        var nonInline = 0;
        long totalBytes = 0;

        for (var index = 0; index < attachments.Count; index++)
        {
            var attachment = attachments[index];
            if (attachment is null)
            {
                context.AddFailure($"Attachments[{index}]", $"attachments[{index}] is missing.");
                continue;
            }

            if (!attachment.IsInline)
            {
                nonInline++;
                if (string.IsNullOrWhiteSpace(attachment.FileName) || attachment.FileName.Length > MaxFileNameLength)
                {
                    context.AddFailure(
                        $"Attachments[{index}].FileName",
                        $"attachments[{index}].fileName is required and must be {MaxFileNameLength} characters or fewer.");
                }
            }

            if (DecodedSize(attachment.ContentBase64, index, context) is { } size)
            {
                totalBytes += size;
            }
        }

        if (nonInline > MaxNonInlineAttachments)
        {
            context.AddFailure("Attachments", $"At most {MaxNonInlineAttachments} attachments are allowed (inline images are not counted).");
        }

        if (totalBytes > MaxTotalAttachmentBytes)
        {
            context.AddFailure("Attachments", "Attachments are limited to 15 MB in total after decoding.");
        }
    }

    // The decoded size, or null (after recording the failure) when the text is not usable base64 or is too large.
    private static long? DecodedSize(string? base64, int index, ValidationContext<IntakeEmailRequest> context)
    {
        var path = $"Attachments[{index}].ContentBase64";
        if (string.IsNullOrEmpty(base64))
        {
            context.AddFailure(path, $"attachments[{index}].contentBase64 must be valid, non-empty base64.");
            return null;
        }

        if (base64.Length > MaxBase64Length)
        {
            context.AddFailure(path, $"attachments[{index}].contentBase64 must decode to 10 MB or less.");
            return null;
        }

        var buffer = ArrayPool<byte>.Shared.Rent(base64.Length / 4 * 3 + 3);
        try
        {
            if (!Convert.TryFromBase64String(base64, buffer, out var written) || written == 0)
            {
                context.AddFailure(path, $"attachments[{index}].contentBase64 must be valid, non-empty base64.");
                return null;
            }

            if (written > MaxAttachmentBytes)
            {
                context.AddFailure(path, $"attachments[{index}].contentBase64 must decode to 10 MB or less.");
                return null;
            }

            return written;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
