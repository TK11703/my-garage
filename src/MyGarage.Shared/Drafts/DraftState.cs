using System.Text.Json.Serialization;

namespace MyGarage.Shared.Drafts;

[JsonConverter(typeof(SnakeCaseEnumConverter<DraftState>))]
public enum DraftState
{
    NeedsReview,
    ManualEntry,
    Included,
    Discarded,
    Expired,
}
