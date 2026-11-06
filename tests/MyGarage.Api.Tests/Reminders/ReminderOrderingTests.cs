using MyGarage.Api.Reminders;
using MyGarage.Shared.Reminders;

namespace MyGarage.Api.Tests.Reminders;

public class ReminderOrderingTests
{
    private static ReminderDto Dto(
        string name,
        ReminderStatus status,
        DateOnly? nextDueDate = null,
        int? milesRemaining = null,
        Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            VehicleId = Guid.NewGuid(),
            VehicleName = "2020 Ford F-150",
            Name = name,
            AppliesTo = ReminderAppliesTo.Vehicle,
            LastDoneOn = new DateOnly(2026, 1, 1),
            IsPaused = status == ReminderStatus.Paused,
            Status = status,
            NextDueDate = nextDueDate,
            MilesRemaining = milesRemaining,
        };

    [Fact]
    public void OrdersByStatus_OverdueDueSoonMileageNeededUpcomingPaused()
    {
        var items = new[]
        {
            Dto("paused", ReminderStatus.Paused),
            Dto("upcoming", ReminderStatus.Upcoming),
            Dto("needs", ReminderStatus.MileageNeeded),
            Dto("soon", ReminderStatus.DueSoon),
            Dto("overdue", ReminderStatus.Overdue),
        };

        var ordered = ReminderOrdering.MostUrgentFirst(items);

        Assert.Equal(["overdue", "soon", "needs", "upcoming", "paused"], ordered.Select(item => item.Name));
    }

    [Fact]
    public void WithinAStatus_TheEarliestDueDateComesFirst_AndNoDateComesLast()
    {
        var items = new[]
        {
            Dto("none", ReminderStatus.Overdue, milesRemaining: -10),
            Dto("later", ReminderStatus.Overdue, new DateOnly(2026, 10, 1)),
            Dto("earlier", ReminderStatus.Overdue, new DateOnly(2026, 8, 1)),
        };

        Assert.Equal(["earlier", "later", "none"], ReminderOrdering.MostUrgentFirst(items).Select(item => item.Name));
    }

    [Fact]
    public void OnTheSameDate_TheFewestMilesRemainingComesFirst_AndNoMilesLast()
    {
        var date = new DateOnly(2026, 12, 1);
        var items = new[]
        {
            Dto("unknown", ReminderStatus.DueSoon, date),
            Dto("many", ReminderStatus.DueSoon, date, 400),
            Dto("few", ReminderStatus.DueSoon, date, 50),
        };

        Assert.Equal(["few", "many", "unknown"], ReminderOrdering.MostUrgentFirst(items).Select(item => item.Name));
    }

    [Fact]
    public void WhenEverythingElseTies_NameThenIdDecide()
    {
        var low = new Guid("00000000-0000-0000-0000-000000000001");
        var high = new Guid("00000000-0000-0000-0000-000000000002");
        var items = new[]
        {
            Dto("beta", ReminderStatus.Upcoming, id: high),
            Dto("alpha", ReminderStatus.Upcoming, id: high),
            Dto("alpha", ReminderStatus.Upcoming, id: low),
        };

        var ordered = ReminderOrdering.MostUrgentFirst(items);

        Assert.Equal(["alpha", "alpha", "beta"], ordered.Select(item => item.Name));
        Assert.Equal([low, high, high], ordered.Select(item => item.Id));
    }

    [Fact]
    public void DoesNotMutateTheInput_AndIsStable()
    {
        var items = new[] { Dto("b", ReminderStatus.Upcoming), Dto("a", ReminderStatus.Overdue) };
        var copy = items.ToArray();

        _ = ReminderOrdering.MostUrgentFirst(items);

        Assert.Equal(copy, items);
        Assert.Equal(ReminderOrdering.MostUrgentFirst(items).Select(item => item.Id), ReminderOrdering.MostUrgentFirst(items).Select(item => item.Id));
    }
}
