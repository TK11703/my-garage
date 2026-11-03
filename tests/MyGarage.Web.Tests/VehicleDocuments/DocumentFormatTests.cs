using MyGarage.Shared.Drafts;
using MyGarage.Web.VehicleDocuments;

namespace MyGarage.Web.Tests.VehicleDocuments;

public class DocumentFormatTests
{
    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(1023, "1023 bytes")]
    [InlineData(1024, "1 KB")]
    [InlineData(120_000, "117 KB")]
    [InlineData(1_048_576, "1.0 MB")]
    [InlineData(2_500_000, "2.4 MB")]
    public void Size_UsesBytesKilobytesOrMegabytes(long bytes, string expected) => Assert.Equal(expected, DocumentFormat.Size(bytes));

    [Theory]
    [InlineData("application/pdf", "PDF")]
    [InlineData("APPLICATION/PDF", "PDF")]
    [InlineData("image/png", "Image")]
    [InlineData("image/webp", "Image")]
    [InlineData("text/plain", "File")]
    public void TypeLabel_NamesPdfsAndImages(string contentType, string expected) => Assert.Equal(expected, DocumentFormat.TypeLabel(contentType));

    [Theory]
    [InlineData(DraftKind.ServiceRecord, "From service record")]
    [InlineData(DraftKind.Energy, "From charge")]
    [InlineData(null, "Kept from an email")]
    public void Source_NamesTheRecordTypeTheFileWasKeptWith(DraftKind? kind, string expected) => Assert.Equal(expected, DocumentFormat.Source(kind));
}
