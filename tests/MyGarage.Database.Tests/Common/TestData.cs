using MyGarage.Database.Vehicles;

namespace MyGarage.Database.Tests.Common;

public static class TestData
{
    public static readonly DateTimeOffset Created = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    public static VehicleRecord Vehicle(
        string ownerId,
        string nickname = "Daily",
        string make = "Ford",
        string model = "F-150",
        string? trim = null,
        string? color = null,
        int? mileage = null,
        int? doors = null,
        Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            OwnerId = ownerId,
            Nickname = nickname,
            Make = make,
            Model = model,
            Trim = trim,
            Color = color,
            Mileage = mileage,
            Doors = doors,
            CreatedAt = Created,
            UpdatedAt = Created,
            CreatedBy = ownerId,
            UpdatedBy = ownerId,
        };
}
