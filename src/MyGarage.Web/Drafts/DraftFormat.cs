using System.Text.RegularExpressions;
using MyGarage.Shared.Drafts;
using MyGarage.Shared.EmailIntake;
using MyGarage.Web.Maintenance;

namespace MyGarage.Web.Drafts;

// Wording, form values, and element ids for drafts. Compatibility paths such as serviceRecord.lineItems[1].parts[0].unitPrice
// map to the id of the field they describe, so the panel can link straight to it.
public static partial class DraftFormat
{
    public static string KindLabel(DraftKind kind) =>
        kind switch
        {
            DraftKind.ServiceRecord => "Service record",
            DraftKind.Fuel => "Fuel",
            DraftKind.Energy => "Charge",
            DraftKind.Inspection => "Inspection",
            DraftKind.Registration => "Registration",
            DraftKind.Insurance => "Insurance",
            DraftKind.Tax => "Tax",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown draft kind."),
        };

    public static string KindLabelOrChoose(DraftKind? kind) => kind is { } value ? KindLabel(value) : "Choose a record type";

    // The text the record type select posts back.
    public static string KindValue(DraftKind? kind) =>
        kind switch
        {
            DraftKind.ServiceRecord => "service_record",
            DraftKind.Fuel => "fuel",
            DraftKind.Energy => "energy",
            DraftKind.Inspection => "inspection",
            DraftKind.Registration => "registration",
            DraftKind.Insurance => "insurance",
            DraftKind.Tax => "tax",
            _ => string.Empty,
        };

    public static DraftKind? ParseKind(string? value) =>
        value switch
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

    public static readonly DraftKind[] Kinds =
        [DraftKind.ServiceRecord, DraftKind.Fuel, DraftKind.Energy, DraftKind.Inspection, DraftKind.Registration, DraftKind.Insurance, DraftKind.Tax];

    // The Maintenance accordion item that holds records of this kind.
    public static MaintenanceSection SectionFor(DraftKind kind) =>
        kind switch
        {
            DraftKind.ServiceRecord => MaintenanceSection.Records,
            DraftKind.Fuel or DraftKind.Energy => MaintenanceSection.Fuel,
            _ => MaintenanceSection.Compliance,
        };

    public static string MatchNote(VehicleMatch match) =>
        match switch
        {
            VehicleMatch.Vin => "Matched by VIN.",
            VehicleMatch.Description => "Matched by year, make, and model.",
            VehicleMatch.OnlyVehicle => "This is your only vehicle.",
            _ => "Choose a vehicle.",
        };

    // "May 1, 2026 at 3:30 PM UTC".
    public static string Arrived(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("MMM d, yyyy 'at' h:mm tt 'UTC'", System.Globalization.CultureInfo.InvariantCulture);

    public static string OutcomeLabel(EmailHistoryOutcome outcome) =>
        outcome switch
        {
            EmailHistoryOutcome.AwaitingReview => "Awaiting review",
            EmailHistoryOutcome.Included => "Included",
            EmailHistoryOutcome.Discarded => "Discarded",
            EmailHistoryOutcome.Expired => "Expired",
            EmailHistoryOutcome.NotApplicable => "Not applicable",
            EmailHistoryOutcome.Failed => "Failed",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown email outcome."),
        };

    // The badge colour follows the outcome; the label always carries the meaning in words.
    public static string OutcomeBadgeClass(EmailHistoryOutcome outcome) =>
        outcome switch
        {
            EmailHistoryOutcome.AwaitingReview => "text-bg-warning",
            EmailHistoryOutcome.Included => "text-bg-success",
            EmailHistoryOutcome.Failed => "text-bg-danger",
            _ => "text-bg-secondary",
        };

    // serviceRecord.lineItems[1].parts[0].unitPrice becomes serviceRecord-lineItems-1-parts-0-unitPrice.
    public static string FieldId(string path) => path.Replace('.', '-').Replace('[', '-').Replace("]", string.Empty, StringComparison.Ordinal);

    // "Line item 2, part 1: Unit price" for a nested path; "Date serviced" for a plain one.
    public static string FieldLabel(string path)
    {
        if (path == "kind")
        {
            return "Record type";
        }

        var parts = path.Split('.');
        var prefix = new List<string>();
        string? last = null;
        foreach (var segment in parts.Skip(1))
        {
            var match = IndexedSegment().Match(segment);
            if (!match.Success)
            {
                last = segment;
                continue;
            }

            var name = match.Groups["name"].Value;
            var number = int.Parse(match.Groups["index"].Value, System.Globalization.CultureInfo.InvariantCulture) + 1;
            prefix.Add(name switch
            {
                "lineItems" => $"Line item {number}",
                "parts" => $"part {number}",
                _ => $"{Words(name)} {number}",
            });
            last = null;
        }

        var field = last is null ? null : Words(last);
        var where = string.Join(", ", prefix);
        return (where.Length, field) switch
        {
            (0, null) => Words(parts[0]),
            (0, _) => field!,
            (_, null) => where,
            _ => $"{where}: {field}",
        };
    }

    // dateServiced becomes "Date serviced".
    public static string Words(string camel)
    {
        var spaced = CapitalLetter().Replace(camel, match => " " + match.Value.ToLowerInvariant());
        return spaced.Length == 0 ? spaced : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    [GeneratedRegex(@"^(?<name>[A-Za-z]+)\[(?<index>\d+)\]$")]
    private static partial Regex IndexedSegment();

    [GeneratedRegex("[A-Z]")]
    private static partial Regex CapitalLetter();
}
