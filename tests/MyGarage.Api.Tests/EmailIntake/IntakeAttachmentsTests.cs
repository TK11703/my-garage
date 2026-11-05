using System.Security.Cryptography;
using MyGarage.Api.EmailIntake;
using MyGarage.Api.Tests.Common;
using MyGarage.Shared.Drafts;

namespace MyGarage.Api.Tests.EmailIntake;

public class IntakeAttachmentsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

    private static readonly byte[] Webp = [.. "RIFF"u8.ToArray(), 0x24, 0x00, 0x00, 0x00, .. "WEBPVP8 "u8.ToArray()];

    private static PreparedAttachments Prepare(params IntakeFile[] files) =>
        IntakeAttachments.Prepare(Guid.CreateVersion7(), "owner-a", Now, files);

    [Fact]
    public void Prepare_RecognizesPdfAndImagesFromTheirBytes_WhateverTheNameSays()
    {
        var prepared = Prepare(
            new IntakeFile("a.png", IntakeClient.PdfBytes),
            new IntakeFile("b.pdf", IntakeClient.PngBytes),
            new IntakeFile("c", Jpeg),
            new IntakeFile("d.txt", Webp));

        Assert.Empty(prepared.Skipped);
        Assert.Equal(
            ["application/pdf", "image/png", "image/jpeg", "image/webp"],
            prepared.Stored.Select(stored => stored.Record.ContentType));
    }

    [Fact]
    public void Prepare_SkipsAPdfNamedFileThatIsText_WithTheUnsupportedTypeReason()
    {
        var prepared = Prepare(new IntakeFile("ok.pdf", IntakeClient.PdfBytes), new IntakeFile("invoice.pdf", "just some text"u8.ToArray()));

        Assert.Single(prepared.Stored);
        var skipped = Assert.Single(prepared.Skipped);
        Assert.Equal(1, skipped.Index);
        Assert.Equal("invoice.pdf", skipped.FileName);
        Assert.Equal(DraftUploadLimits.UnsupportedType, skipped.Reason);
    }

    [Fact]
    public void Prepare_SkipsAnEmptyFile_WithTheEmptyReason()
    {
        var prepared = Prepare(new IntakeFile("empty.pdf", []));

        Assert.Empty(prepared.Stored);
        Assert.Equal(DraftUploadLimits.Empty, Assert.Single(prepared.Skipped).Reason);
    }

    [Fact]
    public void Prepare_SkipsAFileOverTheLimit_WithTheTooLargeReason_AndKeepsOneAtTheLimit()
    {
        var atLimit = new byte[DraftUploadLimits.MaxFileBytes];
        IntakeClient.PdfBytes.CopyTo(atLimit, 0);
        var over = new byte[DraftUploadLimits.MaxFileBytes + 1];
        IntakeClient.PdfBytes.CopyTo(over, 0);

        var prepared = Prepare(new IntakeFile("limit.pdf", atLimit), new IntakeFile("over.pdf", over));

        Assert.Equal(DraftUploadLimits.MaxFileBytes, Assert.Single(prepared.Stored).Record.SizeBytes);
        var skipped = Assert.Single(prepared.Skipped);
        Assert.Equal(1, skipped.Index);
        Assert.Equal(DraftUploadLimits.TooLarge, skipped.Reason);
    }

    [Fact]
    public void Prepare_BuildsRecordsWithOwnerSizeHashAndNewIds()
    {
        var submissionId = Guid.CreateVersion7();

        var prepared = IntakeAttachments.Prepare(
            submissionId, "owner-a", Now, [new IntakeFile("one.pdf", IntakeClient.PdfBytes), new IntakeFile("two.pdf", IntakeClient.PdfBytes)]);

        Assert.Equal(2, prepared.Stored.Count);
        Assert.NotEqual(prepared.Stored[0].Record.Id, prepared.Stored[1].Record.Id);
        Assert.All(prepared.Stored, stored =>
        {
            Assert.Equal(submissionId, stored.Record.SubmissionId);
            Assert.Equal("owner-a", stored.Record.OwnerId);
            Assert.Equal(IntakeClient.PdfBytes.Length, stored.Record.SizeBytes);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(IntakeClient.PdfBytes)), stored.Record.Sha256);
            Assert.Equal(Now, stored.Record.CreatedAt);
            Assert.Equal(IntakeClient.PdfBytes, stored.Content);
        });
    }

    [Theory]
    [InlineData("C:\\Users\\me\\invoice.pdf", "invoice.pdf")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("in\u0007voice\u0000.pdf", "invoice.pdf")]
    [InlineData("   ", "attachment")]
    [InlineData(null, "attachment")]
    [InlineData("folder/", "attachment")]
    public void SafeFileName_DropsPathsAndControlCharacters_AndNeverReturnsEmpty(string? input, string expected) =>
        Assert.Equal(expected, IntakeAttachments.SafeFileName(input));

    [Fact]
    public void Prepare_NeverPutsFileContentInTheSkippedList()
    {
        var prepared = Prepare(new IntakeFile("notes.txt", "secret"u8.ToArray()));

        var skipped = Assert.Single(prepared.Skipped);
        Assert.DoesNotContain("secret", skipped.FileName + skipped.Reason, StringComparison.Ordinal);
    }
}
