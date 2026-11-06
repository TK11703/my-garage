using Dapper;
using MyGarage.Database.Tests.Common;
using Npgsql;

namespace MyGarage.Database.Tests.Reminders;

[Collection(DatabaseCollection.Name)]
public class ReminderMigrationTests(PostgresFixture postgres)
{
    private async Task<(NpgsqlDataSource DataSource, Guid VehicleId)> CreateWithVehicleAsync()
    {
        var dataSource = await postgres.CreateMigratedDatabaseAsync();
        return (dataSource, await ReminderTestData.InsertVehicleAsync(dataSource));
    }

    private static Task InsertAsync(
        NpgsqlDataSource dataSource,
        Guid vehicleId,
        string appliesTo = "vehicle",
        string? subject = null,
        int? miles = 5000,
        int? months = null,
        int? lastMileage = 40_000,
        DateOnly? lastDoneOn = null,
        string? notified = null) =>
        ReminderTestData.InsertReminderAsync(
            dataSource, vehicleId, appliesTo: appliesTo, subject: subject, intervalMiles: miles, intervalMonths: months,
            lastDoneMileage: lastMileage, lastDoneOn: lastDoneOn, notifiedState: notified);

    [Fact]
    public async Task Reminders_AcceptAValidRow_AndDefaultToActive()
    {
        var (dataSource, vehicleId) = await CreateWithVehicleAsync();
        await InsertAsync(dataSource, vehicleId);

        await using var connection = await dataSource.OpenConnectionAsync();
        Assert.False(await connection.ExecuteScalarAsync<bool>("SELECT is_paused FROM reminders"));
    }

    [Theory]
    [InlineData("vehicle", true)]
    [InlineData("equipment", true)]
    [InlineData("location", true)]
    [InlineData("garage", false)]
    public async Task AppliesTo_AcceptsOnlyTheKnownValues(string appliesTo, bool accepted)
    {
        var (dataSource, vehicleId) = await CreateWithVehicleAsync();
        var subject = appliesTo == "vehicle" ? null : "Trailer hitch";

        var insert = InsertAsync(dataSource, vehicleId, appliesTo, subject);

        if (accepted) { await insert; } else { await Assert.ThrowsAsync<PostgresException>(() => insert); }
    }

    [Theory]
    [InlineData("vehicle", "Hitch", false)]
    [InlineData("vehicle", null, true)]
    [InlineData("equipment", null, false)]
    [InlineData("equipment", " ", false)]
    [InlineData("location", "Home garage", true)]
    public async Task Subject_IsNullForVehicleAndNotBlankOtherwise(string appliesTo, string? subject, bool accepted)
    {
        var (dataSource, vehicleId) = await CreateWithVehicleAsync();

        var insert = InsertAsync(dataSource, vehicleId, appliesTo, subject);

        if (accepted) { await insert; } else { await Assert.ThrowsAsync<PostgresException>(() => insert); }
    }

    [Theory]
    [InlineData(0, null, false)]
    [InlineData(1, null, true)]
    [InlineData(999_999, null, true)]
    [InlineData(1_000_000, null, false)]
    [InlineData(null, 0, false)]
    [InlineData(null, 1, true)]
    [InlineData(null, 600, true)]
    [InlineData(null, 601, false)]
    [InlineData(null, null, false)]
    [InlineData(5000, 6, true)]
    public async Task Intervals_AreBoundedAndAtLeastOneIsRequired(int? miles, int? months, bool accepted)
    {
        var (dataSource, vehicleId) = await CreateWithVehicleAsync();

        var insert = InsertAsync(dataSource, vehicleId, miles: miles, months: months, lastMileage: miles is null ? null : 100);

        if (accepted) { await insert; } else { await Assert.ThrowsAsync<PostgresException>(() => insert); }
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(9_999_999, true)]
    [InlineData(10_000_000, false)]
    [InlineData(null, false)]
    public async Task LastDoneMileage_IsBounded_AndRequiredWithADistanceInterval(int? mileage, bool accepted)
    {
        var (dataSource, vehicleId) = await CreateWithVehicleAsync();

        var insert = InsertAsync(dataSource, vehicleId, miles: 5000, lastMileage: mileage);

        if (accepted) { await insert; } else { await Assert.ThrowsAsync<PostgresException>(() => insert); }
    }

    [Fact]
    public async Task LastDoneMileage_MayBeNull_ForATimeOnlyReminder()
    {
        var (dataSource, vehicleId) = await CreateWithVehicleAsync();

        await InsertAsync(dataSource, vehicleId, miles: null, months: 12, lastMileage: null);
    }

    [Theory]
    [InlineData("1899-12-31", false)]
    [InlineData("1900-01-01", true)]
    public async Task LastDoneOn_IsNotBefore1900(string date, bool accepted)
    {
        var (dataSource, vehicleId) = await CreateWithVehicleAsync();

        var insert = InsertAsync(dataSource, vehicleId, lastDoneOn: DateOnly.Parse(date));

        if (accepted) { await insert; } else { await Assert.ThrowsAsync<PostgresException>(() => insert); }
    }

