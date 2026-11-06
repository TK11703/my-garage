using Dapper;
using MyGarage.Database.Common;
using Npgsql;

namespace MyGarage.Database.Dashboard;

public sealed class DashboardRepository(NpgsqlDataSource dataSource, SqlScriptStore scripts)
{
    // Every figure is scoped to the owner; an owner with nothing gets zeros.
    public async Task<DashboardCountsRecord> GetCountsAsync(string ownerId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleAsync<DashboardCountsRecord>(new CommandDefinition(
            scripts.Get("Dashboard/Counts"), new { OwnerId = ownerId }, cancellationToken: cancellationToken));
    }

    // Months with no entries are absent; the caller fills them in.
    public async Task<IReadOnlyList<FuelEnergySpendRecord>> GetFuelEnergySpendAsync(string ownerId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return [.. await connection.QueryAsync<FuelEnergySpendRecord>(new CommandDefinition(
            scripts.Get("Dashboard/FuelEnergySpend"), new { OwnerId = ownerId, From = from, To = to }, cancellationToken: cancellationToken))];
    }

    // The newest journal entries across the owner's vehicles.
    public async Task<IReadOnlyList<RecentActivityRecord>> GetRecentActivityAsync(string ownerId, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return [.. await connection.QueryAsync<RecentActivityRecord>(new CommandDefinition(
            scripts.Get("Dashboard/RecentActivity"), new { OwnerId = ownerId, Limit = limit }, cancellationToken: cancellationToken))];
    }
}
