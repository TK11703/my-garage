using Dapper;
using MyGarage.Database.Vehicles;
using Npgsql;

namespace MyGarage.Database.Tests.Common;

public static class TestData
{
    public static readonly DateTimeOffset Created = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    // Creates the shared feature if it is missing (keeping an existing casing) and links it to the vehicle.
    public static Task LinkFeatureAsync(NpgsqlConnection connection, Guid vehicleId, string name) =>
        connection.ExecuteAsync(
            "WITH f AS (INSERT INTO features (id, name, created_at, created_by) VALUES (gen_random_uuid(), @name, now(), 'test') " +
            "ON CONFLICT (name_key) DO UPDATE SET name = features.name RETURNING id) " +
            "INSERT INTO vehicle_tags (id, vehicle_id, feature_id, created_at) SELECT gen_random_uuid(), @vehicleId, id, now() FROM f",
            new { vehicleId, name });

    public static async Task LinkFeatureAsync(NpgsqlDataSource dataSource, Guid vehicleId, string name)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await LinkFeatureAsync(connection, vehicleId, name);
    }

    public static VehicleRecord Vehicle(
        string ownerId,
        string model = "F-150",
        string make = "Ford",
        string? trim = null,
        string? color = null,
        int? mileage = null,
        int? doors = null,
        Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            OwnerId = ownerId,
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