    [Theory]
    [InlineData("due_soon", true)]
    [InlineData("overdue", true)]
    [InlineData("upcoming", false)]
    public async Task NotifiedState_AcceptsOnlyDueSoonAndOverdue(string state, bool accepted)
    {
        var (dataSource, vehicleId) = await CreateWithVehicleAsync();

        var insert = InsertAsync(dataSource, vehicleId, notified: state);

        if (accepted) { await insert; } else { await Assert.ThrowsAsync<PostgresException>(() => insert); }
    }

    [Fact]
    public async Task DeletingAVehicle_RemovesItsRemindersAndTheirCompletions()
    {
        var (dataSource, vehicleId) = await CreateWithVehicleAsync();
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);
        await ReminderTestData.InsertCompletionAsync(dataSource, reminderId);

        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("DELETE FROM vehicles WHERE id = @vehicleId", new { vehicleId });

        Assert.Equal(0, await ReminderTestData.CountAsync(dataSource, "reminders"));
        Assert.Equal(0, await ReminderTestData.CountAsync(dataSource, "reminder_completions"));
    }

    [Theory]
    [InlineData("1899-12-31", 0, false)]
    [InlineData("1900-01-01", 0, true)]
    [InlineData("2026-09-01", -1, false)]
    [InlineData("2026-09-01", 9_999_999, true)]
    [InlineData("2026-09-01", 10_000_000, false)]
    public async Task Completions_AreBounded(string doneOn, int mileage, bool accepted)
    {
        var (dataSource, vehicleId) = await CreateWithVehicleAsync();
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);

        var insert = ReminderTestData.InsertCompletionAsync(dataSource, reminderId, DateOnly.Parse(doneOn), mileage);

        if (accepted) { await insert; } else { await Assert.ThrowsAsync<PostgresException>(() => insert); }
    }

    [Theory]
    [InlineData("draft_ready", true)]
    [InlineData("reminder_due_soon", true)]
    [InlineData("reminder_overdue", true)]
    [InlineData("reminder_soon", false)]
    public async Task NotificationKind_AcceptsOnlyTheKnownKinds(string kind, bool accepted)
    {
        var (dataSource, vehicleId) = await CreateWithVehicleAsync();
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);
        Guid? link = kind.StartsWith("reminder", StringComparison.Ordinal) ? reminderId : null;

        var insert = InsertNotificationAsync(dataSource, kind, link);

        if (accepted) { await insert; } else { await Assert.ThrowsAsync<PostgresException>(() => insert); }
    }

    [Fact]
    public async Task NotificationShape_RejectsReminderLinksOnDraftNotifications_AndMissingOrMixedLinksOnReminderKinds()
    {
        var (dataSource, vehicleId) = await CreateWithVehicleAsync();
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);

        await Assert.ThrowsAsync<PostgresException>(() => InsertNotificationAsync(dataSource, "draft_ready", reminderId));
        await Assert.ThrowsAsync<PostgresException>(() => InsertNotificationAsync(dataSource, "reminder_overdue", null));
        await Assert.ThrowsAsync<PostgresException>(() => InsertNotificationAsync(dataSource, "reminder_overdue", reminderId, Guid.NewGuid()));
    }

    [Fact]
    public async Task DeletingAReminder_RemovesItsNotifications_AndLeavesDraftNotifications()
    {
        var (dataSource, vehicleId) = await CreateWithVehicleAsync();
        var reminderId = await ReminderTestData.InsertReminderAsync(dataSource, vehicleId);
        await InsertNotificationAsync(dataSource, "reminder_due_soon", reminderId);
        await EmailIntakeTestData.InsertNotificationAsync(dataSource, "owner-a");

        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("DELETE FROM reminders WHERE id = @reminderId", new { reminderId });

        Assert.Equal(1, await ReminderTestData.CountAsync(dataSource, "notifications"));
    }

    [Fact]
    public async Task TheReminderIndexes_Exist()
    {
        var (dataSource, _) = await CreateWithVehicleAsync();

        await using var connection = await dataSource.OpenConnectionAsync();
        var indexes = (await connection.QueryAsync<string>(
            "SELECT indexname FROM pg_indexes WHERE indexname IN ('ix_reminders_vehicle', 'ix_reminder_completions_reminder', 'ix_notifications_reminder')")).ToList();

        Assert.Equal(3, indexes.Count);
    }

    private static async Task InsertNotificationAsync(NpgsqlDataSource dataSource, string kind, Guid? reminderId, Guid? draftId = null)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO notifications (id, owner_id, kind, draft_id, reminder_id, title, created_at) VALUES (gen_random_uuid(), 'owner-a', @kind, @draftId, @reminderId, 'Title', now())",
            new { kind, draftId, reminderId });
    }
}