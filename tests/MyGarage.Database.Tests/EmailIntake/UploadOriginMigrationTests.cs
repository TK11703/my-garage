using Dapper;
using MyGarage.Database.Tests.Common;
using Npgsql;

namespace MyGarage.Database.Tests.EmailIntake;

[Collection(DatabaseCollection.Name)]
public class UploadOriginMigrationTests(PostgresFixture postgres)
{
    private static async Task<Guid> InsertVehicleAsync(NpgsqlConnection connection, string ownerId = "owner-a")
    {
        var id = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO vehicles (id, owner_id, make, model, created_at, updated_at, created_by, updated_by) " +
            "VALUES (@id, @ownerId, 'Ford', 'F-150', now(), now(), @ownerId, @ownerId)", new { id, ownerId });
        return id;
    }

    [Fact]
    public async Task Origin_DefaultsToEmail_ForRowsInsertedWithoutIt()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        var id = await EmailIntakeTestData.InsertSubmissionAsync(dataSource);

        await using var connection = await dataSource.OpenConnectionAsync();
        var row = await connection.QuerySingleAsync<(string Origin, Guid? Preselected)>(
            "SELECT origin, preselected_vehicle_id FROM email_submissions WHERE id = @id", new { id });

        Assert.Equal("email", row.Origin);
        Assert.Null(row.Preselected);
    }

    [Theory]
    [InlineData("email")]
    [InlineData("upload")]
    public async Task Origin_AcceptsEmailAndUpload(string origin)
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();

        await connection.ExecuteAsync(
            "INSERT INTO email_submissions (id, message_id, status, arrived_at, owner_id, next_attempt_at, origin) " +
            "VALUES (gen_random_uuid(), @messageId, 'queued', now(), 'owner-a', now(), @origin)",
            new { messageId = $"upload:{Guid.NewGuid()}", origin });
    }

    [Fact]
    public async Task Origin_RejectsOtherValues()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();

        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            "INSERT INTO email_submissions (id, message_id, status, arrived_at, owner_id, next_attempt_at, origin) " +
            "VALUES (gen_random_uuid(), 'm1', 'queued', now(), 'owner-a', now(), 'fax')"));
    }

    [Fact]
    public async Task PreselectedVehicle_BecomesNull_WhenTheVehicleIsDeleted()
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var vehicleId = await InsertVehicleAsync(connection);
        var submissionId = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO email_submissions (id, message_id, status, arrived_at, owner_id, next_attempt_at, origin, preselected_vehicle_id) " +
            "VALUES (@submissionId, @messageId, 'queued', now(), 'owner-a', now(), 'upload', @vehicleId)",
            new { submissionId, messageId = $"upload:{submissionId}", vehicleId });

        await connection.ExecuteAsync("DELETE FROM vehicles WHERE id = @vehicleId", new { vehicleId });

        var preselected = await connection.ExecuteScalarAsync<Guid?>(
            "SELECT preselected_vehicle_id FROM email_submissions WHERE id = @submissionId", new { submissionId });
        Assert.Null(preselected);
    }

    [Theory]
    [InlineData("vin", true)]
    [InlineData("description", true)]
    [InlineData("only_vehicle", true)]
    [InlineData("selected", true)]
    [InlineData("none", true)]
    [InlineData("guess", false)]
    public async Task DraftVehicleMatch_AcceptsOnlyTheKnownValues(string match, bool accepted)
    {
        await using var dataSource = await postgres.CreateMigratedDatabaseAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        var submissionId = await EmailIntakeTestData.InsertSubmissionAsync(dataSource);

        var insert = connection.ExecuteAsync(
            "INSERT INTO maintenance_drafts (id, owner_id, submission_id, state, vehicle_match, created_at, expires_at) " +
            "VALUES (gen_random_uuid(), 'owner-a', @submissionId, 'needs_review', @match, now(), now() + interval '30 days')",
            new { submissionId, match });

        if (accepted)
        {
            await insert;
        }
        else
        {
            await Assert.ThrowsAsync<PostgresException>(() => insert);
        }
    }
}
