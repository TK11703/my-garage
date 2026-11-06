namespace MyGarage.Database.Vehicles;

public sealed record VehicleSummaryRecord
{
    public Guid Id { get; init; }

    public int? Year { get; init; }

    public string Make { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public int? Mileage { get; init; }

    public Guid? MainImageId { get; init; }

    public string? FuelTypeName { get; init; }

    public string? TransmissionName { get; init; }

    // Stored text: "purchased" or "leased".
    public string? Ownership { get; init; }

    public DateOnly? PurchasedOn { get; init; }

    // JSON array of the fields the search text matched; "[]" without a search.
    public string MatchesJson { get; init; } = "[]";

    public IReadOnlyList<VehicleMatchRecord> Matches =>
        System.Text.Json.JsonSerializer.Deserialize<List<VehicleMatchRecord>>(MatchesJson, System.Text.Json.JsonSerializerOptions.Web) ?? [];
}

public sealed record VehicleMatchRecord(string Field, string Text);
