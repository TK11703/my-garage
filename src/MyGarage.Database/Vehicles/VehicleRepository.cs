using Dapper;
using MyGarage.Database.Common;
using Npgsql;

namespace MyGarage.Database.Vehicles;

public sealed class VehicleRepository(NpgsqlDataSource dataSource, SqlScriptStore scripts)
{
    public async Task InsertAsync(VehicleRecord vehicle, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(scripts.Get("Vehicles/Insert"), vehicle, cancellationToken: cancellationToken));
    }

    public async Task<VehicleRecord?> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<VehicleRecord>(
            new CommandDefinition(scripts.Get("Vehicles/GetById"), new { Id = id, OwnerId = ownerId }, cancellationToken: cancellationToken));
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
        };

        var items = (await connection.QueryAsync<VehicleSummaryRecord>(new CommandDefinition(
            scripts.Get("Vehicles/ListPage"),
            new
            {
                filters.OwnerId, filters.Pattern, filters.Make, filters.Color, filters.Doors, filters.Tag, filters.MinMileage, filters.MaxMileage,
                PageSize = pageSize,
                Offset = (page - 1) * pageSize,
            },
            cancellationToken: cancellationToken))).ToList();
        var total = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(scripts.Get("Vehicles/Count"), filters, cancellationToken: cancellationToken));

        return new PagedResult<VehicleSummaryRecord>(items, page, pageSize, total);
    }

    public async Task<VehicleFilterOptionsRecord> GetFilterOptionsAsync(string ownerId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        using var results = await connection.QueryMultipleAsync(
            new CommandDefinition(scripts.Get("Vehicles/FilterOptions"), new { OwnerId = ownerId }, cancellationToken: cancellationToken));

        var makes = (await results.ReadAsync<string>()).ToList();
        var colors = (await results.ReadAsync<string>()).ToList();
        var tags = (await results.ReadAsync<string>()).ToList();

        return new VehicleFilterOptionsRecord(makes, colors, tags);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
