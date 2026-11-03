using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.ServiceRecords;
using Npgsql;

namespace MyGarage.Database.Drafts;

// Turns a draft into a record in one transaction: the guarded decision comes first, then the record (reusing the existing insert scripts),
// the optional kept document, and the clean-up of the email content and notification. Any failure rolls everything back.
public sealed class DraftAcceptanceRepository(NpgsqlDataSource dataSource, SqlScriptStore scripts)
{
    // keepDocumentId is the id of the vehicle_documents row to create from the draft's attachment (the caller has already copied the file); null keeps nothing.
    public async Task<AcceptDraftResult> AcceptAsync(
        string ownerId,
        Guid draftId,
        Guid vehicleId,
        AcceptedRecordValues values,
        Guid? keepDocumentId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var recordId = Guid.CreateVersion7();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var decided = await connection.QuerySingleOrDefaultAsync<DecidedDraft>(new CommandDefinition(
            scripts.Get("Drafts/Decide"),
            new { DraftId = draftId, OwnerId = ownerId, NewState = "included", Now = now, VehicleId = (Guid?)vehicleId, CreatedRecordKind = (string?)values.DraftKind, CreatedRecordId = (Guid?)recordId },
            transaction,
            cancellationToken: cancellationToken));
        if (decided is null)
        {
            var state = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
                scripts.Get("Drafts/GetState"), new { DraftId = draftId, OwnerId = ownerId }, transaction, cancellationToken: cancellationToken));
            return new AcceptDraftResult(state is null ? AcceptDraftOutcome.NotFound : AcceptDraftOutcome.AlreadyDecided);
        }

        // The insert scripts select from the owner's vehicle, so a vehicle that is not theirs inserts nothing.
        if (!await InsertRecordAsync(connection, transaction, ownerId, vehicleId, recordId, values, now, cancellationToken))
        {
            return new AcceptDraftResult(AcceptDraftOutcome.VehicleNotFound);
        }

        Guid? documentId = null;
        if (keepDocumentId is { } newDocumentId)
        {
            documentId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                scripts.Get("Drafts/InsertKeptDocument"),
                new
                {
                    DocumentId = newDocumentId,
                    VehicleId = vehicleId,
                    SourceKind = values.DraftKind,
                    SourceRecordId = recordId,
                    Now = now,
                    OwnerId = ownerId,
                    DraftId = draftId,
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("Notifications/DeleteForDraft"), new { DraftId = draftId, OwnerId = ownerId }, transaction, cancellationToken: cancellationToken));
        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("Drafts/PurgeSubmissionContent"), new { decided.SubmissionId, OwnerId = ownerId, Now = now }, transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return new AcceptDraftResult(
            AcceptDraftOutcome.Accepted,
            recordId,
            documentId,
            decided.AttachmentId is { } attachmentId ? new DraftAttachmentRef(decided.SubmissionId, attachmentId) : null);
    }

    private async Task<bool> InsertRecordAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string ownerId,
        Guid vehicleId,
        Guid recordId,
        AcceptedRecordValues values,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        async Task<bool> InsertAsync(string script, object parameters) =>
            await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(scripts.Get(script), parameters, transaction, cancellationToken: cancellationToken)) is not null;

        switch (values)
        {
            case ServiceRecordValues service:
                if (!await InsertAsync(
                        "ServiceRecords/Insert",
                        new
                        {
                            Id = recordId,
                            VehicleId = vehicleId,
                            OwnerId = ownerId,
                            service.DateServiced,
                            service.OdometerReading,
                            service.ShopOrPerson,
                            Now = now,
                            FromEmail = true,
                        }))
                {
                    return false;
                }

                foreach (var line in service.LineItems)
                {
                    await InsertLineItemAsync(connection, transaction, ownerId, vehicleId, recordId, line, now, cancellationToken);
                }

                return true;

            case FuelEnergyValues fuel:
                return await InsertAsync(
                    "FuelEnergyEntries/Insert",
                    new { Id = recordId, VehicleId = vehicleId, OwnerId = ownerId, fuel.Kind, fuel.EntryDate, fuel.UnitPrice, fuel.Quantity, fuel.TotalCost, Now = now, FromEmail = true });

            case InspectionValues inspection:
                return await InsertAsync(
                    "Inspections/Insert",
                    new { Id = recordId, VehicleId = vehicleId, OwnerId = ownerId, inspection.InspectionType, inspection.InspectionDate, inspection.Cost, inspection.NextDueDate, Now = now, FromEmail = true });

            case RegistrationValues registration:
                return await InsertAsync(
                    "Registrations/Insert",
                    new { Id = recordId, VehicleId = vehicleId, OwnerId = ownerId, registration.State, registration.Cost, registration.ExpirationDate, Now = now, FromEmail = true });

            case InsuranceValues insurance:
                return await InsertAsync(
                    "Insurance/Insert",
                    new { Id = recordId, VehicleId = vehicleId, OwnerId = ownerId, insurance.StartDate, insurance.EndDate, insurance.Duration, insurance.Cost, Now = now, FromEmail = true });

            case TaxValues tax:
                return await InsertAsync(
                    "Taxes/Insert",
                    new { Id = recordId, VehicleId = vehicleId, OwnerId = ownerId, tax.TaxType, tax.Amount, tax.Recurrence, Now = now, FromEmail = true });

            default:
                throw new ArgumentOutOfRangeException(nameof(values), values, "Unknown record values.");
        }
    }

    // Same statements and totals as ServiceRecordRepository.AddLineItemAsync, inside the accept transaction.
    private async Task InsertLineItemAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string ownerId,
        Guid vehicleId,
        Guid recordId,
        ServiceLineItemInput line,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var lineItemId = Guid.CreateVersion7();
        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("ServiceRecords/InsertLineItem"),
            new
            {
                LineItemId = lineItemId,
                RecordId = recordId,
                OwnerId = ownerId,
                line.Description,
                line.FlatCost,
                line.LaborHours,
                line.LaborRate,
                TotalCost = ServiceCostCalculator.LineTotal(
                    line.FlatCost, line.LaborHours, line.LaborRate, line.Parts.Select(part => (part.Quantity, part.UnitPrice))),
                Now = now,
            },
            transaction,
            cancellationToken: cancellationToken));

        for (var index = 0; index < line.Parts.Count; index++)
        {
            var part = line.Parts[index];
            await connection.ExecuteAsync(new CommandDefinition(
                scripts.Get("ServiceRecords/InsertPart"),
                new
                {
                    Id = Guid.CreateVersion7(),
                    LineItemId = lineItemId,
                    RecordId = recordId,
                    VehicleId = vehicleId,
                    OwnerId = ownerId,
                    Position = (short)(index + 1),
                    part.Name,
                    part.Quantity,
                    part.UnitPrice,
                },
                transaction,
                cancellationToken: cancellationToken));
        }
    }
}
