using FluentValidation.Results;
using MyGarage.Api.EmailIntake;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.EmailIntake;

namespace MyGarage.Api.Tests.EmailIntake;

public class IntakeRequestValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly IntakeRequestValidator validator = new(new TestTimeProvider(Now));

    private static IntakeEmailRequest Valid() => IntakeClient.NewEmail(receivedAt: Now.AddMinutes(-1));

    private static IntakeEmailRequest With(
        IntakeEmailRequest email,
        string? messageId = null,
        string? from = null,
        DateTimeOffset? receivedAt = null,
        string? subject = null,
        string? bodyText = null,
        IReadOnlyList<IntakeAttachmentRequest>? attachments = null) =>
        new()
        {
            MessageId = messageId ?? email.MessageId,
            From = from ?? email.From,
            ReceivedAt = receivedAt ?? email.ReceivedAt,
            Subject = subject ?? email.Subject,
            BodyText = bodyText ?? email.BodyText,
            Attachments = attachments ?? email.Attachments,
        };

    private static IntakeAttachmentRequest Attachment(string? fileName = "invoice.pdf", string? base64 = null, bool isInline = false, int sizeBytes = 16) =>
        new()
        {
            FileName = fileName,
            ContentType = "application/pdf",
            IsInline = isInline,
            ContentBase64 = base64 ?? Convert.ToBase64String(new byte[sizeBytes]),
        };

    private static string[] Errors(ValidationResult result, string property) =>
        [.. result.Errors.Where(e => e.PropertyName == property).Select(e => e.ErrorMessage)];

    [Fact]
    public void ValidRequest_Passes() => Assert.True(validator.Validate(Valid()).IsValid);

    [Fact]
    public void RequestWithAPdfAndAPngAttachment_Passes() =>
        Assert.True(validator.Validate(Valid().AddPdf().AddPng()).IsValid);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MessageId_Missing_Fails(string? messageId)
    {
        var result = validator.Validate(With(Valid(), messageId: messageId ?? string.Empty));

        Assert.NotEmpty(Errors(result, "MessageId"));
    }

    [Fact]
    public void MessageId_512Characters_Passes_513_Fails()
    {
        Assert.True(validator.Validate(With(Valid(), messageId: new string('m', 512))).IsValid);

        Assert.NotEmpty(Errors(validator.Validate(With(Valid(), messageId: new string('m', 513))), "MessageId"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("two@@example.com")]
    [InlineData("a b@example.com")]
    public void From_EmptyOrNotAnAddress_Fails(string from) =>
        Assert.NotEmpty(Errors(validator.Validate(With(Valid(), from: from)), "From"));

    [Fact]
    public void From_Over320Characters_Fails()
    {
        var from = new string('a', 310) + "@example.com";

        Assert.NotEmpty(Errors(validator.Validate(With(Valid(), from: from)), "From"));
    }

    [Fact]
    public void From_WithSurroundingWhitespace_Passes() =>
        Assert.True(validator.Validate(With(Valid(), from: "  alice@contoso.test ")).IsValid);

    [Fact]
    public void ReceivedAt_MoreThanFiveMinutesInTheFuture_Fails_ExactlyFiveMinutesPasses()
    {
        Assert.True(validator.Validate(With(Valid(), receivedAt: Now.AddMinutes(5))).IsValid);

        Assert.NotEmpty(Errors(validator.Validate(With(Valid(), receivedAt: Now.AddMinutes(5).AddSeconds(1))), "ReceivedAt"));
    }

    [Fact]
    public void Subject_501Characters_Fails_500Passes()
    {
        Assert.True(validator.Validate(With(Valid(), subject: new string('s', 500))).IsValid);

        Assert.NotEmpty(Errors(validator.Validate(With(Valid(), subject: new string('s', 501))), "Subject"));
    }

    [Fact]
    public void BodyText_100001Characters_Fails_100000Passes()
    {
        Assert.True(validator.Validate(With(Valid(), bodyText: new string('b', 100_000))).IsValid);

        Assert.NotEmpty(Errors(validator.Validate(With(Valid(), bodyText: new string('b', 100_001))), "BodyText"));
    }

    [Fact]
    public void NullAttachments_AreTreatedAsNone()
    {
        var request = new IntakeEmailRequest { MessageId = "<m@x>", From = "a@b.test", Attachments = null! };

        Assert.True(validator.Validate(request).IsValid);
    }

    [Fact]
    public void MoreThanFiveNonInlineAttachments_Fails_FiveWithInlineOnesPasses()
    {
        var five = Valid();
        for (var i = 0; i < 5; i++)
        {
            five = five.AddPdf($"f{i}.pdf");
        }

        var withInline = five.AddPng("logo.png", isInline: true).AddPng("logo2.png", isInline: true);
        Assert.True(validator.Validate(withInline).IsValid);

        var result = validator.Validate(five.AddPdf("f6.pdf"));
        Assert.NotEmpty(Errors(result, "Attachments"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Attachment_MissingFileName_FailsNamingTheIndex(string? fileName)
    {
        var request = Valid().AddPdf().AddRawAttachment(Attachment(fileName));

        var messages = Errors(validator.Validate(request), "Attachments[1].FileName");

        Assert.Single(messages);
        Assert.Contains("attachments[1].fileName", messages[0]);
    }

    [Fact]
    public void Attachment_FileNameOver255_Fails() =>
        Assert.NotEmpty(Errors(validator.Validate(Valid().AddRawAttachment(Attachment(new string('f', 256)))), "Attachments[0].FileName"));

    [Theory]
    [InlineData("not base64 !!")]
    [InlineData("QUJD=")]
    [InlineData("")]
    public void Attachment_InvalidOrEmptyBase64_FailsNamingTheIndex(string base64)
    {
        var request = Valid().AddPdf().AddRawAttachment(Attachment(base64: base64));

        var messages = Errors(validator.Validate(request), "Attachments[1].ContentBase64");

        Assert.Single(messages);
        Assert.Contains("attachments[1].contentBase64", messages[0]);
    }

    [Fact]
    public void Attachment_DecodingToTenMegabytes_Passes_OneByteMoreFails()
    {
        Assert.True(validator.Validate(Valid().AddRawAttachment(Attachment(sizeBytes: 10_485_760))).IsValid);

        var result = validator.Validate(Valid().AddRawAttachment(Attachment(sizeBytes: 10_485_761)));
        Assert.Single(Errors(result, "Attachments[0].ContentBase64"));
    }

    [Fact]
    public void TotalDecodedSizeOver15Megabytes_Fails()
    {
        var request = Valid().AddRawAttachment(Attachment(sizeBytes: 8_000_000)).AddRawAttachment(Attachment("b.pdf", sizeBytes: 7_800_000));

        Assert.NotEmpty(Errors(validator.Validate(request), "Attachments"));
        Assert.True(validator.Validate(Valid().AddRawAttachment(Attachment(sizeBytes: 8_000_000)).AddRawAttachment(Attachment("b.pdf", sizeBytes: 7_000_000))).IsValid);
    }

    [Fact]
    public void InlineAttachments_AreIgnoredForTheCountButStillSizeChecked()
    {
        var oversize = Valid().AddRawAttachment(Attachment("logo.png", isInline: true, sizeBytes: 10_485_761));

        Assert.NotEmpty(Errors(validator.Validate(oversize), "Attachments[0].ContentBase64"));
    }

    [Fact]
    public void ValidBase64OfTheWrongKindOfFile_IsNotAValidationError()
    {
        var request = Valid().AddAttachment("invoice.pdf", "application/pdf", "this is plain text, not a pdf"u8.ToArray());

        Assert.True(validator.Validate(request).IsValid);
    }
}
