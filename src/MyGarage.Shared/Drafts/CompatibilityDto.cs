using System.Text.Json.Serialization;

namespace MyGarage.Shared.Drafts;

// How well a proposal fits the record it would become: every checked field is ok, missing, or invalid.
public sealed class CompatibilityDto
{
    public DraftKind? Kind { get; init; }

    // True only when no field is missing or invalid; warnings never block.
    public required bool IsAcceptable { get; init; }

    public required IReadOnlyList<CompatibilityFieldDto> Fields { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class CompatibilityFieldDto
{
    // For example serviceRecord.lineItems[1].parts[0].unitPrice.
    public required string Path { get; init; }

    public required CompatibilityStatus Status { get; init; }

    public string? Message { get; init; }
}

[JsonConverter(typeof(SnakeCaseEnumConverter<CompatibilityStatus>))]
public enum CompatibilityStatus
{
    Ok,
    Missing,
    Invalid,
}
