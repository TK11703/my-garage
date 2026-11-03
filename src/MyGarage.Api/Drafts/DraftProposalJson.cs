using System.Text.Json;
using System.Text.Json.Serialization;
using MyGarage.Shared.Drafts;

namespace MyGarage.Api.Drafts;

// The one place a DraftProposal becomes the jsonb text the Database project stores (it never interprets it) and back.
// camelCase names, snake_case draft enums through their own converter, and no null properties, as in the OpenAPI contract.
public static class DraftProposalJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(DraftProposal proposal) => JsonSerializer.Serialize(proposal, Options);

    // Empty text and the cleared value "{}" read as an empty proposal; unreadable text throws JsonException.
    public static DraftProposal Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json) ? new DraftProposal() : JsonSerializer.Deserialize<DraftProposal>(json, Options) ?? new DraftProposal();
}
