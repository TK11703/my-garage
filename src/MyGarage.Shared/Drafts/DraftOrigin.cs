using System.Text.Json.Serialization;

namespace MyGarage.Shared.Drafts;

// JSON text: email, upload.
[JsonConverter(typeof(SnakeCaseEnumConverter<DraftOrigin>))]
public enum DraftOrigin
{
    Email,
    Upload,
}
