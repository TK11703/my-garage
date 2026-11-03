using MyGarage.Api.Drafts;
using MyGarage.Database.VehicleDocuments;
using MyGarage.Shared.VehicleDocuments;

namespace MyGarage.Api.VehicleDocuments;

public static class VehicleDocumentMapper
{
    public static VehicleDocumentDto ToDto(VehicleDocumentRecord record) =>
        new()
        {
            Id = record.Id,
            FileName = record.FileName,
            ContentType = record.ContentType,
            SizeBytes = record.SizeBytes,
            SourceKind = DraftKinds.FromText(record.SourceKind),
            SourceRecordId = record.SourceRecordId,
            CreatedAt = record.CreatedAt,
        };
}
