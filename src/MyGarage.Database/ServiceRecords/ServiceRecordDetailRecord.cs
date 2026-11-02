namespace MyGarage.Database.ServiceRecords;

public sealed record ServiceRecordDetailRecord(ServiceRecordRecord Record, IReadOnlyList<ServiceLineItemRecord> LineItems);
