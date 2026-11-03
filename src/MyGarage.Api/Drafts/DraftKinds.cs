using MyGarage.Api.EmailIntake.Extraction;
using MyGarage.Shared.Drafts;

namespace MyGarage.Api.Drafts;

// Explicit conversions between the draft enums and the lowercase text stored in the database (research R11).
public static class DraftKinds
{
    public static string ToText(DraftKind kind) => kind switch
    {
        DraftKind.ServiceRecord => "service_record",
        DraftKind.Fuel => "fuel",
        DraftKind.Energy => "energy",
        DraftKind.Inspection => "inspection",
        DraftKind.Registration => "registration",
        DraftKind.Insurance => "insurance",
        DraftKind.Tax => "tax",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown draft kind."),
    };

    public static DraftKind? FromText(string? text) => text switch
    {
        "service_record" => DraftKind.ServiceRecord,
        "fuel" => DraftKind.Fuel,
        "energy" => DraftKind.Energy,
        "inspection" => DraftKind.Inspection,
        "registration" => DraftKind.Registration,
        "insurance" => DraftKind.Insurance,
        "tax" => DraftKind.Tax,
        _ => null,
    };

    // None (and any unknown value) has no draft kind.
    public static DraftKind? FromExtraction(ExtractionKind kind) => kind switch
    {
        ExtractionKind.ServiceRecord => DraftKind.ServiceRecord,
        ExtractionKind.Fuel => DraftKind.Fuel,
        ExtractionKind.Energy => DraftKind.Energy,
        ExtractionKind.Inspection => DraftKind.Inspection,
        ExtractionKind.Registration => DraftKind.Registration,
        ExtractionKind.Insurance => DraftKind.Insurance,
        ExtractionKind.Tax => DraftKind.Tax,
        _ => null,
    };

    public static string ToText(DraftState state) => state switch
    {
        DraftState.NeedsReview => "needs_review",
        DraftState.ManualEntry => "manual_entry",
        DraftState.Included => "included",
        DraftState.Discarded => "discarded",
        DraftState.Expired => "expired",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown draft state."),
    };

    public static string ToText(VehicleMatch match) => match switch
    {
        VehicleMatch.Vin => "vin",
        VehicleMatch.Description => "description",
        VehicleMatch.OnlyVehicle => "only_vehicle",
        VehicleMatch.None => "none",
        _ => throw new ArgumentOutOfRangeException(nameof(match), match, "Unknown vehicle match."),
    };

    public static DraftState StateFromText(string text) => text switch
    {
        "needs_review" => DraftState.NeedsReview,
        "manual_entry" => DraftState.ManualEntry,
        "included" => DraftState.Included,
        "discarded" => DraftState.Discarded,
        "expired" => DraftState.Expired,
        _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Unknown stored draft state."),
    };

    public static VehicleMatch MatchFromText(string text) => text switch
    {
        "vin" => VehicleMatch.Vin,
        "description" => VehicleMatch.Description,
        "only_vehicle" => VehicleMatch.OnlyVehicle,
        "none" => VehicleMatch.None,
        _ => throw new ArgumentOutOfRangeException(nameof(text), text, "Unknown stored vehicle match."),
    };

    // A draft is open (can still be changed, accepted, or discarded) in these two states.
    public static bool IsOpen(string state) => state is "needs_review" or "manual_entry";
}
