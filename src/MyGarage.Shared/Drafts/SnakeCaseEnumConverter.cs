using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyGarage.Shared.Drafts;

// Enums on the draft and notification contracts travel as lowercase snake_case strings (for example service_record).
public sealed class SnakeCaseEnumConverter<TEnum>() : JsonStringEnumConverter<TEnum>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false)
    where TEnum : struct, Enum;
