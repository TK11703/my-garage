using MyGarage.Api.Reminders;
using MyGarage.Database.Reminders;
using MyGarage.Shared.Reminders;

namespace MyGarage.Api.Tests.Reminders;

// A reminder of an archived vehicle is disabled whatever else holds (specs/014-archive-vehicle).
public class ArchivedReminderStatusTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);

    private static ReminderCalculation Calculate(int? miles, int? months, int? lastMileage, int? current, bool paused, bool archived, DateOnly lastDoneOn) =>
        ReminderStatusCalculator.Calculate(new ReminderCalculationInput(miles, months, lastDoneOn, lastMileage, paused, current, archived), Today);

    [Theory]
    [InlineData(5_000, null, 40_000, 50_000, false)]
    [InlineData(5_000, null, 40_000, 44_800, false)]
    [InlineData(5_000, null, 40_000, 41_000, false)]
    [InlineData(5_000, null, 40_000, 50_000, true)]
    [InlineData(5_000, null, 40_000, null, false)]
    public void AnArchivedVehicle_GivesArchived_WhateverTheDistanceStatusWouldBe(int? miles, int? months, int? lastMileage, int? current, bool paused)
    {
        var result = Calculate(miles, months, lastMileage, current, paused, archived: true, new DateOnly(2026, 5, 1));

        Assert.Equal(ReminderStatus.Archived, result.Status);
        Assert.Equal(45_000, result.NextDueMileage);
    }

    [Fact]
    public void AnArchivedVehicle_GivesArchived_EvenWhenTheDateIsOverdue_AndStillReturnsTheNextDueDate()
    {
        var result = Calculate(null, 12, null, null, paused: false, archived: true, new DateOnly(2025, 1, 1));

        Assert.Equal(ReminderStatus.Archived, result.Status);
        Assert.Equal(new DateOnly(2026, 1, 1), result.NextDueDate);
    }

    [Fact]
    public void ADisabledReminderThatTheUserAlsoPaused_IsArchivedNotPaused_AndPausedAgainOnceTheVehicleIsActive()
    {
        Assert.Equal(ReminderStatus.Archived, Calculate(5_000, null, 40_000, 41_000, paused: true, archived: true, new DateOnly(2026, 5, 1)).Status);
        Assert.Equal(ReminderStatus.Paused, Calculate(5_000, null, 40_000, 41_000, paused: true, archived: false, new DateOnly(2026, 5, 1)).Status);
    }

    [Fact]
    public void Ordering_PlacesArchivedAfterPaused()
    {
        ReminderDto Dto(string name, ReminderStatus status) => new()
        {
            Id = Guid.NewGuid(), VehicleId = Guid.NewGuid(), VehicleName = "v", Name = name, LastDoneOn = new DateOnly(2026, 1, 1), IsPaused = false, Status = status,
        };

        var ordered = ReminderOrdering.MostUrgentFirst([Dto("archived", ReminderStatus.Archived), Dto("paused", ReminderStatus.Paused), Dto("upcoming", ReminderStatus.Upcoming), Dto("overdue", ReminderStatus.Overdue)]);

        Assert.Equal(["overdue", "upcoming", "paused", "archived"], ordered.Select(item => item.Name));
    }

    [Fact]
    public void ToDto_CarriesVehicleArchivedAndTheArchivedStatus_AndDetailDoesToo()
    {
        var record = new ReminderRecord
        {
            Id = Guid.NewGuid(), VehicleId = Guid.NewGuid(), VehicleYear = 2020, VehicleMake = "Ford", VehicleModel = "F-150", Name = "Registration due",
            IntervalMonths = 12, LastDoneOn = new DateOnly(2025, 1, 1), IsPaused = true, VehicleArchived = true,
        };

        var dto = ReminderMapper.ToDto(record, Today);
        var detail = ReminderMapper.ToDetailDto(record, [], Today);

        Assert.True(dto.VehicleArchived);
        Assert.True(dto.IsPaused);
        Assert.Equal(ReminderStatus.Archived, dto.Status);
        Assert.True(detail.VehicleArchived);
        Assert.Equal(ReminderStatus.Archived, detail.Status);
        Assert.False(ReminderMapper.ToDto(record with { VehicleArchived = false }, Today).VehicleArchived);
    }

    [Fact]
    public void TheStatus_RoundTripsAsArchivedInJson()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(ReminderStatus.Archived);

        Assert.Equal("\"archived\"", json);
        Assert.Equal(ReminderStatus.Archived, System.Text.Json.JsonSerializer.Deserialize<ReminderStatus>(json));
    }
}
