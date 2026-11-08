using Dapper;
using MyGarage.Database.Common;
using MyGarage.Database.Lookups;
using MyGarage.Database.Tags;
using Npgsql;

namespace MyGarage.Database.Vehicles;

public sealed class VehicleRepository(NpgsqlDataSource dataSource, SqlScriptStore scripts)
{
    public async Task InsertAsync(VehicleRecord vehicle, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(scripts.Get("Vehicles/Insert"), vehicle, cancellationToken: cancellationToken));
    }

    // The vehicle and its window-sticker features are written in one transaction; any status other than Written leaves nothing behind.
    public async Task<VehicleFeatureWriteStatus> InsertWithFeaturesAsync(
        VehicleRecord vehicle,
        IReadOnlyList<StickerFeatureWrite> features,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(scripts.Get("Vehicles/Insert"), vehicle, transaction, cancellationToken: cancellationToken));
        var status = await ApplyFeaturesAsync(connection, transaction, vehicle.OwnerId, vehicle.Id, features, now, cancellationToken);
        if (status != VehicleFeatureWriteStatus.Written)
        {
            await transaction.RollbackAsync(cancellationToken);
            return status;
        }

        await transaction.CommitAsync(cancellationToken);
        return status;
    }

    // Like UpdateAsync, plus the features: the listed tags of this vehicle are removed and the new features linked, all or nothing.
    public async Task<VehicleFeatureWriteStatus> UpdateWithFeaturesAsync(
        VehicleRecord vehicle,
        IReadOnlyList<StickerFeatureWrite> features,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var rows = await connection.ExecuteAsync(new CommandDefinition(scripts.Get("Vehicles/Update"), vehicle, transaction, cancellationToken: cancellationToken));
        if (rows == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return VehicleFeatureWriteStatus.NotFound;
        }

        var status = await ApplyFeaturesAsync(connection, transaction, vehicle.OwnerId, vehicle.Id, features, now, cancellationToken);
        if (status != VehicleFeatureWriteStatus.Written)
        {
            await transaction.RollbackAsync(cancellationToken);
            return status;
        }

        await transaction.CommitAsync(cancellationToken);
        return status;
    }

    private async Task<VehicleFeatureWriteStatus> ApplyFeaturesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string ownerId,
        Guid vehicleId,
        IReadOnlyList<StickerFeatureWrite> features,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (features.Count == 0)
        {
            return VehicleFeatureWriteStatus.Written;
        }

        var locked = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            scripts.Get("Tags/LockVehicle"), new { VehicleId = vehicleId, OwnerId = ownerId }, transaction, cancellationToken: cancellationToken));
        if (locked is null)
        {
            return VehicleFeatureWriteStatus.NotFound;
        }

        // Removed first so a replacement frees its place under the limit and a feature may replace itself.
        var replaced = features.SelectMany(feature => feature.ReplacesTagIds).Distinct().ToArray();
        if (replaced.Length > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                scripts.Get("Tags/DeleteByIds"), new { VehicleId = vehicleId, OwnerId = ownerId, TagIds = replaced }, transaction, cancellationToken: cancellationToken));
        }

        foreach (var feature in features)
        {
            var linked = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                scripts.Get("Tags/Exists"), new { VehicleId = vehicleId, feature.Name }, transaction, cancellationToken: cancellationToken));
            if (linked)
            {
                continue;
            }

            var featureId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
                scripts.Get("Tags/UpsertFeature"),
                new { Id = Guid.CreateVersion7(), feature.Name, CreatedAt = now, OwnerId = ownerId },
                transaction,
                cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                scripts.Get("Tags/Insert"),
                new { Id = Guid.CreateVersion7(), VehicleId = vehicleId, FeatureId = featureId, CreatedAt = now },
                transaction,
                cancellationToken: cancellationToken));
        }

        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            scripts.Get("Tags/CountForVehicle"), new { VehicleId = vehicleId }, transaction, cancellationToken: cancellationToken));
        return count > VehicleTagRepository.MaxTagsPerVehicle ? VehicleFeatureWriteStatus.FeatureLimit : VehicleFeatureWriteStatus.Written;
    }

    // Whether the owner already has a vehicle with this VIN, other than excludeVehicleId (the one being edited); never looks at other owners.
    public async Task<bool> VinExistsAsync(string ownerId, string vin, Guid? excludeVehicleId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            scripts.Get("Vehicles/VinExists"), new { OwnerId = ownerId, Vin = vin, ExcludeId = excludeVehicleId }, cancellationToken: cancellationToken));
    }

    public async Task<VehicleRecord?> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<VehicleRecord>(
            new CommandDefinition(scripts.Get("Vehicles/GetById"), new { Id = id, OwnerId = ownerId }, cancellationToken: cancellationToken));
    }

    // Null when the vehicle does not exist for that owner; otherwise whether it is archived.
    public async Task<bool?> GetArchivedStateAsync(string ownerId, Guid vehicleId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool?>(new CommandDefinition(
            scripts.Get("Vehicles/GetArchivedState"), new { VehicleId = vehicleId, OwnerId = ownerId }, cancellationToken: cancellationToken));
    }

    // Archives the owner's vehicle and marks its waiting reminder notices read, in one transaction.
    public async Task<VehicleStateChange> ArchiveAsync(string ownerId, Guid vehicleId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var parameters = new { VehicleId = vehicleId, OwnerId = ownerId, Now = now };

        var changed = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            scripts.Get("Vehicles/Archive"), parameters, transaction, cancellationToken: cancellationToken));
        if (changed is null)
        {
            return await WhyUnchangedAsync(connection, transaction, ownerId, vehicleId, cancellationToken);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            scripts.Get("Notifications/MarkReadForVehicleReminders"), parameters, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return VehicleStateChange.Changed;
    }

    public async Task<VehicleStateChange> RestoreAsync(string ownerId, Guid vehicleId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var changed = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            scripts.Get("Vehicles/Restore"), new { VehicleId = vehicleId, OwnerId = ownerId, Now = now }, cancellationToken: cancellationToken));
        return changed is null
            ? await WhyUnchangedAsync(connection, null, ownerId, vehicleId, cancellationToken)
            : VehicleStateChange.Changed;
    }

    private async Task<VehicleStateChange> WhyUnchangedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string ownerId,
        Guid vehicleId,
        CancellationToken cancellationToken)
    {
        var archived = await connection.ExecuteScalarAsync<bool?>(new CommandDefinition(
            scripts.Get("Vehicles/GetArchivedState"), new { VehicleId = vehicleId, OwnerId = ownerId }, transaction, cancellationToken: cancellationToken));
        return archived is null ? VehicleStateChange.NotFound : VehicleStateChange.AlreadyInState;
    }

    // Replaces the editable fields of the owner's vehicle; false when it does not exist for that owner.
    public async Task<bool> UpdateAsync(VehicleRecord vehicle, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(scripts.Get("Vehicles/Update"), vehicle, cancellationToken: cancellationToken));
        return rows > 0;
    }

    // Child rows go with the vehicle through ON DELETE CASCADE.
    public async Task<bool> DeleteAsync(string ownerId, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(
            new CommandDefinition(scripts.Get("Vehicles/Delete"), new { Id = id, OwnerId = ownerId }, cancellationToken: cancellationToken));
        return rows > 0;
    }

    // page is 1-based; a page past the last returns an empty slice with the real (filtered) TotalCount.
    public async Task<PagedResult<VehicleSummaryRecord>> ListPageAsync(
        string ownerId,
        VehicleSearchCriteria criteria,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        var filters = new
        {
            OwnerId = ownerId,
            Pattern = LikePattern.Contains(criteria.Query),
            Make = Clean(criteria.Make),
            Color = Clean(criteria.Color),
            criteria.Doors,
            Tag = Clean(criteria.Tag),
            criteria.MinMileage,
            criteria.MaxMileage,
            criteria.MinYear,
            criteria.MaxYear,
            criteria.VehicleTypeId,
            criteria.FuelTypeId,
            criteria.TransmissionId,
            criteria.DrivetrainId,
            criteria.Status,
        };

        var items = (await connection.QueryAsync<VehicleSummaryRecord>(new CommandDefinition(
            scripts.Get("Vehicles/ListPage"),
            new
            {
                filters.OwnerId, filters.Pattern, filters.Make, filters.Color, filters.Doors, filters.Tag, filters.MinMileage, filters.MaxMileage,
                filters.MinYear, filters.MaxYear, filters.VehicleTypeId, filters.FuelTypeId, filters.TransmissionId, filters.DrivetrainId, filters.Status,
                PageSize = pageSize,
                Offset = (page - 1) * pageSize,
            },
            cancellationToken: cancellationToken))).ToList();
        var total = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(scripts.Get("Vehicles/Count"), filters, cancellationToken: cancellationToken));

        return new PagedResult<VehicleSummaryRecord>(items, page, pageSize, total);
    }

    public Task<VehicleFilterOptionsRecord> GetFilterOptionsAsync(string ownerId, CancellationToken cancellationToken) =>
        GetFilterOptionsAsync(ownerId, "active", cancellationToken);

    // status is active, archived, or all: the choices come only from vehicles in that status.
    public async Task<VehicleFilterOptionsRecord> GetFilterOptionsAsync(string ownerId, string status, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        using var results = await connection.QueryMultipleAsync(
            new CommandDefinition(scripts.Get("Vehicles/FilterOptions"), new { OwnerId = ownerId, Status = status }, cancellationToken: cancellationToken));

        var makes = (await results.ReadAsync<string>()).ToList();
        var colors = (await results.ReadAsync<string>()).ToList();
        var tags = (await results.ReadAsync<string>()).ToList();
        var vehicleTypes = (await results.ReadAsync<LookupOptionRecord>()).ToList();
        var fuelTypes = (await results.ReadAsync<LookupOptionRecord>()).ToList();
        var transmissions = (await results.ReadAsync<LookupOptionRecord>()).ToList();
        var drivetrains = (await results.ReadAsync<LookupOptionRecord>()).ToList();

        return new VehicleFilterOptionsRecord(makes, colors, tags)
        {
            VehicleTypes = vehicleTypes,
            FuelTypes = fuelTypes,
            Transmissions = transmissions,
            Drivetrains = drivetrains,
        };
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
